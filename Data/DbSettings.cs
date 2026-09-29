using Microsoft.Data.SqlClient;

namespace TutorSpace.Data;

/// <summary>
/// SQL Server connection settings, kept in %LOCALAPPDATA%\TutorSpace\settings.json.
/// An empty user name means Windows authentication. The TUTORSPACE_DB environment
/// variable (a full connection string) overrides the file.
/// </summary>
public class DbSettings
{
    public string Server { get; set; } = ".";
    public string Database { get; set; } = "TutorSpace";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool WindowsAuth => string.IsNullOrWhiteSpace(Username);

    /// <summary>Servers tried on the very first run, before any settings are saved.</summary>
    public static readonly string[] CandidateServers = { ".", @".\SQLEXPRESS", @"(localdb)\MSSQLLocalDB" };

    public static string SettingsFile => Path.Combine(AppPaths.DataDir, "settings.json");

    /// <summary>False for a missing file or one left over from the PostgreSQL version.</summary>
    public static bool HasSavedSettings => File.Exists(SettingsFile) && File.ReadAllText(SettingsFile).Contains("\"Server\"");

    public static bool FromEnvironment => Environment.GetEnvironmentVariable("TUTORSPACE_DB") is { Length: > 0 };

    public static DbSettings Load() =>
        HasSavedSettings ? Json.Read<DbSettings>(File.ReadAllText(SettingsFile)) : new DbSettings();

    public void Save()
    {
        AppPaths.Ensure();
        File.WriteAllText(SettingsFile, Json.Write(this));
    }

    /// <summary>Built from the fields above; never written to settings.json.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ConnectionString
    {
        get
        {
            var cs = new SqlConnectionStringBuilder
            {
                DataSource = Server,
                InitialCatalog = Database,
                TrustServerCertificate = true,
                ConnectTimeout = 5,
            };
            if (WindowsAuth)
                cs.IntegratedSecurity = true;
            else
            {
                cs.UserID = Username;
                cs.Password = Password;
            }
            return cs.ConnectionString;
        }
    }

    /// <summary>The connection string every <see cref="AppDbContext"/> uses.</summary>
    public static string Current { get; set; } =
        Environment.GetEnvironmentVariable("TUTORSPACE_DB") is { Length: > 0 } env ? env : Load().ConnectionString;
}
