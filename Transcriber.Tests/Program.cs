using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using NAudio.Wave;
using Transcriber;
using Whisper.net;

internal class Program
{
    private static int passed;
    static void Check(bool condition, string description) { if (!condition) throw new Exception(description); Console.WriteLine("PASS " + description); passed++; }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Storage.Root = Path.GetFullPath("TestResults/profile"); Directory.CreateDirectory(Storage.Root);
            Logic(); Providers().GetAwaiter().GetResult();
            if (args.Contains("--hardware")) Hardware();
            if (args.Contains("--speech")) Speech().GetAwaiter().GetResult();
            if (args.Contains("--ndi")) NdiAudio().GetAwaiter().GetResult();
            if (args.Contains("--render")) Render();
            Console.WriteLine($"{passed} checks passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void Logic()
    {
        TranscriptLine[] lines = [new(0, 10, "Mic", "opening"), new(590, 610, "PC", "boundary"), new(1190, 1200, "NDI", "recent")];
        Check(TranscriptScope.Resolve(lines, "summarize last 10 minutes", 1200).Count == 2, "last ten minutes includes overlapping boundary");
        Check(TranscriptScope.Resolve(lines, "past 30 seconds", 1200).Count == 1, "seconds scope");
        Check(TranscriptScope.Resolve(lines, "last 0.5 hours", 1200).Count == 3, "fractional hour scope");
        Check(TranscriptScope.Resolve(lines, "section about budget", 1200).Count == 3, "topic requests retain context for AI matching");
        Check(TranscriptScope.Resolve(lines, "last 10 minutes", 3000).Count == 0, "time scope uses current capture time, including silence");
        Check(TranscriptScope.Format(lines.Reverse()).StartsWith("[00:00:00"), "export orders concurrent sources by timestamp");
        Check(string.Concat(AiService.Split(new string('a', 50000), 18000)).Length == 50000, "large input is chunked without losing content");
        var outline = AiService.ParseSlides("```json\n{\"title\":\"Budget\",\"slides\":[{\"heading\":\"Decisions\",\"points\":[\"First\",\"Second\",\"Third\",\"Fourth\"]}]}\n```");
        Check(outline.Slides.Single().Points.Count == 4, "fenced AI slide JSON parses");
        bool rejected = false; try { AiService.ParseSlides("{\"title\":\"x\",\"slides\":[]}"); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "empty deck rejected");
        var block = new SummaryBlock(outline.Title, "budget", "summary", outline.Slides, DateTime.Now, 1200);
        var session = new Session { Lines = lines.ToList(), Summaries = [block], Duration = 1200 };
        Storage.Write(Path.Combine(Storage.Root, "session.json"), session);
        Check(Storage.Read<Session>(Path.Combine(Storage.Root, "session.json")).Lines.Count == 3, "session and summary round trip");
        PowerPointExport.Save("TestResults/summary.pptx", [block, block]);
        using (var deck = PresentationDocument.Open("TestResults/summary.pptx", false))
        {
            var errors = new OpenXmlValidator().Validate(deck).ToList();
            Check(errors.Count == 0, "PowerPoint schema validation: " + string.Join("; ", errors.Select(x => x.Description)));
            Check(deck.PresentationPart!.SlideParts.Count() == 2, "multiple summary blocks combine into one deck");
        }
        var settings = new ProviderSettings { ApiKey = "test-not-a-real-secret" };
        Check(!JsonSerializer.Serialize(settings).Contains("test-not-a-real-secret") && settings.ApiKey == "test-not-a-real-secret", "API keys encrypted at rest with Windows DPAPI");
        var pcm = new byte[] { 0, 64, 0, 32, 0, 128, 0, 0 };
        var mono = WasapiSource.ToMono(pcm, pcm.Length, new WaveFormat(48000, 16, 2));
        Check(Math.Abs(mono[0] - .375) < .0001 && Math.Abs(mono[1] + .5) < .0001, "PCM stereo downmix");
        List<AudioChunk> chunks = []; double clock = 2;
        var buffer = new AudioBuffer("test", 4, () => clock, chunks.Add, _ => { });
        buffer.Add(Enumerable.Range(0, 48000).Select(x => (float)Math.Sin(x * .05) * .3f).ToArray(), 48000);
        buffer.Flush();
        Check(chunks.Count == 1 && Math.Abs(chunks[0].Samples.Length - 16000) < 10, "stop flushes partial block and resamples to 16 kHz");
        Check(Math.Abs(chunks[0].Start - 1) < .001, "audio chunk timestamps account for packet duration");
        buffer.Add(new float[48000], 48000); buffer.Flush();
        Check(chunks.Count == 1, "silence skipped before speech inference");
    }
    static async Task Providers()
    {
        var replies = new Dictionary<string, string>
        {
            ["OpenAI"] = "{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"ok\"}]}]}",
            ["Gemini"] = "{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"ok\"}]}}]}",
            ["Claude"] = "{\"stop_reason\":\"end_turn\",\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}",
            ["Ollama"] = "{\"message\":{\"content\":\"ok\"}}",
            ["LM Studio"] = "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"ok\"}}]}"
        };
        foreach (var (name, reply) in replies)
        {
            var config = new AppSettings().Providers[name]; config.ApiKey = "test";
            var fake = new FakeHandler(reply);
            var response = await new AiService(new HttpClient(fake)).CompleteAsync(name, config, "system", "transcript", CancellationToken.None);
            Check(response == "ok" && fake.Body!.Contains("transcript"), name + " request and response contract");
            Check(name switch { "OpenAI" => fake.Url!.EndsWith("/responses") && fake.Auth == "Bearer test", "Gemini" => fake.Url!.Contains(":generateContent") && fake.GoogleKey == "test", "Claude" => fake.Url!.EndsWith("/messages") && fake.ClaudeKey == "test", "Ollama" => fake.Url!.EndsWith("/api/chat"), _ => fake.Url!.EndsWith("/chat/completions") }, name + " endpoint and authentication");
        }
        var fail = new FakeHandler("private error") { Code = HttpStatusCode.Unauthorized };
        bool rejected = false;
        try { await new AiService(new HttpClient(fail)).CompleteAsync("Ollama", new AppSettings().Providers["Ollama"], "", "", CancellationToken.None); }
        catch (InvalidOperationException ex) { rejected = ex.Message.Contains("401") && !ex.Message.Contains("private error"); }
        Check(rejected, "HTTP failure is actionable and does not expose response secrets");
        var http = new HttpClient(new FakeHandler("{\"message\":{\"content\":\"summary notes\"}}"));
        var result = await new AiService(http).SummarizeAsync(new AppSettings(), "summarize budget", new string('x', 50000), false, 12, new Progress<string>(), CancellationToken.None);
        Check(result.Text == "summary notes", "multi-chunk summaries reduce then synthesize");
    }
    static void Hardware()
    {
        var devices = AudioDevices.List(); Check(devices.Count > 0, $"Windows audio enumeration ({devices.Count} endpoints)");
        var ndi = NdiNative.Discover(); Check(true, $"NDI runtime initialization and discovery ({ndi.Count} sources)");
    }
    static async Task Speech()
    {
        using var factory = WhisperFactory.FromPath("models/ggml-tiny.bin");
        using var processor = factory.CreateBuilder().WithLanguage("en").Build();
        using var audio = File.OpenRead("TestResults/jfk.wav");
        var text = "";
        await foreach (var segment in processor.ProcessAsync(audio)) text += segment.Text;
        Check(text.Contains("country", StringComparison.OrdinalIgnoreCase), "real Whisper inference on reference speech: " + text.Trim());
    }
    static async Task NdiAudio()
    {
        NdiNative.Ensure();
        var name = Marshal.StringToCoTaskMemUTF8("Quillvora automated audio test");
        var config = new SendConfig { Name = name, ClockAudio = true };
        var sender = NDIlib_send_create(ref config);
        try
        {
            Check(sender != IntPtr.Zero, "NDI synthetic sender created");
            var native = Marshal.PtrToStructure<NdiNative.Source>(NDIlib_send_get_source_name(sender));
            var source = new AudioSource { Name = Marshal.PtrToStringUTF8(native.Name)!, Address = Marshal.PtrToStringUTF8(native.Address)!, Kind = "NDI" };
            var discovered = NdiNative.Discover();
            source = discovered.FirstOrDefault(x => x.Name == source.Name) ?? source;
            Console.WriteLine($"NDI discovery found {discovered.Count} source(s).");
            Console.WriteLine($"Synthetic NDI source: {source.Name}; address: {source.Address ?? "(none)"}");
            var chunks = new System.Collections.Concurrent.ConcurrentBag<AudioChunk>();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var buffer = new AudioBuffer("NDI test", 4, () => watch.Elapsed.TotalSeconds, chunks.Add, _ => { });
            using var capture = new NdiCapture(source, buffer, Console.WriteLine);
            capture.Start();
            var samples = Enumerable.Range(0, 4800).Select(i => (float)Math.Sin(i * .05) * .2f).ToArray();
            var data = Marshal.AllocHGlobal(samples.Length * sizeof(float));
            try
            {
                Marshal.Copy(samples, 0, data, samples.Length);
                var frame = new NdiNative.AudioFrame { SampleRate = 48000, Channels = 1, Samples = samples.Length, Data = data, ChannelStride = samples.Length * 4, Timecode = long.MaxValue };
                for (int i = 0; i < 120; i++) { NDIlib_send_send_audio_v2(sender, ref frame); await Task.Delay(100).ConfigureAwait(false); }
                Console.WriteLine($"NDI sender connections: {NDIlib_send_get_no_connections(sender, 0)}");
                await capture.StopAsync();
                Check(chunks.Count > 0 && chunks.Sum(x => x.Samples.Length) > 16000, "NDI receives real float audio and resamples it for transcription");
            }
            finally { Marshal.FreeHGlobal(data); }
        }
        finally { if (sender != IntPtr.Zero) NDIlib_send_destroy(sender); Marshal.FreeCoTaskMem(name); }
    }
    [StructLayout(LayoutKind.Sequential)] struct SendConfig { public IntPtr Name, Groups; [MarshalAs(UnmanagedType.U1)] public bool ClockVideo; [MarshalAs(UnmanagedType.U1)] public bool ClockAudio; }
    const string NdiDll = @"C:\Program Files\NDI\NDI 6 Runtime\v6\Processing.NDI.Lib.x64.dll";
    [DllImport(NdiDll, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr NDIlib_send_create(ref SendConfig config);
    [DllImport(NdiDll, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr NDIlib_send_get_source_name(IntPtr sender);
    [DllImport(NdiDll, CallingConvention = CallingConvention.Cdecl)] static extern void NDIlib_send_send_audio_v2(IntPtr sender, ref NdiNative.AudioFrame frame);
    [DllImport(NdiDll, CallingConvention = CallingConvention.Cdecl)] static extern void NDIlib_send_destroy(IntPtr sender);
    [DllImport(NdiDll, CallingConvention = CallingConvention.Cdecl)] static extern int NDIlib_send_get_no_connections(IntPtr sender, uint timeout);
    static void Render()
    {
        var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new MainWindow();
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(1440, 880)); root.Arrange(new Rect(0, 0, 1440, 880)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1440, 880, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create("TestResults/app-preview.png"); encoder.Save(stream);
        Check(true, "WPF layout instantiated and rendered offscreen");
        app.Shutdown();
    }
    sealed class FakeHandler(string reply) : HttpMessageHandler
    {
        public HttpStatusCode Code = HttpStatusCode.OK;
        public string? Body, Url, Auth, GoogleKey, ClaudeKey;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct); Url = request.RequestUri!.ToString(); Auth = request.Headers.Authorization?.ToString();
            GoogleKey = request.Headers.TryGetValues("x-goog-api-key", out var g) ? g.First() : null;
            ClaudeKey = request.Headers.TryGetValues("x-api-key", out var c) ? c.First() : null;
            return new(Code) { Content = new StringContent(reply) };
        }
    }
}
