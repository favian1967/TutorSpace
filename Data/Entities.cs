using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using TutorSpace.Services;

namespace TutorSpace.Data;

public enum UserRole { Tutor, Student }

public enum MissionKind { Words, Sentence, Voice, Retell, Custom }

public enum ExerciseKind { GapFill, OpenAnswer, Theses, Speaking, Monologue }

public enum LessonComparison { Better, Same, Worse }

public enum LessonVerdict { Good, Bad }

public enum RuleTrigger { ExerciseFailed, ExercisePassed, AssignmentDone }

public enum RuleAction { RepeatInDays, FlagTutor }

public enum LanguageLevel { Beginner, Intermediate, Confident }

public enum WordStatus { New, Learning, Mastered }

public enum ReviewGrade { Again, Hard, Good, Easy }

public enum MediaSource { TutorClip, StudentRecording }

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public string FullName { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Student-only data (survey, streak, visits), stored as columns of Users.</summary>
    public StudentProfile Profile { get; set; } = new();

    [NotMapped] public string DisplayName => string.IsNullOrWhiteSpace(FullName) ? Username : FullName;
    [NotMapped] public bool IsTutor => Role == UserRole.Tutor;
}

/// <summary>Link between a tutor and a student (table TutorStudents). Everything else hangs off this.</summary>
public class Enrollment
{
    public int Id { get; set; }
    public int TutorId { get; set; }
    public User Tutor { get; set; } = null!;
    public int StudentId { get; set; }
    public User Student { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<WeekPlan> WeekPlans { get; set; } = new();
}

/// <summary>Onboarding answers, streak and visits of a student — columns of the Users table.</summary>
public class StudentProfile
{
    public string Interests { get; set; } = "";
    public LanguageLevel? Level { get; set; }
    public DateTime? OnboardedAt { get; set; }
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    public DateOnly? LastActiveDate { get; set; }
    public DateOnly? LastFrozenDate { get; set; }
    public string VisitsJson { get; set; } = "[]";

    [NotMapped] public bool IsOnboarded => OnboardedAt != null;

    /// <summary>One entry per day the student opened the app.</summary>
    [NotMapped]
    public List<SiteVisit> Visits
    {
        get => Json.Read<List<SiteVisit>>(VisitsJson);
        set => VisitsJson = Json.Write(value);
    }
}

/// <summary>
/// A Monday-anchored week of assignments for one student (table Weeks).
/// A template is the same thing without a student: <see cref="IsTemplate"/>, <see cref="TutorId"/> and a name.
/// </summary>
public class WeekPlan
{
    public const int DaysInWeek = 7;

    public int Id { get; set; }
    public int? EnrollmentId { get; set; }
    public Enrollment? Enrollment { get; set; }
    public DateOnly StartDate { get; set; }
    public string Title { get; set; } = "";
    /// <summary>Private, never shown to the student.</summary>
    public string TutorNotes { get; set; } = "";
    public bool IsPublished { get; set; } = true;
    public DateTime? ReviewAt { get; set; }
    public bool IntroRequired { get; set; }

    // Template-only fields.
    public bool IsTemplate { get; set; }
    public int? TutorId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int TimesUsed { get; set; }

    // Small per-week lists, stored as JSON columns.
    public string CallQuestionsJson { get; set; } = "[]";
    public string LessonReviewsJson { get; set; } = "[]";
    public string IntrosJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public List<Assignment> Assignments { get; set; } = new();

    /// <summary>A fresh copy each time: change it and assign it back to save.</summary>
    [NotMapped]
    public List<CallQuestion> CallQuestions
    {
        get => Json.Read<List<CallQuestion>>(CallQuestionsJson);
        set => CallQuestionsJson = Json.Write(value);
    }

    [NotMapped]
    public List<LessonReview> LessonReviews
    {
        get
        {
            var list = Json.Read<List<LessonReview>>(LessonReviewsJson);
            foreach (var r in list) r.WeekPlan = this;
            return list;
        }
        set => LessonReviewsJson = Json.Write(value);
    }

    [NotMapped]
    public List<WeeklyIntro> Intros
    {
        get => Json.Read<List<WeeklyIntro>>(IntrosJson);
        set => IntrosJson = Json.Write(value);
    }

    [NotMapped] public int AssignmentCount => Assignments.Count;
    [NotMapped] public DateOnly EndDate => StartDate.AddDays(DaysInWeek - 1);
    [NotMapped] public DateOnly ReviewDate => ReviewAt is { } at ? DateOnly.FromDateTime(at) : EndDate;
    public DateOnly DateForDay(int dayIndex) => StartDate.AddDays(dayIndex);
}

/// <summary>Question the tutor plans to ask during the end-of-week call. Tutor-only. Stored inside Weeks.</summary>
public class CallQuestion
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public int Order { get; set; }
    public bool IsAsked { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>One task on one day (table Tasks). Content stays hidden until its day arrives.</summary>
public class Assignment
{
    public int Id { get; set; }
    public int WeekPlanId { get; set; }
    public WeekPlan WeekPlan { get; set; } = null!;
    /// <summary>0 = Monday ... 6 = Sunday.</summary>
    public int DayIndex { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Url { get; set; } = "";
    public MissionKind MissionKind { get; set; } = MissionKind.Sentence;
    public string Mission { get; set; } = "";
    public int EstimatedMinutes { get; set; } = 10;
    public int DeferredDays { get; set; }
    public int? RepeatOfId { get; set; }
    public Assignment? RepeatOf { get; set; }

    public string Provider { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string PreviewTitle { get; set; } = "";
    public string PreviewThumbnail { get; set; } = "";

    public int Order { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>The student's side of the task, stored as columns of the Tasks table.</summary>
    public AssignmentProgress Progress { get; set; } = new();
    public List<Exercise> Exercises { get; set; } = new();
    public List<AssignmentRule> Rules { get; set; } = new();

    /// <summary>When the student may open it — the planned day plus any deferral.</summary>
    public DateOnly ReleaseDate => WeekPlan.DateForDay(DayIndex).AddDays(DeferredDays);
    public DateOnly PlannedDate => WeekPlan.DateForDay(DayIndex);

    /// <summary>Single source of truth for visibility.</summary>
    public bool IsUnlockedFor(DateOnly today) => WeekPlan.IsPublished && ReleaseDate <= today;
}

/// <summary>The student's side of an assignment — columns of the Tasks table.</summary>
public class AssignmentProgress
{
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string StudentComment { get; set; } = "";
    public string MissionResponse { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>One block inside an assignment.</summary>
public class Exercise
{
    public static readonly ExerciseKind[] VoiceKinds = { ExerciseKind.Speaking, ExerciseKind.Monologue };

    public int Id { get; set; }
    public int AssignmentId { get; set; }
    public Assignment Assignment { get; set; } = null!;
    public ExerciseKind Kind { get; set; }
    public string Prompt { get; set; } = "";
    public string Instruction { get; set; } = "";
    public string ConfigJson { get; set; } = "{}";
    public int? AudioId { get; set; }
    public MediaAsset? Audio { get; set; }
    public int Order { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [NotMapped]
    public ExerciseConfig Config
    {
        get => Json.Read<ExerciseConfig>(ConfigJson);
        set => ConfigJson = Json.Write(value);
    }

    [NotMapped] public bool IsAutoChecked => Kind == ExerciseKind.GapFill;
    [NotMapped] public bool WantsVoice => VoiceKinds.Contains(Kind);

    /// <summary>0 means unlimited. Voice blocks default to three takes.</summary>
    [NotMapped]
    public int AttemptsAllowed => Math.Max(0, Config.AttemptsAllowed ?? (WantsVoice ? 3 : 0));
}

public class GapBlank
{
    public List<string> Answers { get; set; } = new();
    public string Hint { get; set; } = "";
}

/// <summary>Kind-specific settings of an exercise, stored as JSON.</summary>
public class ExerciseConfig
{
    // GAP_FILL: derived from the prompt, never edited by hand.
    public string Template { get; set; } = "";
    public List<GapBlank> Blanks { get; set; } = new();

    // THESES
    public string SegmentStart { get; set; } = "";
    public string SegmentEnd { get; set; } = "";
    public List<string> Reference { get; set; } = new();
    public int MinTheses { get; set; } = 3;
    public int MaxTheses { get; set; } = 5;

    // OPEN_ANSWER
    public int MinWords { get; set; }
    public string SampleAnswer { get; set; } = "";

    // SPEAKING: text to repeat when there is no audio clip transcript.
    public string Transcript { get; set; } = "";

    public int? AttemptsAllowed { get; set; }
}

/// <summary>One submission. Kept as a history rather than overwritten.</summary>
public class ExerciseAttempt
{
    public int Id { get; set; }
    public int ExerciseId { get; set; }
    public Exercise Exercise { get; set; } = null!;
    public int StudentId { get; set; }
    public User Student { get; set; } = null!;
    public int AttemptNo { get; set; } = 1;
    public string AnswersJson { get; set; } = "{}";
    public int? AudioId { get; set; }
    public MediaAsset? Audio { get; set; }
    public int Score { get; set; }
    public int MaxScore { get; set; }
    public bool IsAutoChecked { get; set; }
    public string TutorComment { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [NotMapped]
    public AttemptAnswers Answers
    {
        get => Json.Read<AttemptAnswers>(AnswersJson);
        set => AnswersJson = Json.Write(value);
    }

    /// <summary>No key to check against counts as done, not as failed.</summary>
    [NotMapped] public bool IsPassed => MaxScore == 0 || Score >= MaxScore;
    [NotMapped] public int Percent => MaxScore == 0 ? 100 : (int)Math.Round(100.0 * Score / MaxScore);
}

public class AttemptAnswers
{
    public List<string> Blanks { get; set; } = new();
    public List<bool> Results { get; set; } = new();
    public string Text { get; set; } = "";
    public List<string> Theses { get; set; } = new();
    public List<bool> Marks { get; set; } = new();
}

/// <summary>The student's verdict on one day of the week. Visible to the tutor. Stored inside Weeks.</summary>
public class LessonReview
{
    public int DayIndex { get; set; }
    public LessonComparison? Comparison { get; set; }
    public LessonVerdict? Verdict { get; set; }
    public string Reason { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>The week it belongs to; filled in when read, not stored.</summary>
    [JsonIgnore] public WeekPlan WeekPlan { get; set; } = null!;

    [JsonIgnore]
    public bool WentBadly => Verdict == LessonVerdict.Bad || Comparison == LessonComparison.Worse;
}

/// <summary>A recorded self-introduction, one entry per take. Stored inside Weeks.</summary>
public class WeeklyIntro
{
    public const int MinSeconds = 30;
    public const int MaxSeconds = 60;
    public const int MaxAttempts = 3;

    public int StudentId { get; set; }
    public int AudioId { get; set; }
    public int AttemptNo { get; set; } = 1;
    public int DurationSeconds { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>The recording; filled in when read, not stored.</summary>
    [JsonIgnore] public MediaAsset? Audio { get; set; }
}

/// <summary>«Если ошибся — верни мне это через неделю», выраженное данными.</summary>
public class AssignmentRule
{
    public int Id { get; set; }
    public int AssignmentId { get; set; }
    public Assignment Assignment { get; set; } = null!;
    public RuleTrigger Trigger { get; set; }
    public RuleAction Action { get; set; }
    public int ThresholdPercent { get; set; } = 100;
    public int Days { get; set; } = 7;
    public string Note { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool FireOnce { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string FiresJson { get; set; } = "[]";

    /// <summary>What the rule has done so far. A fresh copy each time: change it and assign it back to save.</summary>
    [NotMapped]
    public List<RuleFire> Fires
    {
        get => Json.Read<List<RuleFire>>(FiresJson);
        set => FiresJson = Json.Write(value);
    }
}

/// <summary>A log entry of what a rule actually did — automation the tutor can see. Stored inside Rules.</summary>
public class RuleFire
{
    public int Id { get; set; }
    public int? CreatedAssignmentId { get; set; }
    public string Detail { get; set; } = "";
    public bool IsSeen { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Filled in when read, not stored.</summary>
    [JsonIgnore] public AssignmentRule Rule { get; set; } = null!;
    [JsonIgnore] public Assignment? CreatedAssignment { get; set; }
}

/// <summary>One word in a student's personal dictionary, with its SM-2 state.</summary>
public class VocabularyEntry
{
    public const double MinEase = 1.3;
    public const double MaxEase = 3.0;
    public const double DefaultEase = 2.5;
    public const int MaxIntervalDays = 365;
    public const int LearnedIntervalDays = 180;

    public int Id { get; set; }
    public int StudentId { get; set; }
    public User Student { get; set; } = null!;
    public string Word { get; set; } = "";
    public string Translation { get; set; } = "";
    public string Example { get; set; } = "";
    public bool IsLearned { get; set; }
    public double EaseFactor { get; set; } = DefaultEase;
    public int IntervalDays { get; set; }
    public int Repetitions { get; set; }
    public int Lapses { get; set; }
    public DateOnly? NextReviewOn { get; set; }
    public DateTime? LastReviewedAt { get; set; }
    public int ReviewCount { get; set; }
    public WordStatus Status { get; set; } = WordStatus.New;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public bool IsDue(DateOnly today) => !IsLearned && NextReviewOn is { } next && next <= today;
}

/// <summary>A tutor's word for a given day, broadcast to all of their students.</summary>
public class WordOfDay
{
    public int Id { get; set; }
    public int TutorId { get; set; }
    public DateOnly Date { get; set; }
    public string Word { get; set; } = "";
    public string Translation { get; set; } = "";
    public string Example { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>One audio file — a clip from the tutor's library or a student's recording.</summary>
public class MediaAsset
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public User Owner { get; set; } = null!;
    public MediaSource Source { get; set; } = MediaSource.TutorClip;
    /// <summary>Path relative to the media folder.</summary>
    public string StoredPath { get; set; } = "";
    public string Title { get; set; } = "";
    public string Transcript { get; set; } = "";
    public string Origin { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public long SizeBytes { get; set; }
    public int DurationSeconds { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [NotMapped]
    public string DisplayName => !string.IsNullOrWhiteSpace(Title) ? Title
        : !string.IsNullOrWhiteSpace(OriginalName) ? OriginalName : $"Аудио #{Id}";

    [NotMapped] public string FullPath => Path.Combine(AppPaths.MediaDir, StoredPath);
}

/// <summary>One day the student opened the app. Stored inside Users.</summary>
public class SiteVisit
{
    public DateOnly Date { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public int Hits { get; set; } = 1;
}
