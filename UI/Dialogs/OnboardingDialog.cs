using System.Windows;
using System.Windows.Controls;
using TutorSpace.Data;
using TutorSpace.Services;

namespace TutorSpace.UI.Dialogs;

/// <summary>The short survey a student fills in before anything else opens.</summary>
public static class OnboardingDialog
{
    public static bool Run(DependencyObject owner)
    {
        var profile = ProgressService.GetProfile(Session.User.Id);
        var chosen = profile.Interests.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        var form = new FormDialog("Давай познакомимся", owner, "Готово");
        form.AddNote("Две короткие вещи — чтобы преподаватель подбирал материалы под тебя.");
        form.AddElement(Ui.Text("Что тебе интересно?", "FieldLabel"));
        var checks = new List<(string Key, CheckBox Box)>();
        var wrap = new WrapPanel();
        foreach (var (key, label) in PlannerService.Interests)
        {
            var box = new CheckBox { Content = label, IsChecked = chosen.Contains(key), Margin = new Thickness(0, 4, 16, 4) };
            checks.Add((key, box));
            wrap.Children.Add(box);
        }
        form.AddElement(wrap);

        form.AddElement(Ui.Text("Твой уровень языка", "FieldLabel"));
        var levels = new List<(LanguageLevel Level, RadioButton Radio)>();
        foreach (var level in Enum.GetValues<LanguageLevel>())
        {
            var radio = new RadioButton { Content = PlannerService.Label(level), GroupName = "level", IsChecked = profile.Level == level, Margin = new Thickness(0, 4, 0, 0) };
            levels.Add((level, radio));
            form.AddElement(radio);
        }

        form.OnSubmit = () =>
        {
            var interests = checks.Where(c => c.Box.IsChecked == true).Select(c => c.Key).ToList();
            if (interests.Count == 0) throw new UserError("Выбери хотя бы один интерес.");
            var level = levels.FirstOrDefault(l => l.Radio.IsChecked == true);
            if (level.Radio == null) throw new UserError("Выбери уровень языка.");
            ProgressService.CompleteOnboarding(Session.User.Id, interests, level.Level);
        };
        return form.Run();
    }
}
