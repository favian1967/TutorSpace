using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TutorSpace.Data;
using TutorSpace.Services;

namespace TutorSpace.UI.Views.Tutor;

/// <summary>The student's card: visits, words, assignments, answers, ratings, automation.</summary>
public class OverviewView : UserControl
{
    public OverviewView(Enrollment enrollment)
    {
        var o = OverviewService.For(enrollment);
        var root = new StackPanel { Margin = new Thickness(0, 12, 12, 24) };
        root.Children.Add(Ui.Text($"Поведение — за последние {OverviewService.WindowWeeks} недели; словарь — за всё время.", "Muted", margin: new Thickness(0, 0, 0, 12)));

        var tiles = new WrapPanel();
        tiles.Children.Add(Ui.Tile("Серия дней", o.Streak.Current.ToString(), $"лучшая: {o.Streak.Longest}{(o.Streak.ActiveToday ? " · сегодня занимался" : "")}"));
        tiles.Children.Add(Ui.Tile("Заходил в программу", Ui.Plural(o.VisitDays, "день", "дня", "дней"),
            o.LastVisit is { } lv ? $"последний раз {lv:dd.MM.yyyy} · всего {o.LifetimeVisitDays}" : "ещё ни разу"));
        tiles.Children.Add(Ui.Tile("Задания", $"{o.Assignments.Completed} / {o.Assignments.Released}",
            $"выполнено из открытых · просрочено {o.Assignments.Overdue} · всего {o.Assignments.Total}"));
        tiles.Children.Add(Ui.Tile("Ответы на упражнения", o.Answers.Total.ToString(),
            $"ждут комментария {o.Answers.AwaitingCheck} · с ошибками {o.Answers.Failed}"));
        tiles.Children.Add(Ui.Tile("Словарь", o.Words.Total.ToString(),
            $"выучено {o.Words.Learned} · к повторению {o.Words.DueToday} · +{o.Words.AddedLast7Days} за неделю"));
        tiles.Children.Add(Ui.Tile("Оценки уроков", o.Rated.ToString(),
            o.WentBadly > 0 ? $"плохо/хуже: {o.WentBadly}" : "жалоб нет"));
        tiles.Children.Add(Ui.Tile("Срабатывания правил", o.Flags.ToString(), o.UnseenFlags > 0 ? $"новых: {o.UnseenFlags}" : "всё прочитано"));
        root.Children.Add(tiles);

        if (o.LastReview is { } last)
        {
            var text = $"Последняя оценка ({last.WeekPlan.DateForDay(last.DayIndex):dd.MM}): {LessonService.Describe(last)}"
                       + (last.Reason.Length > 0 ? " — " + last.Reason : "");
            root.Children.Add(Ui.Card(Ui.Text(text, color: Ui.Res(last.WentBadly ? "Danger" : "TextMain"))));
        }

        root.Children.Add(Ui.Card(BuildCalendar(o.Visits)));

        var profile = new StackPanel();
        profile.Children.Add(Ui.Text("Анкета ученика", "H2"));
        if (!o.Profile.IsOnboarded)
        {
            profile.Children.Add(Ui.Text("Ученик ещё не заполнил анкету (заполнит при первом входе).", "Muted"));
        }
        else
        {
            var interests = o.Profile.Interests.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(k => PlannerService.Interests.FirstOrDefault(i => i.Key == k).Label ?? k);
            profile.Children.Add(Ui.Text("Интересы: " + string.Join(", ", interests)));
            profile.Children.Add(Ui.Text("Уровень: " + (o.Profile.Level is { } l ? PlannerService.Label(l) : "—")));
        }
        root.Children.Add(Ui.Card(profile));

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>Four weeks of day squares, Monday first — filled when the student opened the app.</summary>
    private static UIElement BuildCalendar(List<SiteVisit> visits)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Text("Посещения", "H2"));
        var days = visits.ToDictionary(v => v.Date);
        var today = Clock.Today;
        var start = Clock.MondayOf(today).AddDays(-7 * (OverviewService.WindowWeeks - 1));

        var header = new UniformGridRow();
        foreach (var d in PlannerService.DayShort) header.Add(Ui.Text(d, "Muted", size: 12));
        panel.Children.Add(header.Panel);

        for (var week = 0; week < OverviewService.WindowWeeks; week++)
        {
            var row = new UniformGridRow();
            for (var day = 0; day < 7; day++)
            {
                var date = start.AddDays(week * 7 + day);
                var visited = days.TryGetValue(date, out var visit);
                var cell = new Border
                {
                    Height = 30, Margin = new Thickness(2), CornerRadius = new CornerRadius(5),
                    Background = date > today ? Brushes.Transparent : visited ? Ui.Res("Primary") : Ui.Res("AppBackground"),
                    BorderBrush = date == today ? Ui.Res("Primary") : Ui.Res("BorderBrushSoft"),
                    BorderThickness = new Thickness(date == today ? 2 : 1),
                    ToolTip = visited ? $"{date:dd.MM}: заходил {Ui.Plural(visit!.Hits, "раз", "раза", "раз")}" : $"{date:dd.MM}",
                    Child = new TextBlock
                    {
                        Text = date.Day.ToString(), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center, Foreground = visited ? Brushes.White : Ui.Res("TextMuted"),
                    },
                };
                row.Add(cell);
            }
            panel.Children.Add(row.Panel);
        }
        return panel;
    }

    private sealed class UniformGridRow
    {
        public readonly System.Windows.Controls.Primitives.UniformGrid Panel = new() { Columns = 7, Width = 420, HorizontalAlignment = HorizontalAlignment.Left };
        public void Add(UIElement e) => Panel.Children.Add(e);
    }
}
