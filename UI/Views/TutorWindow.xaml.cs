using System.Windows;
using System.Windows.Controls;
using TutorSpace.Services;
using TutorSpace.UI.Dialogs;
using TutorSpace.UI.Views.Tutor;

namespace TutorSpace.UI.Views;

public partial class TutorWindow : Window
{
    private bool _loggingOut;

    public TutorWindow()
    {
        InitializeComponent();
        UserNameText.Text = Session.User.DisplayName;
        AvatarHost.Child = Ui.Avatar(Session.User.DisplayName);
        Nav.SelectedIndex = 0;
        Closed += (_, _) =>
        {
            AudioPlayer.Stop();
            if (!_loggingOut) Application.Current.Shutdown();
        };
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not ListBoxItem item) return;
        Host.Content = item.Tag switch
        {
            "students" => new StudentsView(),
            "templates" => new TemplatesView(),
            "sandbox" => new SandboxView(),
            "library" => new LibraryView(),
            "wordofday" => new WordOfDayView(),
            _ => null,
        };
    }

    private void ChangePassword_Click(object sender, RoutedEventArgs e) => AccountDialogs.ChangePassword(this);

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        _loggingOut = true;
        Close();
        App.ShowLogin();
    }
}

public static class AccountDialogs
{
    public static void ChangePassword(DependencyObject owner)
    {
        var form = new FormDialog("Смена пароля", owner);
        var old = form.AddPassword("Текущий пароль");
        var fresh = form.AddPassword("Новый пароль");
        var again = form.AddPassword("Новый пароль ещё раз");
        form.OnSubmit = () =>
        {
            if (fresh.Password != again.Password) throw new UserError("Пароли не совпадают.");
            AuthService.ChangePassword(Session.User.Id, old.Password, fresh.Password);
        };
        if (form.Run()) Ui.Info("Пароль изменён.");
    }
}
