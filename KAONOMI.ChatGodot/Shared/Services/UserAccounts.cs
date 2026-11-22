using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using KAONOMI.Chat.Models;

namespace KAONOMI.Chat.Services;

/// <summary>The three levels of the users of the chat, from the least to the most rights.</summary>
public enum UserRole
{
    /// <summary>Looks: the chat and the windows, but no SAVE / DELETE / PUBLISH button and no system command.</summary>
    Viewer,

    /// <summary>Works: everything but the management of the users.</summary>
    User,

    /// <summary>Everything, with the management of the users (<c>/users</c>).</summary>
    Administrator
}

/// <summary>A user of the chat as kept in <c>users.json</c>: never its password, only its salted hash (PBKDF2).</summary>
public sealed class UserAccount
{
    public string Login { get; set; } = string.Empty;
    public UserRole Role { get; set; }

    /// <summary>The salt of the password (Base64, 16 bytes, its own for each user and each password).</summary>
    public string Salt { get; set; } = string.Empty;

    /// <summary>PBKDF2-HMAC-SHA256 of the password with <see cref="Salt"/> (Base64, 32 bytes).</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>The rounds of PBKDF2 this hash was made with (kept: the number may grow for the next passwords).</summary>
    public int Iterations { get; set; }

    public DateTimeOffset Created { get; set; }
    public DateTimeOffset? LastLogin { get; set; }

    // ── The profile, filled by the user itself (/user) ──

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;

    /// <summary>Who the user is, in its own words (job, interests…).</summary>
    public string Profile { get; set; } = string.Empty;

    /// <summary>Its hashtags, each with its # (<c>#ia</c>, <c>#cyber</c>).</summary>
    public List<string> Hashtags { get; set; } = [];

    /// <summary>The file of its photo (a square PNG in <see cref="UserStore.PhotoFolder"/>); empty: none.</summary>
    public string Photo { get; set; } = string.Empty;

    /// <summary>First and last name, or the login when there is neither.</summary>
    [JsonIgnore]
    public string DisplayName => $"{FirstName} {LastName}".Trim() is { Length: > 0 } name ? name : Login;
}

/// <summary>The profile of a user as it is typed in /user (<see cref="UserStore.SaveProfile"/>).</summary>
public sealed record UserProfile(string FirstName, string LastName, string Email, string Phone, string Profile, string Hashtags);

/// <summary>
/// The users of the chat, in <c>~/kaonomi/users.json</c> (read and written by its owner only): their login, their level
/// and the salted hash of their password. When the file is missing or holds no user, the user <c>admin</c> (password
/// <c>admin</c>, administrator) is created, so that somebody can always get in. There is always one administrator at
/// least: the last one is neither deleted nor given a lower level. The methods that change something return null when
/// done, else why not.
/// </summary>
public static class UserStore
{
    public const string DefaultLogin = "admin";
    private const string DefaultPassword = "admin";
    private const int Rounds = 210_000;
    public const int MinPasswordLength = 4;

    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>The file of the users (another one for a test: <c>--users-file=</c>).</summary>
    public static string FilePath { get; set; } = Path.Combine(ChatSettings.KaonomiHome, "users.json");

    /// <summary>The folder of the photos of the users (<c>~/kaonomi/users/photos</c>, beside the default file of the users).</summary>
    public static string PhotoFolder => Path.Combine(Path.GetDirectoryName(FilePath)!, "users", "photos");

    /// <summary>The path of the photo of <paramref name="user"/>, or null when it has none (or its file is gone).</summary>
    public static string? PhotoPath(UserAccount user) =>
        user.Photo.Length > 0 && Path.Combine(PhotoFolder, Path.GetFileName(user.Photo)) is var path && File.Exists(path) ? path : null;

    /// <summary>The file the photo of <paramref name="login"/> is written to (a PNG named after its login).</summary>
    public static string PhotoFileFor(string login)
    {
        Directory.CreateDirectory(PhotoFolder);
        return Path.Combine(PhotoFolder, login.Trim().ToLowerInvariant() + ".png");
    }

    /// <summary>The user of <paramref name="login"/>, read again from the file (null: none).</summary>
    public static UserAccount? Get(string login)
    {
        lock (Gate)
            return Find(Load(), login);
    }

    private static readonly Regex EmailShape = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant);
    private static readonly Regex PhoneShape = new(@"^\+?[0-9 ().\-/]{6,24}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The profile of <paramref name="login"/> as typed: the e-mail and the phone checked when given, the hashtags
    /// (separated by spaces or commas, with or without #) each written <c>#word</c>, without twice the same.
    /// </summary>
    public static string? SaveProfile(string login, UserProfile profile)
    {
        var email = profile.Email.Trim();
        var phone = profile.Phone.Trim();
        if (email.Length > 0 && !EmailShape.IsMatch(email))
            return $"\"{email}\" is not an e-mail address";
        if (phone.Length > 0 && !PhoneShape.IsMatch(phone))
            return $"\"{phone}\" is not a phone number (digits, spaces, + . - ( ) /)";
        lock (Gate)
        {
            var users = Load();
            if (Find(users, login) is not { } user)
                return $"The user {login} does not exist";
            user.FirstName = profile.FirstName.Trim();
            user.LastName = profile.LastName.Trim();
            user.Email = email;
            user.Phone = phone;
            user.Profile = profile.Profile.Trim();
            user.Hashtags = ParseHashtags(profile.Hashtags);
            Save(users);
            return null;
        }
    }

    /// <summary>The hashtags of <paramref name="text"/>: <c>ia, #Cyber  défense</c> → <c>#ia #Cyber #défense</c>.</summary>
    public static List<string> ParseHashtags(string text) =>
        [.. text.Split([' ', ',', ';', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => tag.TrimStart('#').Trim())
            .Where(tag => tag.Length > 0 && tag.All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
            .Select(tag => "#" + tag)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The photo of <paramref name="login"/>: <paramref name="file"/> (a file of <see cref="PhotoFolder"/>), or none (null).</summary>
    public static string? SetPhoto(string login, string? file)
    {
        lock (Gate)
        {
            var users = Load();
            if (Find(users, login) is not { } user)
                return $"The user {login} does not exist";
            if (file is null && PhotoPath(user) is { } old)
                File.Delete(old);
            user.Photo = file is null ? string.Empty : Path.GetFileName(file);
            Save(users);
            return null;
        }
    }

    /// <summary>Every user, by login.</summary>
    public static IReadOnlyList<UserAccount> All()
    {
        lock (Gate)
            return [.. Load().OrderBy(u => u.Login, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The user of <paramref name="login"/> when <paramref name="password"/> is its password (its last login is then now), else null.</summary>
    public static UserAccount? Verify(string login, string password)
    {
        lock (Gate)
        {
            var users = Load();
            var user = Find(users, login);
            // An unknown login costs the same time as a wrong password: the answer does not tell which logins exist.
            var salt = user is null ? new byte[16] : Convert.FromBase64String(user.Salt);
            var hash = Derive(password, salt, user?.Iterations > 0 ? user.Iterations : Rounds);
            if (user is null || !CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(user.Hash)))
                return null;
            user.LastLogin = DateTimeOffset.Now;
            Save(users);
            return user;
        }
    }

    public static string? Add(string login, string password, UserRole role)
    {
        login = login.Trim();
        if (CheckLogin(login) is { } badLogin)
            return badLogin;
        if (CheckPassword(password) is { } badPassword)
            return badPassword;
        lock (Gate)
        {
            var users = Load();
            if (Find(users, login) is not null)
                return $"The user {login} already exists";
            var user = new UserAccount { Login = login, Role = role, Created = DateTimeOffset.Now };
            SetHash(user, password);
            users.Add(user);
            Save(users);
            return null;
        }
    }

    public static string? SetRole(string login, UserRole role)
    {
        lock (Gate)
        {
            var users = Load();
            if (Find(users, login) is not { } user)
                return $"The user {login} does not exist";
            if (user.Role == UserRole.Administrator && role != UserRole.Administrator && users.Count(u => u.Role == UserRole.Administrator) == 1)
                return $"{user.Login} is the last administrator: another one is needed first";
            user.Role = role;
            Save(users);
            return null;
        }
    }

    /// <summary>A new password for <paramref name="login"/>, set by an administrator (the current one is not asked).</summary>
    public static string? SetPassword(string login, string password)
    {
        if (CheckPassword(password) is { } bad)
            return bad;
        lock (Gate)
        {
            var users = Load();
            if (Find(users, login) is not { } user)
                return $"The user {login} does not exist";
            SetHash(user, password);
            Save(users);
            return null;
        }
    }

    /// <summary>A new password for <paramref name="login"/>, set by that user: <paramref name="current"/> must be its password.</summary>
    public static string? ChangePassword(string login, string current, string password)
    {
        if (CheckPassword(password) is { } bad)
            return bad;
        if (current == password)
            return "The new password is the current one";
        lock (Gate)
        {
            if (Verify(login, current) is null)
                return "The current password is wrong";
            return SetPassword(login, password);
        }
    }

    public static string? Delete(string login)
    {
        lock (Gate)
        {
            var users = Load();
            if (Find(users, login) is not { } user)
                return $"The user {login} does not exist";
            if (user.Role == UserRole.Administrator && users.Count(u => u.Role == UserRole.Administrator) == 1)
                return $"{user.Login} is the last administrator: it cannot be deleted";
            users.Remove(user);
            Save(users);
            if (PhotoPath(user) is { } photo)
                File.Delete(photo);
            return null;
        }
    }

    private static string? CheckLogin(string login) =>
        login.Length is < 3 or > 32 || !login.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
            ? "A login is 3 to 32 letters, digits, dots, hyphens or underscores"
            : null;

    private static string? CheckPassword(string password) =>
        password.Length < MinPasswordLength ? $"A password is {MinPasswordLength} characters at least" : null;

    private static UserAccount? Find(List<UserAccount> users, string login) =>
        users.FirstOrDefault(u => string.Equals(u.Login, login.Trim(), StringComparison.OrdinalIgnoreCase));

    private static void SetHash(UserAccount user, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        user.Salt = Convert.ToBase64String(salt);
        user.Iterations = Rounds;
        user.Hash = Convert.ToBase64String(Derive(password, salt, Rounds));
    }

    private static byte[] Derive(string password, byte[] salt, int rounds) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, rounds, HashAlgorithmName.SHA256, 32);

    private static List<UserAccount> Load()
    {
        List<UserAccount>? users = null;
        if (File.Exists(FilePath))
        {
            try
            {
                users = JsonSerializer.Deserialize<List<UserAccount>>(File.ReadAllText(FilePath), Json);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // A file that cannot be read is put aside, not lost: the users start again from admin.
                var aside = FilePath + $".unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
                try
                {
                    File.Move(FilePath, aside);
                }
                catch (Exception move) when (move is IOException or UnauthorizedAccessException)
                {
                    aside = "(it could not be moved)";
                }
                ActivityLog.Error("Users", $"{FilePath} could not be read, put aside as {aside}", ex);
            }
        }
        users?.RemoveAll(u => string.IsNullOrWhiteSpace(u.Login) || string.IsNullOrEmpty(u.Hash) || string.IsNullOrEmpty(u.Salt));
        if (users is { Count: > 0 })
            return users;
        var admin = new UserAccount { Login = DefaultLogin, Role = UserRole.Administrator, Created = DateTimeOffset.Now };
        SetHash(admin, DefaultPassword);
        users = [admin];
        Save(users);
        ActivityLog.Info("Users", $"No user in {FilePath}: the administrator {DefaultLogin} is created with its default password");
        return users;
    }

    private static void Save(List<UserAccount> users)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        // Written beside, then put in place: a stop in the middle never leaves half a file.
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(users, Json));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, FilePath, overwrite: true);
    }
}

/// <summary>
/// Who uses the chat now (logged in at the start), and what its level lets it do. Nobody logged in has the rights of a
/// viewer and no more.
/// </summary>
public static class UserSession
{
    /// <summary>The commands of the administrators only.</summary>
    private static readonly HashSet<string> AdministratorOnly = new(StringComparer.OrdinalIgnoreCase) { "/users" };

    /// <summary>The commands a viewer does not run: they change the configuration, run programs or stop the services.</summary>
    private static readonly HashSet<string> NotForViewers = new(StringComparer.OrdinalIgnoreCase)
    {
        "/config", "/setup", "/process", "/processes", "/cmd", "/killall", "/json", "/gateway", "/remote", "/preset", "/presets"
    };

    public static UserAccount? Current { get; private set; }

    public static UserRole Role => Current?.Role ?? UserRole.Viewer;
    public static bool IsAdministrator => Current is { Role: UserRole.Administrator };

    /// <summary>False for a viewer: the buttons that save, delete or publish do nothing.</summary>
    public static bool CanChange => Current is not null && Role != UserRole.Viewer;

    /// <summary>The user logged in, read again (its profile changed).</summary>
    public static void Reload()
    {
        if (Current is { } user && UserStore.Get(user.Login) is { } again)
            Current = again;
    }

    public static void SignIn(UserAccount user)
    {
        Current = user;
        ActivityLog.Info("Users", $"{user.Login} logged in ({Name(user.Role)})");
    }

    /// <summary>Nobody is logged in any more (/logout): the login screen asks who comes next.</summary>
    public static void SignOut()
    {
        if (Current is not { } user)
            return;
        Current = null;
        ActivityLog.Info("Users", $"{user.Login} logged out");
    }

    /// <summary>The level in words.</summary>
    public static string Name(UserRole role) => role.ToString().ToLowerInvariant();

    /// <summary>
    /// True when the user logged in may run <paramref name="command"/> (<c>/config</c>, or <c>/nconfig</c>, its own
    /// window); else <paramref name="why"/> says why not.
    /// </summary>
    public static bool CanRun(string command, out string why)
    {
        why = string.Empty;
        var text = command.Trim();
        var attached = text.StartsWith("/n", StringComparison.OrdinalIgnoreCase) ? "/" + text[2..] : text;
        bool Is(HashSet<string> commands) => commands.Contains(text) || commands.Contains(attached);
        if (Is(AdministratorOnly) && !IsAdministrator)
            why = $"`{text}` is for the administrators only (you are logged in as {Current?.Login ?? "nobody"}, {Name(Role)}).";
        else if (Is(NotForViewers) && !CanChange)
            why = $"`{text}` is not for the viewers (you are logged in as {Current?.Login ?? "nobody"}, {Name(Role)}).";
        return why.Length == 0;
    }
}
