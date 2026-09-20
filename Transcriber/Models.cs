using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;

namespace Transcriber;

public record TranscriptLine(double Start, double End, string Source, string Text)
{
    public string Display => $"[{Time(Start)} – {Time(End)}] {Source}: {Text}";
    public static string Time(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss");
}
public record SlideContent(string Heading, List<string> Points);
public record SummaryBlock(string Title, string Prompt, string Text, List<SlideContent> Slides, DateTime Created, double ThroughSeconds);
public class Session
{
    public string Title { get; set; } = "Untitled session";
    public DateTime Created { get; set; } = DateTime.Now;
    public double Duration { get; set; }
    public List<TranscriptLine> Lines { get; set; } = [];
    public List<SummaryBlock> Summaries { get; set; } = [];
}

public static class TranscriptScope
{
    public static List<TranscriptLine> Resolve(IEnumerable<TranscriptLine> lines, string prompt, double now)
    {
        var match = Regex.Match(prompt, @"\b(?:last|past|previous)\s+(\d+(?:\.\d+)?)\s*(seconds?|secs?|minutes?|mins?|hours?|hrs?)\b", RegexOptions.IgnoreCase);
        if (!match.Success) return lines.OrderBy(x => x.Start).ToList();
        double count = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        string unit = match.Groups[2].Value.ToLowerInvariant();
        double window = count * (unit.StartsWith("h") ? 3600 : unit.StartsWith("m") ? 60 : 1);
        return lines.Where(x => x.End > now - window && x.Start <= now).OrderBy(x => x.Start).ToList();
    }
    public static string Format(IEnumerable<TranscriptLine> lines) => string.Join(Environment.NewLine, lines.OrderBy(x => x.Start).Select(x => x.Display));
}

public static class Storage
{
    public static string Root { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SourceScribe");
    public static JsonSerializerOptions Json { get; } = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Json));
        File.Move(temp, path, true);
    }
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("The file is empty.");
}
