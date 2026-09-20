using System.ComponentModel;
using System.Runtime.CompilerServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Transcriber;

public sealed class AudioSource : INotifyPropertyChanged
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Address { get; init; } = "";
    private bool selected;
    public bool Selected { get => selected; set { selected = value; Changed(); } }
    private double level;
    public double Level { get => level; set { level = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
public record AudioChunk(string Source, double Start, float[] Samples);
public interface IAudioCapture : IDisposable { void Start(); Task StopAsync(); }

public static class AudioDevices
{
    public static List<AudioSource> List()
    {
        using var enumerator = new MMDeviceEnumerator();
        List<AudioSource> result = [];
        foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device) result.Add(new() { Id = device.ID, Name = device.FriendlyName, Kind = flow == DataFlow.Render ? "PC audio" : "Mic / line in" });
        }
        return result;
    }
}

// Independent buffers preserve source labels and prevent overlapping speakers from being mixed.
public sealed class AudioBuffer(string source, int chunkSeconds, Func<double> clock, Action<AudioChunk> emit, Action<double> meter)
{
    private readonly object gate = new();
    private readonly List<float> pending = [];
    private int sampleRate;
    private double start, lastArrival;
    public void Add(float[] samples, int rate)
    {
        lock (gate)
        {
            double now = clock();
            if (pending.Count > 0 && (sampleRate != rate || now - lastArrival > .75)) FlushLocked();
            if (pending.Count == 0) { start = Math.Max(0, now - (double)samples.Length / rate); sampleRate = rate; }
            lastArrival = now;
            pending.AddRange(samples);
            meter(samples.Length == 0 ? 0 : Math.Min(100, Math.Sqrt(samples.Average(x => (double)x * x)) * 300));
            if (pending.Count >= rate * chunkSeconds) FlushLocked();
        }
    }
    public void FlushIdle()
    {
        lock (gate) if (pending.Count > 0 && clock() - lastArrival > .75) { FlushLocked(); meter(0); }
    }
    public void Flush() { lock (gate) FlushLocked(); }
    private void FlushLocked()
    {
        if (pending.Count == 0) return;
        var data = pending.ToArray(); pending.Clear();
        if (data.Length < sampleRate / 5 || data.Average(x => (double)x * x) < .000001) return;
        var provider = new FloatSamples(data, sampleRate);
        var resampler = new WdlResamplingSampleProvider(provider, 16000);
        List<float> output = [];
        var block = new float[4096];
        int read;
        while ((read = resampler.Read(block, 0, block.Length)) > 0) output.AddRange(block.Take(read));
        emit(new(source, start, output.ToArray()));
    }
    private sealed class FloatSamples(float[] data, int rate) : ISampleProvider
    {
        private int position;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(rate, 1);
        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, data.Length - position);
            Array.Copy(data, position, buffer, offset, n); position += n; return n;
        }
    }
}

public sealed class WasapiSource : IAudioCapture
{
    private readonly MMDevice device;
    private readonly WasapiCapture capture;
    private readonly AudioBuffer buffer;
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly System.Threading.Timer timer;
    private bool started;
    public WasapiSource(AudioSource source, AudioBuffer buffer, Action<string> error)
    {
        this.buffer = buffer;
        using var enumerator = new MMDeviceEnumerator();
        device = enumerator.GetDevice(source.Id);
        capture = source.Kind == "PC audio" ? new WasapiLoopbackCapture(device) : new WasapiCapture(device);
        capture.DataAvailable += (_, e) =>
        {
            try { buffer.Add(ToMono(e.Buffer, e.BytesRecorded, capture.WaveFormat), capture.WaveFormat.SampleRate); }
            catch (Exception ex) { error($"{source.Name}: {ex.Message}"); }
        };
        capture.RecordingStopped += (_, e) => { if (e.Exception != null) error($"{source.Name}: {e.Exception.Message}"); stopped.TrySetResult(); };
        timer = new(_ => buffer.FlushIdle(), null, Timeout.Infinite, Timeout.Infinite);
    }
    public void Start() { capture.StartRecording(); started = true; timer.Change(250, 250); }
    public async Task StopAsync()
    {
        timer.Change(Timeout.Infinite, Timeout.Infinite);
        if (started) { capture.StopRecording(); await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        buffer.Flush();
    }
    public void Dispose() { timer.Dispose(); capture.Dispose(); device.Dispose(); }
    public static float[] ToMono(byte[] bytes, int length, WaveFormat format)
    {
        int channels = format.Channels, width = format.BitsPerSample / 8;
        bool floating = format.Encoding == WaveFormatEncoding.IeeeFloat || format is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
        int frames = length / format.BlockAlign;
        float[] output = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            double sum = 0;
            for (int c = 0; c < channels; c++)
            {
                int i = f * format.BlockAlign + c * width;
                sum += (floating, width) switch
                {
                    (true, 4) => BitConverter.ToSingle(bytes, i),
                    (false, 2) => BitConverter.ToInt16(bytes, i) / 32768f,
                    (false, 3) => ((bytes[i] | bytes[i + 1] << 8 | bytes[i + 2] << 16) << 8 >> 8) / 8388608f,
                    (false, 4) => BitConverter.ToInt32(bytes, i) / 2147483648f,
                    (false, 1) => (bytes[i] - 128) / 128f,
                    _ => throw new NotSupportedException($"Audio format {format} is not supported.")
                };
            }
            output[f] = (float)(sum / channels);
        }
        return output;
    }
}
