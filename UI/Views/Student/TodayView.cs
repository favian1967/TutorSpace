using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TutorSpace.Data;
using TutorSpace.Services;

namespace TutorSpace.UI.Views.Student;

/// <summary>The student's home: streak, the week at a glance, today's lesson and the leftovers.</summary>
public class TodayView : UserControl
{
    private static readonly CultureInfo Russian = new("ru-RU");
    private readonly StackPanel _root = new() { Margin = new Thickness(32, 28, 32, 32), MaxWidth = 1080, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ScrollViewer _scroll;

    public TodayView()
    {
        _scroll = new ScrollViewer { Content = _root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Content = _scroll;
        Reload();
    }

    private StudentWindow? Shell => Window.GetWindow(this) as StudentWindow;

    private void Reload()
    {
        var studentId = Session.User.Id;
        var today = Clock.Today;
        var monday = Clock.MondayOf(today);
        var plans = PlannerService.StudentPlans(studentId, monday)
            .Concat(PlannerService.StudentPlans(studentId, monday.AddDays(-7))).ToList();
        var words = StudentWords.ForPrompts(studentId);
        var streak = ProgressService.Streak(studentId, today);
        var todays = plans.SelectMany(p => p.Assignments)
            .Where(a => a.IsUnlockedFor(today) && a.ReleaseDate == today)
            .OrderBy(a => a.DayIndex).ThenBy(a => a.Order).ToList();

        _root.Children.Clear();
        _root.Children.Add(BuildHeader(streak, today));
        _root.Children.Add(BuildWeekStrip(plans.Where(p => p.StartDate == monday).ToList(), monday, today));

        var cards = new Dictionary<int, FrameworkElement>();
        _root.Children.Add(TwoColumns(BuildProgress(todays), BuildWordOfDay(studentId, today), 1.15, 1));
        _root.Children.Add(TwoColumns(BuildTaskList(todays, cards), BuildDueWords(studentId, today), 1.45, 1));

        foreach (var plan in plans.Where(p => p.StartDate == monday && p.IsPublished))
            _root.Children.Add(new IntroCard(plan, Reload));

        _root.Children.Add(Ui.Text("Урок на сегодня", "H2", margin: new Thickness(0, 8, 0, 12)));
        if (todays.Count == 0)
            _root.Children.Add(Ui.Card(Ui.Text("На сегодня заданий нет. Можно повторить слова или доделать задания прошлых дней.", "Muted")));

        foreach (var plan in plans)
        {
            var mine = todays.Where(a => a.WeekPlanId == plan.Id).ToList();
            if (mine.Count == 0) continue;
            foreach (var a in mine)
            {
                var card = new AssignmentCard(a, words, Reload);
                cards[a.Id] = card;
                _root.Children.Add(card);
            }
            foreach (var day in mine.Select(a => a.DayIndex).Distinct())
            {
                _root.Children.Add(BuildFreeze(plan, day));
                _root.Children.Add(new LessonReviewCard(plan, day, Reload));
            }
        }

        var leftovers = plans.SelectMany(p => p.Assignments)
            .Where(a => a.IsUnlockedFor(today) && a.ReleaseDate < today && a.Progress?.IsCompleted != true)
            .OrderBy(a => a.ReleaseDate).ToList();
        if (leftovers.Count > 0)
        {
            _root.Children.Add(Ui.Text("Не доделано", "H2", margin: new Thickness(0, 16, 0, 12)));
            foreach (var a in leftovers)
            {
                var card = new AssignmentCard(a, words, Reload);
                cards[a.Id] = card;
                _root.Children.Add(card);
            }
        }
    }

    // ---------- Header ----------

    private UIElement BuildHeader(ProgressService.StreakInfo streak, DateOnly today)
    {
        var hour = DateTime.Now.Hour;
        var greeting = hour < 12 ? "Доброе утро" : hour < 18 ? "Добрый день" : "Добрый вечер";
        var firstName = Session.User.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 22) };

        var streakPanel = new StackPanel { Orientation = Orientation.Horizontal };
        streakPanel.Children.Add(Ui.IconBadge(Icons.Bolt, Ui.Res("PrimarySoft"), Ui.Res("Primary"), 34));
        var streakText = new StackPanel { Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        streakText.Children.Add(Ui.Text("Серия", "Muted", size: 12));
        streakText.Children.Add(Ui.Text(Ui.Plural(streak.Current, "день", "дня", "дней"), bold: true, size: 15));
        streakPanel.Children.Add(streakText);
        var streakCard = new Border
        {
            Child = streakPanel, Background = Ui.Res("Surface"), BorderBrush = Ui.Res("BorderBrushSoft"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 8, 14, 8), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = $"Лучшая серия: {Ui.Plural(streak.Longest, "день", "дня", "дней")}" + (streak.ActiveToday ? ". Сегодня уже засчитано." : ""),
        };
        DockPanel.SetDock(streakCard, Dock.Right);
        dock.Children.Add(streakCard);

        var title = new StackPanel();
        title.Children.Add(Ui.Text($"{greeting}, {firstName}", "H1", margin: new Thickness(0)));
        title.Children.Add(Ui.Text($"Сегодня {PlannerService.DayNames[Clock.DayIndexOf(today)].ToLower()}, {today.ToString("d MMMM", Russian)}", "Muted", size: 15, margin: new Thickness(0, 4, 0, 0)));
        dock.Children.Add(title);
        return dock;
    }

    /// <summary>Seven day tiles: today highlighted, days ahead locked.</summary>
    private UIElement BuildWeekStrip(List<WeekPlan> week, DateOnly monday, DateOnly today)
    {
        var grid = new UniformGrid { Columns = 7, Margin = new Thickness(-5, 0, -5, 16) };
        for (var day = 0; day < WeekPlan.DaysInWeek; day++)
        {
            var date = monday.AddDays(day);
            var items = week.SelectMany(p => p.Assignments).Where(a => a.PlannedDate == date).ToList();
            var isToday = date == today;
            var allDone = items.Count > 0 && items.All(a => a.Progress?.IsCompleted == true);

            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var label = Ui.Text(isToday ? "Сегодня" : PlannerService.DayShort[day], size: 12,
                color: isToday ? System.Windows.Media.Brushes.White : Ui.Res("TextMuted"));
            label.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(label);

            UIElement value = date > today && items.Count > 0
                ? Ui.Icon(Icons.Lock, 15, Ui.Res("TextFaint"), new Thickness(0, 5, 0, 3))
                : Ui.Text(date.Day.ToString(), size: 19, bold: true, color: isToday ? System.Windows.Media.Brushes.White : null);
            ((FrameworkElement)value).HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(value);

            var mark = new Border { Height = 4, Width = 18, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 4, 0, 0) };
            mark.Background = allDone ? Ui.Res("Success") : isToday ? System.Windows.Media.Brushes.Transparent
                : items.Count > 0 && date < today ? Ui.Res("Warning") : System.Windows.Media.Brushes.Transparent;
            panel.Children.Add(mark);

            var tile = new Border
            {
                Child = panel, Margin = new Thickness(5, 0, 5, 0), Padding = new Thickness(6, 10, 6, 8), CornerRadius = new CornerRadius(12),
                Background = isToday ? Ui.Res("Primary") : Ui.Res("Surface"),
                BorderBrush = isToday ? Ui.Res("Primary") : Ui.Res("BorderBrushSoft"), BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = items.Count == 0 ? "Нет заданий" : $"{Ui.Plural(items.Count, "задание", "задания", "заданий")}" + (allDone ? ", всё выполнено" : ""),
            };
            tile.MouseLeftButtonUp += (_, _) => Shell?.Navigate("week");
            grid.Children.Add(tile);
        }
        return grid;
    }

    // ---------- Top cards ----------

    private static UIElement BuildProgress(List<Assignment> todays)
    {
        var done = todays.Count(a => a.Progress?.IsCompleted == true);
        var panel = new StackPanel();
        panel.Children.Add(Ui.Header("Прогресс за сегодня"));

        var row = new DockPanel();
        var ring = new Grid { Width = 112, Height = 112, Margin = new Thickness(0, 0, 24, 0), VerticalAlignment = VerticalAlignment.Top };
        ring.Children.Add(Ui.Ring(todays.Count == 0 ? 0 : (double)done / todays.Count, 112, 10));
        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var count = Ui.Text($"{done}/{todays.Count}", size: 24, bold: true);
        count.HorizontalAlignment = HorizontalAlignment.Center;
        center.Children.Add(count);
        var caption = Ui.Text("заданий", "Muted", size: 12);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        center.Children.Add(caption);
        ring.Children.Add(center);
        DockPanel.SetDock(ring, Dock.Left);
        row.Children.Add(ring);

        var list = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (todays.Count == 0) list.Children.Add(Ui.Text("Сегодня заданий нет.", "Muted"));
        foreach (var a in todays.Take(5))
        {
            var isDone = a.Progress?.IsCompleted == true;
            var line = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var dot = StatusDot(isDone);
            dot.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(dot, Dock.Left);
            line.Children.Add(dot);
            line.Children.Add(new TextBlock
            {
                Text = a.Title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                Foreground = isDone ? Ui.Res("TextMuted") : Ui.Res("TextMain"),
            });
            list.Children.Add(line);
        }
        row.Children.Add(list);
        panel.Children.Add(row);
        return Ui.Card(panel);
    }

    private static Border StatusDot(bool done)
    {
        var dot = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(done ? 0 : 1.5),
            BorderBrush = Ui.Res("BorderStrong"),
            Background = done ? Ui.Res("Success") : Ui.Res("Surface"),
        };
        if (done)
        {
            var check = Ui.Icon(Icons.Check, 10, System.Windows.Media.Brushes.White);
            check.HorizontalAlignment = HorizontalAlignment.Center;
            dot.Child = check;
        }
        return dot;
    }

    private UIElement BuildWordOfDay(int studentId, DateOnly today)
    {
        var panel = new StackPanel();
        var wod = VocabularyService.WordOfDayForStudent(studentId, today);
        var headerIcon = Ui.Icon(Icons.Star, 16, Ui.Res("Primary"));
        panel.Children.Add(Ui.Header("Слово дня", wod != null && wod.Date != today ? Ui.Text(wod.Date.ToString("dd.MM"), "Muted", size: 12) : headerIcon));

        if (wod == null)
        {
            panel.Children.Add(Ui.Text("Преподаватель ещё не опубликовал слово дня.", "Muted"));
            return Ui.Card(panel);
        }

        panel.Children.Add(Ui.Text(wod.Word, size: 26, bold: true));
        panel.Children.Add(Ui.Text(wod.Translation, color: Ui.Res("TextSecondary"), size: 15, margin: new Thickness(0, 2, 0, 0)));
        if (wod.Example.Length > 0)
            panel.Children.Add(new Border
            {
                Background = Ui.Res("SurfaceMuted"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 12, 0, 0),
                Child = Ui.Text(wod.Example, color: Ui.Res("TextSecondary"), size: 13.5),
            });
        var add = Ui.Button("В мой словарь", () =>
        {
            VocabularyService.Add(studentId, wod.Word, wod.Translation, wod.Example);
            Ui.Info("Слово добавлено в словарь.");
        }, "SoftButton", icon: Icons.Add);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(add);
        return Ui.Card(panel);
    }

    // ---------- Bottom cards ----------

    private UIElement BuildTaskList(List<Assignment> todays, Dictionary<int, FrameworkElement> cards)
    {
        var done = todays.Count(a => a.Progress?.IsCompleted == true);
        var panel = new StackPanel();
        panel.Children.Add(Ui.Header("Задания на сегодня", Ui.Chip($"{done}/{todays.Count}", Ui.Res("PrimarySoft"), Ui.Res("Primary"))));
        if (todays.Count == 0) panel.Children.Add(Ui.Text("На сегодня ничего не запланировано.", "Muted"));

        for (var i = 0; i < todays.Count; i++)
        {
            var a = todays[i];
            var isDone = a.Progress?.IsCompleted == true;
            var row = new DockPanel { Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand };

            var dot = StatusDot(isDone);
            dot.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);

            var badge = a.Provider == "youtube"
                ? Ui.IconBadge(Icons.Video, Ui.Res("DangerSoft"), Ui.Res("Danger"), 38)
                : a.Exercises.Count > 0
                    ? KindStyle.Badge(a.Exercises.OrderBy(x => x.Order).First().Kind, 38)
                    : Ui.IconBadge(Icons.Book, Ui.Res("PrimarySoft"), Ui.Res("Primary"), 38);
            badge.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(badge, Dock.Left);
            row.Children.Add(badge);

            var chevron = Ui.Icon(Icons.ChevronRight, 12, Ui.Res("TextFaint"), new Thickness(12, 0, 0, 0));
            DockPanel.SetDock(chevron, Dock.Right);
            row.Children.Add(chevron);
            var minutes = Ui.Text($"{a.EstimatedMinutes} мин", "Muted", size: 12.5);
            minutes.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(minutes, Dock.Right);
            row.Children.Add(minutes);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = a.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var sub = a.Exercises.Count > 0
                ? Ui.Plural(a.Exercises.Count, "упражнение", "упражнения", "упражнений")
                : a.Mission.Length > 0 ? "Миссия" : "Материал";
            text.Children.Add(Ui.Text(sub, "Muted", size: 12.5));
            row.Children.Add(text);

            var id = a.Id;
            row.MouseLeftButtonUp += (_, _) => { if (cards.TryGetValue(id, out var card)) card.BringIntoView(); };

            panel.Children.Add(new Border
            {
                Child = row, Padding = new Thickness(0, 10, 0, 10),
                BorderBrush = Ui.Res("BorderBrushSoft"), BorderThickness = new Thickness(0, i == 0 ? 0 : 1, 0, 0),
            });
        }
        return Ui.Card(panel);
    }

    private UIElement BuildDueWords(int studentId, DateOnly today)
    {
        var due = VocabularyService.DueEntries(studentId, today);
        var panel = new StackPanel();
        panel.Children.Add(Ui.Header("Слова к повторению", Ui.Chip(Ui.Plural(due.Count, "слово", "слова", "слов"), Ui.Res("PrimarySoft"), Ui.Res("Primary"))));

        if (due.Count == 0)
        {
            var stats = VocabularyService.StatsFor(studentId, today);
            panel.Children.Add(Ui.Text("На сегодня повторять нечего.", "Muted"));
            panel.Children.Add(Ui.Text($"В словаре {stats.Total}, выучено {stats.Learned}.", "Muted", size: 12.5, margin: new Thickness(0, 4, 0, 0)));
            return Ui.Card(panel);
        }

        var list = new StackPanel();
        foreach (var (v, i) in due.Take(6).Select((v, i) => (v, i)))
        {
            var row = new DockPanel();
            var translation = new TextBlock
            {
                Text = v.Translation, Foreground = Ui.Res("TextMuted"), FontSize = 12.5, MaxWidth = 150,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(translation, Dock.Right);
            row.Children.Add(translation);
            row.Children.Add(new TextBlock { Text = v.Word, TextTrimming = TextTrimming.CharacterEllipsis });
            list.Children.Add(new Border
            {
                Child = row, Padding = new Thickness(0, 8, 0, 8),
                BorderBrush = Ui.Res("BorderBrushSoft"), BorderThickness = new Thickness(0, i == 0 ? 0 : 1, 0, 0),
            });
        }
        panel.Children.Add(list);
        if (due.Count > 6) panel.Children.Add(Ui.Text($"и ещё {due.Count - 6}", "Muted", size: 12.5, margin: new Thickness(0, 4, 0, 0)));

        var open = Ui.Button("Открыть карточки", () => Shell?.Navigate("review"), "PrimaryButton", icon: Icons.Repeat);
        open.HorizontalAlignment = HorizontalAlignment.Stretch;
        open.Margin = new Thickness(0, 14, 0, 0);
        open.MinHeight = 40;
        panel.Children.Add(open);
        return Ui.Card(panel);
    }

    private static Grid TwoColumns(UIElement left, UIElement right, double leftWeight, double rightWeight)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftWeight, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rightWeight, GridUnitType.Star) });
        grid.Children.Add(left);
        Grid.SetColumn((UIElement)right, 2);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>The freeze moves today's lesson to tomorrow without breaking the streak.</summary>
    private UIElement BuildFreeze(WeekPlan plan, int dayIndex)
    {
        var dayItems = plan.Assignments.Where(a => a.DayIndex == dayIndex).ToList();
        var (total, used) = ProgressService.FreezeBudget(plan);
        if (dayItems.Any(a => a.DeferredDays > 0 || a.Progress?.IsCompleted == true) || dayIndex >= WeekPlan.DaysInWeek - 1)
            return new Border();

        var dock = new DockPanel();
        var button = Ui.Button("Заморозить урок", () =>
        {
            if (!Ui.Confirm("Перенести сегодняшний урок на завтра?")) return;
            ProgressService.DeferDay(Session.User.Id, plan.Id, dayIndex);
            Reload();
        }, icon: Icons.Snow);
        button.IsEnabled = total - used > 0;
        button.Margin = new Thickness(16, 0, 0, 0);
        DockPanel.SetDock(button, Dock.Right);
        dock.Children.Add(button);

        var badge = Ui.IconBadge(Icons.Snow, Ui.Res("PrimarySoft"), Ui.Res("Primary"), 38);
        badge.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(badge, Dock.Left);
        dock.Children.Add(badge);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Ui.Text("Не успеваешь сегодня?", "H3"));
        text.Children.Add(Ui.Text($"Заморозка переносит урок на завтра и сохраняет серию. Осталось на неделю: {Math.Max(0, total - used)} из {total}.", "Muted", size: 13));
        dock.Children.Add(text);
        return Ui.Card(dock);
    }
}

public static class StudentWords
{
    /// <summary>Words substituted into «<<word>>» prompts: the ones still being learned, newest first.</summary>
    public static List<VocabularyEntry> ForPrompts(int studentId) =>
        VocabularyService.EntriesOf(studentId).Where(v => !v.IsLearned).ToList();
}
