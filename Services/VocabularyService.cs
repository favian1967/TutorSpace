using Microsoft.EntityFrameworkCore;
using TutorSpace.Data;

namespace TutorSpace.Services;

/// <summary>
/// Spaced repetition (SM-2 with four answers). The four grades map to four
/// distinct interval rules; the ease factor remembers how hard this word has been.
/// </summary>
public static class VocabularyService
{
    private const int FirstIntervalDays = 1;
    private const int SecondIntervalDays = 6;
    private const double HardMultiplier = 1.2;
    private const double EasyBonus = 1.3;

    private static double EaseDelta(ReviewGrade grade) => grade switch
    {
        ReviewGrade.Again => -0.20,
        ReviewGrade.Hard => -0.15,
        ReviewGrade.Good => 0.0,
        _ => +0.15,
    };

    public static string Label(ReviewGrade g) => g switch
    {
        ReviewGrade.Again => "Снова",
        ReviewGrade.Hard => "Трудно",
        ReviewGrade.Good => "Хорошо",
        _ => "Легко",
    };

    public static string Label(WordStatus s) => s switch
    {
        WordStatus.New => "Новое",
        WordStatus.Learning => "Учу",
        _ => "Выучено",
    };

    public static List<VocabularyEntry> EntriesOf(int studentId)
    {
        using var db = new AppDbContext();
        return db.Vocabulary.AsNoTracking().Where(v => v.StudentId == studentId)
            .OrderBy(v => v.IsLearned).ThenByDescending(v => v.CreatedAt).ToList();
    }

    /// <summary>Words whose repetition date has arrived, oldest schedule first.</summary>
    public static List<VocabularyEntry> DueEntries(int studentId, DateOnly today)
    {
        using var db = new AppDbContext();
        return db.Vocabulary.AsNoTracking()
            .Where(v => v.StudentId == studentId && !v.IsLearned && v.NextReviewOn != null && v.NextReviewOn <= today)
            .OrderBy(v => v.NextReviewOn).ThenBy(v => v.Id).ToList();
    }

    private static void ValidateWord(ref string word, ref string translation, ref string example)
    {
        word = (word ?? "").Trim();
        translation = (translation ?? "").Trim();
        example = (example ?? "").Trim();
        if (word.Length == 0 || translation.Length == 0) throw new UserError("Нужны слово и перевод.");
        if (word.Length > 120) throw new UserError("Слово — не больше 120 символов.");
        if (translation.Length > 200) throw new UserError("Перевод — не больше 200 символов.");
        if (example.Length > 2000) throw new UserError("Пример — не больше 2000 символов.");
    }

    private static void EnsureUnique(AppDbContext db, int studentId, string word, int exceptId = 0)
    {
        var lower = word.ToLower();
        if (db.Vocabulary.Any(v => v.StudentId == studentId && v.Word.ToLower() == lower && v.Id != exceptId))
            throw new UserError($"Слово «{word}» уже есть в словаре.");
    }

    public static VocabularyEntry Add(int studentId, string word, string translation, string example)
    {
        ValidateWord(ref word, ref translation, ref example);
        using var db = new AppDbContext();
        EnsureUnique(db, studentId, word);
        var entry = new VocabularyEntry
        {
            StudentId = studentId, Word = word, Translation = translation, Example = example,
            NextReviewOn = Clock.Today,
        };
        db.Vocabulary.Add(entry);
        db.SaveChanges();
        return entry;
    }

    public static void Update(int entryId, string word, string translation, string example)
    {
        ValidateWord(ref word, ref translation, ref example);
        using var db = new AppDbContext();
        var entry = db.Vocabulary.Find(entryId) ?? throw new UserError("Слово не найдено.");
        EnsureUnique(db, entry.StudentId, word, entry.Id);
        entry.Word = word;
        entry.Translation = translation;
        entry.Example = example;
        db.SaveChanges();
    }

    public static void Delete(int entryId)
    {
        using var db = new AppDbContext();
        var entry = db.Vocabulary.Find(entryId);
        if (entry == null) return;
        db.Vocabulary.Remove(entry);
        db.SaveChanges();
    }

    /// <summary>Days until the next repetition. Pure, so the UI can preview each button.</summary>
    public static int NextInterval(VocabularyEntry entry, ReviewGrade grade)
    {
        if (grade == ReviewGrade.Again) return 0;
        var current = Math.Max(entry.IntervalDays, 0);
        int proposed;
        if (grade == ReviewGrade.Hard)
        {
            proposed = Math.Max(current + 1, (int)Math.Round(current * HardMultiplier, MidpointRounding.ToEven));
        }
        else
        {
            proposed = entry.Repetitions switch
            {
                0 => FirstIntervalDays,
                1 => SecondIntervalDays,
                _ => (int)Math.Round(current * entry.EaseFactor, MidpointRounding.ToEven),
            };
            if (grade == ReviewGrade.Easy)
                proposed = Math.Max(proposed + 1, (int)Math.Round(proposed * EasyBonus, MidpointRounding.ToEven));
        }
        return Math.Min(Math.Max(proposed, 1), VocabularyEntry.MaxIntervalDays);
    }

    public static string IntervalLabel(int days) => days switch
    {
        0 => "сегодня",
        1 => "1 день",
        < 30 => $"{days} дн.",
        < 365 => $"{days / 30} мес.",
        _ => "1 год",
    };

    public static void RecordReview(int entryId, ReviewGrade grade)
    {
        var today = Clock.Today;
        using var db = new AppDbContext();
        var entry = db.Vocabulary.Find(entryId) ?? throw new UserError("Слово не найдено.");

        entry.IntervalDays = NextInterval(entry, grade);
        entry.EaseFactor = Math.Clamp(Math.Round(entry.EaseFactor + EaseDelta(grade), 3), VocabularyEntry.MinEase, VocabularyEntry.MaxEase);

        if (grade == ReviewGrade.Again)
        {
            if (entry.Repetitions > 0) entry.Lapses++;
            entry.Repetitions = 0;
            entry.IsLearned = false;
        }
        else
        {
            entry.Repetitions++;
            entry.IsLearned = entry.IntervalDays >= VocabularyEntry.LearnedIntervalDays;
        }
        entry.ReviewCount++;
        entry.LastReviewedAt = DateTime.Now;
        entry.Status = StatusFromProgress(entry);
        entry.NextReviewOn = today.AddDays(entry.IntervalDays);
        db.SaveChanges();
    }

    /// <summary>Manual toggle from the dictionary, independent of the review flow.</summary>
    public static void SetLearned(int entryId, bool learned)
    {
        using var db = new AppDbContext();
        var entry = db.Vocabulary.Find(entryId) ?? throw new UserError("Слово не найдено.");
        entry.IsLearned = learned;
        if (learned)
        {
            entry.IntervalDays = VocabularyEntry.LearnedIntervalDays;
        }
        else
        {
            entry.IntervalDays = 0;
            entry.Repetitions = 0;
            entry.EaseFactor = VocabularyEntry.DefaultEase;
        }
        entry.Status = StatusFromProgress(entry);
        entry.NextReviewOn = Clock.Today.AddDays(entry.IntervalDays);
        db.SaveChanges();
    }

    private static WordStatus StatusFromProgress(VocabularyEntry entry) =>
        entry.IsLearned ? WordStatus.Mastered : entry.ReviewCount > 0 ? WordStatus.Learning : WordStatus.New;

    public record Stats(int Total, int Learned, int DueToday, int ReviewedToday, int New, int Learning, int AddedLast7Days);

    public static Stats StatsFor(int studentId, DateOnly today)
    {
        using var db = new AppDbContext();
        var all = db.Vocabulary.AsNoTracking().Where(v => v.StudentId == studentId).ToList();
        var start = today.ToDateTime(TimeOnly.MinValue);
        var weekAgo = today.AddDays(-6).ToDateTime(TimeOnly.MinValue);
        return new Stats(
            all.Count,
            all.Count(v => v.IsLearned),
            all.Count(v => v.IsDue(today)),
            all.Count(v => v.LastReviewedAt >= start && v.LastReviewedAt < start.AddDays(1)),
            all.Count(v => v.Status == WordStatus.New),
            all.Count(v => v.Status == WordStatus.Learning),
            all.Count(v => v.CreatedAt >= weekAgo));
    }

    // ---------- Word of the day ----------

    public static void SetWordOfDay(int tutorId, DateOnly date, string word, string translation, string example)
    {
        ValidateWord(ref word, ref translation, ref example);
        using var db = new AppDbContext();
        var row = db.WordsOfDay.FirstOrDefault(w => w.TutorId == tutorId && w.Date == date);
        if (row == null)
        {
            row = new WordOfDay { TutorId = tutorId, Date = date };
            db.WordsOfDay.Add(row);
        }
        row.Word = word.Trim();
        row.Translation = translation.Trim();
        row.Example = example.Trim();
        db.SaveChanges();
    }

    public static List<WordOfDay> WordsOfDayOf(int tutorId)
    {
        using var db = new AppDbContext();
        return db.WordsOfDay.AsNoTracking().Where(w => w.TutorId == tutorId).OrderByDescending(w => w.Date).ToList();
    }

    public static void DeleteWordOfDay(int id)
    {
        using var db = new AppDbContext();
        var row = db.WordsOfDay.Find(id);
        if (row == null) return;
        db.WordsOfDay.Remove(row);
        db.SaveChanges();
    }

    /// <summary>The most recent word among all of the student's active tutors.</summary>
    public static WordOfDay? WordOfDayForStudent(int studentId, DateOnly today)
    {
        using var db = new AppDbContext();
        var tutorIds = db.Enrollments.Where(e => e.StudentId == studentId && e.IsActive).Select(e => e.TutorId).ToList();
        return db.WordsOfDay.AsNoTracking()
            .Where(w => tutorIds.Contains(w.TutorId) && w.Date <= today)
            .OrderByDescending(w => w.Date).ThenByDescending(w => w.CreatedAt)
            .FirstOrDefault();
    }
}
