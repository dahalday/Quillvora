using System.Runtime.InteropServices;

namespace Transcriber;

// NDI SDK C ABI. Runtime remains separately installed under its own licence.
internal static class NdiNative
{
    private const string Dll = "Processing.NDI.Lib.x64.dll";
    private static readonly object Gate = new();
    private static bool initialized;
    static NdiNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(NdiNative).Assembly, (name, _, _) =>
        {
            if (name != Dll) return IntPtr.Zero;
            string[] dirs = [AppContext.BaseDirectory, Environment.GetEnvironmentVariable("NDI_RUNTIME_DIR_V6") ?? "", Environment.GetEnvironmentVariable("NDI_RUNTIME_DIR_V5") ?? "", @"C:\Program Files\NDI\NDI 6 Runtime\v6", @"C:\Program Files\NDI\NDI 6 Tools\Runtime", @"C:\Program Files\NDI\NDI 5 Runtime\v5"];
            foreach (var dir in dirs.Where(d => d.Length > 0)) if (NativeLibrary.TryLoad(Path.Combine(dir, Dll), out var library)) return library;
            return IntPtr.Zero;
        });
    }
    internal static void Ensure()
    {
        lock (Gate)
        {
            if (initialized) return;
            try { if (!NDIlib_initialize()) throw new InvalidOperationException("NDI could not initialize on this computer."); initialized = true; }
            catch (DllNotFoundException) { throw new InvalidOperationException("Install the x64 NDI Runtime, then restart Quillvora."); }
        }
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Source { public IntPtr Name, Address; }
    [StructLayout(LayoutKind.Sequential)] internal struct FindConfig { [MarshalAs(UnmanagedType.U1)] public bool ShowLocal; public IntPtr Groups, ExtraIps; }
    [StructLayout(LayoutKind.Sequential)] internal struct ReceiveConfig { public Source Source; public int ColorFormat, Bandwidth; [MarshalAs(UnmanagedType.U1)] public bool AllowFields; public IntPtr Name; }
    [StructLayout(LayoutKind.Sequential)] internal struct AudioFrame { public int SampleRate, Channels, Samples; public long Timecode; public IntPtr Data; public int ChannelStride; public IntPtr Metadata; public long Timestamp; }
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool NDIlib_initialize();
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr NDIlib_find_create_v2(ref FindConfig config);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void NDIlib_find_destroy(IntPtr finder);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool NDIlib_find_wait_for_sources(IntPtr finder, uint milliseconds);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr NDIlib_find_get_current_sources(IntPtr finder, out uint count);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr NDIlib_recv_create_v3(ref ReceiveConfig config);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void NDIlib_recv_destroy(IntPtr receiver);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int NDIlib_recv_capture_v2(IntPtr receiver, IntPtr video, ref AudioFrame audio, IntPtr metadata, uint milliseconds);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void NDIlib_recv_free_audio_v2(IntPtr receiver, ref AudioFrame frame);

    internal static List<AudioSource> Discover()
    {
        Ensure();
        var config = new FindConfig { ShowLocal = true };
        var finder = NDIlib_find_create_v2(ref config);
        if (finder == IntPtr.Zero) throw new InvalidOperationException("NDI discovery could not start.");
        try
        {
            // Allow discovery to settle instead of returning after only the first source arrives.
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline) NDIlib_find_wait_for_sources(finder, 250);
            var pointer = NDIlib_find_get_current_sources(finder, out uint count);
            List<AudioSource> result = [];
            for (int i = 0; i < count; i++)
            {
                var source = Marshal.PtrToStructure<Source>(pointer + i * Marshal.SizeOf<Source>());
                var name = Marshal.PtrToStringUTF8(source.Name) ?? "NDI source";
                result.Add(new() { Id = "ndi:" + name, Name = name, Kind = "NDI", Address = Marshal.PtrToStringUTF8(source.Address) ?? "" });
            }
            return result;
        }
        finally { NDIlib_find_destroy(finder); }
    }
}

public sealed class NdiCapture(AudioSource source, AudioBuffer buffer, Action<string> status) : IAudioCapture
{
    private readonly CancellationTokenSource cancellation = new();
    private IntPtr receiver;
    private Task? worker;
    public void Start()
    {
        NdiNative.Ensure();
        IntPtr name = Marshal.StringToCoTaskMemUTF8(source.Name), address = string.IsNullOrWhiteSpace(source.Address) ? IntPtr.Zero : Marshal.StringToCoTaskMemUTF8(source.Address);
        try
        {
            var config = new NdiNative.ReceiveConfig { Source = new() { Name = name, Address = address }, ColorFormat = 0, Bandwidth = 10, AllowFields = false };
            receiver = NdiNative.NDIlib_recv_create_v3(ref config); // bandwidth_audio_only = 10
            if (receiver == IntPtr.Zero) throw new InvalidOperationException("Cannot create NDI receiver.");
        }
        finally { Marshal.FreeCoTaskMem(name); Marshal.FreeCoTaskMem(address); }
        worker = Task.Run(Receive);
    }
    private void Receive()
    {
        var lastAudio = DateTime.UtcNow;
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var frame = new NdiNative.AudioFrame();
                int type = NdiNative.NDIlib_recv_capture_v2(receiver, IntPtr.Zero, ref frame, IntPtr.Zero, 250);
                if (type == 2)
                {
                    try
                    {
                        if (frame.Samples <= 0 || frame.Samples > 1920000 || frame.Channels <= 0 || frame.Channels > 256 || frame.SampleRate <= 0 || frame.Data == IntPtr.Zero || frame.ChannelStride < frame.Samples * sizeof(float))
                            throw new InvalidDataException("Invalid NDI audio frame.");
                        var mono = new float[frame.Samples]; var channel = new float[frame.Samples];
                        for (int c = 0; c < frame.Channels; c++)
                        {
                            Marshal.Copy(frame.Data + c * frame.ChannelStride, channel, 0, frame.Samples);
                            for (int i = 0; i < mono.Length; i++) mono[i] += channel[i] / frame.Channels;
                        }
                        buffer.Add(mono, frame.SampleRate); lastAudio = DateTime.UtcNow;
                    }
                    finally { NdiNative.NDIlib_recv_free_audio_v2(receiver, ref frame); }
                }
                else if (type == 4) throw new IOException("NDI receiver lost its connection.");
                buffer.FlushIdle();
                if ((DateTime.UtcNow - lastAudio).TotalSeconds > 15) { status($"Waiting for NDI audio: {source.Name}"); lastAudio = DateTime.UtcNow; }
            }
        }
        catch (Exception ex) { status($"NDI {source.Name}: {ex.Message}"); }
        finally { buffer.Flush(); }
    }
    public async Task StopAsync() { cancellation.Cancel(); if (worker != null) await worker; }
    public void Dispose() { if (receiver != IntPtr.Zero) { NdiNative.NDIlib_recv_destroy(receiver); receiver = IntPtr.Zero; } cancellation.Dispose(); }
}
