namespace Zashita.Web.Services.Connections;

public sealed class WokUsersOptions
{
    public const string SectionName = "Wok";

    /// <summary>Удалённый API WOK (основной источник при работе не локально).</summary>
    public string ApiBaseUrl { get; set; } = string.Empty;

    /// <summary>Email админа WOK для входа в API.</summary>
    public string ApiAdminEmail { get; set; } = string.Empty;

    /// <summary>Пароль админа WOK (задаётся в appsettings / секретах окружения).</summary>
    public string ApiAdminPassword { get; set; } = string.Empty;

    /// <summary>Готовый Bearer-токен (если задан — логин не нужен).</summary>
    public string ApiAdminToken { get; set; } = string.Empty;

    /// <summary>
    /// Прямое подключение к PostgreSQL (опционально).
    /// Пусто — не используем локальную БД.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    public string DatabaseUrl { get; set; } = string.Empty;

    /// <summary>Локальный db.json только как запасной вариант. Пусто — не использовать.</summary>
    public string DbJsonPath { get; set; } = string.Empty;

    /// <summary>Корень проекта WOK (если интеграция включена).</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Автоматически поднимать SSH-туннель к PostgreSQL WOK.</summary>
    public bool AutoTunnel { get; set; }

    public string SshHost { get; set; } = string.Empty;
    public int SshPort { get; set; } = 22;
    public string SshUser { get; set; } = string.Empty;
    public string SshKeyPath { get; set; } = @"%USERPROFILE%\.ssh\id_ed25519";
    public int TunnelLocalPort { get; set; } = 5433;
    public int TunnelRemotePort { get; set; } = 5432;
}

public sealed class WokUserDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public DateTime? CreatedAt { get; set; }

    /// <summary>Хеш пароля WOK (только в кэше/БД, не в UI).</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public string DisplayName
    {
        get
        {
            var name = $"{FirstName} {LastName}".Trim();
            return string.IsNullOrWhiteSpace(name) ? Email : name;
        }
    }

    public string RoleLabel => Role.Trim().ToLowerInvariant() switch
    {
        "admin" => "Администратор",
        "contractor" => "Подрядчик",
        "auditor" => "Аудитор",
        "viewer" => "Наблюдатель",
        _ => string.IsNullOrWhiteSpace(Role) ? "—" : Role
    };

    public string StatusLabel => Status.Trim().ToLowerInvariant() switch
    {
        "active" => "Активен",
        "pending" => "Ожидает",
        "rejected" => "Отклонён",
        _ => string.IsNullOrWhiteSpace(Status) ? "—" : Status
    };
}

public sealed class WokUsersLoadResult
{
    public IReadOnlyList<WokUserDto> Users { get; init; } = [];
    public string Source { get; init; } = string.Empty;
    public string? Warning { get; set; }
}

public sealed class WokSyncResult
{
    public bool Success { get; init; }
    public int Count { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class WokForgotPasswordResult
{
    public bool Success { get; init; }
    public bool NeedsVerification { get; init; }
    public string Message { get; init; } = string.Empty;

    public static WokForgotPasswordResult Fail(string message) => new()
    {
        Success = false,
        Message = message
    };
}
