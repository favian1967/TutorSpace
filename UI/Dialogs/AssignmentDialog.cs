using System.Windows;
using System.Windows.Controls;
using TutorSpace.Data;
using TutorSpace.Services;

namespace TutorSpace.UI.Dialogs;

/// <summary>Edit one assignment: its fields, micro-mission, exercises and automatic rules.</summary>
public class AssignmentDialog : Window
{
    private Assignment _assignment;
    private readonly ComboBox _day = new();
    private readonly TextBox _title = Ui.Box();
    private readonly TextBox _url = Ui.Box();
    private readonly TextBox _description = Ui.Box(multiline: true, minHeight: 80);
    private readonly ComboBox _missionKind = new();
    private readonly TextBox _mission = Ui.Box();
    private readonly TextBox _minutes = Ui.Box();
    private readonly StackPanel _exercises = new();
    private readonly StackPanel _rules = new();
    private readonly TextBlock _extrasHint = Ui.Text("Сохраните задание, чтобы добавлять упражнения и правила.", "Muted");

    public AssignmentDialog(Assignment assignment, DependencyObject owner)
    {
        _assignment = assignment;
        Title = assignment.Id == 0 ? "Новое задание" : "Задание: " + assignment.Title;
        Width = 780;
        Height = 820;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Ui.SetOwner(this, owner);
        Style = (Style)FindResource(typeof(Window));

        foreach (var name in PlannerService.DayNames) _day.Items.Add(name);
        foreach (var kind in Enum.GetValues<MissionKind>())
            _missionKind.Items.Add(new ComboItem(kind, PlannerService.Label(kind)));
        _missionKind.SelectionChanged += (_, _) =>
        {
            if (_missionKind.SelectedItem is ComboItem { Value: MissionKind kind })
            {
                var preset = PlannerService.DefaultMission(kind);
                if (preset.Length > 0 && (_mission.Text.Length == 0 || Enum.GetValues<MissionKind>().Any(k => PlannerService.DefaultMission(k) == _mission.Text)))
                    _mission.Text = preset;
            }
        };

        var form = new StackPanel { Margin = new Thickness(20) };
        form.Children.Add(Ui.Text("Задание", "H2"));

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        var dayPanel = Field("День", _day);
        var titlePanel = Field("Заголовок", _title);
        top.Children.Add(dayPanel);
        Grid.SetColumn(titlePanel, 2);
        top.Children.Add(titlePanel);
        form.Children.Add(top);

        form.Children.Add(Field("Ссылка на материал (YouTube определяется автоматически; можно оставить пустой)", _url));
        form.Children.Add(Field("Описание", _description));

        var mission = new Grid();
        mission.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        mission.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        mission.ColumnDefinitions.Add(new ColumnDefinition());
        mission.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        mission.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        var kindPanel = Field("Микро-миссия", _missionKind);
        var missionPanel = Field("Формулировка миссии", _mission);
        var minutesPanel = Field("Минут", _minutes);
        mission.Children.Add(kindPanel);
        Grid.SetColumn(missionPanel, 2);
        mission.Children.Add(missionPanel);
        Grid.SetColumn(minutesPanel, 4);
        mission.Children.Add(minutesPanel);
        form.Children.Add(mission);
        form.Children.Add(Ui.Text("Если есть миссия и нет упражнений, ученик не сможет закрыть задание без ответа на миссию.", "Muted", size: 12));

        form.Children.Add(Ui.Row(Ui.Button("Сохранить задание", Save, "PrimaryButton")));

        form.Children.Add(Ui.Text("Упражнения", "H2", margin: new Thickness(0, 20, 0, 4)));
        form.Children.Add(_extrasHint);
        form.Children.Add(_exercises);

        form.Children.Add(Ui.Text("Автоматические правила", "H2", margin: new Thickness(0, 20, 0, 4)));
        form.Children.Add(Ui.Text("Например: «ошибся в упражнении — повторить через 7 дней». Срабатывают только в ответ на действия ученика.", "Muted"));
        form.Children.Add(_rules);

        var close = new Button { Content = "Закрыть", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 8, 20, 16) };
        close.Click += (_, _) => Close();
        var dock = new DockPanel();
        DockPanel.SetDock(close, Dock.Bottom);
        dock.Children.Add(close);
        dock.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = dock;

        Fill();
    }

    private static StackPanel Field(string label, UIElement input)
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Text(label, "FieldLabel"));
        panel.Children.Add(input);
        return panel;
    }

    private void Fill()
    {
        var a = _assignment;
        _day.SelectedIndex = a.DayIndex;
        _title.Text = a.Title;
        _url.Text = a.Url;
        _description.Text = a.Description;
        foreach (ComboItem item in _missionKind.Items)
            if ((MissionKind)item.Value == a.MissionKind) _missionKind.SelectedItem = item;
        _mission.Text = a.Id == 0 && a.Mission.Length == 0 ? PlannerService.DefaultMission(a.MissionKind) : a.Mission;
        _minutes.Text = a.EstimatedMinutes.ToString();
        RenderExtras();
    }

    private void Save()
    {
        var data = new Assignment
        {
            Id = _assignment.Id,
            WeekPlanId = _assignment.WeekPlanId,
            DayIndex = _day.SelectedIndex,
            Title = _title.Text,
            Url = _url.Text,
            Description = _description.Text,
            MissionKind = (MissionKind)((ComboItem)_missionKind.SelectedItem).Value,
            Mission = _mission.Text,
            EstimatedMinutes = FormDialog.ParseInt(_minutes, "Минут", 0, 600),
        };
        var saved = PlannerService.SaveAssignment(data);
        _assignment = PlannerService.GetAssignment(saved.Id)!;
        Title = "Задание: " + _assignment.Title;
        RenderExtras();
    }

    private void Refresh()
    {
        _assignment = PlannerService.GetAssignment(_assignment.Id)!;
        RenderExtras();
    }

    private void RenderExtras()
    {
        _exercises.Children.Clear();
        _rules.Children.Clear();
        var saved = _assignment.Id != 0;
        _extrasHint.Visibility = saved ? Visibility.Collapsed : Visibility.Visible;
        if (!saved) return;

        foreach (var ex in _assignment.Exercises.OrderBy(x => x.Order))
        {
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Ui.Text(PlannerService.Label(ex.Kind), bold: true));
            var prompt = ex.Kind == ExerciseKind.GapFill ? ex.Config.Template : ex.Prompt;
            text.Children.Add(Ui.Text(prompt.Length > 160 ? prompt[..160] + "…" : prompt, "Muted"));
            if (ex.Audio != null) text.Children.Add(Ui.Text("Аудио: " + ex.Audio.DisplayName, "Muted", size: 12));
            var info = new DockPanel();
            var badge = KindStyle.Badge(ex.Kind, 34);
            badge.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(badge, Dock.Left);
            info.Children.Add(badge);
            info.Children.Add(text);
            _exercises.Children.Add(ItemRow(info,
                Ui.Button("Изменить", () => { new ExerciseDialog(ex, this).ShowDialog(); Refresh(); }),
                Ui.IconButton(Icons.Delete, () =>
                {
                    if (!Ui.Confirm("Удалить упражнение вместе с ответами ученика?")) return;
                    ExerciseService.Delete(ex.Id);
                    Refresh();
                }, "Удалить", danger: true)));
        }
        _exercises.Children.Add(Ui.Row(Ui.Button("Добавить упражнение", () =>
        {
            new ExerciseDialog(new Exercise { AssignmentId = _assignment.Id }, this).ShowDialog();
            Refresh();
        }, "SoftButton", icon: Icons.Add)));

        foreach (var rule in _assignment.Rules)
        {
            _rules.Children.Add(ItemRow(Ui.Text(PlannerService.Describe(rule)),
                Ui.Button("Изменить", () => { if (RuleDialog.Edit(rule, this)) Refresh(); }),
                Ui.IconButton(Icons.Delete, () => { PlannerService.DeleteRule(rule.Id); Refresh(); }, "Удалить", danger: true)));
        }
        _rules.Children.Add(Ui.Row(Ui.Button("Добавить правило", () =>
        {
            if (RuleDialog.Edit(new AssignmentRule { AssignmentId = _assignment.Id, Trigger = RuleTrigger.ExerciseFailed }, this)) Refresh();
        }, "SoftButton", icon: Icons.Add)));
    }

    private static UIElement ItemRow(UIElement info, params Button[] buttons)
    {
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var actions = Ui.Row(buttons);
        actions.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(actions, Dock.Right);
        row.Children.Add(actions);
        row.Children.Add(info);
        return new Border
        {
            Child = row, Padding = new Thickness(10), Margin = new Thickness(0, 6, 0, 0), CornerRadius = new CornerRadius(8),
            Background = Ui.Res("AppBackground"),
        };
    }
}

public static class RuleDialog
{
    public static bool Edit(AssignmentRule rule, DependencyObject owner)
    {
        var form = new FormDialog(rule.Id == 0 ? "Новое правило" : "Правило", owner);
        var trigger = form.AddCombo("Когда", Enum.GetValues<RuleTrigger>().Select(t => (t, PlannerService.Label(t))), rule.Trigger);
        var threshold = form.AddText("Порог, % (для упражнений: «ошибся» — результат ниже порога, «справился» — не ниже)", rule.ThresholdPercent.ToString());
        var action = form.AddCombo("Что сделать", Enum.GetValues<RuleAction>().Select(a => (a, PlannerService.Label(a))), rule.Action);
        var days = form.AddText("Через сколько дней повторить", rule.Days.ToString());
        var note = form.AddText("Заметка для себя (для «Отметить преподавателю»)", rule.Note);
        var once = form.AddCheck("Сработать только один раз", rule.FireOnce);
        var active = form.AddCheck("Правило включено", rule.IsActive);
        form.OnSubmit = () => PlannerService.SaveRule(new AssignmentRule
        {
            Id = rule.Id,
            AssignmentId = rule.AssignmentId,
            Trigger = FormDialog.Selected<RuleTrigger>(trigger),
            Action = FormDialog.Selected<RuleAction>(action),
            ThresholdPercent = FormDialog.ParseInt(threshold, "Порог", 0, 100),
            Days = FormDialog.ParseInt(days, "Дней", 1, 365),
            Note = note.Text.Trim(),
            FireOnce = once.IsChecked == true,
            IsActive = active.IsChecked == true,
        });
        return form.Run();
    }
}
