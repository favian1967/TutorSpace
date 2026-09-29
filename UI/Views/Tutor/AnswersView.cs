using System.Windows;
using System.Windows.Controls;
using TutorSpace.Data;
using TutorSpace.Services;

namespace TutorSpace.UI.Views.Tutor;

/// <summary>The student's answers to exercises and missions, with the tutor's comments.</summary>
public class AnswersView : UserControl
{
    private readonly Enrollment _enrollment;
    private readonly StackPanel _list = new();
    private readonly CheckBox _onlyUnchecked = new() { Content = "Только без комментария", Margin = new Thickness(0, 0, 16, 0) };
    private readonly CheckBox _showMissions = new() { Content = "Показать ответы на миссии", IsChecked = true };

    public AnswersView(Enrollment enrollment)
    {
        _enrollment = enrollment;
        var root = new StackPanel { Margin = new Thickness(0, 12, 12, 24) };
        root.Children.Add(Ui.Text("Автоматически проверяются только пропуски. Остальное — прочитайте или прослушайте и оставьте комментарий (без оценки).", "Muted"));
        var filters = Ui.Row(_onlyUnchecked, _showMissions);
        filters.Margin = new Thickness(0, 8, 0, 12);
        root.Children.Add(filters);
        root.Children.Add(_list);
        _onlyUnchecked.Click += (_, _) => Reload();
        _showMissions.Click += (_, _) => Reload();
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Reload();
    }

    private void Reload()
    {
        _list.Children.Clear();
        var attempts = OverviewService.AnswersFor(_enrollment.Id);
        if (_onlyUnchecked.IsChecked == true) attempts = attempts.Where(a => !a.IsAutoChecked && a.TutorComment.Length == 0).ToList();

        if (_showMissions.IsChecked == true && _onlyUnchecked.IsChecked != true)
        {
            var missions = OverviewService.MissionAnswersFor(_enrollment.Id);
            if (missions.Count > 0)
            {
                var panel = new StackPanel();
                panel.Children.Add(Ui.Text("Миссии и комментарии к заданиям", "H2"));
                foreach (var a in missions)
                {
                    var p = a.Progress!;
                    panel.Children.Add(Ui.Text($"{a.WeekPlan.DateForDay(a.DayIndex):dd.MM} · {a.Title}" + (p.IsCompleted ? "  — выполнено" : ""), bold: true, margin: new Thickness(0, 8, 0, 0)));
                    if (a.Mission.Length > 0) panel.Children.Add(Ui.Text("Миссия: " + a.Mission, "Muted"));
                    if (p.MissionResponse.Length > 0) panel.Children.Add(Ui.Text("Ответ: " + p.MissionResponse));
                    if (p.StudentComment.Length > 0) panel.Children.Add(Ui.Text("Комментарий ученика: " + p.StudentComment, color: Ui.Res("Primary")));
                }
                _list.Children.Add(Ui.Card(panel));
            }
        }

        if (attempts.Count == 0)
            _list.Children.Add(Ui.Text("Ответов на упражнения нет.", "Muted", margin: new Thickness(0, 8, 0, 0)));

        foreach (var attempt in attempts) _list.Children.Add(BuildAttempt(attempt));
    }

    private UIElement BuildAttempt(ExerciseAttempt attempt)
    {
        var ex = attempt.Exercise;
        var a = ex.Assignment;
        var panel = new StackPanel();

        var head = new WrapPanel();
        head.Children.Add(Ui.Text($"{a.WeekPlan.DateForDay(a.DayIndex):dd.MM} · {a.Title} · {PlannerService.Label(ex.Kind)} · попытка {attempt.AttemptNo}  ", bold: true));
        if (attempt.IsAutoChecked)
            head.Children.Add(attempt.IsPassed
                ? Ui.Chip($"{attempt.Score}/{attempt.MaxScore} верно", Ui.Res("SuccessSoft"), Ui.Res("Success"))
                : Ui.Chip($"{attempt.Score}/{attempt.MaxScore}", Ui.Res("DangerSoft"), Ui.Res("Danger")));
        else if (attempt.TutorComment.Length == 0)
            head.Children.Add(Ui.Chip("ждёт комментария", Ui.Res("WarningSoft"), Ui.Res("Warning")));
        panel.Children.Add(head);
        panel.Children.Add(Ui.Text(attempt.CreatedAt.ToString("dd.MM.yyyy HH:mm"), "Muted", size: 12));

        var prompt = ex.Kind == ExerciseKind.GapFill ? ex.Config.Template : ex.Prompt;
        panel.Children.Add(Ui.Text("Задание: " + prompt, "Muted", margin: new Thickness(0, 6, 0, 0)));
        panel.Children.Add(Ui.Text(ExerciseService.DescribeAnswer(attempt), margin: new Thickness(0, 6, 0, 0)));

        if (attempt.Audio != null || ex.Audio != null)
        {
            var row = new WrapPanel();
            if (attempt.Audio != null) row.Children.Add(Ui.Button("Запись ученика", () => AudioPlayer.Play(attempt.Audio), icon: Icons.Play));
            if (ex.Audio != null) row.Children.Add(Ui.Button("Оригинал", () => AudioPlayer.Play(ex.Audio), icon: Icons.Play));
            row.Children.Add(Ui.IconButton(Icons.Stop, AudioPlayer.Stop, "Стоп"));
            panel.Children.Add(row);
        }

        var comment = Ui.Box(attempt.TutorComment, multiline: true, minHeight: 40);
        panel.Children.Add(Ui.Text("Ваш комментарий", "FieldLabel"));
        panel.Children.Add(comment);
        panel.Children.Add(Ui.Row(Ui.Button("Сохранить комментарий", () =>
        {
            ExerciseService.Comment(attempt.Id, comment.Text);
            attempt.TutorComment = comment.Text.Trim();
            Ui.Info("Комментарий сохранён — ученик увидит его под своим ответом.");
        })));
        return Ui.Card(panel);
    }
}
