using Microsoft.EntityFrameworkCore;
using TutorSpace.Data;

namespace TutorSpace.Services;

/// <summary>
/// Rating a lesson by comparison — «лучше / так же / хуже» — and the recorded
/// introduction that opens a week. The first lesson of a week gets a verdict instead.
/// </summary>
public static class LessonService
{
    public static List<int> LessonDays(WeekPlan plan) =>
        plan.Assignments.Select(a => a.DayIndex).Distinct().OrderBy(d => d).ToList();

    private static bool HasStarted(WeekPlan plan, int dayIndex, DateOnly today) =>
        plan.Assignments.Any(a => a.DayIndex == dayIndex && a.IsUnlockedFor(today));

    public record ReviewState(int DayIndex, bool IsOpening, bool CanReview, int? ComparedToDay, LessonReview? Review);

    public static ReviewState State(WeekPlan plan, int dayIndex, DateOnly today)
    {
        var days = LessonDays(plan);
        var opening = days.Count == 0 || dayIndex == days[0];
        var earlier = days.Where(d => d < dayIndex).ToList();
        return new ReviewState(
            dayIndex,
            opening,
            days.Contains(dayIndex) && HasStarted(plan, dayIndex, today),
            earlier.Count > 0 ? earlier[^1] : null,
            plan.LessonReviews.FirstOrDefault(r => r.DayIndex == dayIndex));
    }

    /// <summary>Stores the student's verdict on one day. Re-rating replaces the old answer.</summary>
    public static void Submit(int planId, int dayIndex, LessonVerdict? verdict, LessonComparison? comparison, string reason)
    {
        var today = Clock.Today;
        using var db = new AppDbContext();
        var plan = db.WeekPlans.Include(w => w.Assignments)
            .FirstOrDefault(w => w.Id == planId) ?? throw new UserError("План не найден.");

        var days = LessonDays(plan);
        if (dayIndex is < 0 or >= WeekPlan.DaysInWeek) throw new UserError("Такого дня в неделе нет.");
        if (!days.Contains(dayIndex)) throw new UserError("В этот день урока не было — оценивать нечего.");
        if (!HasStarted(plan, dayIndex, today)) throw new UserError("Этот урок ещё не начался.");

        reason = (reason ?? "").Trim();
        if (reason.Length > 5000) throw new UserError("Слишком длинное объяснение.");
        if (verdict is { } v && !Enum.IsDefined(v) || comparison is { } c && !Enum.IsDefined(c)) throw new UserError("Неизвестная оценка.");
        var opening = dayIndex == days[0];
        bool needsReason;
        if (opening)
        {
            if (verdict == null) throw new UserError("Первый урок недели оценивается как «Хорошо» или «Плохо».");
            comparison = null;
            needsReason = verdict == LessonVerdict.Bad;
        }
        else
        {
            if (comparison == null) throw new UserError("Оцените урок в сравнении с предыдущим: лучше, так же или хуже.");
            verdict = null;
            needsReason = comparison == LessonComparison.Worse;
        }
        if (needsReason && reason.Length == 0) throw new UserError("Если было плохо — напишите, что именно не пошло.");

        var reviews = plan.LessonReviews;
        var review = reviews.FirstOrDefault(r => r.DayIndex == dayIndex);
        if (review == null)
        {
            review = new LessonReview { DayIndex = dayIndex };
            reviews.Add(review);
        }
        review.Verdict = verdict;
        review.Comparison = comparison;
        review.Reason = reason;
        review.UpdatedAt = DateTime.Now;
        plan.LessonReviews = reviews.OrderBy(r => r.DayIndex).ToList();
        db.SaveChanges();
    }

    public static string Describe(LessonReview review) =>
        review.Verdict is { } v ? PlannerService.Label(v)
        : review.Comparison is { } c ? PlannerService.Label(c) : "—";

    public static List<LessonReview> ReviewsFor(int enrollmentId)
    {
        using var db = new AppDbContext();
        return db.WeekPlans.AsNoTracking().Where(w => w.EnrollmentId == enrollmentId).ToList()
            .SelectMany(w => w.LessonReviews)
            .OrderByDescending(r => r.WeekPlan.StartDate).ThenByDescending(r => r.DayIndex).ToList();
    }

    // ---------- Weekly intro ----------

    /// <summary>The week's takes with their recordings; a take whose recording was deleted is gone too.</summary>
    public static List<WeeklyIntro> Intros(int planId)
    {
        using var db = new AppDbContext();
        var plan = db.WeekPlans.AsNoTracking().FirstOrDefault(w => w.Id == planId);
        if (plan == null) return new();
        var takes = plan.Intros;
        var ids = takes.Select(t => t.AudioId).ToList();
        var audio = db.Media.AsNoTracking().Where(m => ids.Contains(m.Id)).ToDictionary(m => m.Id);
        foreach (var t in takes) t.Audio = audio.GetValueOrDefault(t.AudioId);
        return takes.Where(t => t.Audio != null).OrderBy(t => t.AttemptNo).ToList();
    }

    public static void SubmitIntro(int studentId, int planId, int audioId, int durationSeconds)
    {
        const int slack = 2;
        using var db = new AppDbContext();
        var plan = db.WeekPlans.Find(planId) ?? throw new UserError("План не найден.");
        if (!plan.IntroRequired) throw new UserError("На этой неделе самопрезентация не нужна.");
        if (!db.Media.Any(m => m.Id == audioId)) throw new UserError("Аудиозапись не найдена.");
        var used = Intros(planId).Count(i => i.StudentId == studentId);
        if (used >= WeeklyIntro.MaxAttempts)
            throw new UserError($"Попытки закончились — их {WeeklyIntro.MaxAttempts} на неделю.");
        if (durationSeconds > 0)
        {
            if (durationSeconds < WeeklyIntro.MinSeconds - slack)
                throw new UserError($"Слишком коротко — нужно хотя бы {WeeklyIntro.MinSeconds} секунд.");
            if (durationSeconds > WeeklyIntro.MaxSeconds + slack)
                throw new UserError($"Слишком длинно — уложитесь в {WeeklyIntro.MaxSeconds} секунд.");
        }
        var takes = plan.Intros;
        takes.Add(new WeeklyIntro
        {
            StudentId = studentId, AudioId = audioId,
            AttemptNo = (takes.Count == 0 ? 0 : takes.Max(t => t.AttemptNo)) + 1, DurationSeconds = durationSeconds,
        });
        plan.Intros = takes;
        db.SaveChanges();
    }

    public static readonly string[] IntroQuestions = { "Кто ты?", "Где ты живёшь?", "О чём мечтаешь?", "Чем занимаешься?" };
}
