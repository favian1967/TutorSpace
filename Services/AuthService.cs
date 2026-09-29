using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TutorSpace.Data;

namespace TutorSpace.Services;

/// <summary>A rejected action. The message is safe to show the user.</summary>
public class UserError : Exception
{
    public UserError(string message) : base(message) { }
}

/// <summary>Database failures translated into something a person can act on.</summary>
public static class Errors
{
    /// <summary>Turns database failures into something a person can act on.</summary>
    public static string? Friendly(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is Microsoft.Data.SqlClient.SqlException sql)
                return sql.Number switch
                {
                    547 => "Связанная запись уже удалена (например, в другом окне). Обновите экран и попробуйте ещё раз.",
                    2601 or 2627 => "Такая запись уже существует.",
                    4060 => "База данных не найдена. Проверьте настройки подключения (окно входа → «Настройки подключения к БД»).",
                    18456 => "SQL Server отклонил вход. Проверьте настройки подключения (окно входа → «Настройки подключения к БД»).",
                    -2 or 2 or 53 or -1 => "Нет связи с SQL Server. Проверьте, что служба SQL Server запущена, и настройки подключения (окно входа → «Настройки подключения к БД»).",
                    _ => null,
                };
            if (e is ArgumentException && e.StackTrace?.Contains("SqlClient") == true)
                return "Строка подключения к БД повреждена. Откройте окно входа → «Настройки подключения к БД».";
            if (e is System.Net.Sockets.SocketException or TimeoutException)
                return "Нет связи с SQL Server. Проверьте, что служба SQL Server запущена, и настройки подключения (окно входа → «Настройки подключения к БД»).";
        }
        return null;
    }

}

/// <summary>Who is using the app right now.</summary>
public static class Session
{
    public static User? Current { get; set; }
    public static User User => Current ?? throw new InvalidOperationException("Нет входа в систему.");
}

public static class PasswordHasher
{
    private const int Iterations = 100_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = (stored ?? "").Split('$');
        if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations) || iterations is < 1 or > 10_000_000) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            if (expected.Length == 0) return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            // A damaged hash is a wrong password, not a crash.
            return false;
        }
    }
}

public static class AuthService
{
    public const int MaxLogin = 50;
    public const int MaxName = 150;
    public const int MaxPassword = 200;

    public static bool AnyUsers()
    {
        using var db = new AppDbContext();
        return db.Users.Any();
    }

    public static User Login(string username, string password)
    {
        using var db = new AppDbContext();
        var name = username.Trim().ToLower();
        var user = db.Users.FirstOrDefault(u => u.Username.ToLower() == name);
        if (user == null || !PasswordHasher.Verify(password, user.PasswordHash))
            throw new UserError("Неверный логин или пароль.");
        if (user.Role == UserRole.Student)
            ActivityService.RecordVisit(user.Id);
        return user;
    }

    public static User Register(string username, string password, string fullName, UserRole role)
    {
        username = username.Trim();
        fullName = fullName.Trim();
        if (username.Length < 3) throw new UserError("Логин — минимум 3 символа.");
        if (username.Length > MaxLogin) throw new UserError($"Логин — не больше {MaxLogin} символов.");
        if (password.Length < 4) throw new UserError("Пароль — минимум 4 символа.");
        if (password.Length > MaxPassword) throw new UserError($"Пароль — не больше {MaxPassword} символов.");
        if (fullName.Length > MaxName) throw new UserError($"Имя — не больше {MaxName} символов.");

        using var db = new AppDbContext();
        var lower = username.ToLower();
        if (db.Users.Any(u => u.Username.ToLower() == lower))
            throw new UserError("Такой логин уже занят.");

        var user = new User
        {
            Username = username,
            PasswordHash = PasswordHasher.Hash(password),
            FullName = fullName.Trim(),
            Role = role,
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    /// <summary>Locally the tutor creates the student account and the link in one step.</summary>
    public static User CreateStudent(int tutorId, string username, string password, string fullName)
    {
        var student = Register(username, password, fullName, UserRole.Student);
        using var db = new AppDbContext();
        db.Enrollments.Add(new Enrollment { TutorId = tutorId, StudentId = student.Id });
        db.SaveChanges();
        return student;
    }

    /// <summary>Attach an existing student account to one more tutor.</summary>
    public static void EnrollExisting(int tutorId, string username)
    {
        using var db = new AppDbContext();
        var lower = username.Trim().ToLower();
        var student = db.Users.FirstOrDefault(u => u.Username.ToLower() == lower && u.Role == UserRole.Student)
            ?? throw new UserError("Ученика с таким логином нет.");
        var existing = db.Enrollments.FirstOrDefault(e => e.TutorId == tutorId && e.StudentId == student.Id);
        if (existing != null)
        {
            if (existing.IsActive) throw new UserError("Этот ученик уже у вас.");
            existing.IsActive = true;
        }
        else
        {
            db.Enrollments.Add(new Enrollment { TutorId = tutorId, StudentId = student.Id });
        }
        db.SaveChanges();
    }

    public static void ChangePassword(int userId, string oldPassword, string newPassword)
    {
        using var db = new AppDbContext();
        var user = db.Users.Find(userId) ?? throw new UserError("Пользователь не найден.");
        if (!PasswordHasher.Verify(oldPassword, user.PasswordHash)) throw new UserError("Старый пароль неверен.");
        if (newPassword.Length < 4) throw new UserError("Пароль — минимум 4 символа.");
        if (newPassword.Length > MaxPassword) throw new UserError($"Пароль — не больше {MaxPassword} символов.");
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        db.SaveChanges();
    }

    /// <summary>The tutor resets a student's password (there is no e-mail locally).</summary>
    public static void ResetStudentPassword(int studentId, string newPassword)
    {
        if (newPassword.Length < 4) throw new UserError("Пароль — минимум 4 символа.");
        if (newPassword.Length > MaxPassword) throw new UserError($"Пароль — не больше {MaxPassword} символов.");
        using var db = new AppDbContext();
        var user = db.Users.Find(studentId) ?? throw new UserError("Пользователь не найден.");
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        db.SaveChanges();
    }

    public static List<Enrollment> StudentsOf(int tutorId, bool includeInactive = false)
    {
        using var db = new AppDbContext();
        return db.Enrollments.AsNoTracking()
            .Include(e => e.Student)
            .Where(e => e.TutorId == tutorId && (includeInactive || e.IsActive))
            .OrderBy(e => e.Student.FullName).ThenBy(e => e.Student.Username)
            .ToList();
    }

    public static List<Enrollment> TutorsOf(int studentId)
    {
        using var db = new AppDbContext();
        return db.Enrollments.AsNoTracking()
            .Include(e => e.Tutor)
            .Where(e => e.StudentId == studentId && e.IsActive)
            .ToList();
    }

    public static void SetEnrollmentActive(int enrollmentId, bool active)
    {
        using var db = new AppDbContext();
        var enrollment = db.Enrollments.Find(enrollmentId) ?? throw new UserError("Связь не найдена.");
        enrollment.IsActive = active;
        db.SaveChanges();
    }
}
