using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Transcriber;

public sealed class SettingsWindow : Window
{
    public AppSettings Settings { get; }
    private readonly ComboBox provider = new(), language = new() { IsEditable = true }, chunk = new(), points = new();
    private readonly TextBox model = new(), url = new(), speechPath = new();
    private readonly PasswordBox key = new() { Padding = new Thickness(8) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly Button tiny = new() { Content = "Download Tiny (~75 MB)" }, baseModel = new() { Content = "Download Base (~142 MB)" }, save = new() { Content = "Save settings" };
    private string active;
    private bool downloading;
    public SettingsWindow(AppSettings original)
    {
        Settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(original))!;
        active = Settings.Provider;
        Title = "Quillvora · Settings"; Width = 630; Height = 820; MinWidth = 560; MinHeight = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var stack = new StackPanel { Margin = new Thickness(24) }; Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        stack.Children.Add(new TextBlock { Text = "Speech recognition", FontSize = 22, FontWeight = FontWeights.SemiBold });
        AddLabel(stack, "Local Whisper model (.bin)", speechPath); speechPath.Text = Settings.ModelPath;
        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        var browse = new Button { Content = "Browse…" }; browse.Click += (_, _) => { var pick = new OpenFileDialog { Filter = "Whisper GGML model (*.bin)|*.bin" }; if (pick.ShowDialog() == true) speechPath.Text = pick.FileName; };
        row.Children.Add(browse); row.Children.Add(tiny); row.Children.Add(baseModel); stack.Children.Add(row);
        tiny.Click += async (_, _) => await Download(false); baseModel.Click += async (_, _) => await Download(true);
        language.ItemsSource = new[] { "en", "auto", "es", "fr", "de", "pt", "zh", "ja", "ar" }; language.Text = Settings.Language;
        AddLabel(stack, "Language (ISO code, or auto)", language);
        chunk.ItemsSource = new[] { 4, 6, 8, 10, 15, 20 }; chunk.SelectedItem = Settings.ChunkSeconds; AddLabel(stack, "Audio block length in seconds (shorter = faster updates)", chunk);
        stack.Children.Add(new TextBlock { Text = "AI summaries", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 0) });
        provider.ItemsSource = Settings.Providers.Keys; provider.SelectedItem = active;
        AddLabel(stack, "Provider", provider); AddLabel(stack, "Model name (editable; must be available to your account/server)", model); AddLabel(stack, "Base URL", url); AddLabel(stack, "API key / local server token (encrypted for this Windows account)", key);
        points.ItemsSource = new[] { 4, 5 }; points.SelectedItem = Settings.PointsPerSlide; AddLabel(stack, "Target points per slide", points);
        LoadProvider(); provider.SelectionChanged += (_, _) => { StoreProvider(); active = (string)provider.SelectedItem; LoadProvider(); };
        stack.Children.Add(new TextBlock { Text = "Cloud providers receive the transcript you ask to summarize. Local servers must already be running with a model loaded. Ollama/LM Studio are for summaries; Whisper handles speech.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Foreground = System.Windows.Media.Brushes.SlateGray });
        stack.Children.Add(status);
        save.Click += (_, _) =>
        {
            try
            {
                StoreProvider(); Settings.Provider = active; Settings.ModelPath = speechPath.Text.Trim(); Settings.Language = language.Text.Trim(); Settings.ChunkSeconds = (int)(chunk.SelectedItem ?? 8); Settings.PointsPerSlide = (int)(points.SelectedItem ?? 4);
                Settings.Save(); DialogResult = true;
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        stack.Children.Add(save);
        Closing += (_, e) => { if (downloading) { e.Cancel = true; status.Text = "Wait for the model download to finish before closing Settings."; } };
    }
    private static void AddLabel(Panel panel, string text, UIElement control)
    {
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 5) }); panel.Children.Add(control);
    }
    private void StoreProvider() { var config = Settings.Providers[active]; config.Model = model.Text.Trim(); config.BaseUrl = url.Text.Trim(); config.ApiKey = key.Password; }
    private void LoadProvider()
    {
        var config = Settings.Providers[active]; model.Text = config.Model; url.Text = config.BaseUrl;
        try { key.Password = config.ApiKey; } catch { key.Password = ""; status.Text = "Re-enter this provider's key; it could not be decrypted for this account."; }
    }
    private async Task Download(bool useBase)
    {
        downloading = true; tiny.IsEnabled = baseModel.IsEnabled = save.IsEnabled = false;
        status.Text = "Downloading speech model from Hugging Face. This may take several minutes…";
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        try
        {
            string path = Path.Combine(Storage.Root, "Models", useBase ? "ggml-base.bin" : "ggml-tiny.bin");
            await TranscriptionEngine.DownloadModelAsync(path, useBase, timeout.Token); speechPath.Text = path; status.Text = "Model ready. Save settings to use it.";
        }
        catch (Exception ex) { status.Text = "Download failed: " + ex.Message; }
        finally { downloading = false; tiny.IsEnabled = baseModel.IsEnabled = save.IsEnabled = true; }
    }
}
