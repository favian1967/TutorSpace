using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using TutorSpace.Data;

namespace TutorSpace.Services;

/// <summary>Audio files: the tutor's clip library and the students' recordings.</summary>
public static class MediaService
{
    public const long MaxUploadBytes = 15 * 1024 * 1024;
    public const string AudioFilter = "Аудио|*.mp3;*.wav;*.m4a;*.wma;*.aac;*.ogg|Все файлы|*.*";

    /// <summary>Copies a file into the app's media folder under an unguessable name.</summary>
    public static MediaAsset Import(int ownerId, string sourcePath, MediaSource source, string title = "", string transcript = "", int durationSeconds = 0)
    {
        var info = new FileInfo(sourcePath);
        if (!info.Exists) throw new UserError("Файл не найден.");
        if (info.Length > MaxUploadBytes) throw new UserError("Файл больше 15 МБ.");
        if (info.Length == 0) throw new UserError("Файл пустой.");

        var ext = info.Extension.ToLowerInvariant();
        if (ext.Length > 10) ext = ext[..10];
        var relative = Path.Combine(ownerId.ToString(), Guid.NewGuid().ToString("N") + ext);
        var destination = Path.Combine(AppPaths.MediaDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(sourcePath, destination);

        if (durationSeconds == 0) durationSeconds = AudioPlayer.ProbeDuration(destination);

        try
        {
            return Save(ownerId, source, relative, title, transcript, info, durationSeconds);
        }
        catch
        {
            // The row did not make it into the database — do not leave the copy behind.
            try { File.Delete(destination); } catch (IOException) { }
            throw;
        }
    }

    private static MediaAsset Save(int ownerId, MediaSource source, string relative, string title, string transcript, FileInfo info, int durationSeconds)
    {
        using var db = new AppDbContext();
        var asset = new MediaAsset
        {
            OwnerId = ownerId,
            Source = source,
            StoredPath = relative,
            Title = title.Trim().Length > 200 ? title.Trim()[..200] : title.Trim(),
            Transcript = transcript.Trim(),
            OriginalName = info.Name,
            SizeBytes = info.Length,
            DurationSeconds = durationSeconds,
        };
        db.Media.Add(asset);
        db.SaveChanges();
        return asset;
    }

    public static List<MediaAsset> LibraryOf(int ownerId, MediaSource source = MediaSource.TutorClip)
    {
        using var db = new AppDbContext();
        return db.Media.AsNoTracking().Where(m => m.OwnerId == ownerId && m.Source == source)
            .OrderByDescending(m => m.CreatedAt).ToList();
    }

    public static void Update(int assetId, string title, string transcript, string origin)
    {
        using var db = new AppDbContext();
        var asset = db.Media.Find(assetId) ?? throw new UserError("Файл не найден.");
        if (title.Trim().Length > 200) throw new UserError("Название — не больше 200 символов.");
        if (transcript.Length > 20000 || origin.Length > 200) throw new UserError("Слишком длинный текст.");
        asset.Title = title.Trim();
        asset.Transcript = transcript.Trim();
        asset.Origin = origin.Trim();
        db.SaveChanges();
    }

    /// <summary>Takes the file with the row — an orphaned blob is pure disk cost.</summary>
    public static void Delete(int assetId)
    {
        using var db = new AppDbContext();
        var asset = db.Media.Find(assetId);
        if (asset == null) return;
        var path = asset.FullPath;
        db.Media.Remove(asset);
        db.SaveChanges();
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }

    public static string NewRecordingPath() =>
        Path.Combine(Path.GetTempPath(), "tutorspace_" + Guid.NewGuid().ToString("N") + ".wav");
}

/// <summary>Plays one audio file at a time through WPF's MediaPlayer.</summary>
public static class AudioPlayer
{
    private static readonly MediaPlayer Player = new();

    public static void Play(string path)
    {
        if (!File.Exists(path)) throw new UserError("Аудиофайл не найден на диске.");
        Player.Stop();
        Player.Open(new Uri(path));
        Player.Play();
    }

    public static void Play(MediaAsset? asset)
    {
        if (asset == null) throw new UserError("Аудио не прикреплено.");
        Play(asset.FullPath);
    }

    public static void Stop() => Player.Stop();

    /// <summary>Asks the Windows MCI layer for the length of a file; 0 when unknown.</summary>
    public static int ProbeDuration(string path)
    {
        const string alias = "tsprobe";
        try
        {
            Mci.Send($"open \"{path}\" alias {alias}");
            Mci.Send($"set {alias} time format milliseconds");
            var length = Mci.Send($"status {alias} length");
            return int.TryParse(length, out var ms) ? (int)Math.Round(ms / 1000.0) : 0;
        }
        catch
        {
            return 0;
        }
        finally
        {
            Mci.Send($"close {alias}", throwOnError: false);
        }
    }
}

/// <summary>Microphone recording to a WAV file via winmm (no extra packages needed).</summary>
public sealed class AudioRecorder
{
    private const string Alias = "tsrec";
    private DateTime _startedAt;

    public bool IsRecording { get; private set; }
    public TimeSpan Elapsed => IsRecording ? DateTime.Now - _startedAt : TimeSpan.Zero;

    public void Start()
    {
        if (IsRecording) return;
        Mci.Send($"close {Alias}", throwOnError: false);
        Mci.Send($"open new type waveaudio alias {Alias}");
        Mci.Send($"set {Alias} bitspersample 16 channels 1 samplespersec 22050", throwOnError: false);
        Mci.Send($"record {Alias}");
        _startedAt = DateTime.Now;
        IsRecording = true;
    }

    /// <summary>Stops and saves to <paramref name="path"/>. Returns the duration in seconds.</summary>
    public int Stop(string path)
    {
        if (!IsRecording) return 0;
        var seconds = (int)Math.Round((DateTime.Now - _startedAt).TotalSeconds);
        try
        {
            Mci.Send($"stop {Alias}");
            Mci.Send($"save {Alias} \"{path}\"");
        }
        finally
        {
            Mci.Send($"close {Alias}", throwOnError: false);
            IsRecording = false;
        }
        return seconds;
    }

    public void Cancel()
    {
        if (!IsRecording) return;
        Mci.Send($"close {Alias}", throwOnError: false);
        IsRecording = false;
    }
}

internal static class Mci
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, StringBuilder? buffer, int bufferSize, IntPtr callback);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool mciGetErrorString(int error, StringBuilder buffer, int bufferSize);

    public static string Send(string command, bool throwOnError = true)
    {
        var buffer = new StringBuilder(256);
        var code = mciSendString(command, buffer, buffer.Capacity, IntPtr.Zero);
        if (code != 0 && throwOnError)
        {
            var error = new StringBuilder(256);
            mciGetErrorString(code, error, error.Capacity);
            throw new UserError("Ошибка звука: " + error + " (проверьте микрофон).");
        }
        return buffer.ToString();
    }
}
