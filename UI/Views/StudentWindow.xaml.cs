using System.Windows;
using System.Windows.Controls;
using TutorSpace.Services;
using TutorSpace.UI.Dialogs;
using TutorSpace.UI.Views.Student;

namespace TutorSpace.UI.Views;

public partial class StudentWindow : Window
{
    private bool _loggingOut;

    public StudentWindow()
    {
        InitializeComponent();
        UserNameText.Text = Session.User.DisplayName;
        AvatarHost.Child = Ui.Avatar(Session.User.DisplayName);
        Closed += (_, _) =>
        {
            AudioPlayer.Stop();
            if (!_loggingOut) Application.Current.Shutdown();
        };
        Loaded += (_, _) =>
        {
            // The onboarding survey gates the rest of the interface.
            if (!ProgressService.GetProfile(Session.User.Id).IsOnboarded && !OnboardingDialog.Run(this))
            {
                Logout();
                return;
            }
            Nav.SelectedIndex = 0;
        };
    }

    public void Navigate(string tag)
    {
        foreach (ListBoxItem item in Nav.Items)
            if ((string)item.Tag == tag) Nav.SelectedItem = item;
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not ListBoxItem item) return;
        AudioPlayer.Stop();
        Host.Content = item.Tag switch
        {
            "today" => new TodayView(),
            "week" => new StudentWeekView(),
            "review" => new ReviewView(),
            "words" => new VocabularyView(),
            "profile" => new ProfileView(),
            _ => null,
        };
    }

    private void Logout_Click(object sender, RoutedEventArgs e) => Logout();

    private void Logout()
    {
        _loggingOut = true;
        Close();
        App.ShowLogin();
    }
}
