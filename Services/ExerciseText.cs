using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TutorSpace.Data;

namespace TutorSpace.Services;

/// <summary>
/// Exercise text: parsing gaps, checking answers, personalising prompts.
/// Pure functions over plain data — the constructor preview, the importer and
/// submission all run the same code.
/// </summary>
public static partial class ExerciseText
{
    public const string BlankMark = "____";
    public const string WordPlaceholder = "<<word>>";
    public const string TranslationPlaceholder = "<<translation>>";
    public const string ExamplePlaceholder = "<<example>>";

    [GeneratedRegex(@"\{\{(.+?)\}\}", RegexOptions.Singleline)]
    private static partial Regex GapPattern();

    [GeneratedRegex(@"(<<word>>|<<translation>>|<<example>>)")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"_{4,}")]
    private static partial Regex LongUnderscores();

    /// <summary>Casefold, collapse whitespace, drop punctuation and curly quotes.</summary>
    public static string NormalizeAnswer(string? value)
    {
        var text = (value ?? "").Replace('’', '\'').Replace('‘', '\'');
        text = Whitespace().Replace(text, " ").Trim();
        text = text.Trim(text.Where(c => char.IsPunctuation(c) && c < 0x2FF).Distinct().ToArray());
        return text.ToLower(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// <c>"He {{went|did go::go}} to school"</c> → <c>("He ____ to school", [{answers: [went, did go], hint: go}])</c>.
    /// </summary>
    public static (string Template, List<GapBlank> Blanks) ParseGaps(string text)
    {
        var blanks = new List<GapBlank>();
        // A literal «____» in the text would be taken for a blank by the player.
        var source = LongUnderscores().Replace(text ?? "", "___");
        var template = GapPattern().Replace(source, match =>
        {
            var body = match.Groups[1].Value.Trim();
            var hint = "";
            var sep = body.IndexOf("::", StringComparison.Ordinal);
            if (sep >= 0)
            {
                hint = body[(sep + 2)..].Trim();
                body = body[..sep];
            }
            blanks.Add(new GapBlank
            {
                Answers = body.Split('|').Select(a => a.Trim()).Where(a => a.Length > 0).ToList(),
                Hint = hint,
            });
            return BlankMark;
        });
        return (template, blanks);
    }

    public static (int Score, int Max, List<bool> Results) CheckGapFill(List<GapBlank> blanks, List<string> answers)
    {
        var results = new List<bool>();
        for (var i = 0; i < blanks.Count; i++)
        {
            var given = NormalizeAnswer(i < answers.Count ? answers[i] : "");
            var accepted = blanks[i].Answers.Select(NormalizeAnswer).ToHashSet();
            results.Add(given.Length > 0 && accepted.Contains(given));
        }
        return (results.Count(r => r), blanks.Count, results);
    }

    /// <summary>Substitute the student's own vocabulary into an authored prompt.</summary>
    public static string Personalize(string text, IEnumerable<VocabularyEntry> words)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("<<")) return text;

        var remaining = new Queue<VocabularyEntry>(words);
        VocabularyEntry? current = null;
        VocabularyEntry? Take() => current = remaining.Count > 0 ? remaining.Dequeue() : null;

        var result = new StringBuilder();
        foreach (var chunk in PlaceholderPattern().Split(text))
        {
            switch (chunk)
            {
                case WordPlaceholder:
                    result.Append(Take()?.Word ?? "");
                    break;
                case TranslationPlaceholder:
                    result.Append((current ?? Take())?.Translation ?? "");
                    break;
                case ExamplePlaceholder:
                    result.Append((current ?? Take())?.Example ?? "");
                    break;
                default:
                    result.Append(chunk);
                    break;
            }
        }
        return Whitespace().Replace(result.ToString(), " ").Trim();
    }

    /// <summary><c>"13:23"</c> or <c>"1:02:11"</c> → seconds; null when not a timecode.</summary>
    public static int? ParseTimecode(string? value)
    {
        var parts = (value ?? "").Trim().Split(':');
        if (parts.Length is < 1 or > 3) return null;
        var seconds = 0;
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var number) || number < 0) return null;
            seconds = seconds * 60 + number;
        }
        return seconds;
    }

    public static string FormatTimecode(int seconds)
    {
        seconds = Math.Max(0, seconds);
        var hours = seconds / 3600;
        var minutes = seconds % 3600 / 60;
        var secs = seconds % 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{secs:00}" : $"{minutes}:{secs:00}";
    }
}

/// <summary>Recognises the resource behind a pasted link. Never fetches the URL.</summary>
public static partial class ResourceResolver
{
    [GeneratedRegex(@"(?:youtube\.com/watch\?(?:.*&)?v=|youtu\.be/|youtube\.com/embed/|youtube\.com/shorts/|youtube\.com/live/)([A-Za-z0-9_-]{11})")]
    private static partial Regex YouTubeId();

    /// <summary>
    /// Only web links are allowed: a «ссылка» of <c>C:\Windows\cmd.exe</c> or <c>javascript:</c>
    /// would otherwise be launched by the shell when the student clicks it.
    /// A link without a scheme («youtu.be/…») gets https:// added.
    /// </summary>
    public static string Normalize(string? url)
    {
        var value = (url ?? "").Trim();
        if (value.Length == 0) return "";
        if (value.Length > 2000) throw new UserError("Ссылка слишком длинная.");
        if (!value.Contains("://") && !value.Contains('\\') && !value.StartsWith("javascript", StringComparison.OrdinalIgnoreCase) && value.Contains('.'))
            value = "https://" + value;
        if (!IsWebLink(value)) throw new UserError("Ссылка должна быть веб-адресом (http:// или https://).");
        return value;
    }

    public static bool IsWebLink(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.Host.Length > 0;

    public static void Apply(Assignment assignment)
    {
        assignment.Provider = "";
        assignment.ExternalId = "";
        assignment.PreviewTitle = "";
        assignment.PreviewThumbnail = "";
        var url = assignment.Url?.Trim() ?? "";
        if (url.Length == 0) return;

        var match = YouTubeId().Match(url);
        if (match.Success)
        {
            assignment.Provider = "youtube";
            assignment.ExternalId = match.Groups[1].Value;
            assignment.PreviewTitle = "YouTube";
            assignment.PreviewThumbnail = $"https://i.ytimg.com/vi/{assignment.ExternalId}/hqdefault.jpg";
            return;
        }
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
        {
            assignment.Provider = "link";
            assignment.PreviewTitle = uri.Host;
        }
    }

    /// <summary>Opens a link in the default browser; a timecode jumps a YouTube video to a segment.</summary>
    public static void Open(string url, int? startSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        var target = url.Trim();
        if (!IsWebLink(target)) throw new UserError("Эта ссылка не похожа на веб-адрес — открывать её небезопасно.");
        if (startSeconds is { } s && YouTubeId().IsMatch(target))
            target += (target.Contains('?') ? "&" : "?") + $"t={s}";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            throw new UserError("Не удалось открыть ссылку: " + ex.Message);
        }
    }
}
