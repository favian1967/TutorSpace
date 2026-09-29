using System.Windows;
using System.Windows.Controls;
using TutorSpace.Data;
using TutorSpace.Services;

namespace TutorSpace.UI.Views.Student;

/// <summary>One unlocked assignment: material, micro-mission, exercises and completion.</summary>
public class AssignmentCard : UserControl
{
    public AssignmentCard(Assignment a, IReadOnlyList<VocabularyEntry> words, Action changed)
    {
        var done = a.Progress?.IsCompleted == true;
        var panel = new StackPanel();

        // Header: kind badge, title with time, status on the right.
        var head = new DockPanel();
        var badge = a.Provider == "youtube"
            ? Ui.IconBadge(Icons.Video, Ui.Res("DangerSoft"), Ui.Res("Danger"), 40)
            : Ui.IconBadge(Icons.Book, Ui.Res("PrimarySoft"), Ui.Res("Primary"), 40);
        badge.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(badge, Dock.Left);
        head.Children.Add(badge);

        var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        if (a.RepeatOfId != null) chips.Children.Add(Ui.Chip("Повтор", Ui.Res("PrimarySoft"), Ui.Res("Primary")));
        if (a.DeferredDays > 0) chips.Children.Add(Ui.Chip("Перенесено", Ui.Res("WarningSoft"), Ui.Res("Warning")));
        chips.Children.Add(done
            ? Ui.Chip("Выполнено", Ui.Res("SuccessSoft"), Ui.Res("Success"))
            : Ui.Chip("Не выполнено", Ui.Res("SurfaceMuted"), Ui.Res("TextMuted")));
        DockPanel.SetDock(chips, Dock.Right);
        head.Children.Add(chips);

        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(Ui.Text(a.Title, "H2", margin: new Thickness(0)));
        var meta = Ui.Row(
            Ui.Icon(Icons.Clock, 12, Ui.Res("TextMuted"), new Thickness(0, 1, 6, 0)),
            Ui.Text($"{a.EstimatedMinutes} мин · {PlannerService.DayNames[Clock.DayIndexOf(a.ReleaseDate)]}, {a.ReleaseDate:dd.MM}", "Muted", size: 12.5));
        meta.Margin = new Thickness(0, 2, 0, 0);
        title.Children.Add(meta);
        head.Children.Add(title);
        panel.Children.Add(head);

        if (a.Description.Length > 0) panel.Children.Add(Ui.Text(a.Description, color: Ui.Res("TextSecondary"), margin: new Thickness(0, 14, 0, 0)));

        if (a.Url.Length > 0)
        {
            var youtube = a.Provider == "youtube";
            var material = new DockPanel();
            var open = Ui.Button(youtube ? "Смотреть видео" : "Открыть", () => ResourceResolver.Open(a.Url), "PrimaryButton",
                icon: youtube ? Icons.Play : Icons.Open);
            open.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(open, Dock.Right);
            material.Children.Add(open);
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(Ui.Text(a.PreviewTitle.Length > 0 ? a.PreviewTitle : "Материал", bold: true));
            info.Children.Add(new TextBlock { Text = a.Url, Foreground = Ui.Res("Primary"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
            material.Children.Add(info);
            panel.Children.Add(new Border
            {
                Child = material, Background = Ui.Res("SurfaceMuted"), BorderBrush = Ui.Res("BorderBrushSoft"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 14, 0, 0),
            });
        }

        TextBox? missionBox = null;
        if (a.Mission.Length > 0)
        {
            var mission = new StackPanel();
            mission.Children.Add(Ui.Row(
                Ui.Icon(Icons.Flag, 14, Ui.Res("Primary"), new Thickness(0, 0, 8, 0)),
                Ui.Text("Твоя миссия", bold: true, color: Ui.Res("Primary"))));
            mission.Children.Add(Ui.Text(a.Mission, color: Ui.Res("TextSecondary"), margin: new Thickness(0, 6, 0, 10)));
            missionBox = Ui.Box(a.Progress?.MissionResponse ?? "", multiline: true, minHeight: 56);
            mission.Children.Add(missionBox);
            panel.Children.Add(new Border
            {
                Child = mission, Background = Ui.Res("PrimarySoft"), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16), Margin = new Thickness(0, 14, 0, 0),
            });
        }

        var exercises = a.Exercises.OrderBy(x => x.Order).ToList();
        if (exercises.Count > 0)
        {
            panel.Children.Add(Ui.Row(
                Ui.Text("Упражнения", "H3", margin: new Thickness(0, 0, 8, 0)),
                Ui.Chip(exercises.Count.ToString(), Ui.Res("PrimarySoft"), Ui.Res("Primary"))));
            ((FrameworkElement)panel.Children[^1]).Margin = new Thickness(0, 20, 0, 0);
            foreach (var ex in exercises)
                panel.Children.Add(new ExerciseBlock(ex, a, words, () => { }));
        }

        var footer = new StackPanel();
        footer.Children.Add(Ui.Text("Комментарий преподавателю (необязательно)", "FieldLabel"));
        var comment = Ui.Box(a.Progress?.StudentComment ?? "");
        footer.Children.Add(comment);
        panel.Children.Add(new Border
        {
            Child = footer, BorderBrush = Ui.Res("BorderBrushSoft"), BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 20, 0, 0), Padding = new Thickness(0, 4, 0, 0),
        });

        void Save(bool completed)
        {
            ProgressService.SetProgress(Session.User.Id, a.Id, completed, missionBox?.Text ?? "", comment.Text);
            changed();
        }

        panel.Children.Add(Ui.Wrap(
            done
                ? Ui.Button("Снять отметку о выполнении", () => Save(false))
                : Ui.Button("Отметить выполненным", () => Save(true), "PrimaryButton", icon: Icons.Check),
            Ui.Button("Сохранить ответ", () =>
            {
                Save(done);
                Ui.Info("Сохранено.");
            })));

        Content = Ui.Card(panel);
    }
}

/// <summary>«Лучше / так же / хуже, чем в прошлый раз» — or «хорошо / плохо» for a week's first lesson.</summary>
public class LessonReviewCard : UserControl
{
    public LessonReviewCard(WeekPlan plan, int dayIndex, Action changed)
    {
        var state = LessonService.State(plan, dayIndex, Clock.Today);
        if (!state.CanReview)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        var panel = new StackPanel();
        var question = state.IsOpening
            ? "Как прошёл урок?"
            : $"Как урок по сравнению с предыдущим ({PlannerService.DayNames[state.ComparedToDay!.Value].ToLower()})?";
        panel.Children.Add(Ui.Text(question, "H3"));

        var reason = Ui.Box(state.Review?.Reason ?? "");
        var options = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };

        void Submit(LessonVerdict? verdict, LessonComparison? comparison)
        {
            LessonService.Submit(plan.Id, dayIndex, verdict, comparison, reason.Text);
            changed();
        }

        if (state.IsOpening)
        {
            foreach (var v in Enum.GetValues<LessonVerdict>())
            {
                var selected = state.Review?.Verdict == v;
                options.Children.Add(Ui.Button(PlannerService.Label(v), () => Submit(v, null), selected ? "PrimaryButton" : null));
            }
        }
        else
        {
            foreach (var c in Enum.GetValues<LessonComparison>())
            {
                var selected = state.Review?.Comparison == c;
                options.Children.Add(Ui.Button(PlannerService.Label(c), () => Submit(null, c), selected ? "PrimaryButton" : null));
            }
        }
        panel.Children.Add(options);
        panel.Children.Add(Ui.Text("Если было плохо или хуже — сначала напиши, что именно не понравилось:", "FieldLabel"));
        panel.Children.Add(reason);
        if (state.Review != null)
            panel.Children.Add(Ui.Text("Твоя оценка: " + LessonService.Describe(state.Review), "Muted", margin: new Thickness(0, 6, 0, 0)));

        Content = Ui.Card(panel);
    }
}

/// <summary>The recorded self-introduction that opens a week: 30–60 seconds, three takes.</summary>
public class IntroCard : UserControl
{
    public IntroCard(WeekPlan plan, Action changed)
    {
        if (!plan.IntroRequired)
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        var takes = LessonService.Intros(plan.Id);
        var panel = new StackPanel();
        panel.Children.Add(Ui.Text("Самопрезентация недели", "H2"));
        panel.Children.Add(Ui.Text($"Расскажи о себе за {WeeklyIntro.MinSeconds}–{WeeklyIntro.MaxSeconds} секунд. Попыток: {takes.Count} из {WeeklyIntro.MaxAttempts}.", "Muted"));
        foreach (var q in LessonService.IntroQuestions) panel.Children.Add(Ui.Text("— " + q, color: Ui.Res("TextSecondary")));

        var row = new WrapPanel();
        foreach (var t in takes) row.Children.Add(Ui.Button($"Попытка {t.AttemptNo} ({t.DurationSeconds} с)", () => AudioPlayer.Play(t.Audio), icon: Icons.Play));
        panel.Children.Add(row);

        if (takes.Count < WeeklyIntro.MaxAttempts)
        {
            panel.Children.Add(new Controls.RecorderPanel(WeeklyIntro.MaxSeconds, allowFile: false)
            {
                Recorded = (path, seconds) => Ui.Try(() =>
                {
                    var asset = MediaService.Import(Session.User.Id, path, MediaSource.StudentRecording, $"Самопрезентация {plan.StartDate:dd.MM}", durationSeconds: seconds);
                    try
                    {
                        LessonService.SubmitIntro(Session.User.Id, plan.Id, asset.Id, seconds);
                    }
                    catch
                    {
                        MediaService.Delete(asset.Id);
                        throw;
                    }
                    changed();
                }),
            });
        }
        Content = Ui.Card(panel);
    }
}
