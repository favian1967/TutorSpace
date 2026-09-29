using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TutorSpace.Data;
using TutorSpace.Services;

namespace TutorSpace.UI.Views.Student;

/// <summary>The week at a glance. Future days stay locked — only their count is visible.</summary>
public class StudentWeekView : UserControl
{
    private DateOnly _monday = Clock.MondayOf(Clock.Today);
    private readonly StackPanel _root = new() { Margin = new Thickness(32, 28, 32, 32), MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Stretch };

    public StudentWeekView()
    {
        Content = new ScrollViewer { Content = _root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Reload();
    }

    private void Reload()
    {
        var today = Clock.Today;
        var plans = PlannerService.StudentPlans(Session.User.Id, _monday).Where(p => p.IsPublished).ToList();
        var words = StudentWords.ForPrompts(Session.User.Id);

        _root.Children.Clear();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
        var nav = Ui.Row(
            Ui.IconButton(Icons.ChevronLeft, () => { _monday = _monday.AddDays(-7); Reload(); }, "Предыдущая неделя"),
            Ui.Text(PlannerService.WeekLabel(_monday), size: 16, bold: true, margin: new Thickness(6, 10, 6, 0)),
            Ui.IconButton(Icons.ChevronRight, () => { _monday = _monday.AddDays(7); Reload(); }, "Следующая неделя"),
            Ui.Button("Эта неделя", () => { _monday = Clock.MondayOf(today); Reload(); }));
        DockPanel.SetDock(nav, Dock.Right);
        head.Children.Add(nav);
        head.Children.Add(Ui.Text("Неделя", "H1", margin: new Thickness(0)));
        _root.Children.Add(head);

        if (plans.Count == 0)
        {
            _root.Children.Add(Ui.Card(Ui.Text("На эту неделю плана нет.", "Muted")));
            return;
        }

        foreach (var plan in plans)
        {
            var total = plan.Assignments.Count;
            var done = plan.Assignments.Count(a => a.Progress?.IsCompleted == true);
            var summary = new StackPanel();
            var percent = Ui.Text(total == 0 ? "" : $"{100 * done / total}%", size: 22, bold: true, color: Ui.Res("Primary"));
            summary.Children.Add(Ui.Header(plan.Title.Length > 0 ? plan.Title : "План недели", percent));
            ((FrameworkElement)summary.Children[0]).Margin = new Thickness(0, 0, 0, 4);
            summary.Children.Add(Ui.Text($"Преподаватель: {plan.Enrollment!.Tutor.DisplayName} · выполнено {done} из {total} · созвон-разбор {plan.ReviewDate:dd.MM}", "Muted"));
            summary.Children.Add(new ProgressBar { Maximum = Math.Max(1, total), Value = done, Margin = new Thickness(0, 12, 0, 0) });
            _root.Children.Add(Ui.Card(summary));

            _root.Children.Add(new IntroCard(plan, Reload));

            for (var day = 0; day < WeekPlan.DaysInWeek; day++)
                _root.Children.Add(BuildDay(plan, day, today, words));
        }
    }

    private UIElement BuildDay(WeekPlan plan, int day, DateOnly today, List<VocabularyEntry> words)
    {
        var date = plan.DateForDay(day);
        var isToday = date == today;
        var items = plan.Assignments.Where(a => a.DayIndex == day).OrderBy(a => a.Order).ToList();
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };

        // Date tile + day name, like a calendar leaf.
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var leaf = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var shortName = Ui.Text(PlannerService.DayShort[day], size: 11, color: isToday ? Brushes.White : Ui.Res("TextMuted"));
        shortName.HorizontalAlignment = HorizontalAlignment.Center;
        leaf.Children.Add(shortName);
        var number = Ui.Text(date.Day.ToString(), size: 16, bold: true, color: isToday ? Brushes.White : null);
        number.HorizontalAlignment = HorizontalAlignment.Center;
        leaf.Children.Add(number);
        var tile = new Border
        {
            Width = 46, Height = 46, CornerRadius = new CornerRadius(10), Child = leaf, Margin = new Thickness(0, 0, 14, 0),
            Background = isToday ? Ui.Res("Primary") : Ui.Res("Surface"),
            BorderBrush = isToday ? Ui.Res("Primary") : Ui.Res("BorderBrushSoft"), BorderThickness = new Thickness(1),
        };
        DockPanel.SetDock(tile, Dock.Left);
        header.Children.Add(tile);

        var doneCount = items.Count(a => a.Progress?.IsCompleted == true);
        if (items.Count > 0)
        {
            var chip = doneCount == items.Count
                ? Ui.Chip("Всё выполнено", Ui.Res("SuccessSoft"), Ui.Res("Success"))
                : Ui.Chip($"{doneCount} из {items.Count}", Ui.Res("SurfaceMuted"), Ui.Res("TextMuted"));
            DockPanel.SetDock(chip, Dock.Right);
            header.Children.Add(chip);
        }
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(Ui.Text(PlannerService.DayNames[day] + (isToday ? " — сегодня" : ""), "H3"));
        title.Children.Add(Ui.Text(items.Count == 0 ? "Выходной" : Ui.Plural(items.Count, "задание", "задания", "заданий"), "Muted", size: 12.5));
        header.Children.Add(title);
        panel.Children.Add(header);

        if (items.Count == 0) return panel;

        var locked = items.Where(a => !a.IsUnlockedFor(today)).ToList();
        if (locked.Count > 0)
        {
            var opens = locked.Min(a => a.ReleaseDate);
            var row = Ui.Row(
                Ui.Icon(Icons.Lock, 14, Ui.Res("TextMuted"), new Thickness(0, 0, 10, 0)),
                Ui.Text($"{Ui.Plural(locked.Count, "задание откроется", "задания откроются", "заданий откроются")} {opens:dd.MM}", "Muted"));
            var card = Ui.Card(row, Ui.Res("SurfaceMuted"));
            card.Padding = new Thickness(16, 12, 16, 12);
            card.Margin = new Thickness(0, 0, 0, 8);
            panel.Children.Add(card);
        }

        foreach (var a in items.Where(a => a.IsUnlockedFor(today)))
            panel.Children.Add(Collapsible(a, words, expanded: isToday && a.Progress?.IsCompleted != true));

        panel.Children.Add(new LessonReviewCard(plan, day, Reload));
        return panel;
    }

    /// <summary>A one-line summary that opens into the full assignment card.</summary>
    private UIElement Collapsible(Assignment a, List<VocabularyEntry> words, bool expanded)
    {
        var done = a.Progress?.IsCompleted == true;
        var host = new StackPanel();
        var body = new ContentControl { Visibility = Visibility.Collapsed };

        var row = new DockPanel { Background = Brushes.Transparent };
        var dot = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = done ? Ui.Res("Success") : Ui.Res("Surface"), BorderBrush = Ui.Res("BorderStrong"), BorderThickness = new Thickness(done ? 0 : 1.5),
        };
        if (done)
        {
            var check = Ui.Icon(Icons.Check, 10, Brushes.White);
            check.HorizontalAlignment = HorizontalAlignment.Center;
            dot.Child = check;
        }
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);

        var chevron = Ui.Icon("", 12, Ui.Res("TextMuted"), new Thickness(14, 0, 0, 0));
        DockPanel.SetDock(chevron, Dock.Right);
        row.Children.Add(chevron);
        var minutes = Ui.Text($"{a.EstimatedMinutes} мин", "Muted", size: 12.5);
        minutes.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(minutes, Dock.Right);
        row.Children.Add(minutes);
        row.Children.Add(new TextBlock
        {
            Text = a.Title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = done ? Ui.Res("TextMuted") : Ui.Res("TextMain"),
        });

        var summary = Ui.Card(row);
        summary.Padding = new Thickness(16, 12, 16, 12);
        summary.Margin = new Thickness(0, 0, 0, 8);
        summary.Cursor = Cursors.Hand;

        void Toggle(bool open)
        {
            if (open && body.Content == null) body.Content = new AssignmentCard(a, words, Reload);
            body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            summary.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        }
        summary.MouseLeftButtonUp += (_, _) => Toggle(true);

        host.Children.Add(summary);
        host.Children.Add(body);
        Toggle(expanded);
        return host;
    }
}
