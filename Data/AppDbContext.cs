using Microsoft.EntityFrameworkCore;

namespace TutorSpace.Data;

/// <summary>
/// Ten tables: Users, TutorStudents, Weeks, Tasks, Exercises, Answers, Rules, Words, WordOfDay, Audio.
/// Small per-row lists (call questions, lesson ratings, intros, visits, rule log) live in JSON columns.
/// </summary>
public class AppDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<WeekPlan> WeekPlans => Set<WeekPlan>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<Exercise> Exercises => Set<Exercise>();
    public DbSet<ExerciseAttempt> Attempts => Set<ExerciseAttempt>();
    public DbSet<AssignmentRule> Rules => Set<AssignmentRule>();
    public DbSet<VocabularyEntry> Vocabulary => Set<VocabularyEntry>();
    public DbSet<WordOfDay> WordsOfDay => Set<WordOfDay>();
    public DbSet<MediaAsset> Media => Set<MediaAsset>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
            options.UseSqlServer(DbSettings.Current);
    }

    // SQL Server refuses cascades that reach one table by several paths (and self-references),
    // so those foreign keys are NoAction in the database and the references are cleared here.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var deletedPlans = Deleted<WeekPlan>().Select(p => p.Id).ToList();
        var deletedAssignments = Deleted<Assignment>().Select(a => a.Id).ToList();
        var deletedMedia = Deleted<MediaAsset>().Select(m => m.Id).ToList();
        if (deletedPlans.Count + deletedAssignments.Count + deletedMedia.Count == 0)
            return base.SaveChanges(acceptAllChangesOnSuccess);

        var ownTransaction = Database.CurrentTransaction == null ? Database.BeginTransaction() : null;
        try
        {
            var gone = Assignments
                .Where(a => deletedAssignments.Contains(a.Id) || deletedPlans.Contains(a.WeekPlanId))
                .Select(a => a.Id);
            Assignments.Where(a => a.RepeatOfId != null && gone.Contains(a.RepeatOfId.Value))
                .ExecuteUpdate(s => s.SetProperty(a => a.RepeatOfId, (int?)null));
            if (deletedMedia.Count > 0)
                Attempts.Where(a => a.AudioId != null && deletedMedia.Contains(a.AudioId.Value))
                    .ExecuteUpdate(s => s.SetProperty(a => a.AudioId, (int?)null));

            var result = base.SaveChanges(acceptAllChangesOnSuccess);
            ownTransaction?.Commit();
            return result;
        }
        finally
        {
            ownTransaction?.Dispose();
        }
    }

    private IEnumerable<T> Deleted<T>() where T : class =>
        ChangeTracker.Entries<T>().Where(e => e.State == EntityState.Deleted).Select(e => e.Entity);

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // The app works in the PC's local time, so timestamps are stored without a time zone.
        builder.Properties<DateTime>().HaveColumnType("datetime2");

        // Enums are stored as their names, so the database stays readable.
        builder.Properties<UserRole>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<MissionKind>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<ExerciseKind>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<LessonComparison>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<LessonVerdict>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<RuleTrigger>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<RuleAction>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<LanguageLevel>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<WordStatus>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<MediaSource>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>(u =>
        {
            u.ToTable("Users");
            u.Property(x => x.Username).HasMaxLength(200);
            u.HasIndex(x => x.Username).IsUnique();
            // Student survey, streak and visits are columns of Users; tutors simply leave them empty.
            u.OwnsOne(x => x.Profile, p =>
            {
                p.Property(x => x.Interests).HasColumnName("Interests");
                p.Property(x => x.Level).HasColumnName("Level");
                p.Property(x => x.OnboardedAt).HasColumnName("OnboardedAt");
                p.Property(x => x.CurrentStreak).HasColumnName("CurrentStreak");
                p.Property(x => x.LongestStreak).HasColumnName("LongestStreak");
                p.Property(x => x.LastActiveDate).HasColumnName("LastActiveDate");
                p.Property(x => x.LastFrozenDate).HasColumnName("LastFrozenDate");
                p.Property(x => x.VisitsJson).HasColumnName("VisitsJson");
            });
            u.Navigation(x => x.Profile).IsRequired();
        });

        model.Entity<Enrollment>(e =>
        {
            e.ToTable("TutorStudents");
            e.HasIndex(x => new { x.TutorId, x.StudentId }).IsUnique();
            e.HasOne(x => x.Tutor).WithMany().HasForeignKey(x => x.TutorId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<WeekPlan>(w =>
        {
            w.ToTable("Weeks");
            w.Property(x => x.EnrollmentId).HasColumnName("TutorStudentId");
            w.HasOne(x => x.Enrollment).WithMany(e => e.WeekPlans).HasForeignKey(x => x.EnrollmentId)
                .IsRequired(false).OnDelete(DeleteBehavior.Cascade);
            w.HasOne<User>().WithMany().HasForeignKey(x => x.TutorId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            w.HasIndex(x => new { x.EnrollmentId, x.StartDate }).IsUnique();
            w.Property(x => x.Name).HasMaxLength(200);
            w.HasIndex(x => new { x.TutorId, x.Name }).IsUnique().HasFilter("[IsTemplate] = 1");
        });

        model.Entity<Assignment>(a =>
        {
            a.ToTable("Tasks");
            a.Property(x => x.WeekPlanId).HasColumnName("WeekId");
            a.HasIndex(x => new { x.WeekPlanId, x.DayIndex });
            a.HasOne(x => x.RepeatOf).WithMany().HasForeignKey(x => x.RepeatOfId).OnDelete(DeleteBehavior.ClientSetNull);
            // Completion, mission answer and comment are columns of Tasks.
            a.OwnsOne(x => x.Progress, p =>
            {
                p.Property(x => x.IsCompleted).HasColumnName("IsCompleted");
                p.Property(x => x.CompletedAt).HasColumnName("CompletedAt");
                p.Property(x => x.StudentComment).HasColumnName("StudentComment");
                p.Property(x => x.MissionResponse).HasColumnName("MissionResponse");
                p.Property(x => x.UpdatedAt).HasColumnName("ProgressUpdatedAt");
            });
            a.Navigation(x => x.Progress).IsRequired();
        });

        model.Entity<Exercise>(e =>
        {
            e.ToTable("Exercises");
            e.Property(x => x.AssignmentId).HasColumnName("TaskId");
            e.HasOne(x => x.Audio).WithMany().HasForeignKey(x => x.AudioId).OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<ExerciseAttempt>(a =>
        {
            a.ToTable("Answers");
            a.HasIndex(x => new { x.ExerciseId, x.StudentId, x.AttemptNo }).IsUnique();
            a.HasOne(x => x.Audio).WithMany().HasForeignKey(x => x.AudioId).OnDelete(DeleteBehavior.ClientSetNull);
            a.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.NoAction);
        });

        model.Entity<AssignmentRule>(r =>
        {
            r.ToTable("Rules");
            r.Property(x => x.AssignmentId).HasColumnName("TaskId");
        });

        model.Entity<VocabularyEntry>(v =>
        {
            v.ToTable("Words");
            v.Property(x => x.Word).HasMaxLength(200);
            v.HasIndex(x => new { x.StudentId, x.Word }).IsUnique();
            v.HasIndex(x => new { x.StudentId, x.NextReviewOn });
        });

        model.Entity<WordOfDay>(w =>
        {
            w.ToTable("WordOfDay");
            w.HasIndex(x => new { x.TutorId, x.Date }).IsUnique();
            w.HasOne<User>().WithMany().HasForeignKey(x => x.TutorId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<MediaAsset>(m =>
        {
            m.ToTable("Audio");
            m.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
