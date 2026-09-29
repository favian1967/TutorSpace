using Microsoft.EntityFrameworkCore;
using TutorSpace.Data;

namespace TutorSpace.Services;

/// <summary>Completion, the streak and the "freeze" that postpones today's lesson.</summary>
public static class ProgressService
{
    public const int FreezesPerWeek = 1;

    // ---------- Profile & streak ----------

    public static StudentProfile GetProfile(int userId)
    {
        using var db = new AppDbContext();
        return GetProfile(db, userId);
    }

    /// <summary>The student's profile columns; tracked by <paramref name="db"/>, so changes save with it.</summary>
    public static StudentProfile GetProfile(AppDbContext db, int userId) =>
        (db.Users.Find(userId) ?? throw new UserError("Пользователь не найден.")).Profile;

    public static void CompleteOnboarding(int userId, IEnumerable<string> interests, LanguageLevel level)
    {
        using var db = new AppDbContext();
        var profile = GetProfile(db, userId);
        profile.Interests = string.Join(",", interests);
        profile.Level = level;
        profile.OnboardedAt = DateTime.Now;
        db.SaveChanges();
    }

    /// <summary>Completing anything on a day extends the streak (once per day).</summary>
    public static void RegisterActivity(StudentProfile profile, DateOnly today)
    {
        if (profile.LastActiveDate == today) return;
        var continued = profile.LastActiveDate == today.AddDays(-1);
        profile.CurrentStreak = continued ? profile.CurrentStreak + 1 : 1;
        profile.LongestStreak = Math.Max(profile.LongestStreak, profile.CurrentStreak);
        profile.LastActiveDate = today;
    }

    /// <summary>A postponed lesson keeps the chain alive without growing it.</summary>
    public static void HoldStreak(StudentProfile profile, DateOnly today)
    {
        if (profile.LastActiveDate is { } last && last >= today.AddDays(-1))
            profile.LastActiveDate = today;
        profile.LastFrozenDate = today;
    }

    public record StreakInfo(int Current, int Longest, bool ActiveToday);

    /// <summary>A student who stopped three days ago must see 0, not their stale record.</summary>
    public static StreakInfo Streak(int userId, DateOnly today)
    {
        var profile = GetProfile(userId);
        var alive = profile.LastActiveDate is { } last && last >= today.AddDays(-1);
        return new StreakInfo(alive ? profile.CurrentStreak : 0, profile.LongestStreak, profile.LastActiveDate == today);
    }

    // ---------- Progress ----------

    /// <summary>
    /// Records completion / comment / mission answer. Completion is gated on the
    /// micro-mission: with a mission and no exercises, an answer is required.
    /// </summary>
    public static void SetProgress(int studentId, int assignmentId, bool isCompleted, string missionResponse, string comment)
    {
        var today = Clock.Today;
        using var db = new AppDbContext();
        var assignment = db.Assignments
            .Include(a => a.WeekPlan).ThenInclude(w => w.Enrollment)
            .Include(a => a.Exercises)
            .FirstOrDefault(a => a.Id == assignmentId) ?? throw new UserError("Задание не найдено.");

        if (assignment.WeekPlan.Enrollment?.StudentId != studentId) throw new UserError("Это не ваше задание.");
        if (!assignment.IsUnlockedFor(today)) throw new UserError("Это задание ещё не открылось.");

        var progress = assignment.Progress;
        var wasCompleted = progress.IsCompleted;

        if ((missionResponse ?? "").Length > 20000 || (comment ?? "").Length > 20000)
            throw new UserError("Слишком длинный текст.");
        progress.IsCompleted = isCompleted;
        progress.MissionResponse = missionResponse ?? "";
        progress.StudentComment = comment ?? "";
        progress.UpdatedAt = DateTime.Now;

        if (progress.IsCompleted && assignment.Mission.Length > 0 && assignment.Exercises.Count == 0
            && string.IsNullOrWhiteSpace(progress.MissionResponse))
            throw new UserError("Чтобы закрыть задание, сдайте ответ на миссию: " + assignment.Mission);

        if (progress.IsCompleted && !wasCompleted) progress.CompletedAt = DateTime.Now;
        else if (!progress.IsCompleted && wasCompleted) progress.CompletedAt = null;

        if (progress.IsCompleted && !wasCompleted)
            RegisterActivity(GetProfile(db, studentId), today);

        db.SaveChanges();

        if (progress.IsCompleted && !wasCompleted)
            RuleService.SafeEvaluate(() => RuleService.EvaluateForCompletion(assignmentId, studentId));
    }

    // ---------- Freeze ----------

    public static (int Total, int Used) FreezeBudget(WeekPlan plan)
    {
        var used = plan.Assignments.Where(a => a.DeferredDays > 0).Select(a => a.DayIndex).Distinct().Count();
        return (FreezesPerWeek, used);
    }

    /// <summary>
    /// Moves every assignment of a day one day later. Only today's lesson, only by
    /// one day, never Sunday, and the budget refills each week.
    /// </summary>
    public static void DeferDay(int studentId, int planId, int dayIndex)
    {
        var today = Clock.Today;
        if (dayIndex >= WeekPlan.DaysInWeek - 1)
            throw new UserError("Воскресный урок переносить некуда — неделя не удлиняется.");

        using var db = new AppDbContext();
        var plan = db.WeekPlans.Include(w => w.Enrollment).Include(w => w.Assignments)
            .FirstOrDefault(w => w.Id == planId) ?? throw new UserError("План не найден.");
        if (plan.Enrollment?.StudentId != studentId) throw new UserError("Это не ваш план.");

        var assignments = plan.Assignments.Where(a => a.DayIndex == dayIndex).ToList();
        if (assignments.Count == 0) throw new UserError("В этот день нет заданий.");
        if (assignments.Any(a => a.DeferredDays > 0))
            throw new UserError("Этот урок уже перенесён. Больше чем на день отложить нельзя.");
        if (assignments[0].ReleaseDate != today) throw new UserError("Отложить можно только сегодняшний урок.");
        if (assignments.Any(a => a.Progress?.IsCompleted == true)) throw new UserError("Урок уже выполнен — переносить нечего.");

        var (total, used) = FreezeBudget(plan);
        if (total - used <= 0) throw new UserError("Заморозки на эту неделю закончились.");

        foreach (var a in assignments)
        {
            a.DeferredDays = 1;
            a.UpdatedAt = DateTime.Now;
        }
        HoldStreak(GetProfile(db, studentId), today);
        db.SaveChanges();
    }
}
