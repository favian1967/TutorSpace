using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TutorSpace.Data;
using TutorSpace.Services;

namespace TutorSpace.UI.Views.Tutor;

/// <summary>Everything about one student, one tab per question the tutor asks.</summary>
public class StudentDetailView : UserControl
{
    private readonly Enrollment _enrollment;

    public StudentDetailView(Enrollment enrollment)
    {
        _enrollment = enrollment;
        var dock = new DockPanel();
        var tabs = new TabControl();
        AddTab(tabs, "План недели", () => new WeekEditorView(enrollment));
        AddTab(tabs, "Обзор", () => new OverviewView(enrollment));
        AddTab(tabs, "Ответы", () => new AnswersView(enrollment));
        AddTab(tabs, "Оценки уроков", BuildReviews);
        AddTab(tabs, "Словарь", BuildVocabulary);
        AddTab(tabs, "Автоматика", BuildFires);
        dock.Children.Add(tabs);
        Content = dock;
    }

    /// <summary>Tabs are rebuilt each time they are opened, so they always show fresh data.</summary>
    private static void AddTab(TabControl tabs, string header, Func<UIElement> build)
    {
        var tab = new TabItem { Header = header };
        tabs.Items.Add(tab);
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source == tabs && tabs.SelectedItem == tab) tab.Content = build();
        };
        if (tabs.Items.Count == 1) tabs.SelectedIndex = 0;
    }

    private static DataGridTextColumn Column(string header, string path, double width = 0, bool wrap = false)
    {
        var column = new DataGridTextColumn { Header = header, Binding = new System.Windows.Data.Binding(path) };
        column.Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star);
        if (wrap)
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            column.ElementStyle = style;
        }
        return column;
    }

    private UIElement BuildReviews()
    {
        var rows = LessonService.ReviewsFor(_enrollment.Id).Select(r => new
        {
            Week = PlannerService.WeekLabel(r.WeekPlan.StartDate),
            Day = $"{PlannerService.DayShort[r.DayIndex]} {r.WeekPlan.DateForDay(r.DayIndex):dd.MM}",
            Kind = r.Verdict != null ? "Первый урок недели" : "Сравнение",
            Answer = LessonService.Describe(r) + (r.WentBadly ? "  (!)" : ""),
            r.Reason,
        }).ToList();

        var grid = new DataGrid { ItemsSource = rows, Margin = new Thickness(0, 12, 0, 0) };
        grid.Columns.Add(Column("Неделя", "Week", 170));
        grid.Columns.Add(Column("День", "Day", 90));
        grid.Columns.Add(Column("Вопрос", "Kind", 150));
        grid.Columns.Add(Column("Оценка", "Answer", 170));
        grid.Columns.Add(Column("Что не понравилось", "Reason", wrap: true));
        return rows.Count == 0 ? Empty("Ученик ещё не оценивал уроки.") : grid;
    }

    private UIElement BuildVocabulary()
    {
        var today = Clock.Today;
        var stats = VocabularyService.StatsFor(_enrollment.StudentId, today);
        var rows = VocabularyService.EntriesOf(_enrollment.StudentId).Select(v => new
        {
            v.Word, v.Translation, v.Example,
            Status = VocabularyService.Label(v.Status),
            Next = v.IsLearned ? "—" : v.NextReviewOn?.ToString("dd.MM.yyyy") ?? "—",
            Sm2 = $"EF {v.EaseFactor:0.00} · {v.IntervalDays} дн. · повт. {v.Repetitions} · провалов {v.Lapses}",
        }).ToList();

        var dock = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var header = Ui.Text($"Всего {stats.Total} · выучено {stats.Learned} · к повторению сегодня {stats.DueToday} · " +
                             $"повторено сегодня {stats.ReviewedToday} · добавлено за 7 дней {stats.AddedLast7Days}", "Muted",
            margin: new Thickness(0, 0, 0, 8));
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);

        var grid = new DataGrid { ItemsSource = rows };
        grid.Columns.Add(Column("Слово", "Word", 150));
        grid.Columns.Add(Column("Перевод", "Translation", 150));
        grid.Columns.Add(Column("Пример", "Example", wrap: true));
        grid.Columns.Add(Column("Статус", "Status", 90));
        grid.Columns.Add(Column("Следующее повторение", "Next", 160));
        grid.Columns.Add(Column("SM-2", "Sm2", 290));
        dock.Children.Add(grid);
        return dock;
    }

    private UIElement BuildFires()
    {
        var holder = new ContentControl();
        void Fill()
        {
            var fires = RuleService.FiresFor(_enrollment.Id);
            var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            panel.Children.Add(Ui.Text("Каждое срабатывание автоматических правил — чтобы автоматизация не была «магией».", "Muted"));
            var markSeen = Ui.Button("Отметить всё прочитанным", () =>
            {
                RuleService.MarkSeen(_enrollment.Id);
                Fill();
            }, icon: Icons.Check);
            markSeen.HorizontalAlignment = HorizontalAlignment.Left;
            markSeen.Margin = new Thickness(0, 8, 0, 12);
            panel.Children.Add(markSeen);
            if (fires.Count == 0) panel.Children.Add(Ui.Text("Правила ещё ни разу не срабатывали.", "Muted", margin: new Thickness(0, 16, 0, 0)));
            foreach (var f in fires)
            {
                var body = new StackPanel();
                body.Children.Add(Ui.Row(
                    Ui.Text(f.CreatedAt.ToString("dd.MM.yyyy HH:mm") + "  ", "Muted"),
                    f.IsSeen ? new TextBlock() : Ui.Chip("новое", Ui.Res("WarningSoft"), Ui.Res("Warning"))));
                body.Children.Add(Ui.Text(f.Detail, bold: true));
                body.Children.Add(Ui.Text("Правило: " + PlannerService.Describe(f.Rule), "Muted"));
                if (f.CreatedAssignment is { } created)
                    body.Children.Add(Ui.Text($"Создано задание на {created.WeekPlan.DateForDay(created.DayIndex):dd.MM.yyyy} ({PlannerService.DayShort[created.DayIndex]})",
                        color: Ui.Res("Primary")));
                panel.Children.Add(Ui.Card(body, f.IsSeen ? null : Ui.Res("PrimarySoft")));
            }
            holder.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
        Fill();
        return holder;
    }

    private static UIElement Empty(string text) =>
        Ui.Text(text, "Muted", margin: new Thickness(0, 16, 0, 0));
}
