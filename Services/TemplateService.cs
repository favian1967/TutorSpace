using Microsoft.EntityFrameworkCore;
using TutorSpace.Data;

namespace TutorSpace.Services;

// A week as plain data. Templates, week copies and the text importer all
// produce this shape and hand it to TemplateService.Materialize.

public class WeekPayload
{
    public int Version { get; set; } = 1;
    public string Title { get; set; } = "";
    public string TutorNotes { get; set; } = "";
    public bool IntroRequired { get; set; }
    public List<AssignmentPayload> Assignments { get; set; } = new();
    public List<string> CallQuestions { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class AssignmentPayload
{
    public int DayIndex { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Url { get; set; } = "";
    public string Mission { get; set; } = "";
    public MissionKind MissionKind { get; set; } = MissionKind.Sentence;
    public int EstimatedMinutes { get; set; } = 10;
    public int Order { get; set; }
    public List<ExercisePayload> Exercises { get; set; } = new();
    public List<RulePayload> Rules { get; set; } = new();
}

public class ExercisePayload
{
    public ExerciseKind Kind { get; set; }
    public string Prompt { get; set; } = "";
    public string Instruction { get; set; } = "";
    public ExerciseConfig Config { get; set; } = new();
    public int? AudioId { get; set; }
    public int Order { get; set; }
}

public class RulePayload
{
    public RuleTrigger Trigger { get; set; }
    public RuleAction Action { get; set; }
    public int ThresholdPercent { get; set; } = 100;
    public int Days { get; set; } = 7;
    public string Note { get; set; } = "";
    public bool FireOnce { get; set; } = true;
}

/// <summary>Saving a week and putting it back down somewhere else.</summary>
public static class TemplateService
{
    public static WeekPayload Snapshot(int planId)
    {
        using var db = new AppDbContext();
        var plan = PlannerService.FullPlans(db).AsNoTracking().First(w => w.Id == planId);
        return new WeekPayload
        {
            Title = plan.Title,
            TutorNotes = plan.TutorNotes,
            IntroRequired = plan.IntroRequired,
            Assignments = plan.Assignments.OrderBy(a => a.DayIndex).ThenBy(a => a.Order).Select(a => new AssignmentPayload
            {
                DayIndex = a.DayIndex,
                Title = a.Title,
                Description = a.Description,
                Url = a.Url,
                Mission = a.Mission,
                MissionKind = a.MissionKind,
                EstimatedMinutes = a.EstimatedMinutes,
                Order = a.Order,
                Exercises = a.Exercises.OrderBy(x => x.Order).Select(x => new ExercisePayload
                {
                    Kind = x.Kind, Prompt = x.Prompt, Instruction = x.Instruction,
                    Config = x.Config, AudioId = x.AudioId, Order = x.Order,
                }).ToList(),
                Rules = a.Rules.Where(r => r.IsActive).Select(r => new RulePayload
                {
                    Trigger = r.Trigger, Action = r.Action, ThresholdPercent = r.ThresholdPercent,
                    Days = r.Days, Note = r.Note, FireOnce = r.FireOnce,
                }).ToList(),
            }).ToList(),
            CallQuestions = plan.CallQuestions.OrderBy(q => q.Order).Select(q => q.Text).ToList(),
        };
    }

    // ---------- Templates: weeks without a student ----------

    /// <summary>Templates have no real date; they all sit on this Monday.</summary>
    private static readonly DateOnly TemplateMonday = new(2000, 1, 3);

    public static void SaveAsTemplate(int tutorId, int planId, string name, string description) =>
        SaveTemplate(tutorId, name, description, Snapshot(planId));

    /// <summary>Creates the template, or replaces the one with the same name.</summary>
    public static WeekPlan SaveTemplate(int tutorId, string name, string description, WeekPayload payload)
    {
        name = name.Trim();
        if (name.Length == 0) throw new UserError("Нужно название шаблона.");
        if (name.Length > 120) name = name[..120];

        using var db = new AppDbContext();
        using var tx = db.Database.BeginTransaction();
        var template = db.WeekPlans.Include(w => w.Assignments)
            .FirstOrDefault(w => w.IsTemplate && w.TutorId == tutorId && w.Name == name);
        if (template == null)
        {
            template = new WeekPlan { IsTemplate = true, TutorId = tutorId, Name = name, StartDate = TemplateMonday, IsPublished = false };
            db.WeekPlans.Add(template);
        }
        else
            db.Assignments.RemoveRange(template.Assignments);
        template.Description = description ?? "";
        template.Title = payload.Title;
        template.TutorNotes = payload.TutorNotes;
        template.IntroRequired = payload.IntroRequired;
        template.CallQuestions = new();
        template.UpdatedAt = DateTime.Now;
        db.SaveChanges();

        Fill(db, template, payload, tutorId);
        tx.Commit();
        return template;
    }

    public static List<WeekPlan> TemplatesOf(int tutorId)
    {
        using var db = new AppDbContext();
        return db.WeekPlans.AsNoTracking().Include(w => w.Assignments).ThenInclude(a => a.Exercises)
            .Where(w => w.IsTemplate && w.TutorId == tutorId)
            .OrderByDescending(w => w.UpdatedAt).ToList();
    }

    public static void RenameTemplate(int templateId, string name, string description)
    {
        name = name.Trim();
        if (name.Length == 0) throw new UserError("Нужно название.");
        using var db = new AppDbContext();
        var t = db.WeekPlans.FirstOrDefault(w => w.Id == templateId && w.IsTemplate) ?? throw new UserError("Шаблон не найден.");
        if (db.WeekPlans.Any(w => w.IsTemplate && w.TutorId == t.TutorId && w.Name == name && w.Id != t.Id))
            throw new UserError("Шаблон с таким названием уже есть.");
        t.Name = name;
        t.Description = description ?? "";
        t.UpdatedAt = DateTime.Now;
        db.SaveChanges();
    }

    public static void DeleteTemplate(int templateId)
    {
        using var db = new AppDbContext();
        var t = db.WeekPlans.FirstOrDefault(w => w.Id == templateId && w.IsTemplate);
        if (t == null) return;
        db.WeekPlans.Remove(t);
        db.SaveChanges();
    }

    public static bool HasPlan(int enrollmentId, DateOnly monday)
    {
        using var db = new AppDbContext();
        return db.WeekPlans.Any(w => w.EnrollmentId == enrollmentId && w.StartDate == monday && w.Assignments.Any());
    }

    /// <summary>
    /// Creates the student's week described by <paramref name="payload"/>.
    /// <paramref name="replace"/> empties an existing week instead of refusing.
    /// </summary>
    public static WeekPlan Materialize(int tutorId, int enrollmentId, DateOnly monday, WeekPayload payload, bool replace)
    {
        if (Clock.DayIndexOf(monday) != 0) throw new UserError("Неделя всегда начинается с понедельника.");

        using var db = new AppDbContext();
        using var tx = db.Database.BeginTransaction();
        var enrollment = db.Enrollments.Find(enrollmentId) ?? throw new UserError("Ученик не найден.");
        if (enrollment.TutorId != tutorId) throw new UserError("Это не ваш ученик.");

        var plan = db.WeekPlans.Include(w => w.Assignments)
            .FirstOrDefault(w => w.EnrollmentId == enrollmentId && w.StartDate == monday);
        if (plan == null)
        {
            plan = new WeekPlan
            {
                EnrollmentId = enrollmentId, StartDate = monday, Title = payload.Title,
                TutorNotes = payload.TutorNotes, IntroRequired = payload.IntroRequired,
            };
            db.WeekPlans.Add(plan);
        }
        else if (plan.Assignments.Count == 0 || replace)
        {
            db.Assignments.RemoveRange(plan.Assignments);
            plan.CallQuestions = new();
            if (payload.Title.Length > 0) plan.Title = payload.Title;
        }
        else
        {
            throw new UserError($"На неделю с {monday:dd.MM.yyyy} у этого ученика уже есть план.");
        }
        db.SaveChanges();

        Fill(db, plan, payload, tutorId);
        tx.Commit();
        return plan;
    }

    /// <summary>Plants the payload's tasks, exercises, rules and call questions into an empty week.</summary>
    private static void Fill(AppDbContext db, WeekPlan plan, WeekPayload payload, int tutorId)
    {
        var ownedAudio = db.Media.Where(m => m.OwnerId == tutorId).Select(m => m.Id).ToHashSet();

        foreach (var raw in payload.Assignments ?? new())
        {
            string url;
            try { url = ResourceResolver.Normalize(raw.Url); }
            catch (UserError) { url = ""; }

            var assignment = new Assignment
            {
                WeekPlanId = plan.Id,
                DayIndex = Math.Clamp(raw.DayIndex, 0, WeekPlan.DaysInWeek - 1),
                Title = string.IsNullOrWhiteSpace(raw.Title) ? "Без названия" : Cut(raw.Title.Trim(), 200),
                Url = url,
                Description = Cut(raw.Description ?? "", 20000),
                Mission = Cut(raw.Mission ?? "", 300),
                MissionKind = Enum.IsDefined(raw.MissionKind) ? raw.MissionKind : MissionKind.Sentence,
                EstimatedMinutes = Math.Clamp(raw.EstimatedMinutes, 0, 600),
                Order = Math.Clamp(raw.Order, 0, 999),
            };
            ResourceResolver.Apply(assignment);

            foreach (var ex in raw.Exercises ?? new())
            {
                // A block nothing can render or check is dropped rather than planted.
                try { ExerciseService.Validate(ex.Kind, ex.Prompt ?? "", ex.Config); }
                catch (UserError) { continue; }
                assignment.Exercises.Add(new Exercise
                {
                    Kind = ex.Kind,
                    Prompt = ex.Prompt!,
                    Instruction = Cut(ex.Instruction ?? "", 300),
                    Config = ExerciseService.BuildConfig(ex.Kind, ex.Prompt!, ex.Config ?? new ExerciseConfig()),
                    AudioId = ex.AudioId is { } id && ownedAudio.Contains(id) ? id : null,
                    Order = ex.Order,
                });
            }
            foreach (var r in (raw.Rules ?? new()).Where(r => Enum.IsDefined(r.Trigger) && Enum.IsDefined(r.Action)))
            {
                assignment.Rules.Add(new AssignmentRule
                {
                    Trigger = r.Trigger, Action = r.Action,
                    ThresholdPercent = Math.Clamp(r.ThresholdPercent, 0, 100),
                    Days = Math.Max(1, r.Days), Note = Cut(r.Note ?? "", 300), FireOnce = r.FireOnce,
                });
            }
            db.Assignments.Add(assignment);
        }

        var n = 0;
        plan.CallQuestions = (payload.CallQuestions ?? new()).Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => { n++; return new CallQuestion { Id = n, Text = Cut(t.Trim(), 2000), Order = n }; }).ToList();

        db.SaveChanges();
    }

    public static void ApplyTemplate(int tutorId, int templateId, int enrollmentId, DateOnly monday, bool replace)
    {
        using (var db = new AppDbContext())
            if (!db.WeekPlans.Any(w => w.Id == templateId && w.IsTemplate)) throw new UserError("Шаблон не найден.");

        Materialize(tutorId, enrollmentId, monday, Snapshot(templateId), replace);

        using var db2 = new AppDbContext();
        var t = db2.WeekPlans.Find(templateId)!;
        t.TimesUsed++;
        db2.SaveChanges();
    }

    /// <summary>Duplicates a week onto another student, or onto another week of the same one.</summary>
    public static void CopyWeek(int tutorId, int sourcePlanId, int enrollmentId, DateOnly monday, bool replace) =>
        Materialize(tutorId, enrollmentId, monday, Snapshot(sourcePlanId), replace);

    private static string Cut(string value, int limit) => value.Length > limit ? value[..limit] : value;
}
