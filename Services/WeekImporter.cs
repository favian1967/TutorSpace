using System.Text.RegularExpressions;
using TutorSpace.Data;

namespace TutorSpace.Services;

/// <summary>
/// Turning written-out lesson text into a week (the "sandbox"). The parser is
/// forgiving: text that does not match a rule becomes a warning and a best guess.
/// <code>
/// # ПН
/// ## Смотрим отрезок про семью
/// ссылка: https://youtu.be/abc
/// время: 15
/// миссия: поймай 3 новых слова
/// повтор: 7
/// ### пропуски
/// He {{went|did go}} to school yesterday.
/// ### тезисы 13:23-14:44
/// - he has 10 kids
/// </code>
/// Nothing here writes to the database.
/// </summary>
public static partial class WeekImporter
{
    public const string Example = """
        # ПН
        ## Смотрим отрезок про семью
        ссылка: https://www.youtube.com/watch?v=dQw4w9WgXcQ
        время: 15
        миссия: поймай 3 новых слова
        описание: Смотри без субтитров, потом сверимся.
        повтор: 7
        ### пропуски
        He {{went|did go}} to school yesterday.
        ### вопрос
        What does he do for a living?
        ### тезисы 13:23-14:44
        - he has 10 kids
        - he lives in Texas
        - he loves his job

        # СР
        ## Произношение
        ### произношение
        текст: I'll be back

        # ПТ
        ## Итоги недели
        ### монолог
        Расскажи, что нового ты узнал за неделю.
        """;

    private static readonly Dictionary<string, int> DayAliases = BuildDays();

    private static Dictionary<string, int> BuildDays()
    {
        var names = new[]
        {
            new[] { "пн", "понедельник", "mon", "monday" },
            new[] { "вт", "вторник", "tue", "tues", "tuesday" },
            new[] { "ср", "среда", "wed", "wednesday" },
            new[] { "чт", "четверг", "thu", "thur", "thursday" },
            new[] { "пт", "пятница", "fri", "friday" },
            new[] { "сб", "суббота", "sat", "saturday" },
            new[] { "вс", "воскресенье", "sun", "sunday" },
        };
        var map = new Dictionary<string, int>();
        for (var i = 0; i < names.Length; i++)
        {
            foreach (var n in names[i]) map[n] = i;
            map[$"{i + 1}"] = i;
            map[$"день {i + 1}"] = i;
            map[$"day {i + 1}"] = i;
        }
        return map;
    }

    private static readonly Dictionary<string, string> FieldAliases = new()
    {
        ["ссылка"] = "url", ["link"] = "url", ["url"] = "url", ["видео"] = "url",
        ["время"] = "minutes", ["минуты"] = "minutes", ["time"] = "minutes",
        ["миссия"] = "mission", ["mission"] = "mission",
        ["описание"] = "description", ["description"] = "description",
        ["повтор"] = "repeat", ["repeat"] = "repeat",
    };

    private static readonly Dictionary<string, ExerciseKind> ExerciseAliases = new()
    {
        ["пропуски"] = ExerciseKind.GapFill, ["пропуск"] = ExerciseKind.GapFill, ["gaps"] = ExerciseKind.GapFill, ["gap"] = ExerciseKind.GapFill,
        ["вопрос"] = ExerciseKind.OpenAnswer, ["ответ"] = ExerciseKind.OpenAnswer, ["question"] = ExerciseKind.OpenAnswer,
        ["тезисы"] = ExerciseKind.Theses, ["theses"] = ExerciseKind.Theses,
        ["произношение"] = ExerciseKind.Speaking, ["speaking"] = ExerciseKind.Speaking, ["pronunciation"] = ExerciseKind.Speaking,
        ["монолог"] = ExerciseKind.Monologue, ["голосовое"] = ExerciseKind.Monologue, ["monologue"] = ExerciseKind.Monologue,
    };

    private static readonly Dictionary<string, string> ExerciseFieldAliases = new()
    {
        ["текст"] = "transcript", ["transcript"] = "transcript",
        ["аудио"] = "audio", ["audio"] = "audio",
        ["подсказка"] = "instruction", ["instruction"] = "instruction",
        ["образец"] = "sample", ["sample"] = "sample",
    };

    [GeneratedRegex(@"(\d{1,2}(?::\d{1,2}){1,2})\s*[-–—]\s*(\d{1,2}(?::\d{1,2}){1,2})")]
    private static partial Regex SegmentPattern();

    [GeneratedRegex(@"^[-*•]\s+")]
    private static partial Regex BulletPattern();

    [GeneratedRegex(@"^([A-Za-zА-Яа-яЁё _]{2,20}):\s*(.*)$")]
    private static partial Regex FieldPattern();

    [GeneratedRegex(@"-?\d+")]
    private static partial Regex IntPattern();

    private const int DefaultMinutes = 10;

    private sealed class Builder
    {
        public readonly List<AssignmentPayload> Assignments = new();
        public readonly List<string> Warnings = new();
        public int Day;
        public AssignmentPayload? Assignment;
        public ExercisePayload? Exercise;
        public readonly List<string> ExerciseLines = new();

        public void OpenAssignment(string title)
        {
            CloseExercise();
            title = title.Trim();
            Assignment = new AssignmentPayload
            {
                DayIndex = Day,
                Title = title.Length == 0 ? "Без названия" : title.Length > 200 ? title[..200] : title,
                EstimatedMinutes = DefaultMinutes,
                Order = Assignments.Count(a => a.DayIndex == Day) + 1,
            };
            Assignments.Add(Assignment);
        }

        public AssignmentPayload RequireAssignment(string reason)
        {
            if (Assignment == null)
            {
                Warnings.Add($"{reason} — до первого «##», собрал в задание «Без названия».");
                OpenAssignment("Без названия");
            }
            return Assignment!;
        }

        public void OpenExercise(ExerciseKind kind, string heading)
        {
            CloseExercise();
            var assignment = RequireAssignment("Упражнение вне задания");
            var config = new ExerciseConfig();
            var segment = SegmentPattern().Match(heading);
            if (segment.Success)
            {
                config.SegmentStart = segment.Groups[1].Value;
                config.SegmentEnd = segment.Groups[2].Value;
            }
            Exercise = new ExercisePayload { Kind = kind, Config = config, Order = assignment.Exercises.Count + 1 };
            assignment.Exercises.Add(Exercise);
        }

        public void CloseExercise()
        {
            if (Exercise == null) return;
            var lines = ExerciseLines.Where(l => l.Trim().Length > 0).ToList();
            ExerciseLines.Clear();

            if (Exercise.Kind == ExerciseKind.Theses)
            {
                Exercise.Config.Reference = lines.Where(l => BulletPattern().IsMatch(l))
                    .Select(l => BulletPattern().Replace(l, "").Trim()).ToList();
                var prompt = string.Join(" ", lines.Where(l => !BulletPattern().IsMatch(l))).Trim();
                Exercise.Prompt = prompt.Length > 0 ? prompt : ThesesPrompt(Exercise.Config);
            }
            else
            {
                Exercise.Prompt = string.Join("\n", lines).Trim();
                if (Exercise.Prompt.Length == 0 && Exercise.Kind == ExerciseKind.Speaking && Exercise.Config.Transcript.Length > 0)
                    Exercise.Prompt = "Послушай и повтори за оригиналом.";
            }

            if (Exercise.Kind == ExerciseKind.GapFill && ExerciseText.ParseGaps(Exercise.Prompt).Blanks.Count == 0)
            {
                var head = Exercise.Prompt.Length > 40 ? Exercise.Prompt[..40] : Exercise.Prompt;
                Warnings.Add($"«{head}…» — в предложении нет ни одного {{{{пропуска}}}}, проверьте фигурные скобки.");
            }
            if (Exercise.Prompt.Length == 0)
            {
                Warnings.Add("Пустое упражнение пропущено.");
                Assignments[^1].Exercises.Remove(Exercise);
            }
            Exercise = null;
        }

        public bool SetField(string key, string value)
        {
            key = key.Trim().ToLowerInvariant();
            if (Exercise != null && ExerciseFieldAliases.TryGetValue(key, out var exField))
            {
                switch (exField)
                {
                    case "instruction": Exercise.Instruction = value.Length > 300 ? value[..300] : value; break;
                    case "audio": Exercise.AudioId = AsInt(value); break;
                    case "transcript": Exercise.Config.Transcript = value; break;
                    case "sample": Exercise.Config.SampleAnswer = value; break;
                }
                return true;
            }
            if (!FieldAliases.TryGetValue(key, out var field)) return false;

            var assignment = RequireAssignment($"Поле «{key}»");
            switch (field)
            {
                case "minutes":
                    assignment.EstimatedMinutes = AsInt(value) ?? DefaultMinutes;
                    break;
                case "repeat":
                    if (AsInt(value) is { } days and > 0)
                        assignment.Rules.Add(new RulePayload { Trigger = RuleTrigger.AssignmentDone, Action = RuleAction.RepeatInDays, Days = days });
                    break;
                case "description":
                    assignment.Description = assignment.Description.Length > 0 ? assignment.Description + "\n" + value : value;
                    break;
                case "url":
                    assignment.Url = value;
                    break;
                case "mission":
                    assignment.Mission = value;
                    if (value.Length > 0) assignment.MissionKind = MissionKind.Custom;
                    break;
            }
            return true;
        }

        public void AddText(string line)
        {
            if (Exercise != null)
            {
                ExerciseLines.Add(line);
                return;
            }
            var assignment = RequireAssignment("Текст");
            assignment.Description = (assignment.Description + "\n" + line).Trim();
        }
    }

    private static string ThesesPrompt(ExerciseConfig config) =>
        config.SegmentStart.Length > 0 && config.SegmentEnd.Length > 0
            ? $"Посмотрите отрезок {config.SegmentStart} — {config.SegmentEnd} и выпишите 3–5 тезисов."
            : "Выпишите 3–5 тезисов о том, что вы поняли.";

    private static int? AsInt(string value)
    {
        var m = IntPattern().Match(value ?? "");
        return m.Success && int.TryParse(m.Value, out var n) ? n : null;
    }

    public static WeekPayload Parse(string text)
    {
        var b = new Builder();
        foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Trim().Length == 0) continue;

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
            {
                var stripped = trimmed.TrimStart('#');
                var level = trimmed.Length - stripped.Length;
                var heading = stripped.Trim();
                if (level == 1)
                {
                    b.CloseExercise();
                    b.Assignment = null;
                    if (DayAliases.TryGetValue(heading.ToLowerInvariant().TrimEnd(':'), out var day)) b.Day = day;
                    else b.Warnings.Add($"Не понял день «{heading}», оставил предыдущий.");
                }
                else if (level == 2)
                {
                    b.OpenAssignment(heading);
                }
                else
                {
                    var first = heading.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? "";
                    if (!ExerciseAliases.TryGetValue(first, out var kind))
                    {
                        b.Warnings.Add($"Не понял тип упражнения «{heading}» — записал как вопрос.");
                        kind = ExerciseKind.OpenAnswer;
                    }
                    b.OpenExercise(kind, heading);
                }
                continue;
            }

            var match = FieldPattern().Match(line.Trim());
            if (match.Success && b.SetField(match.Groups[1].Value, match.Groups[2].Value.Trim())) continue;

            b.AddText(line.Trim());
        }
        b.CloseExercise();

        foreach (var ex in b.Assignments.SelectMany(a => a.Exercises))
        {
            if (ex.Config.SegmentStart.Length > 0 && ExerciseText.ParseTimecode(ex.Config.SegmentStart) == null)
            {
                b.Warnings.Add($"Не понял таймкод «{ex.Config.SegmentStart}» — убрал его.");
                ex.Config.SegmentStart = "";
            }
            if (ex.Config.SegmentEnd.Length > 0 && ExerciseText.ParseTimecode(ex.Config.SegmentEnd) == null)
            {
                b.Warnings.Add($"Не понял таймкод «{ex.Config.SegmentEnd}» — убрал его.");
                ex.Config.SegmentEnd = "";
            }
        }

        return new WeekPayload { Assignments = b.Assignments, Warnings = b.Warnings };
    }
}
