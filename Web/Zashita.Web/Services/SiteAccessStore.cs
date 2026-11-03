using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Zashita.Web.Services;

/// <summary>
/// Белый список доступа к «Охране труда»: коронка = можно входить.
/// </summary>
public sealed class SiteAccessStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;
    private readonly object _gate = new();

    public SiteAccessStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "App_Data", "wok");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "site-access.json");
    }

    public SiteAccessData Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return new SiteAccessData();
            }

            try
            {
                var data = JsonSerializer.Deserialize<SiteAccessData>(File.ReadAllText(_path), JsonOpts);
                return data ?? new SiteAccessData();
            }
            catch
            {
                return new SiteAccessData();
            }
        }
    }

    public void Save(SiteAccessData data)
    {
        lock (_gate)
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(data, JsonOpts));
        }
    }

    public bool HasCrown(string? loginOrEmail)
    {
        var key = Normalize(loginOrEmail);
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        var data = Load();
        return data.Allowed.Any(a => string.Equals(Normalize(a.Email), key, StringComparison.Ordinal));
    }

    public void SetCrown(string email, string? displayName, bool allowed)
    {
        var key = Normalize(email);
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        var data = Load();
        data.Allowed.RemoveAll(a => string.Equals(Normalize(a.Email), key, StringComparison.Ordinal));
        if (allowed)
        {
            data.Allowed.Add(new SiteAccessEntry
            {
                Email = key,
                DisplayName = (displayName ?? string.Empty).Trim(),
                PasswordHash = data.LocalUsers
                    .FirstOrDefault(u => string.Equals(Normalize(u.Email), key, StringComparison.Ordinal))
                    ?.PasswordHash
            });
        }

        // Синхронизируем LocalUsers.HasAccess
        foreach (var u in data.LocalUsers.Where(u => string.Equals(Normalize(u.Email), key, StringComparison.Ordinal)))
        {
            u.HasAccess = allowed;
        }

        Save(data);
    }

    public SiteAccessEntry AddLocalUser(string email, string displayName, string password)
    {
        var key = Normalize(email);
        if (string.IsNullOrEmpty(key))
        {
            throw new InvalidOperationException("Укажите email.");
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
        {
            throw new InvalidOperationException("Пароль не короче 4 символов.");
        }

        var data = Load();
        if (data.LocalUsers.Any(u => string.Equals(Normalize(u.Email), key, StringComparison.Ordinal)) ||
            data.Allowed.Any(a => string.Equals(Normalize(a.Email), key, StringComparison.Ordinal) &&
                                  !string.IsNullOrWhiteSpace(a.PasswordHash)))
        {
            throw new InvalidOperationException("Такой пользователь уже есть.");
        }

        var entry = new SiteAccessEntry
        {
            Email = key,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? key : displayName.Trim(),
            PasswordHash = HashPassword(password),
            HasAccess = true,
            IsLocal = true
        };
        data.LocalUsers.RemoveAll(u => string.Equals(Normalize(u.Email), key, StringComparison.Ordinal));
        data.LocalUsers.Add(entry);
        data.Allowed.RemoveAll(a => string.Equals(Normalize(a.Email), key, StringComparison.Ordinal));
        data.Allowed.Add(new SiteAccessEntry
        {
            Email = key,
            DisplayName = entry.DisplayName,
            PasswordHash = entry.PasswordHash
        });
        Save(data);
        return entry;
    }

    public bool RemoveUser(string email)
    {
        var key = Normalize(email);
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        var data = Load();
        var removed = data.LocalUsers.RemoveAll(u => string.Equals(Normalize(u.Email), key, StringComparison.Ordinal)) > 0;
        removed |= data.Allowed.RemoveAll(a => string.Equals(Normalize(a.Email), key, StringComparison.Ordinal)) > 0;
        if (removed)
        {
            Save(data);
        }

        return removed;
    }

    public AuthUser? TryAuthenticateLocal(string login, string password)
    {
        var key = Normalize(login);
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var data = Load();
        var entry = data.LocalUsers.FirstOrDefault(u =>
            string.Equals(Normalize(u.Email), key, StringComparison.Ordinal));
        if (entry is null || string.IsNullOrWhiteSpace(entry.PasswordHash))
        {
            // Пароль мог быть только в Allowed (старые записи).
            entry = data.Allowed.FirstOrDefault(u =>
                string.Equals(Normalize(u.Email), key, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(u.PasswordHash));
        }

        if (entry is null || !VerifyPassword(password, entry.PasswordHash))
        {
            return null;
        }

        if (!HasCrown(key))
        {
            return null;
        }

        return new AuthUser
        {
            Login = entry.Email,
            DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.Email : entry.DisplayName,
            Role = AuthRoles.HeadAdmin
        };
    }

    public static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            32);
        return $"pbkdf2${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored) || !stored.StartsWith("pbkdf2$", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = stored.Split('$', 3);
        if (parts.Length != 3)
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            32);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}

public sealed class SiteAccessData
{
    /// <summary>Email с коронкой (могут входить).</summary>
    public List<SiteAccessEntry> Allowed { get; set; } = [];

    /// <summary>Локально добавленные пользователи (не только из WOK).</summary>
    public List<SiteAccessEntry> LocalUsers { get; set; } = [];
}

public sealed class SiteAccessEntry
{
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public bool HasAccess { get; set; } = true;
    public bool IsLocal { get; set; }
}

public sealed class SiteUserRow
{
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string RoleLabel { get; set; } = string.Empty;
    public bool HasCrown { get; set; }
    public bool IsLocal { get; set; }
}
