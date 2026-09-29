using Microsoft.EntityFrameworkCore;
using TutorSpace.Data;

namespace TutorSpace.Services;

/// <summary>
/// Creating exercises and accepting answers. Only gap-fills are marked
/// automatically; everything else is self-checked against the key or read by the tutor.
/// </summary>
public static class ExerciseService
{
    /// <summary>The gaps of a GAP_FILL live in the prompt text, so they are parsed out here.</summary>
    public static ExerciseConfig BuildConfig(ExerciseKind kind, string prompt, ExerciseConfig config)
    {
        if (kind == ExerciseKind.GapFill)
        {
            var (template, blanks) = ExerciseText.ParseGaps(prompt);
            config.Template = template;
            config.Blanks = blanks;
        }
        return config;
    }

    public const int MaxText = 20000;

    public static void Validate(ExerciseKind kind, string prompt, ExerciseConfig? config = null)
    {
        if (!Enum.IsDefined(kind)) throw new UserError("Неизвестный тип упражнения.");
        if (string.IsNullOrWhiteSpace(prompt)) throw new UserError("Нужен текст упражнения.");
        if (prompt.Length > MaxText) throw new UserError("Текст упражнения слишком длинный.");
        if (kind == ExerciseKind.GapFill)
        {
            var (template, blanks) = ExerciseText.ParseGaps(prompt);
            if (blanks.Count == 0 && (prompt.Contains("{{") || prompt.Contains("}}")))
                throw new UserError("Фигурные скобки не закрыты. Каждый пропуск — {{ответ}}.");
            if (blanks.Count == 0)
                throw new UserError("В тексте нет ни одного {{пропуска}}. Пример: He {{went|did go::go}} home.");
            if (blanks.Any(b => b.Answers.Count == 0))
                throw new UserError("В одном из пропусков нет правильного ответа: {{ответ::подсказка}}.");
            if (template.Contains("{{") || template.Contains("}}") || blanks.Any(b => b.Answers.Any(a => a.Contains("{{") || a.Contains("}}"))))
                throw new UserError("Фигурные скобки не закрыты или вложены. Каждый пропуск — {{ответ}}.");
        }
        if (kind == ExerciseKind.Theses && config != null && config.MaxTheses > 0 && config.MinTheses > config.MaxTheses)
            throw new UserError("Минимум тезисов больше максимума.");
    }

    public static void Save(Exercise data)
    {
        Validate(data.Kind, data.Prompt, data.Config);
        if (data.Instruction.Trim().Length > 300) throw new UserError("Инструкция — не больше 300 символов.");
        using var db = new AppDbContext();
        if (data.AudioId is { } audioId && !db.Media.Any(m => m.Id == audioId))
            throw new UserError("Выбранное аудио удалено из аудиотеки.");
        Exercise exercise;
        if (data.Id == 0)
        {
            if (!db.Assignments.Any(a => a.Id == data.AssignmentId)) throw new UserError("Задание не найдено — возможно, его удалили.");
            exercise = new Exercise { AssignmentId = data.AssignmentId };
            exercise.Order = (db.Exercises.Where(x => x.AssignmentId == data.AssignmentId).Max(x => (int?)x.Order) ?? 0) + 1;
            db.Exercises.Add(exercise);
        }
        else
        {
            exercise = db.Exercises.Find(data.Id) ?? throw new UserError("Упражнение не найдено.");
        }
        exercise.Kind = data.Kind;
        exercise.Prompt = data.Prompt.Trim();
        exercise.Instruction = data.Instruction.Trim();
        exercise.AudioId = data.AudioId;
        exercise.Config = BuildConfig(data.Kind, exercise.Prompt, data.Config);
        db.SaveChanges();
    }

    public static void Delete(int exerciseId)
    {
        using var db = new AppDbContext();
        var exercise = db.Exercises.Find(exerciseId);
        if (exercise == null) return;
        db.Exercises.Remove(exercise);
        db.SaveChanges();
    }

    public static List<ExerciseAttempt> AttemptsOf(int exerciseId, int studentId)
    {
        using var db = new AppDbContext();
        return db.Attempts.AsNoTracking().Include(a => a.Audio)
            .Where(a => a.ExerciseId == exerciseId && a.StudentId == studentId)
            .OrderBy(a => a.AttemptNo).ToList();
    }

    /// <summary>Records one attempt, marking it where the app honestly can.</summary>
    public static ExerciseAttempt SubmitAttempt(int studentId, int exerciseId, AttemptAnswers answers, int? audioId)
    {
        using var db = new AppDbContext();
        var exercise = db.Exercises
            .Include(x => x.Assignment).ThenInclude(a => a.WeekPlan).ThenInclude(w => w.Enrollment)
            .FirstOrDefault(x => x.Id == exerciseId) ?? throw new UserError("Упражнение не найдено.");

        if (exercise.Assignment.WeekPlan.Enrollment?.StudentId != studentId) throw new UserError("Это не ваше упражнение.");
        if (!exercise.Assignment.IsUnlockedFor(Clock.Today)) throw new UserError("Это задание ещё не открылось.");

        var used = db.Attempts.Where(a => a.ExerciseId == exerciseId && a.StudentId == studentId)
            .Max(a => (int?)a.AttemptNo) ?? 0;
        var allowed = exercise.AttemptsAllowed;
        if (allowed > 0 && used >= allowed) throw new UserError($"Попытки закончились — их было {allowed}.");
        if (audioId is { } aid && !db.Media.Any(m => m.Id == aid)) throw new UserError("Аудиозапись не найдена.");

        var (score, max, enriched) = Score(exercise, answers, audioId);
        var attempt = new ExerciseAttempt
        {
            ExerciseId = exerciseId,
            StudentId = studentId,
            AttemptNo = used + 1,
            Answers = enriched,
            AudioId = audioId,
            Score = score,
            MaxScore = max,
            IsAutoChecked = exercise.IsAutoChecked,
        };
        db.Attempts.Add(attempt);
        db.SaveChanges();

        RuleService.SafeEvaluate(() => RuleService.EvaluateForAttempt(attempt.Id));
        return attempt;
    }

    private static (int Score, int Max, AttemptAnswers Answers) Score(Exercise exercise, AttemptAnswers answers, int? audioId)
    {
        var config = exercise.Config;
        switch (exercise.Kind)
        {
            case ExerciseKind.GapFill:
            {
                answers.Blanks = config.Blanks.Select((_, i) => i < answers.Blanks.Count ? Cut(answers.Blanks[i] ?? "", 500) : "").ToList();
                var (score, total, results) = ExerciseText.CheckGapFill(config.Blanks, answers.Blanks);
                answers.Results = results;
                return (score, total, answers);
            }
            case ExerciseKind.OpenAnswer:
            {
                var text = (answers.Text ?? "").Trim();
                if (text.Length > MaxText) throw new UserError("Ответ слишком длинный.");
                if (text.Length == 0) throw new UserError("Нужен ответ — хотя бы одно предложение.");
                if (config.MinWords > 0 && text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < config.MinWords)
                    throw new UserError($"Ответ короче {config.MinWords} слов.");
                answers.Text = text;
                return (0, 0, answers);
            }
            case ExerciseKind.Theses:
            {
                var theses = answers.Theses.Select(t => (t ?? "").Trim()).Where(t => t.Length > 0).ToList();
                if (theses.Any(t => t.Length > 2000)) throw new UserError("Тезис слишком длинный.");
                if (theses.Count > 50) throw new UserError("Слишком много тезисов.");
                if (config.MinTheses > 0 && theses.Count < config.MinTheses)
                    throw new UserError($"Нужно как минимум {config.MinTheses} тезиса.");
                if (config.MaxTheses > 0 && theses.Count > config.MaxTheses)
                    throw new UserError($"Не больше {config.MaxTheses} тезисов.");
                answers.Theses = theses;
                answers.Marks = new();
                return (0, theses.Count, answers);
            }
            default:
                if (audioId == null) throw new UserError("Нужна аудиозапись.");
                return (0, 0, answers);
        }
    }

    /// <summary>
    /// Second half of a THESES block: the student's own verdict on each thesis,
    /// made only after reading the tutor's key.
    /// </summary>
    public static void SetThesesMarks(int attemptId, List<bool> marks)
    {
        using var db = new AppDbContext();
        var attempt = db.Attempts.Include(a => a.Exercise).FirstOrDefault(a => a.Id == attemptId)
            ?? throw new UserError("Попытка не найдена.");
        if (attempt.Exercise.Kind != ExerciseKind.Theses) throw new UserError("Галочки ставятся только в тезисах.");
        var answers = attempt.Answers;
        answers.Marks = marks.Take(answers.Theses.Count).ToList();
        attempt.Answers = answers;
        attempt.Score = answers.Marks.Count(m => m);
        attempt.MaxScore = answers.Theses.Count;
        db.SaveChanges();
    }

    private static string Cut(string value, int limit) => value.Length > limit ? value[..limit] : value;

    /// <summary>The tutor's answer to an answer — a comment, deliberately not a mark.</summary>
    public static void Comment(int attemptId, string comment)
    {
        using var db = new AppDbContext();
        var attempt = db.Attempts.Find(attemptId) ?? throw new UserError("Попытка не найдена.");
        if (comment.Trim().Length > MaxText) throw new UserError("Комментарий слишком длинный.");
        attempt.TutorComment = comment.Trim();
        db.SaveChanges();
    }

    /// <summary>What the student may see after answering, and not one moment before.</summary>
    public static string RevealText(Exercise exercise, ExerciseAttempt? attempt)
    {
        if (attempt == null) return "";
        var config = exercise.Config;
        return exercise.Kind switch
        {
            ExerciseKind.GapFill => "Ключ: " + string.Join("; ", config.Blanks.Select((b, i) => $"{i + 1}) {string.Join(" / ", b.Answers)}")),
            ExerciseKind.Theses => config.Reference.Count == 0 ? "" : "Тезисы преподавателя:\n" + string.Join("\n", config.Reference.Select(r => "• " + r)),
            ExerciseKind.Speaking => SpeakingText(exercise) is { Length: > 0 } t ? "Оригинал: " + t : "",
            ExerciseKind.OpenAnswer => config.SampleAnswer.Length > 0 ? "Пример ответа: " + config.SampleAnswer : "",
            _ => "",
        };
    }

    public static string SpeakingText(Exercise exercise) =>
        exercise.Audio?.Transcript is { Length: > 0 } t ? t : exercise.Config.Transcript;

    /// <summary>A short human-readable rendering of an attempt for the tutor.</summary>
    public static string DescribeAnswer(ExerciseAttempt attempt)
    {
        var a = attempt.Answers;
        return attempt.Exercise.Kind switch
        {
            ExerciseKind.GapFill => string.Join("; ", a.Blanks.Select((b, i) =>
                $"{(i < a.Results.Count && a.Results[i] ? "+" : "−")} {b}")) + $"  —  {attempt.Score}/{attempt.MaxScore}",
            ExerciseKind.OpenAnswer => a.Text,
            ExerciseKind.Theses => string.Join("\n", a.Theses.Select((t, i) =>
                (i < a.Marks.Count ? (a.Marks[i] ? "+ " : "− ") : "• ") + t)),
            _ => attempt.Audio != null ? $"Аудиозапись ({attempt.Audio.DurationSeconds} с)" : "Аудио удалено",
        };
    }
}
