using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Transcriber;

public class ProviderSettings
{
    public string Model { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string EncryptedKey { get; set; } = "";
    [JsonIgnore]
    public string ApiKey
    {
        get => string.IsNullOrEmpty(EncryptedKey) ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(EncryptedKey), null, DataProtectionScope.CurrentUser));
        set => EncryptedKey = string.IsNullOrWhiteSpace(value) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
    }
}
public class AppSettings
{
    public string Provider { get; set; } = "Ollama";
    public string ModelPath { get; set; } = "";
    public string Language { get; set; } = "en";
    public int ChunkSeconds { get; set; } = 8;
    public int PointsPerSlide { get; set; } = 4;
    public Dictionary<string, ProviderSettings> Providers { get; set; } = new()
    {
        ["OpenAI"] = new() { BaseUrl = "https://api.openai.com/v1", Model = "gpt-4.1-mini" },
        ["Gemini"] = new() { BaseUrl = "https://generativelanguage.googleapis.com/v1beta", Model = "gemini-2.5-flash" },
        ["Claude"] = new() { BaseUrl = "https://api.anthropic.com/v1", Model = "claude-sonnet-4-5" },
        ["Ollama"] = new() { BaseUrl = "http://localhost:11434", Model = "llama3.2" },
        ["LM Studio"] = new() { BaseUrl = "http://localhost:1234/v1", Model = "local-model" }
    };
    public void Save() => Storage.Write(Path.Combine(Storage.Root, "settings.json"), this);
    public static AppSettings Load()
    {
        var path = Path.Combine(Storage.Root, "settings.json");
        var settings = File.Exists(path) ? Storage.Read<AppSettings>(path) : new AppSettings();
        var bundled = Path.Combine(AppContext.BaseDirectory, "Models", "ggml-tiny.bin");
        if (!File.Exists(settings.ModelPath) && File.Exists(bundled)) settings.ModelPath = bundled;
        return settings;
    }
}
