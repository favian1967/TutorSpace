using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TutorSpace.Data;
using TutorSpace.Services;
using TutorSpace.UI.Dialogs;

namespace TutorSpace.UI.Views.Tutor;

/// <summary>The tutor's weekly planner for one student: seven day columns plus the week's settings.</summary>
public class WeekEditorView : UserControl
{
    private readonly Enrollment _enrollment;
    private DateOnly _monday = Clock.MondayOf(Clock.Today);
    private WeekPlan? _plan;
    private readonly HashSet<int> _selected = new();
    private readonly StackPanel _root = new() { Margin = new Thickness(0, 0, 16, 24) };
    private TextBlock _selectedLabel = new();

    public WeekEditorView(Enrollment enrollment)
    {
        _enrollment = enrollment;
        Content = new ScrollViewer
        {
            Content = _root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Reload();
    }

    private void Reload()
    {
        _plan = PlannerService.GetPlan(_enrollment.Id, _monday);
        _selected.RemoveWhere(id => _plan?.Assignments.All(a => a.Id != id) ?? true);
        _root.Children.Clear();
        _root.Children.Add(BuildNavigator(_plan));

        if (_plan == null)
        {
            BuildEmpty();
            return;
        }
        _root.Children.Add(BuildBulkBar(_plan));
        _root.Children.Add(BuildBoard(_plan));

        var bottom = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.Children.Add(BuildSettings(_plan));
        var questions = BuildCallQuestions(_plan);
        Grid.SetColumn(questions, 2);
        bottom.Children.Add(questions);
        _root.Children.Add(bottom);
    }

    // ---------- Top bar ----------

    private UIElement BuildNavigator(WeekPlan? plan)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };

        if (plan != null)
        {
            var publish = plan.IsPublished
                ? Ui.Button("Опубликовано", () => SetPublished(plan, false), "SoftButton", "Нажмите, чтобы скрыть неделю от ученика", Icons.Check)
                : Ui.Button("Опубликовать", () => SetPublished(plan, true), "PrimaryButton", "Пока неделя не опубликована, ученик не видит ни одного задания");
            var actions = Ui.Row(
                Ui.Button("Копировать", () => CopyWeek(plan), icon: Icons.Copy),
                Ui.Button("В шаблон", () => SaveTemplate(plan), tooltip: "Сохранить неделю как шаблон", icon: Icons.Save),
                publish);
            actions.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(actions, Dock.Right);
            dock.Children.Add(actions);
        }

        var label = Ui.Text(PlannerService.WeekLabel(_monday), size: 17, bold: true, margin: new Thickness(6, 4, 6, 0));
        var nav = Ui.Row(
            Ui.IconButton(Icons.ChevronLeft, () => { _monday = _monday.AddDays(-7); Reload(); }, "Предыдущая неделя"),
            label,
            Ui.IconButton(Icons.ChevronRight, () => { _monday = _monday.AddDays(7); Reload(); }, "Следующая неделя"),
            Ui.Button("Текущая неделя", () => { _monday = Clock.MondayOf(Clock.Today); Reload(); }),
            Ui.Button("Все недели…", PickWeek));
        dock.Children.Add(nav);
        return dock;
    }

    private void SetPublished(WeekPlan plan, bool published)
    {
        PlannerService.UpdatePlan(plan.Id, plan.Title, plan.TutorNotes, published, plan.ReviewAt, plan.IntroRequired);
        Reload();
    }

    private void PickWeek()
    {
        var plans = PlannerService.PlansOf(_enrollment.Id);
        if (plans.Count == 0) throw new UserError("У ученика ещё нет ни одного плана.");
        var form = new FormDialog("Перейти к неделе", this, "Открыть");
        var combo = form.AddCombo("Неделя", plans.Select(p =>
            (p.StartDate, $"{PlannerService.WeekLabel(p.StartDate)}  {p.Title}  ({Ui.Plural(p.Assignments.Count, "задание", "задания", "заданий")})")));
        form.OnSubmit = () => _monday = FormDialog.Selected<DateOnly>(combo);
        if (form.Run()) Reload();
    }

    private void BuildEmpty()
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 24) };
        var badge = Ui.IconBadge(Icons.Calendar, Ui.Res("PrimarySoft"), Ui.Res("Primary"), 52);
        badge.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(badge);
        var title = Ui.Text("На эту неделю плана нет", "H2", margin: new Thickness(0, 14, 0, 4));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(title);
        var hint = Ui.Text("Создайте пустой план, примените сохранённый шаблон или вставьте текст в «Песочнице».", "Muted");
        hint.TextAlignment = TextAlignment.Center;
        panel.Children.Add(hint);
        var buttons = Ui.Row(
            Ui.Button("Создать пустой план", () =>
            {
                PlannerService.CreatePlan(_enrollment.Id, _monday);
                Reload();
            }, "PrimaryButton", icon: Icons.Add),
            Ui.Button("Применить шаблон…", ApplyWeekTemplate, icon: Icons.Template),
            Ui.Button("Скопировать с прошлой недели", CopyFromPrevious, icon: Icons.Copy));
        buttons.HorizontalAlignment = HorizontalAlignment.Center;
        buttons.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(buttons);
        _root.Children.Add(Ui.Card(panel));
    }

    private void ApplyWeekTemplate()
    {
        var templates = TemplateService.TemplatesOf(Session.User.Id);
        if (templates.Count == 0) throw new UserError("Шаблонов пока нет. Сохраните удачную неделю как шаблон.");
        var form = new FormDialog("Применить шаблон", this, "Применить");
        var combo = form.AddCombo("Шаблон", templates.Select(t => (t.Id, $"{t.Name} ({Ui.Plural(t.AssignmentCount, "задание", "задания", "заданий")})")));
        form.OnSubmit = () => TemplateService.ApplyTemplate(Session.User.Id, FormDialog.Selected<int>(combo), _enrollment.Id, _monday, replace: true);
        if (form.Run()) Reload();
    }

    private void CopyFromPrevious()
    {
        var previous = PlannerService.GetPlan(_enrollment.Id, _monday.AddDays(-7))
                       ?? throw new UserError("На прошлой неделе плана нет.");
        TemplateService.CopyWeek(Session.User.Id, previous.Id, _enrollment.Id, _monday, replace: true);
        Reload();
    }

    // ---------- Bulk actions ----------

    private UIElement BuildBulkBar(WeekPlan plan)
    {
        var dayCombo = new ComboBox { Width = 150, Margin = new Thickness(0, 4, 8, 0) };
        foreach (var name in PlannerService.DayNames) dayCombo.Items.Add(name);
        dayCombo.SelectedIndex = 0;

        _selectedLabel = Ui.Text($"Выбрано: {_selected.Count}", "Muted", margin: new Thickness(0, 11, 12, 0));
        var bulk = Ui.Row(
            Ui.Button("Задание", () => EditAssignment(new Assignment { WeekPlanId = plan.Id, DayIndex = 0 }), "PrimaryButton", icon: Icons.Add),
            new Border { Width = 1, Background = Ui.Res("BorderBrushSoft"), Margin = new Thickness(4, 8, 12, 4) },
            _selectedLabel,
            dayCombo,
            Ui.Button("Переместить", () =>
            {
                if (_selected.Count == 0) throw new UserError("Отметьте задания галочками.");
                PlannerService.MoveAssignments(plan.Id, _selected, dayCombo.SelectedIndex);
                Reload();
            }),
            Ui.Button("Удалить выбранные", () =>
            {
                if (_selected.Count == 0) throw new UserError("Отметьте задания галочками.");
                if (!Ui.Confirm($"Удалить {Ui.Plural(_selected.Count, "задание", "задания", "заданий")} вместе с упражнениями?")) return;
                PlannerService.DeleteAssignments(plan.Id, _selected);
                _selected.Clear();
                Reload();
            }, "DangerButton"));

        var shift = Ui.Row(
            Ui.Button("На день раньше", () => { PlannerService.ShiftWeek(plan.Id, -1); Reload(); }, tooltip: "Сдвинуть все задания недели на день раньше", icon: Icons.Back),
            Ui.Button("На день позже", () => { PlannerService.ShiftWeek(plan.Id, 1); Reload(); }, tooltip: "Сдвинуть все задания недели на день позже", icon: Icons.Forward));

        var wrap = Ui.Wrap(bulk, shift);
        wrap.Margin = new Thickness(0, 0, 0, 12);
        return wrap;
    }

    // ---------- Board ----------

    private UIElement BuildBoard(WeekPlan plan)
    {
        var grid = new Grid();
        for (var day = 0; day < WeekPlan.DaysInWeek; day++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var column = BuildDay(plan, day);
            column.Margin = new Thickness(day == 0 ? 0 : 5, 0, day == WeekPlan.DaysInWeek - 1 ? 0 : 5, 0);
            Grid.SetColumn(column, day);
            grid.Children.Add(column);
        }
        return grid;
    }

    private Border BuildDay(WeekPlan plan, int day)
    {
        var date = plan.DateForDay(day);
        var isToday = date == Clock.Today;
        var items = plan.Assignments.Where(a => a.DayIndex == day).OrderBy(a => a.Order).ThenBy(a => a.Id).ToList();
        var panel = new StackPanel();

        var header = new StackPanel { Margin = new Thickness(0, 2, 0, 10) };
        var name = Ui.Text(PlannerService.DayShort[day], bold: true, color: isToday ? Ui.Res("Primary") : null);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        header.Children.Add(name);
        var dateText = Ui.Text(isToday ? $"{date:dd.MM} · сегодня" : date.ToString("dd.MM"), size: 12, color: isToday ? Ui.Res("Primary") : Ui.Res("TextMuted"));
        dateText.HorizontalAlignment = HorizontalAlignment.Center;
        header.Children.Add(dateText);
        panel.Children.Add(header);

        foreach (var a in items) panel.Children.Add(BuildItem(plan, a));

        if (plan.LessonReviews.FirstOrDefault(r => r.DayIndex == day) is { } review)
        {
            var text = LessonService.Describe(review) + (review.Reason.Length > 0 ? ": " + review.Reason : "");
            panel.Children.Add(new Border
            {
                Background = Ui.Res(review.WentBadly ? "DangerSoft" : "SuccessSoft"), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 8),
                Child = Ui.Text(text, size: 11.5, color: Ui.Res(review.WentBadly ? "Danger" : "Success")),
                ToolTip = "Оценка урока учеником",
            });
        }

        panel.Children.Add(AddButton(() => EditAssignment(new Assignment { WeekPlanId = plan.Id, DayIndex = day })));

        return new Border
        {
            Child = panel, Padding = new Thickness(6, 10, 6, 10), CornerRadius = new CornerRadius(12),
            Background = isToday ? Ui.Res("PrimarySoft") : Ui.Res("Surface"),
            BorderBrush = isToday ? Ui.Res("PrimaryBorder") : Ui.Res("BorderBrushSoft"), BorderThickness = new Thickness(1),
        };
    }

    private UIElement BuildItem(WeekPlan plan, Assignment a)
    {
        var check = new CheckBox { IsChecked = _selected.Contains(a.Id), Margin = new Thickness(0, 0, 0, 0), ToolTip = "Выбрать для переноса или удаления" };
        check.Click += (_, e) =>
        {
            if (check.IsChecked == true) _selected.Add(a.Id); else _selected.Remove(a.Id);
            _selectedLabel.Text = $"Выбрано: {_selected.Count}";
            e.Handled = true;
        };

        var badge = a.Provider == "youtube"
            ? Ui.IconBadge(Icons.Video, Ui.Res("DangerSoft"), Ui.Res("Danger"), 26)
            : a.Exercises.Count > 0
                ? KindStyle.Badge(a.Exercises.OrderBy(x => x.Order).First().Kind, 26)
                : Ui.IconBadge(Icons.Book, Ui.Res("PrimarySoft"), Ui.Res("Primary"), 26);

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(check, Dock.Right);
        top.Children.Add(check);
        badge.HorizontalAlignment = HorizontalAlignment.Left;
        top.Children.Add(badge);

        var info = new StackPanel();
        info.Children.Add(top);
        var title = Ui.Text(a.Title, size: 13, bold: true);
        title.TextWrapping = TextWrapping.WrapWithOverflow;
        info.Children.Add(title);
        var meta = new List<string> { $"{a.EstimatedMinutes} мин" };
        if (a.Exercises.Count > 0) meta.Add($"{a.Exercises.Count} упр.");
        if (a.Rules.Count > 0) meta.Add(Ui.Plural(a.Rules.Count, "правило", "правила", "правил"));
        info.Children.Add(Ui.Text(string.Join(" · ", meta), "Muted", size: 11.5, margin: new Thickness(0, 2, 0, 0)));

        var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        if (a.Progress?.IsCompleted == true) chips.Children.Add(SmallChip("выполнено", "SuccessSoft", "Success"));
        if (a.DeferredDays > 0) chips.Children.Add(SmallChip($"перенесено на {a.ReleaseDate:dd.MM}", "WarningSoft", "Warning"));
        if (a.RepeatOfId != null) chips.Children.Add(SmallChip("повтор", "PrimarySoft", "Primary"));
        if (a.Progress is { MissionResponse.Length: > 0 }) chips.Children.Add(SmallChip("есть ответ", "VioletSoft", "Violet"));
        if (chips.Children.Count > 0) info.Children.Add(chips);

        var card = new Border
        {
            Child = info, Background = Ui.Res("Surface"), BorderBrush = Ui.Res("BorderBrushSoft"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 9, 8, 9), Margin = new Thickness(0, 0, 0, 8), Cursor = Cursors.Hand,
            ToolTip = a.Mission.Length > 0 ? "Миссия: " + a.Mission : "Открыть задание",
        };
        card.MouseEnter += (_, _) => card.BorderBrush = Ui.Res("PrimaryBorder");
        card.MouseLeave += (_, _) => card.BorderBrush = Ui.Res("BorderBrushSoft");
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && IsInside(source, check)) return;
            EditAssignment(a);
        };
        return card;
    }

    private static bool IsInside(DependencyObject element, DependencyObject container)
    {
        for (var node = element; node != null; node = VisualTreeHelper.GetParent(node))
            if (node == container) return true;
        return false;
    }

    private static Border SmallChip(string text, string background, string foreground)
    {
        var chip = Ui.Chip(text, Ui.Res(background), Ui.Res(foreground));
        chip.Padding = new Thickness(6, 1, 6, 2);
        var label = (TextBlock)chip.Child;
        label.FontSize = 11;
        label.TextWrapping = TextWrapping.Wrap;
        return chip;
    }

    /// <summary>A dashed «add» slot at the bottom of a day column.</summary>
    private static UIElement AddButton(Action onClick)
    {
        var grid = new Grid { Cursor = Cursors.Hand, Background = Brushes.Transparent, MinHeight = 40, ToolTip = "Добавить задание на этот день" };
        grid.Children.Add(new Rectangle
        {
            RadiusX = 10, RadiusY = 10, Stroke = Ui.Res("BorderStrong"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 },
        });
        var label = Ui.Row(Ui.Icon(Icons.Add, 11, Ui.Res("TextMuted"), new Thickness(0, 1, 6, 0)), Ui.Text("Добавить", "Muted", size: 12.5));
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(label);
        grid.MouseEnter += (_, _) => ((Rectangle)grid.Children[0]).Stroke = Ui.Res("Primary");
        grid.MouseLeave += (_, _) => ((Rectangle)grid.Children[0]).Stroke = Ui.Res("BorderStrong");
        grid.MouseLeftButtonUp += (_, _) => Ui.Try(onClick);
        return grid;
    }

    private void EditAssignment(Assignment assignment)
    {
        new AssignmentDialog(assignment, this).ShowDialog();
        Reload();
    }

    // ---------- Settings ----------

    private Border BuildSettings(WeekPlan plan)
    {
        var title = Ui.Box(plan.Title);
        var notes = Ui.Box(plan.TutorNotes, multiline: true, minHeight: 96);
        var intro = new CheckBox { Content = Ui.Text("Голосовая самопрезентация в начале недели (30–60 с, 3 попытки)"), IsChecked = plan.IntroRequired };
        var review = new DatePicker { SelectedDate = plan.ReviewAt, FirstDayOfWeek = DayOfWeek.Monday, HorizontalAlignment = HorizontalAlignment.Stretch };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var left = new StackPanel();
        left.Children.Add(Ui.Text("Название недели", "FieldLabel"));
        left.Children.Add(title);
        left.Children.Add(Ui.Text($"Созвон-разбор (по умолчанию — воскресенье {plan.EndDate:dd.MM})", "FieldLabel"));
        left.Children.Add(review);
        var introWrap = new Border { Margin = new Thickness(0, 10, 0, 0), Child = intro };
        left.Children.Add(introWrap);

        var right = new StackPanel();
        right.Children.Add(Ui.Text("Заметки к неделе (ученик их не видит)", "FieldLabel"));
        right.Children.Add(notes);

        grid.Children.Add(left);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        var panel = new StackPanel();
        var save = Ui.Button("Сохранить", () =>
        {
            PlannerService.UpdatePlan(plan.Id, title.Text, notes.Text, plan.IsPublished, review.SelectedDate, intro.IsChecked == true);
            Reload();
        }, "PrimaryButton");
        save.Margin = new Thickness(0);
        panel.Children.Add(Ui.Header("Настройки недели", save));
        ((FrameworkElement)panel.Children[0]).Margin = new Thickness(0, 0, 0, 0);
        panel.Children.Add(grid);

        if (plan.IntroRequired)
        {
            var intros = LessonService.Intros(plan.Id);
            panel.Children.Add(Ui.Text($"Самопрезентация: {Ui.Plural(intros.Count, "запись", "записи", "записей")} из {WeeklyIntro.MaxAttempts}", "FieldLabel"));
            var row = new WrapPanel();
            foreach (var i in intros)
                row.Children.Add(Ui.Button($"Попытка {i.AttemptNo} ({i.DurationSeconds} с)", () => AudioPlayer.Play(i.Audio), icon: Icons.Play));
            if (intros.Count > 0) row.Children.Add(Ui.IconButton(Icons.Stop, AudioPlayer.Stop, "Стоп"));
            panel.Children.Add(row);
        }

        var footer = Ui.Row(Ui.Button("Удалить план недели", () =>
        {
            if (!Ui.Confirm("Удалить весь план недели со всеми заданиями, ответами и оценками?")) return;
            PlannerService.DeletePlan(plan.Id);
            Reload();
        }, "DangerButton", icon: Icons.Delete));
        footer.Margin = new Thickness(0, 12, 0, 0);
        panel.Children.Add(footer);
        return Ui.Card(panel);
    }

    private void SaveTemplate(WeekPlan plan)
    {
        var form = new FormDialog("Сохранить неделю как шаблон", this);
        form.AddNote("Шаблон — это снимок недели. Дальнейшие правки недели его не изменят. Шаблон с тем же названием перезапишется.");
        var name = form.AddText("Название", plan.Title);
        var description = form.AddText("Описание", multiline: true);
        form.OnSubmit = () => TemplateService.SaveAsTemplate(Session.User.Id, plan.Id, name.Text, description.Text);
        if (form.Run()) Ui.Info("Шаблон сохранён.");
    }

    private void CopyWeek(WeekPlan plan)
    {
        var target = TargetPicker.Pick(this, "Копировать неделю", _enrollment.Id, _monday.AddDays(7));
        if (target == null) return;
        if (target.EnrollmentId == _enrollment.Id && target.Monday == _monday) throw new UserError("Это та же самая неделя.");
        if (TargetPicker.WriteWithConfirm(target, replace => TemplateService.CopyWeek(Session.User.Id, plan.Id, target.EnrollmentId, target.Monday, replace)))
            Ui.Info("Неделя скопирована.");
    }

    // ---------- Call questions ----------

    private Border BuildCallQuestions(WeekPlan plan)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Header("Вопросы к созвону", Ui.Chip(plan.ReviewDate.ToString("dd.MM"), Ui.Res("PrimarySoft"), Ui.Res("Primary"))));
        ((FrameworkElement)panel.Children[0]).Margin = new Thickness(0, 0, 0, 4);
        panel.Children.Add(Ui.Text("Видны только вам. Отмечайте заданные вопросы прямо во время созвона.", "Muted", size: 12.5, margin: new Thickness(0, 0, 0, 8)));

        foreach (var q in plan.CallQuestions.OrderBy(q => q.Order))
        {
            var check = new CheckBox { Content = Ui.Text(q.Text), IsChecked = q.IsAsked, Margin = new Thickness(0, 8, 8, 0) };
            check.Click += (_, _) => PlannerService.SetQuestionAsked(plan.Id, q.Id, check.IsChecked == true);
            var row = new DockPanel();
            var delete = Ui.IconButton(Icons.Delete, () => { PlannerService.DeleteCallQuestion(plan.Id, q.Id); Reload(); }, "Удалить", danger: true);
            delete.Width = delete.Height = 30;
            DockPanel.SetDock(delete, Dock.Right);
            row.Children.Add(delete);
            row.Children.Add(check);
            panel.Children.Add(row);
        }

        var box = Ui.Box();
        var add = Ui.Button("Добавить", () =>
        {
            PlannerService.AddCallQuestion(plan.Id, box.Text);
            Reload();
        }, icon: Icons.Add);
        add.Margin = new Thickness(8, 0, 0, 0);
        var addRow = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(add, Dock.Right);
        addRow.Children.Add(add);
        addRow.Children.Add(box);
        panel.Children.Add(addRow);
        return Ui.Card(panel);
    }
}
