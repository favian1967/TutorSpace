using Microsoft.EntityFrameworkCore;
using TutorSpace.Data;

namespace TutorSpace.Services;

/// <summary>Week plans, assignments and the tutor's bulk edits.</summary>
public static class PlannerService
{
    public static readonly string[] DayNames = { "Понедельник", "Вторник", "Среда", "Четверг", "Пятница", "Суббота", "Воскресенье" };
    public static readonly string[] DayShort = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };

    /// <summary>Loads a week with everything the editors and players need.</summary>
    public static IQueryable<WeekPlan> FullPlans(AppDbContext db) => db.WeekPlans
        .Include(w => w.Enrollment!).ThenInclude(e => e.Student)
        .Include(w => w.Assignments).ThenInclude(a => a.Exercises).ThenInclude(x => x.Audio)
        .Include(w => w.Assignments).ThenInclude(a => a.Rules)
        .AsSplitQuery();

    public static WeekPlan? GetPlan(int enrollmentId, DateOnly monday)
    {
        using var db = new AppDbContext();
        return FullPlans(db).AsNoTracking().FirstOrDefault(w => w.EnrollmentId == enrollmentId && w.StartDate == monday);
    }

    public static WeekPlan? GetPlanById(int planId)
    {
        using var db = new AppDbContext();
        return FullPlans(db).AsNoTracking().FirstOrDefault(w => w.Id == planId);
    }

    /// <summary>The student's weeks starting on <paramref name="monday"/>, one per active tutor.</summary>
    public static List<WeekPlan> StudentPlans(int studentId, DateOnly monday)
    {
        using var db = new AppDbContext();
        return FullPlans(db).AsNoTracking()
            .Include(w => w.Enrollment!).ThenInclude(e => e.Tutor)
            .Where(w => w.Enrollment!.StudentId == studentId && w.Enrollment.IsActive && w.StartDate == monday)
            .ToList();
    }

    public static List<WeekPlan> PlansOf(int enrollmentId)
    {
        using var db = new AppDbContext();
        return db.WeekPlans.AsNoTracking().Include(w => w.Assignments)
            .Where(w => w.EnrollmentId == enrollmentId)
            .OrderByDescending(w => w.StartDate).ToList();
    }

    public static WeekPlan CreatePlan(int enrollmentId, DateOnly monday, string title = "")
    {
        if (Clock.DayIndexOf(monday) != 0) throw new UserError("Неделя всегда начинается с понедельника.");
        using var db = new AppDbContext();
        if (db.WeekPlans.Any(w => w.EnrollmentId == enrollmentId && w.StartDate == monday))
            throw new UserError("На эту неделю план уже есть.");
        var plan = new WeekPlan { EnrollmentId = enrollmentId, StartDate = monday, Title = title };
        db.WeekPlans.Add(plan);
        db.SaveChanges();
        return plan;
    }

    public static void UpdatePlan(int planId, string title, string notes, bool published, DateTime? reviewAt, bool introRequired)
    {
        using var db = new AppDbContext();
        var plan = db.WeekPlans.Find(planId) ?? throw new UserError("План не найден.");
        plan.Title = title.Trim();
        plan.TutorNotes = notes;
        plan.IsPublished = published;
        plan.ReviewAt = reviewAt;
        plan.IntroRequired = introRequired;
        plan.UpdatedAt = DateTime.Now;
        db.SaveChanges();
    }

    public static void DeletePlan(int planId)
    {
        using var db = new AppDbContext();
        var plan = db.WeekPlans.Find(planId);
        if (plan == null) return;
        db.WeekPlans.Remove(plan);
        db.SaveChanges();
    }

    // ---------- Assignments ----------

    public static Assignment? GetAssignment(int id)
    {
        using var db = new AppDbContext();
        return db.Assignments.AsNoTracking()
            .Include(a => a.WeekPlan)
            .Include(a => a.Exercises).ThenInclude(x => x.Audio)
            .Include(a => a.Rules)
            .AsSplitQuery()
            .FirstOrDefault(a => a.Id == id);
    }

    /// <summary>Creates or updates the assignment row (exercises and rules are edited separately).</summary>
    public static Assignment SaveAssignment(Assignment data)
    {
        if (string.IsNullOrWhiteSpace(data.Title)) throw new UserError("Нужен заголовок задания.");
        if (data.Title.Trim().Length > 200) throw new UserError("Заголовок — не больше 200 символов.");
        if (data.Mission.Trim().Length > 300) throw new UserError("Миссия — не больше 300 символов.");
        if (data.Description.Length > 20000) throw new UserError("Описание — не больше 20 000 символов.");
        if (data.DayIndex is < 0 or >= WeekPlan.DaysInWeek) throw new UserError("Такого дня в неделе нет.");
        if (!Enum.IsDefined(data.MissionKind)) data.MissionKind = MissionKind.Sentence;
        var url = ResourceResolver.Normalize(data.Url);

        using var db = new AppDbContext();
        if (!db.WeekPlans.Any(w => w.Id == data.WeekPlanId)) throw new UserError("План недели не найден — возможно, его удалили.");
        Assignment target;
        if (data.Id == 0)
        {
            target = new Assignment { WeekPlanId = data.WeekPlanId };
            var siblings = db.Assignments.Where(a => a.WeekPlanId == data.WeekPlanId && a.DayIndex == data.DayIndex);
            target.Order = (siblings.Max(a => (int?)a.Order) ?? 0) + 1;
            db.Assignments.Add(target);
        }
        else
        {
            target = db.Assignments.Find(data.Id) ?? throw new UserError("Задание не найдено.");
            if (target.DayIndex != data.DayIndex) target.DeferredDays = 0;
        }

        var urlChanged = target.Url != url;
        target.DayIndex = data.DayIndex;
        target.Title = data.Title.Trim();
        target.Description = data.Description;
        target.Url = url;
        target.MissionKind = data.MissionKind;
        target.Mission = data.Mission.Trim();
        target.EstimatedMinutes = Math.Clamp(data.EstimatedMinutes, 0, 600);
        target.UpdatedAt = DateTime.Now;
        if (urlChanged || data.Id == 0) ResourceResolver.Apply(target);

        db.SaveChanges();
        return target;
    }

    public static void DeleteAssignments(int planId, IEnumerable<int> ids)
    {
        var set = ids.ToHashSet();
        using var db = new AppDbContext();
        var rows = db.Assignments.Where(a => a.WeekPlanId == planId && set.Contains(a.Id)).ToList();
        if (rows.Count != set.Count) throw new UserError("Часть заданий уже удалена или относится к другой неделе — ничего не менял. Обновите экран.");
        db.Assignments.RemoveRange(rows);
        db.SaveChanges();
    }

    public static int MoveAssignments(int planId, IEnumerable<int> ids, int dayIndex)
    {
        if (dayIndex is < 0 or >= WeekPlan.DaysInWeek) throw new UserError("Такого дня в неделе нет.");
        var set = ids.ToHashSet();
        using var db = new AppDbContext();
        var rows = db.Assignments.Where(a => a.WeekPlanId == planId && set.Contains(a.Id)).ToList();
        if (rows.Count != set.Count) throw new UserError("Часть заданий уже удалена или относится к другой неделе — ничего не менял. Обновите экран.");
        foreach (var row in rows)
        {
            row.DayIndex = dayIndex;
            row.DeferredDays = 0;
            row.UpdatedAt = DateTime.Now;
        }
        db.SaveChanges();
        return rows.Count;
    }

    /// <summary>
    /// Moves every assignment <paramref name="delta"/> days. Refuses when a task would leave the week:
    /// squeezing it onto the edge day would make «later, then earlier» silently change the schedule.
    /// </summary>
    public static int ShiftWeek(int planId, int delta)
    {
        if (delta == 0) return 0;
        using var db = new AppDbContext();
        var rows = db.Assignments.Where(a => a.WeekPlanId == planId).ToList();
        if (rows.FirstOrDefault(a => a.DayIndex + delta is < 0 or >= WeekPlan.DaysInWeek) is { } edge)
            throw new UserError($"Нельзя сдвинуть: задание «{edge.Title}» ({DayNames[edge.DayIndex].ToLower()}) выйдет за пределы недели. Перенесите его отдельно.");
        foreach (var a in rows)
        {
            a.DayIndex += delta;
            a.UpdatedAt = DateTime.Now;
        }
        db.SaveChanges();
        return rows.Count;
    }

    // ---------- Call questions ----------

    public static void AddCallQuestion(int planId, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Trim().Length > 2000) throw new UserError("Вопрос слишком длинный.");
        using var db = new AppDbContext();
        var plan = db.WeekPlans.Find(planId) ?? throw new UserError("План недели не найден.");
        var questions = plan.CallQuestions;
        questions.Add(new CallQuestion
        {
            Id = questions.Count == 0 ? 1 : questions.Max(q => q.Id) + 1,
            Text = text.Trim(),
            Order = questions.Count == 0 ? 1 : questions.Max(q => q.Order) + 1,
        });
        plan.CallQuestions = questions;
        db.SaveChanges();
    }

    public static void SetQuestionAsked(int planId, int questionId, bool asked) =>
        ChangeQuestions(planId, list => { if (list.FirstOrDefault(q => q.Id == questionId) is { } q) q.IsAsked = asked; });

    public static void DeleteCallQuestion(int planId, int questionId) =>
        ChangeQuestions(planId, list => list.RemoveAll(q => q.Id == questionId));

    private static void ChangeQuestions(int planId, Action<List<CallQuestion>> change)
    {
        using var db = new AppDbContext();
        var plan = db.WeekPlans.Find(planId);
        if (plan == null) return;
        var questions = plan.CallQuestions;
        change(questions);
        plan.CallQuestions = questions;
        db.SaveChanges();
    }

    // ---------- Rules ----------

    public static void SaveRule(AssignmentRule data)
    {
        using var db = new AppDbContext();
        AssignmentRule rule;
        if (!Enum.IsDefined(data.Trigger) || !Enum.IsDefined(data.Action)) throw new UserError("Неизвестный тип правила.");
        if (data.Id == 0)
        {
            if (!db.Assignments.Any(a => a.Id == data.AssignmentId)) throw new UserError("Задание не найдено — возможно, его удалили.");
            rule = new AssignmentRule { AssignmentId = data.AssignmentId };
            db.Rules.Add(rule);
        }
        else
        {
            rule = db.Rules.Find(data.Id) ?? throw new UserError("Правило не найдено.");
        }
        rule.Trigger = data.Trigger;
        rule.Action = data.Action;
        rule.ThresholdPercent = Math.Clamp(data.ThresholdPercent, 0, 100);
        rule.Days = Math.Max(1, data.Days);
        rule.Note = data.Note.Trim().Length > 300 ? data.Note.Trim()[..300] : data.Note.Trim();
        rule.IsActive = data.IsActive;
        rule.FireOnce = data.FireOnce;
        db.SaveChanges();
    }

    public static void DeleteRule(int ruleId)
    {
        using var db = new AppDbContext();
        var rule = db.Rules.Find(ruleId);
        if (rule == null) return;
        db.Rules.Remove(rule);
        db.SaveChanges();
    }

    // ---------- Labels ----------

    public static string Label(MissionKind kind) => kind switch
    {
        MissionKind.Words => "Поймать новые слова",
        MissionKind.Sentence => "Ответить одним предложением",
        MissionKind.Voice => "Записать голосовое",
        MissionKind.Retell => "Пересказать своими словами",
        _ => "Своя формулировка",
    };

    public static string DefaultMission(MissionKind kind) => kind switch
    {
        MissionKind.Words => "Поймай 3 новых слова и запиши их",
        MissionKind.Sentence => "Ответь одним предложением: о чём это было?",
        MissionKind.Voice => "Запиши голосовое на 15 секунд",
        MissionKind.Retell => "Перескажи своими словами в 2–3 предложениях",
        _ => "",
    };

    public static string Label(ExerciseKind kind) => kind switch
    {
        ExerciseKind.GapFill => "Вставить пропущенные слова",
        ExerciseKind.OpenAnswer => "Ответить на вопрос",
        ExerciseKind.Theses => "Тезисы по отрезку видео",
        ExerciseKind.Speaking => "Повторить за оригиналом",
        _ => "Голосовой монолог",
    };

    public static string Label(RuleTrigger trigger) => trigger switch
    {
        RuleTrigger.ExerciseFailed => "Ошибся в упражнении",
        RuleTrigger.ExercisePassed => "Справился с упражнением",
        _ => "Закрыл задание",
    };

    public static string Label(RuleAction action) => action switch
    {
        RuleAction.RepeatInDays => "Повторить задание через N дней",
        _ => "Отметить преподавателю",
    };

    public static string Describe(AssignmentRule rule)
    {
        var trigger = Label(rule.Trigger);
        if (rule.Trigger != RuleTrigger.AssignmentDone)
            trigger += rule.Trigger == RuleTrigger.ExerciseFailed ? $" (< {rule.ThresholdPercent}%)" : $" (≥ {rule.ThresholdPercent}%)";
        var action = rule.Action == RuleAction.RepeatInDays ? $"повтор через {rule.Days} дн." : "отметить мне" + (rule.Note.Length > 0 ? $": {rule.Note}" : "");
        return $"{trigger} → {action}{(rule.FireOnce ? " · один раз" : "")}{(rule.IsActive ? "" : " · выключено")}";
    }

    public static string Label(LessonVerdict v) => v == LessonVerdict.Good ? "Хорошо" : "Плохо";

    public static string Label(LessonComparison c) => c switch
    {
        LessonComparison.Better => "Лучше предыдущего",
        LessonComparison.Same => "Такой же",
        _ => "Хуже предыдущего",
    };

    public static string Label(LanguageLevel l) => l switch
    {
        LanguageLevel.Beginner => "Новичок",
        LanguageLevel.Intermediate => "Средний",
        _ => "Уверенный",
    };

    public static readonly (string Key, string Label)[] Interests =
    {
        ("FOOTBALL", "Футбол"), ("GAMES", "Игры"), ("MOVIES", "Фильмы"),
        ("CARTOONS", "Мультики"), ("NEWS", "Новости"), ("IT", "IT"),
    };

    public static string WeekLabel(DateOnly monday) =>
        $"{monday:dd.MM} — {monday.AddDays(6):dd.MM.yyyy}";
}
