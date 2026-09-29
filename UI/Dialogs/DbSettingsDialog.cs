using System.Windows;
using Microsoft.EntityFrameworkCore;
using TutorSpace.Data;
using TutorSpace.Services;

namespace TutorSpace.UI.Dialogs;

/// <summary>SQL Server connection settings. Saving checks the connection and creates the tables.</summary>
public static class DbSettingsDialog
{
    public static bool Run(DependencyObject? owner, string? problem = null)
    {
        var current = DbSettings.Load();
        var form = new FormDialog("Подключение к SQL Server", owner, "Подключиться");
        if (problem != null) form.AddNote(problem);
        form.AddNote("Сервер: «.» — SQL Server на этом компьютере, «.\\SQLEXPRESS» — Express, «(localdb)\\MSSQLLocalDB» — LocalDB. "
                     + "Логин пустой — вход через учётную запись Windows. База и таблицы создаются автоматически.");
        var server = form.AddText("Сервер", current.Server);
        var database = form.AddText("База данных", current.Database);
        var user = form.AddText("Логин SQL (пусто — вход Windows)", current.Username);
        var password = form.AddPassword("Пароль SQL");
        password.Password = current.Password;

        form.OnSubmit = () =>
        {
            var settings = new DbSettings
            {
                Server = server.Text.Trim(),
                Database = database.Text.Trim(),
                Username = user.Text.Trim(),
                Password = password.Password,
            };
            if (settings.Server.Length == 0 || settings.Database.Length == 0)
                throw new UserError("Укажите сервер и базу данных.");
            var previous = DbSettings.Current;
            DbSettings.Current = settings.ConnectionString;
            try
            {
                EnsureDatabase();
            }
            catch (Exception ex)
            {
                DbSettings.Current = previous;
                throw new UserError("Не удалось подключиться:\n" + (Ui.Friendly(ex) ?? Describe(ex)));
            }
            settings.Save();
        };
        return form.Run();
    }

    /// <summary>
    /// First run: tries the usual local SQL Server instances with Windows authentication
    /// and remembers the first one that works. Throws the first error if none do.
    /// </summary>
    public static void EnsureDatabaseOrDetect()
    {
        if (DbSettings.HasSavedSettings || DbSettings.FromEnvironment)
        {
            EnsureDatabase();
            return;
        }

        Exception? first = null;
        foreach (var candidate in DbSettings.CandidateServers)
        {
            var settings = new DbSettings { Server = candidate };
            DbSettings.Current = settings.ConnectionString;
            try
            {
                EnsureDatabase();
                settings.Save();
                return;
            }
            catch (Exception ex)
            {
                first ??= ex;
            }
        }
        DbSettings.Current = new DbSettings().ConnectionString;
        throw first!;
    }

    /// <summary>
    /// Creates the database and tables on first use. A database in the old 18-table layout
    /// is never touched: the user is asked to point the program at another database instead.
    /// </summary>
    public static void EnsureDatabase()
    {
        using var db = new AppDbContext();
        if (db.Database.CanConnect() && IsOldLayout(db))
            throw new UserError($"База «{db.Database.GetDbConnection().Database}» создана старой версией программы (18 таблиц) и не подходит. " +
                                "Укажите другое имя базы — новая будет создана автоматически. Старую базу программа не изменяет.");
        db.Database.EnsureCreated();
    }

    private static bool IsOldLayout(AppDbContext db) =>
        db.Database.SqlQueryRaw<int>(
            "SELECT (SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN ('WeekPlans', 'StudentProfiles', 'Assignments'))" +
            " + (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Tasks' AND COLUMN_NAME = 'WeekPlanId') AS [Value]")
        .AsEnumerable().Single() > 0;

    public static string Describe(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException != null) inner = inner.InnerException;
        return inner.Message;
    }
}
