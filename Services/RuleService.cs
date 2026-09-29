using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using TutorSpace.Data;

namespace TutorSpace.Services;

/// <summary>
/// «Если ошибся — верни мне это через неделю». Rules are evaluated only in
/// reaction to something the student did, never on a timer.
/// </summary>
public static class RuleService
{
    /// <summary>Plants a copy of the assignment <paramref name="days"/> from now, creating the week if needed.</summary>
    public static Assignment ScheduleRepeat(AppDbContext db, Assignment source, int days)
    {
        var target = Clock.Today.AddDays(days);
        var monday = Clock.MondayOf(target);
        var enrollmentId = source.WeekPlan.EnrollmentId;

        var plan = db.WeekPlans.FirstOrDefault(w => w.EnrollmentId == enrollmentId && w.StartDate == monday);
        if (plan == null)
        {
            plan = new WeekPlan { EnrollmentId = enrollmentId, StartDate = monday, Title = "Повторы", IsPublished = true };
            db.WeekPlans.Add(plan);
            db.SaveChanges();
        }

        var repeat = new Assignment
        {
            WeekPlanId = plan.Id,
            DayIndex = Clock.DayIndexOf(target),
            RepeatOfId = source.Id,
            Title = source.Title,
            Description = source.Description,
            Url = source.Url,
            Mission = source.Mission,
            MissionKind = source.MissionKind,
            EstimatedMinutes = source.EstimatedMinutes,
            Provider = source.Provider,
            ExternalId = source.ExternalId,
            PreviewTitle = source.PreviewTitle,
            PreviewThumbnail = source.PreviewThumbnail,
        };
        foreach (var ex in source.Exercises)
        {
            repeat.Exercises.Add(new Exercise
            {
                Kind = ex.Kind, Prompt = ex.Prompt, Instruction = ex.Instruction,
                ConfigJson = ex.ConfigJson, AudioId = ex.AudioId, Order = ex.Order,
            });
        }
        db.Assignments.Add(repeat);
        db.SaveChanges();
        return repeat;
    }

    private static void Apply(AppDbContext db, AssignmentRule rule, Assignment assignment, string detail)
    {
        int? created = null;
        if (rule.Action == RuleAction.RepeatInDays)
        {
            created = ScheduleRepeat(db, assignment, rule.Days).Id;
            detail += $" → повтор через {rule.Days} дн.";
        }
        else if (rule.Note.Length > 0)
        {
            detail += " → " + rule.Note;
        }
        var fires = rule.Fires;
        fires.Add(new RuleFire
        {
            Id = fires.Count == 0 ? 1 : fires.Max(f => f.Id) + 1,
            CreatedAssignmentId = created,
            Detail = detail.Length > 300 ? detail[..300] : detail,
        });
        rule.Fires = fires;
        db.SaveChanges();
    }

    private static IQueryable<Assignment> WithEverything(AppDbContext db) => db.Assignments
        .Include(a => a.WeekPlan)
        .Include(a => a.Exercises)
        .Include(a => a.Rules);

    public static void EvaluateForAttempt(int attemptId)
    {
        using var db = new AppDbContext();
        var attempt = db.Attempts.Include(a => a.Exercise).First(a => a.Id == attemptId);
        var assignment = WithEverything(db).First(a => a.Id == attempt.Exercise.AssignmentId);

        foreach (var rule in assignment.Rules.Where(r => r.IsActive).ToList())
        {
            bool matched;
            if (rule.Trigger == RuleTrigger.ExerciseFailed) matched = attempt.Percent < rule.ThresholdPercent;
            else if (rule.Trigger == RuleTrigger.ExercisePassed) matched = attempt.Percent >= rule.ThresholdPercent;
            else continue;
            if (!matched || (rule.FireOnce && rule.Fires.Count > 0)) continue;

            var verdict = attempt.IsPassed ? "справился" : $"{attempt.Percent}%";
            Apply(db, rule, assignment, $"«{assignment.Title}», упражнение: {verdict}");
        }
    }

    public static void EvaluateForCompletion(int assignmentId, int studentId)
    {
        using var db = new AppDbContext();
        var assignment = WithEverything(db).First(a => a.Id == assignmentId);
        foreach (var rule in assignment.Rules.Where(r => r.IsActive && r.Trigger == RuleTrigger.AssignmentDone).ToList())
        {
            if (rule.FireOnce && rule.Fires.Count > 0) continue;
            Apply(db, rule, assignment, $"«{assignment.Title}» закрыто");
        }
    }

    /// <summary>A broken rule must not eat the student's answer.</summary>
    public static void SafeEvaluate(Action evaluate)
    {
        try { evaluate(); }
        catch (Exception ex) { Debug.WriteLine("Rule evaluation failed: " + ex); }
    }

    /// <summary>Every log entry of the student's rules, newest first, with the rule and the created task filled in.</summary>
    public static List<RuleFire> FiresFor(int enrollmentId)
    {
        using var db = new AppDbContext();
        var rules = db.Rules.AsNoTracking()
            .Include(r => r.Assignment).ThenInclude(a => a.WeekPlan)
            .Where(r => r.Assignment.WeekPlan.EnrollmentId == enrollmentId && r.FiresJson != "[]").ToList();
        var fires = rules.SelectMany(r => r.Fires.Select(f => { f.Rule = r; return f; })).ToList();

        var createdIds = fires.Where(f => f.CreatedAssignmentId != null).Select(f => f.CreatedAssignmentId!.Value).ToList();
        var created = db.Assignments.AsNoTracking().Include(a => a.WeekPlan)
            .Where(a => createdIds.Contains(a.Id)).ToDictionary(a => a.Id);
        foreach (var f in fires)
            f.CreatedAssignment = f.CreatedAssignmentId is { } id ? created.GetValueOrDefault(id) : null;
        return fires.OrderByDescending(f => f.CreatedAt).ToList();
    }

    public static int MarkSeen(int enrollmentId)
    {
        using var db = new AppDbContext();
        var rules = db.Rules.Where(r => r.Assignment.WeekPlan.EnrollmentId == enrollmentId && r.FiresJson != "[]").ToList();
        var count = 0;
        foreach (var rule in rules)
        {
            var fires = rule.Fires;
            count += fires.Count(f => !f.IsSeen);
            foreach (var f in fires) f.IsSeen = true;
            rule.Fires = fires;
        }
        db.SaveChanges();
        return count;
    }
}
