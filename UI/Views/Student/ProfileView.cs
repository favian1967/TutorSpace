using System.Windows;
using System.Windows.Controls;
using TutorSpace.Data;
using TutorSpace.Services;
using TutorSpace.UI.Dialogs;

namespace TutorSpace.UI.Views.Student;

public class ProfileView : UserControl
{
    public ProfileView()
    {
        var root = new StackPanel { Margin = new Thickness(32, 28, 32, 32), MaxWidth = 800, HorizontalAlignment = HorizontalAlignment.Stretch };
        root.Children.Add(Ui.Text("Профиль", "H1", margin: new Thickness(0, 0, 0, 20)));

        var user = Session.User;
        var profile = ProgressService.GetProfile(user.Id);
        var streak = ProgressService.Streak(user.Id, Clock.Today);
        var tutors = AuthService.TutorsOf(user.Id);

        var info = new DockPanel();
        var password = Ui.Button("Сменить пароль", () => AccountDialogs.ChangePassword(this), icon: Icons.Lock);
        password.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(password, Dock.Right);
        info.Children.Add(password);
        var avatar = Ui.Avatar(user.DisplayName, 60);
        avatar.Margin = new Thickness(0, 0, 18, 0);
        DockPanel.SetDock(avatar, Dock.Left);
        info.Children.Add(avatar);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Ui.Text(user.DisplayName, "H2", margin: new Thickness(0)));
        names.Children.Add(Ui.Text("@" + user.Username, "Muted"));
        names.Children.Add(Ui.Text("Преподаватель: " + (tutors.Count == 0 ? "—" : string.Join(", ", tutors.Select(t => t.Tutor.DisplayName))),
            color: Ui.Res("TextSecondary"), margin: new Thickness(0, 6, 0, 0)));
        info.Children.Add(names);
        root.Children.Add(Ui.Card(info));

        root.Children.Add(Ui.Wrap(
            Ui.Tile("Серия", Ui.Plural(streak.Current, "день", "дня", "дней"), streak.ActiveToday ? "сегодня засчитано" : "зайди сегодня, чтобы продлить", Icons.Bolt),
            Ui.Tile("Лучшая серия", Ui.Plural(streak.Longest, "день", "дня", "дней"), null, Icons.Star)));

        var survey = new StackPanel();
        survey.Children.Add(Ui.Text("Анкета", "H2"));
        var interests = profile.Interests.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(k => PlannerService.Interests.FirstOrDefault(i => i.Key == k).Label ?? k);
        survey.Children.Add(Ui.Text("Интересы: " + string.Join(", ", interests)));
        survey.Children.Add(Ui.Text("Уровень: " + (profile.Level is { } level ? PlannerService.Label(level) : "—")));
        survey.Children.Add(Ui.Row(Ui.Button("Изменить анкету", () =>
        {
            if (OnboardingDialog.Run(this)) Content = new ProfileView().Content;
        })));
        root.Children.Add(Ui.Card(survey));

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
