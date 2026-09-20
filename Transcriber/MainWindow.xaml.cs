using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Transcriber;

public partial class MainWindow : Window
{
    private AppSettings settings = new();
    private Session session = new();
    private readonly ObservableCollection<AudioSource> sources = [];
    private readonly ObservableCollection<SummaryBlock> summaries = [];
    private readonly Stopwatch clock = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private TranscriptionEngine? engine;
    private CancellationTokenSource? aiCancellation;
    private double priorDuration;
    private bool busyCapture, closing, closeRequested, dirty;
    private int ticks;
    private string selectedTranscript = "";
    private string recoveryPath = Path.Combine(Storage.Root, "recovery.json");
    private double Now => priorDuration + clock.Elapsed.TotalSeconds;

    public MainWindow()
    {
        InitializeComponent();
        SourcesList.ItemsSource = sources; SummariesList.ItemsSource = summaries;
        try { settings = AppSettings.Load(); } catch (Exception ex) { StatusText.Text = "Settings could not be loaded: " + ex.Message; }
        timer.Tick += (_, _) =>
        {
            ClockText.Text = TranscriptLine.Time(Now);
            if (engine != null) QueueText.Text = $"{engine.Pending} queued · {engine.Dropped} dropped";
            if (++ticks % 10 == 0 && dirty) Recover();
        };
        timer.Start();
        Loaded += async (_, _) =>
        {
            UpdateConfig();
            if (File.Exists(recoveryPath) && MessageBox.Show(this, "Restore your most recent session?", "Session recovery", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                try { LoadSession(Storage.Read<Session>(recoveryPath)); } catch (Exception ex) { ShowError(ex); }
            }
            await RefreshSources();
        };
    }
    private void UpdateConfig() => ConfigText.Text = $"Speech: {(File.Exists(settings.ModelPath) ? Path.GetFileNameWithoutExtension(settings.ModelPath) : "Model needed")}\nSummary: {settings.Provider} · {settings.Providers[settings.Provider].Model}";
    private void SetStatus(string value) => Dispatcher.InvokeAsync(() => StatusText.Text = value);
    private void ShowError(Exception ex) { StatusText.Text = ex.Message; MessageBox.Show(this, ex.Message, "Quillvora", MessageBoxButton.OK, MessageBoxImage.Warning); }
    private async Task RefreshSources()
    {
        if (engine != null || busyCapture) return;
        RefreshButton.IsEnabled = false; StartButton.IsEnabled = false;
        var selected = sources.Where(x => x.Selected).Select(x => x.Id).ToHashSet();
        try
        {
            sources.Clear();
            foreach (var source in AudioDevices.List()) { source.Selected = selected.Contains(source.Id); sources.Add(source); }
            SetStatus("Discovering NDI sources…");
            try
            {
                foreach (var source in await Task.Run(NdiNative.Discover)) { source.Selected = selected.Contains(source.Id); sources.Add(source); }
                SetStatus($"Ready · {sources.Count} audio sources available");
            }
            catch (Exception ex) { SetStatus("Windows sources ready. " + ex.Message); }
        }
        catch (Exception ex) { ShowError(ex); }
        finally { RefreshButton.IsEnabled = true; StartButton.IsEnabled = true; }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshSources();
    private void AllSources_Click(object sender, RoutedEventArgs e)
    {
        bool select = !sources.All(x => x.Selected);
        foreach (var source in sources) source.Selected = select;
    }
    private void SetCaptureControls(bool active)
    {
        StartButton.IsEnabled = !active; StopButton.IsEnabled = active;
        SourcesList.IsEnabled = !active; RefreshButton.IsEnabled = !active; AllSourcesButton.IsEnabled = !active;
    }
    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (engine != null || busyCapture) return;
        var selected = sources.Where(x => x.Selected).ToList();
        if (selected.Count == 0) { ShowError(new InvalidOperationException("Select at least one audio source.")); return; }
        if (!File.Exists(settings.ModelPath)) { Settings_Click(sender, e); if (!File.Exists(settings.ModelPath)) return; }
        busyCapture = true; SetCaptureControls(true); StopButton.IsEnabled = false;
        engine = new();
        engine.Status += SetStatus;
        engine.Line += line => Dispatcher.InvokeAsync(() =>
        {
            session.Lines.Add(line); dirty = true;
            int start = TranscriptBox.SelectionStart, length = TranscriptBox.SelectionLength;
            TranscriptBox.AppendText(line.Display + Environment.NewLine + Environment.NewLine);
            if (length > 0) TranscriptBox.Select(start, length);
            else if (AutoScroll.IsChecked == true) TranscriptBox.ScrollToEnd();
            TranscriptInfo.Text = $"{session.Lines.Count} lines · Select text to save or summarize a block";
        });
        clock.Start();
        try { await engine.StartAsync(selected, settings, () => Now); StopButton.IsEnabled = true; }
        catch (Exception ex) { await engine.DisposeAsync(); engine = null; clock.Stop(); SetCaptureControls(false); ShowError(ex); }
        finally { busyCapture = false; }
    }
    private async Task StopCapture()
    {
        if (engine == null || busyCapture) return;
        busyCapture = true; StopButton.IsEnabled = false; SetStatus("Stopping capture · finishing queued speech…");
        clock.Stop();
        try
        {
            await engine.DisposeAsync();
            // Let queued transcript notifications arrive before saving the final recovery snapshot.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            session.Duration = Now; dirty = true; Recover(); SetStatus($"Stopped · all queued audio processed · {engine.Dropped} blocks dropped");
        }
        catch (Exception ex) { ShowError(ex); }
        finally { engine = null; busyCapture = false; SetCaptureControls(false); foreach (var source in sources) source.Level = 0; }
    }
    private async void Stop_Click(object sender, RoutedEventArgs e) => await StopCapture();
    private void TranscriptSelection_Changed(object sender, RoutedEventArgs e) => selectedTranscript = TranscriptBox.SelectedText;
    private string ScopedText(string prompt)
    {
        if (ScopeBox.SelectedIndex == 1)
        {
            if (string.IsNullOrWhiteSpace(selectedTranscript)) throw new InvalidOperationException("Select text in the live transcript first.");
            return selectedTranscript;
        }
        var scope = ScopeBox.SelectedIndex == 2 ? "last 10 minutes" : prompt;
        return TranscriptScope.Format(TranscriptScope.Resolve(session.Lines, scope, Now));
    }
    private async Task Generate(bool slides)
    {
        if (aiCancellation != null) return;
        try
        {
            string prompt = PromptBox.Text.Trim();
            if (prompt.Length == 0) prompt = "Summarize the key ideas and decisions.";
            string snapshot = ScopedText(prompt); double through = Now;
            BeginAi();
            var result = await new AiService().SummarizeAsync(settings, prompt, snapshot, slides, through, new Progress<string>(s => AiStatus.Text = s), aiCancellation!.Token);
            session.Summaries.Add(result); summaries.Add(result); SummariesList.SelectedItem = result;
            dirty = true; Recover(); AiStatus.Text = $"{settings.Provider} · snapshot through {TranscriptLine.Time(through)}";
        }
        catch (OperationCanceledException) { AiStatus.Text = "Summary cancelled; transcript capture continues."; }
        catch (Exception ex) { AiStatus.Text = "Could not generate summary"; ShowError(ex); }
        finally { EndAi(); }
    }
    private void BeginAi() { aiCancellation = new(); ProseButton.IsEnabled = false; SlidesButton.IsEnabled = false; CancelAiButton.IsEnabled = true; }
    private void EndAi() { aiCancellation?.Dispose(); aiCancellation = null; ProseButton.IsEnabled = true; SlidesButton.IsEnabled = true; CancelAiButton.IsEnabled = false; }
    private async void Prose_Click(object sender, RoutedEventArgs e) => await Generate(false);
    private async void Slides_Click(object sender, RoutedEventArgs e) => await Generate(true);
    private void CancelAi_Click(object sender, RoutedEventArgs e) => aiCancellation?.Cancel();
    private void Summary_Changed(object sender, SelectionChangedEventArgs e)
    {
        SummaryBox.Text = string.Join("\n\n────────────────────\n\n", SummariesList.SelectedItems.Cast<SummaryBlock>().Select(x => x.Text));
    }
    private static string? SavePath(string filter, string filename)
    {
        var dialog = new SaveFileDialog { Filter = filter, FileName = filename, AddExtension = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
    private void SaveText(string text, string name)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("There is no text to save in this selection.");
            string? path = SavePath("Text file (*.txt)|*.txt", name);
            if (path != null) { File.WriteAllText(path, text); SetStatus("Saved " + path); }
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void SaveAll_Click(object sender, RoutedEventArgs e) => SaveText(TranscriptScope.Format(session.Lines.ToList()), "Transcript.txt");
    private void SaveSelection_Click(object sender, RoutedEventArgs e) => SaveText(selectedTranscript, "Transcript selection.txt");
    private void SaveSummary_Click(object sender, RoutedEventArgs e) => SaveText(SummaryBox.Text, "Summary.txt");
    private async Task Export(bool all)
    {
        if (aiCancellation != null) { SetStatus("Wait for the current AI request to finish."); return; }
        var blocks = all ? summaries.ToList() : SummariesList.SelectedItems.Cast<SummaryBlock>().OrderBy(x => x.Created).ToList();
        if (blocks.Count == 0) { ShowError(new InvalidOperationException("Select at least one summary block to export.")); return; }
        string? path = SavePath("PowerPoint presentation (*.pptx)|*.pptx", "Session summary.pptx");
        if (path == null) return;
        try
        {
            BeginAi();
            List<SummaryBlock> output = [];
            foreach (var block in blocks)
            {
                output.Add(block.Slides.Count > 0 ? block : await new AiService().SummarizeAsync(settings, "Convert this summary into a presentation with section headings. " + block.Prompt, block.Text, true, block.ThroughSeconds, new Progress<string>(s => AiStatus.Text = s), aiCancellation!.Token));
            }
            PowerPointExport.Save(path, output); AiStatus.Text = $"Exported {output.Sum(x => x.Slides.Count)} slides from {output.Count} summary blocks"; SetStatus("Saved " + path);
        }
        catch (OperationCanceledException) { AiStatus.Text = "Export cancelled."; }
        catch (Exception ex) { ShowError(ex); }
        finally { EndAi(); }
    }
    private async void ExportSelected_Click(object sender, RoutedEventArgs e) => await Export(false);
    private async void ExportAll_Click(object sender, RoutedEventArgs e) => await Export(true);
    private void SaveSession_Click(object sender, RoutedEventArgs e)
    {
        try { string? path = SavePath("Quillvora session (*.json)|*.json", "Session.json"); if (path != null) { session.Duration = Now; Storage.Write(path, session); SetStatus("Session saved · " + path); } }
        catch (Exception ex) { ShowError(ex); }
    }
    private bool CanReplaceSession()
    {
        if (engine != null || busyCapture || aiCancellation != null) { SetStatus("Stop capture and finish AI requests before changing sessions."); return false; }
        return true;
    }
    private void ArchiveCurrent()
    {
        if (session.Lines.Count == 0 && session.Summaries.Count == 0) return;
        session.Duration = Now;
        Storage.Write(Path.Combine(Storage.Root, "Sessions", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json"), session);
    }
    private void NewSession_Click(object sender, RoutedEventArgs e)
    {
        if (!CanReplaceSession()) return;
        try { ArchiveCurrent(); LoadSession(new()); Recover(); } catch (Exception ex) { ShowError(ex); }
    }
    private void OpenSession_Click(object sender, RoutedEventArgs e)
    {
        if (!CanReplaceSession()) return;
        var dialog = new OpenFileDialog { Filter = "Quillvora session (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;
        try { var loaded = Storage.Read<Session>(dialog.FileName); Validate(loaded); ArchiveCurrent(); LoadSession(loaded); }
        catch (Exception ex) { ShowError(ex); }
    }
    private static void Validate(Session value)
    {
        if (value.Lines == null || value.Summaries == null || !double.IsFinite(value.Duration) || value.Duration < 0 || value.Lines.Any(x => !double.IsFinite(x.Start) || !double.IsFinite(x.End) || x.Start < 0 || x.End < x.Start)) throw new InvalidDataException("Invalid Quillvora session file.");
    }
    private void LoadSession(Session value)
    {
        Validate(value); session = value; clock.Reset(); priorDuration = Math.Max(value.Duration, value.Lines.Select(x => x.End).DefaultIfEmpty(0).Max());
        TranscriptBox.Text = TranscriptScope.Format(value.Lines); if (value.Lines.Count > 0) TranscriptBox.AppendText("\n\n");
        summaries.Clear(); foreach (var block in value.Summaries) summaries.Add(block);
        SummaryBox.Clear(); TranscriptInfo.Text = $"{value.Lines.Count} lines · Select text to save or summarize a block"; dirty = true;
    }
    private void Recover()
    {
        try { session.Duration = Now; Storage.Write(recoveryPath, session); dirty = false; }
        catch (Exception ex) { SetStatus("Autosave failed: " + ex.Message); }
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (engine != null || busyCapture || aiCancellation != null) { SetStatus("Stop capture and finish AI requests before changing settings."); return; }
        var dialog = new SettingsWindow(settings) { Owner = this };
        if (dialog.ShowDialog() == true) { settings = dialog.Settings; UpdateConfig(); }
    }
    private void Help_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "1. Settings: download a Tiny/Base speech model or select a ggml .bin file.\n2. Choose PC playback, microphone/line-in, NDI, or any combination; Start capture.\n3. Lines arrive after short audio blocks are recognized. Stop finishes queued audio.\n4. Select transcript text to save or summarize only that block. All-so-far saves do not stop capture.\n5. Enter a request such as 'summarize last 10 minutes' or 'summarize the section about budgets'. Topic matching is handled by your chosen AI.\n6. Generate prose or slide outlines. Ctrl-click summary blocks to export some together, or Export all for one presentation.\n\nCloud summaries send the scoped transcript to the configured provider. Speech stays local. Ollama/LM Studio use your running local server and installed model. Keys are encrypted for your Windows account.\n\nRecovery saves every 10 seconds under %LOCALAPPDATA%\\SourceScribe. Raw audio is held in memory, not saved. Select headphones to avoid microphone echo of PC playback.", "Quillvora help");
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (closing) return;
        e.Cancel = true;
        if (closeRequested) return;
        if (busyCapture) { SetStatus("Please wait for capture startup or shutdown to finish."); return; }
        closeRequested = true;
        // Return from the original Closing event even when StopCapture completes synchronously.
        await Dispatcher.Yield(DispatcherPriority.Background);
        try
        {
            aiCancellation?.Cancel();
            await StopCapture();
            Recover();
            timer.Stop();
            closing = true;
            Close();
        }
        catch (Exception ex)
        {
            closing = false;
            timer.Start();
            ShowError(ex);
        }
        finally { closeRequested = false; }
    }
}
