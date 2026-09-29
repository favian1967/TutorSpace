using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using TutorSpace.Data;
using TutorSpace.Services;
using TutorSpace.UI;
using TutorSpace.UI.Dialogs;
using TutorSpace.UI.Views;

namespace TutorSpace;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        // Russian dates and number formats everywhere, including DatePicker.
        var russian = new CultureInfo("ru-RU");
        CultureInfo.DefaultThreadCurrentCulture = russian;
        CultureInfo.DefaultThreadCurrentUICulture = russian;
        Thread.CurrentThread.CurrentCulture = russian;
        Thread.CurrentThread.CurrentUICulture = russian;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(russian.IetfLanguageTag)));

        AppPaths.Ensure();
        try
        {
            DbSettingsDialog.EnsureDatabaseOrDetect();
        }
        catch (Exception ex)
        {
            // No server found or no rights: let the user fix the settings (the dialog re-checks them).
            var problem = "Не удалось подключиться к SQL Server:\n" + DbSettingsDialog.Describe(ex)
                          + "\n\nПроверьте, что служба SQL Server запущена, и укажите параметры подключения.";
            if (!DbSettingsDialog.Run(null, problem))
            {
                Shutdown(1);
                return;
            }
        }

        ShowLogin();
    }

    /// <summary>Login → the role's main window → back to login on logout.</summary>
    public static void ShowLogin()
    {
        Session.Current = null;
        var login = new LoginWindow();
        if (login.ShowDialog() != true || Session.Current == null)
        {
            Current.Shutdown();
            return;
        }

        Window main = Session.User.Role == UserRole.Tutor ? new TutorWindow() : new StudentWindow();
        Current.MainWindow = main;
        main.Show();
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Ui.ShowError(e.Exception);
        e.Handled = true;
    }
}
