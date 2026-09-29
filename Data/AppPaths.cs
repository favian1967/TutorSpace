using System.Text.Encodings.Web;
using System.Text.Json;

namespace TutorSpace.Data;

/// <summary>Where the app keeps its data: %LOCALAPPDATA%\TutorSpace.</summary>
public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TutorSpace");

    public static string MediaDir => Path.Combine(DataDir, "media");

    public static void Ensure()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(MediaDir);
    }
}

/// <summary>JSON helpers for the few columns that hold structured data.</summary>
public static class Json
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public static T Read<T>(string? json) where T : new()
    {
        if (string.IsNullOrWhiteSpace(json)) return new T();
        try { return JsonSerializer.Deserialize<T>(json, Options) ?? new T(); }
        catch (JsonException) { return new T(); }
    }

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Clone<T>(T value) where T : new() => Read<T>(Write(value));
}

/// <summary>"Today" for the whole app. The PC's local date gates unlocking.</summary>
public static class Clock
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public static DateOnly MondayOf(DateOnly value) =>
        value.AddDays(-(((int)value.DayOfWeek + 6) % 7));

    public static int DayIndexOf(DateOnly value) => ((int)value.DayOfWeek + 6) % 7;
}
