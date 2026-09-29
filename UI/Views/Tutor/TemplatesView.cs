using System.Windows;
using System.Windows.Controls;
using TutorSpace.Data;
using TutorSpace.Services;
using TutorSpace.UI.Dialogs;

namespace TutorSpace.UI.Views.Tutor;

/// <summary>Saved weeks, ready to be dropped onto any student from any Monday.</summary>
public class TemplatesView : UserControl
{
    private readonly StackPanel _list = new();

    public TemplatesView()
    {
        var root = new StackPanel { Margin = new Thickness(32, 28, 32, 32) };
        root.Children.Add(Ui.Text("Шаблоны недель", "H1"));
        root.Children.Add(Ui.Text("Удачную неделю можно сохранить как шаблон («План недели» → «Сохранить как шаблон») и применить к любому ученику с любого понедельника.", "Muted", margin: new Thickness(0, 0, 0, 16)));
        root.Children.Add(_list);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Reload();
    }

    private void Reload()
    {
        _list.Children.Clear();
        var templates = TemplateService.TemplatesOf(Session.User.Id);
        if (templates.Count == 0) _list.Children.Add(Ui.Text("Шаблонов пока нет.", "Muted"));

        foreach (var t in templates)
        {
            var panel = new StackPanel();
            panel.Children.Add(Ui.Text(t.Name, "H2"));
            if (t.Description.Length > 0) panel.Children.Add(Ui.Text(t.Description, "Muted"));
            panel.Children.Add(Ui.Text($"{Ui.Plural(t.Assignments.Count, "задание", "задания", "заданий")} · применён {Ui.Plural(t.TimesUsed, "раз", "раза", "раз")} · обновлён {t.UpdatedAt:dd.MM.yyyy}", "Muted", size: 12));

            var details = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var group in t.Assignments.OrderBy(a => a.Order).GroupBy(a => a.DayIndex).OrderBy(g => g.Key))
                details.Children.Add(Ui.Text($"{PlannerService.DayShort[group.Key]}: " +
                    string.Join("; ", group.Select(a => a.Title + (a.Exercises.Count > 0 ? $" ({a.Exercises.Count} упр.)" : ""))), size: 13));
            panel.Children.Add(details);

            panel.Children.Add(Ui.Wrap(
                Ui.Button("Применить к ученику…", () => Apply(t), "PrimaryButton"),
                Ui.Button("Переименовать…", () => Rename(t)),
                Ui.Button("Удалить", () =>
                {
                    if (!Ui.Confirm($"Удалить шаблон «{t.Name}»? Недели, созданные из него, останутся.")) return;
                    TemplateService.DeleteTemplate(t.Id);
                    Reload();
                }, "DangerButton")));
            _list.Children.Add(Ui.Card(panel));
        }
    }

    private void Apply(WeekPlan template)
    {
        var target = TargetPicker.Pick(this, $"Применить «{template.Name}»");
        if (target == null) return;
        if (TargetPicker.WriteWithConfirm(target, replace =>
                TemplateService.ApplyTemplate(Session.User.Id, template.Id, target.EnrollmentId, target.Monday, replace)))
        {
            Ui.Info("Неделя создана. Откройте ученика → «План недели».");
            Reload();
        }
    }

    private void Rename(WeekPlan template)
    {
        var form = new FormDialog("Шаблон", this);
        var name = form.AddText("Название", template.Name);
        var description = form.AddText("Описание", template.Description, multiline: true);
        form.OnSubmit = () => TemplateService.RenameTemplate(template.Id, name.Text, description.Text);
        if (form.Run()) Reload();
    }
}
