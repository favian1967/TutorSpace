using Microsoft.EntityFrameworkCore;
using TutorSpace.Data;

namespace TutorSpace.Services;

/// <summary>Visits: one entry per day, kept in the student's row of Users.</summary>
public static class ActivityService
{
    public static void RecordVisit(int studentId)
    {
        var today = Clock.Today;
        using var db = new AppDbContext();
        var profile = ProgressService.GetProfile(db, studentId);
        var visits = profile.Visits;
        var visit = visits.FirstOrDefault(v => v.Date == today);
        if (visit == null)
            visits.Add(new SiteVisit { Date = today, FirstSeenAt = DateTime.Now, LastSeenAt = DateTime.Now });
        else
        {
            visit.Hits++;
            visit.LastSeenAt = DateTime.Now;
        }
        profile.Visits = visits;
        db.SaveChanges();
    }

    public static List<SiteVisit> VisitsOf(int studentId) =>
        ProgressService.GetProfile(studentId).Visits.OrderByDescending(v => v.Date).ToList();
}

/// <summary>One student, gathered from every corner of the app for the tutor.</summary>
public static class OverviewService
{
    public const int WindowWeeks = 4;

    public record AssignmentCounts(int Total, int Released, int Completed, int Overdue);
    public record AnswerCounts(int Total, int AwaitingCheck, int Failed);

    public record Overview(
        int VisitDays, DateOnly? LastVisit, int LifetimeVisitDays, List<SiteVisit> Visits,
        VocabularyService.Stats Words,
        AssignmentCounts Assignments,
        AnswerCounts Answers,
        int Rated, int WentBadly, LessonReview? LastReview,
        int Flags, int UnseenFlags,
        ProgressService.StreakInfo Streak,
        StudentProfile Profile);

    public static Overview For(Enrollment enrollment)
    {
        var today = Clock.Today;
        var since = Clock.MondayOf(today).AddDays(-7 * (WindowWeeks - 1));
        var studentId = enrollment.StudentId;

        using var db = new AppDbContext();

        var visits = ActivityService.VisitsOf(studentId);
        var windowVisits = visits.Where(v => v.Date >= since).ToList();

        var assignments = db.Assignments.AsNoTracking().Include(a => a.WeekPlan)
            .Where(a => a.WeekPlan.EnrollmentId == enrollment.Id && a.WeekPlan.StartDate >= since).ToList();
        var counts = new AssignmentCounts(
            assignments.Count,
            assignments.Count(a => a.IsUnlockedFor(today)),
            assignments.Count(a => a.Progress?.IsCompleted == true),
            assignments.Count(a => a.IsUnlockedFor(today) && a.Progress?.IsCompleted != true && a.ReleaseDate < today));

        var attempts = db.Attempts.AsNoTracking()
            .Where(a => a.Exercise.Assignment.WeekPlan.EnrollmentId == enrollment.Id).ToList();
        var answers = new AnswerCounts(
            attempts.Count,
            attempts.Count(a => !a.IsAutoChecked && a.TutorComment == ""),
            attempts.Count(a => a.MaxScore > 0 && a.Score < a.MaxScore));

        var reviews = LessonService.ReviewsFor(enrollment.Id);
        var last = reviews.FirstOrDefault();

        var fires = RuleService.FiresFor(enrollment.Id);

        return new Overview(
            windowVisits.Count, visits.FirstOrDefault()?.Date, visits.Count, windowVisits,
            VocabularyService.StatsFor(studentId, today),
            counts, answers,
            reviews.Count, reviews.Count(r => r.WentBadly), last,
            fires.Count, fires.Count(f => !f.IsSeen),
            ProgressService.Streak(studentId, today),
            ProgressService.GetProfile(studentId));
    }

    /// <summary>All attempts of a student for the tutor, newest first.</summary>
    public static List<ExerciseAttempt> AnswersFor(int enrollmentId)
    {
        using var db = new AppDbContext();
        return db.Attempts.AsNoTracking()
            .Include(a => a.Audio)
            .Include(a => a.Exercise).ThenInclude(x => x.Assignment).ThenInclude(a => a.WeekPlan)
            .Include(a => a.Exercise).ThenInclude(x => x.Audio)
            .Where(a => a.Exercise.Assignment.WeekPlan.EnrollmentId == enrollmentId)
            .OrderByDescending(a => a.CreatedAt).ToList();
    }

    /// <summary>Mission answers and comments the student left on assignments.</summary>
    public static List<Assignment> MissionAnswersFor(int enrollmentId)
    {
        using var db = new AppDbContext();
        return db.Assignments.AsNoTracking().Include(a => a.WeekPlan)
            .Where(a => a.WeekPlan.EnrollmentId == enrollmentId
                        && (a.Progress.MissionResponse != "" || a.Progress.StudentComment != "" || a.Progress.IsCompleted))
            .OrderByDescending(a => a.Progress.UpdatedAt).ToList();
    }
}
