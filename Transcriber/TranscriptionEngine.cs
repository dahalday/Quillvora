using System.Threading.Channels;
using Whisper.net;
using Whisper.net.Ggml;

namespace Transcriber;

public sealed class TranscriptionEngine : IAsyncDisposable
{
    private readonly Channel<AudioChunk> queue = Channel.CreateBounded<AudioChunk>(new BoundedChannelOptions(120) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private readonly List<IAudioCapture> captures = [];
    private WhisperFactory? factory;
    private WhisperProcessor? processor;
    private Task? worker;
    public event Action<TranscriptLine>? Line;
    public event Action<string>? Status;
    public int Pending => queue.Reader.Count;
    private int dropped;
    public int Dropped => dropped;

    public async Task StartAsync(IEnumerable<AudioSource> sources, AppSettings settings, Func<double> clock)
    {
        if (!File.Exists(settings.ModelPath)) throw new FileNotFoundException("Choose or download a Whisper model in Settings first.");
        Status?.Invoke("Loading local speech model…");
        await Task.Run(() =>
        {
            factory = WhisperFactory.FromPath(settings.ModelPath);
            processor = factory.CreateBuilder().WithLanguage(string.IsNullOrWhiteSpace(settings.Language) ? "auto" : settings.Language).Build();
        });
        worker = Task.Run(async () =>
        {
            await foreach (var chunk in queue.Reader.ReadAllAsync())
            {
                try
                {
                    await foreach (var segment in processor!.ProcessAsync(chunk.Samples))
                    {
                        string text = segment.Text.Trim();
                        if (text.Length == 0) continue;
                        double duration = chunk.Samples.Length / 16000.0;
                        Line?.Invoke(new(chunk.Start + Math.Min(duration, segment.Start.TotalSeconds), chunk.Start + Math.Min(duration, segment.End.TotalSeconds), chunk.Source, text));
                    }
                }
                catch (Exception ex) { Status?.Invoke($"Transcription failed for {chunk.Source}: {ex.Message}"); }
            }
        });
        try
        {
            foreach (var source in sources)
            {
                var buffer = new AudioBuffer(source.Kind + " · " + source.Name, settings.ChunkSeconds, clock, chunk =>
                {
                    if (!queue.Writer.TryWrite(chunk)) { Interlocked.Increment(ref dropped); Status?.Invoke($"OVERLOAD: audio at {TranscriptLine.Time(chunk.Start)} from {chunk.Source} was dropped. Use a smaller model or fewer sources."); }
                }, level => source.Level = level);
                IAudioCapture capture = source.Kind == "NDI" ? new NdiCapture(source, buffer, s => Status?.Invoke(s)) : new WasapiSource(source, buffer, s => Status?.Invoke(s));
                captures.Add(capture); capture.Start();
            }
            Status?.Invoke("Live · listening to selected sources");
        }
        catch { await StopAsync(); throw; }
    }
    public async Task StopAsync()
    {
        foreach (var capture in captures)
        {
            try { await capture.StopAsync(); }
            catch (Exception ex) { Status?.Invoke("Capture stop: " + ex.Message); }
            finally { capture.Dispose(); }
        }
        captures.Clear(); queue.Writer.TryComplete();
        if (worker != null) await worker;
    }
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        if (processor != null) { await processor.DisposeAsync(); processor = null; }
        factory?.Dispose(); factory = null;
    }
    public static async Task DownloadModelAsync(string destination, bool small, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temp = destination + ".download";
        try
        {
            using var stream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(small ? GgmlType.Base : GgmlType.Tiny);
            await using (var file = File.Create(temp)) await stream.CopyToAsync(file, ct);
            File.Move(temp, destination, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
