using System.Windows;
using System.Windows.Input;
using TutorSpace.Data;
using TutorSpace.Services;

namespace TutorSpace.UI.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        ShowDataPath();

        var empty = false;
        Ui.Try(() => empty = !AuthService.AnyUsers());
        if (empty) ShowRegister(firstRun: true);
        Loaded += (_, _) => (LoginPanel.Visibility == Visibility.Visible ? LoginBox : RegNameBox).Focus();
    }

    private void ShowDataPath()
    {
        try
        {
            var cs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(DbSettings.Current);
            DataPathText.Text = $"База: SQL Server {cs.DataSource}/{cs.InitialCatalog} · аудио: {AppPaths.MediaDir}";
        }
        catch (ArgumentException)
        {
            DataPathText.Text = "Строка подключения к БД повреждена — откройте настройки подключения.";
        }
    }

    private void DbSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!UI.Dialogs.DbSettingsDialog.Run(this)) return;
        ShowDataPath();
        var empty = false;
        Ui.Try(() => empty = !AuthService.AnyUsers());
        if (empty) ShowRegister(firstRun: true);
    }

    private void ShowRegister(bool firstRun)
    {
        LoginPanel.Visibility = Visibility.Collapsed;
        RegisterPanel.Visibility = Visibility.Visible;
        BackButton.Visibility = firstRun ? Visibility.Collapsed : Visibility.Visible;
        if (firstRun) RegisterTitle.Text = "Создайте аккаунт преподавателя";
    }

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        Ui.Try(() =>
        {
            Session.Current = AuthService.Login(LoginBox.Text, PasswordBox.Password);
            DialogResult = true;
        });
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Login_Click(sender, e);
    }

    private void ShowRegister_Click(object sender, RoutedEventArgs e) => ShowRegister(firstRun: false);

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        RegisterPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Visible;
    }

    private void Register_Click(object sender, RoutedEventArgs e)
    {
        Ui.Try(() =>
        {
            if (RegPasswordBox.Password != RegPassword2Box.Password) throw new UserError("Пароли не совпадают.");
            AuthService.Register(RegLoginBox.Text, RegPasswordBox.Password, RegNameBox.Text, UserRole.Tutor);
            Session.Current = AuthService.Login(RegLoginBox.Text, RegPasswordBox.Password);
            DialogResult = true;
        });
    }
}
