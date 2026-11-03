using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Isopoh.Cryptography.Argon2;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Zashita.Web.Services.Connections;

/// <summary>
/// Пользователи из той же PostgreSQL, что у WOK (туннель поднимается сам).
/// </summary>
public sealed class WokUsersService
{
    private const string ListUsersSql =
        """
        SELECT id, email, role, first_name, last_name, notify_email, status,
               company, phone, job_title, contractor_id, avatar_url, created_at,
               password_hash
        FROM users
        ORDER BY LOWER(email)
        """;

    private const string FindUserSql =
        """
        SELECT id, email, role, first_name, last_name, notify_email, status,
               company, phone, job_title, contractor_id, avatar_url, created_at,
               password_hash
        FROM users
        WHERE LOWER(email) = LOWER(@email)
        LIMIT 1
        """;

    private readonly WokUsersOptions _options;
    private readonly WokDbTunnelService _tunnel;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<WokUsersService> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTime _tokenCachedAt = DateTime.MinValue;

    public WokUsersService(
        IOptions<WokUsersOptions> options,
        WokDbTunnelService tunnel,
        IHttpClientFactory httpClientFactory,
        IWebHostEnvironment env,
        ILogger<WokUsersService> logger)
    {
        _options = options.Value;
        _tunnel = tunnel;
        _httpClientFactory = httpClientFactory;
        _env = env;
        _logger = logger;
    }

    private string UsersCachePath =>
        Path.Combine(_env.ContentRootPath, "App_Data", "wok", "users-cache.json");

    /// <summary>Список для страницы «Пользователи» (живой источник или кэш).</summary>
    public async Task<WokUsersLoadResult> LoadAsync(CancellationToken ct = default)
    {
        var live = await TryLoadLiveAsync(ct);
        if (live is not null && live.Users.Count > 0)
        {
            SaveUsersCache(live.Users);
            return live;
        }

        var fromCache = LoadUsersCache();
        if (fromCache is not null)
        {
            if (live is not null && live.Users.Count == 0)
            {
                fromCache.Warning = "В WOK сейчас 0 пользователей — показан последний кэш.";
            }

            return fromCache;
        }

        if (live is not null)
        {
            return live;
        }

        return LoadFromJsonFile();
    }

    /// <summary>Кнопка «Обновить» — перечитать ту же БД WOK.</summary>
    public async Task<WokSyncResult> SyncFromWokAsync(CancellationToken ct = default)
    {
        await _tunnel.EnsureAsync(ct);
        var live = await TryLoadLiveAsync(ct);
        if (live is null)
        {
            return new WokSyncResult
            {
                Success = false,
                Message = "Нет связи с базой WOK. Проверьте настройки Wok в appsettings и SSH-туннель."
            };
        }

        if (live.Users.Count == 0)
        {
            return new WokSyncResult
            {
                Success = false,
                Message = $"Подключение есть ({live.Source}), но в таблице users пусто."
            };
        }

        SaveUsersCache(live.Users);
        return new WokSyncResult
        {
            Success = true,
            Count = live.Users.Count,
            Message = $"В базе WOK: {live.Users.Count} пользователей ({live.Source})."
        };
    }

    /// <summary>Вход теми же email/паролем, что в WOK.</summary>
    public async Task<AuthUser?> TryAuthenticateAsync(string login, string password, CancellationToken ct = default)
    {
        var email = (login ?? string.Empty).Trim();
        var pwd = password ?? string.Empty;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(pwd))
        {
            return null;
        }

        // 1) HTTP API WOK в локальной сети (та же база).
        var fromApi = await TryAuthenticateApiAsync(email, pwd, ct);
        if (fromApi is not null)
        {
            return fromApi;
        }

        // 2) Живая проверка по PostgreSQL.
        var fromPg = await TryAuthenticatePostgresAsync(email, pwd, ct);
        if (fromPg is not null)
        {
            return fromPg;
        }

        // 3) Локальный кэш после успешной загрузки списка.
        var cached = LoadUsersCache()?.Users
            .FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
        if (cached is not null &&
            !string.IsNullOrWhiteSpace(cached.PasswordHash) &&
            VerifyPasswordHash(cached.PasswordHash, pwd) &&
            IsLoginAllowed(cached))
        {
            return ToAuthUser(cached);
        }

        return null;
    }

    /// <summary>
    /// Сброс пароля через WOK API (та же отправка письма с кодом, что в мобильном WOK).
    /// Без code — запрос кода; с code + password — смена пароля.
    /// </summary>
    public async Task<WokForgotPasswordResult> ForgotPasswordAsync(
        string email,
        string? code = null,
        string? newPassword = null,
        CancellationToken ct = default)
    {
        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.IndexOf('@') < 1)
        {
            return WokForgotPasswordResult.Fail("Укажите корректный email.");
        }

        var baseUrl = (_options.ApiBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return WokForgotPasswordResult.Fail("Сервис восстановления недоступен (не настроен WOK API).");
        }

        var payload = new Dictionary<string, string> { ["email"] = normalized };
        var verifyCode = (code ?? string.Empty).Trim();
        var password = (newPassword ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(verifyCode))
        {
            if (password.Length < 6)
            {
                return WokForgotPasswordResult.Fail("Пароль не менее 6 символов.");
            }

            payload["code"] = verifyCode;
            payload["password"] = password;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("wok");
            using var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");
            using var response = await client.PostAsync($"{baseUrl}/api/auth/forgot-password", content, ct);
            var bodyText = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(bodyText) ? "{}" : bodyText);
            var root = doc.RootElement;
            var message = GetJsonString(root, "message");
            var error = GetJsonString(root, "error");

            if (!response.IsSuccessStatusCode)
            {
                return WokForgotPasswordResult.Fail(
                    string.IsNullOrWhiteSpace(error)
                        ? $"Не удалось восстановить пароль ({(int)response.StatusCode})."
                        : error);
            }

            var needsVerification = root.TryGetProperty("needsVerification", out var nv) &&
                                    nv.ValueKind == JsonValueKind.True;
            if (!string.IsNullOrEmpty(verifyCode) && !needsVerification)
            {
                InvalidateCachedPasswordHash(normalized);
            }

            return new WokForgotPasswordResult
            {
                Success = true,
                NeedsVerification = needsVerification,
                Message = string.IsNullOrWhiteSpace(message)
                    ? (needsVerification
                        ? $"Код отправляется на {normalized}. Проверьте почту и папку «Спам»."
                        : string.IsNullOrEmpty(verifyCode)
                            ? "Если аккаунт существует, код отправлен на почту."
                            : "Пароль обновлён. Теперь можно войти.")
                    : message
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WOK forgot-password failed for {Email}", normalized);
            return WokForgotPasswordResult.Fail(
                "Не удалось связаться с сервисом восстановления. Попробуйте позже.");
        }
    }

    private void InvalidateCachedPasswordHash(string email)
    {
        try
        {
            if (!File.Exists(UsersCachePath))
            {
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(UsersCachePath));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var users = ParseUsersArray(doc.RootElement).ToList();
            var changed = false;
            foreach (var u in users)
            {
                if (string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(u.PasswordHash))
                {
                    u.PasswordHash = string.Empty;
                    changed = true;
                }
            }

            if (changed)
            {
                SaveUsersCache(users);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Не удалось сбросить кэш пароля WOK для {Email}", email);
        }
    }

    private static string GetJsonString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? string.Empty
            : string.Empty;

    private async Task<WokUsersLoadResult?> TryLoadLiveAsync(CancellationToken ct)
    {
        // В локальной сети основной источник — API WOK (та же БД), если настроен.
        var fromApi = await TryLoadFromApiAsync(ct);
        if (fromApi is not null && fromApi.Users.Count > 0)
        {
            return fromApi;
        }

        await _tunnel.EnsureAsync(ct);
        var fromPg = await TryLoadFromPostgresAsync(ct);
        if (fromPg is not null)
        {
            return fromPg;
        }

        return fromApi;
    }

    private async Task<WokUsersLoadResult?> TryLoadFromApiAsync(CancellationToken ct)
    {
        var baseUrl = (_options.ApiBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        try
        {
            var token = await ResolveApiTokenAsync(baseUrl, ct);
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning("WOK API: нет токена (проверьте ApiAdminEmail/Password)");
                return null;
            }

            var client = _httpClientFactory.CreateClient("wok");
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/users");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, ct);
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                _cachedToken = null;
                token = await ResolveApiTokenAsync(baseUrl, ct, forceLogin: true);
                if (string.IsNullOrWhiteSpace(token))
                {
                    return null;
                }

                using var retry = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/users");
                retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var retryResponse = await client.SendAsync(retry, ct);
                if (!retryResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("WOK /api/users после логина: {Status}", (int)retryResponse.StatusCode);
                    return null;
                }

                return await ReadUsersResponseAsync(retryResponse, baseUrl, ct);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("WOK /api/users вернул {Status}", (int)response.StatusCode);
                return null;
            }

            return await ReadUsersResponseAsync(response, baseUrl, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось загрузить пользователей WOK через API");
            return null;
        }
    }

    private static async Task<WokUsersLoadResult> ReadUsersResponseAsync(
        HttpResponseMessage response,
        string baseUrl,
        CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        var usersEl = root.TryGetProperty("users", out var u) ? u : root;
        return new WokUsersLoadResult
        {
            Users = SortUsers(ParseUsersArray(usersEl)),
            Source = $"WOK API {baseUrl}"
        };
    }

    private async Task<string?> ResolveApiTokenAsync(string baseUrl, CancellationToken ct, bool forceLogin = false)
    {
        var configured = (_options.ApiAdminToken ?? string.Empty).Trim();
        if (!forceLogin && !string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (!forceLogin &&
            !string.IsNullOrWhiteSpace(_cachedToken) &&
            DateTime.UtcNow - _tokenCachedAt < TimeSpan.FromHours(12))
        {
            return _cachedToken;
        }

        var email = (_options.ApiAdminEmail ?? string.Empty).Trim();
        var password = (_options.ApiAdminPassword ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return string.IsNullOrWhiteSpace(configured) ? null : configured;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (!forceLogin &&
                !string.IsNullOrWhiteSpace(_cachedToken) &&
                DateTime.UtcNow - _tokenCachedAt < TimeSpan.FromHours(12))
            {
                return _cachedToken;
            }

            var client = _httpClientFactory.CreateClient("wok");
            var payload = JsonSerializer.Serialize(new { email, password });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync($"{baseUrl}/api/auth/login", content, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("WOK login {Status}: {Body}", (int)response.StatusCode, body);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("token", out var tokenEl) ||
                tokenEl.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            _cachedToken = tokenEl.GetString();
            _tokenCachedAt = DateTime.UtcNow;
            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<WokUsersLoadResult?> TryLoadFromPostgresAsync(CancellationToken ct)
    {
        foreach (var cs in ConnectionCandidates())
        {
            try
            {
                await using var conn = new NpgsqlConnection(cs);
                await conn.OpenAsync(ct);
                await using var cmd = new NpgsqlCommand(ListUsersSql, conn)
                {
                    CommandTimeout = 20
                };
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                var list = new List<WokUserDto>();
                while (await reader.ReadAsync(ct))
                {
                    list.Add(MapReader(reader));
                }

                return new WokUsersLoadResult
                {
                    Users = SortUsers(list),
                    Source = $"PostgreSQL {conn.Host}:{conn.Port}/{conn.Database}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "WOK PostgreSQL кандидат недоступен");
            }
        }

        return null;
    }

    private async Task<AuthUser?> TryAuthenticatePostgresAsync(string email, string password, CancellationToken ct)
    {
        await _tunnel.EnsureAsync(ct);

        foreach (var cs in ConnectionCandidates())
        {
            try
            {
                await using var conn = new NpgsqlConnection(cs);
                await conn.OpenAsync(ct);
                await using var cmd = new NpgsqlCommand(FindUserSql, conn)
                {
                    CommandTimeout = 15
                };
                cmd.Parameters.AddWithValue("email", email);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                {
                    return null;
                }

                var user = MapReader(reader);
                if (!IsLoginAllowed(user) || !VerifyPasswordHash(user.PasswordHash, password))
                {
                    return null;
                }

                // Обновим кэш одним пользователем в фоне не нужно — Sync делает полное.
                return ToAuthUser(user);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "WOK PG auth кандидат недоступен");
            }
        }

        return null;
    }

    private async Task<AuthUser?> TryAuthenticateApiAsync(string email, string password, CancellationToken ct)
    {
        var baseUrl = (_options.ApiBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("wok");
            var payload = JsonSerializer.Serialize(new { email, password });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync($"{baseUrl}/api/auth/login", content, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("user", out var userEl))
            {
                // Токен без user — считаем email валидным.
                return new AuthUser
                {
                    Login = email,
                    DisplayName = email,
                    Role = AuthRoles.HeadAdmin
                };
            }

            var dto = new WokUserDto
            {
                Id = GetString(userEl, "id"),
                Email = GetString(userEl, "email"),
                Role = GetString(userEl, "role"),
                FirstName = GetString(userEl, "firstName", "first_name"),
                LastName = GetString(userEl, "lastName", "last_name"),
                Status = GetString(userEl, "status")
            };
            if (string.IsNullOrWhiteSpace(dto.Email))
            {
                dto.Email = email;
            }

            if (string.IsNullOrWhiteSpace(dto.Status))
            {
                dto.Status = "active";
            }

            return IsLoginAllowed(dto) ? ToAuthUser(dto) : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WOK API auth недоступен");
            return null;
        }
    }

    private IEnumerable<string> ConnectionCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        void Push(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            var trimmed = raw.Trim();
            var cs = trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
                ? ConvertDatabaseUrl(trimmed)
                : trimmed;
            if (!cs.Contains("Timeout=", StringComparison.OrdinalIgnoreCase))
            {
                cs += ";Timeout=15";
            }

            if (!cs.Contains("SSL Mode=", StringComparison.OrdinalIgnoreCase) &&
                !cs.Contains("Ssl Mode=", StringComparison.OrdinalIgnoreCase))
            {
                cs += ";SSL Mode=Disable";
            }

            if (seen.Add(cs))
            {
                list.Add(cs);
            }
        }

        Push(_options.ConnectionString);
        Push(_options.DatabaseUrl);
        return list;
    }

    internal static string ConvertDatabaseUrl(string url)
    {
        if (url.StartsWith("Host=", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("Username=", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "postgresql" && uri.Scheme != "postgres"))
        {
            return url;
        }

        var userInfo = uri.UserInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(0) ?? string.Empty);
        var pass = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(1) ?? string.Empty);
        var db = uri.AbsolutePath.Trim('/');
        var port = uri.Port > 0 ? uri.Port : 5432;
        return $"Host={uri.Host};Port={port};Database={db};Username={user};Password={pass}";
    }

    private void SaveUsersCache(IReadOnlyList<WokUserDto> users)
    {
        if (users.Count == 0)
        {
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(UsersCachePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Сохраняем прежние хеши, если новый источник (API) их не отдал.
            var prevHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(UsersCachePath))
            {
                try
                {
                    using var prevDoc = JsonDocument.Parse(File.ReadAllText(UsersCachePath));
                    if (prevDoc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var u in ParseUsersArray(prevDoc.RootElement))
                        {
                            if (!string.IsNullOrWhiteSpace(u.Email) &&
                                !string.IsNullOrWhiteSpace(u.PasswordHash))
                            {
                                prevHashes[u.Email] = u.PasswordHash;
                            }
                        }
                    }
                }
                catch
                {
                    // ignore stale cache
                }
            }

            var payload = users.Select(u =>
            {
                var hash = u.PasswordHash;
                if (string.IsNullOrWhiteSpace(hash) &&
                    !string.IsNullOrWhiteSpace(u.Email) &&
                    prevHashes.TryGetValue(u.Email, out var old))
                {
                    hash = old;
                }

                return new
                {
                    id = u.Id,
                    email = u.Email,
                    role = u.Role,
                    firstName = u.FirstName,
                    lastName = u.LastName,
                    status = u.Status,
                    company = u.Company,
                    phone = u.Phone,
                    jobTitle = u.JobTitle,
                    createdAt = u.CreatedAt,
                    passwordHash = hash
                };
            });
            File.WriteAllText(
                UsersCachePath,
                JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Не удалось сохранить кэш пользователей WOK");
        }
    }

    private WokUsersLoadResult? LoadUsersCache()
    {
        try
        {
            if (!File.Exists(UsersCachePath))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(UsersCachePath));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var users = SortUsers(ParseUsersArray(doc.RootElement));
            if (users.Count == 0)
            {
                return null;
            }

            return new WokUsersLoadResult
            {
                Users = users,
                Source = "локальная копия WOK (после «Загрузить базу»)",
                Warning = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Не удалось прочитать кэш пользователей WOK");
            return null;
        }
    }

    private WokUsersLoadResult LoadFromJsonFile()
    {
        var path = (_options.DbJsonPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            var linked = Path.Combine(_env.ContentRootPath, "..", "..", "WOK", "victory-check-api", "data", "db.json");
            linked = Path.GetFullPath(linked);
            if (File.Exists(linked))
            {
                path = linked;
            }
            else
            {
                var projectPath = (_options.ProjectPath ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(projectPath))
                {
                    var fromProject = Path.Combine(projectPath, "victory-check-api", "data", "db.json");
                    if (File.Exists(fromProject))
                    {
                        path = fromProject;
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new WokUsersLoadResult
            {
                Users = [],
                Source = "WOK",
                Warning =
                    "Нет связи с базой пользователей WOK. Откройте страницу позже или проверьте сеть/SSH."
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("users", out var usersEl) ||
                usersEl.ValueKind != JsonValueKind.Array)
            {
                return new WokUsersLoadResult
                {
                    Users = [],
                    Source = path,
                    Warning = "В db.json нет массива users."
                };
            }

            return new WokUsersLoadResult
            {
                Users = SortUsers(ParseUsersArray(usersEl)),
                Source = "WOK db.json",
                Warning = "Живая БД недоступна — показан локальный файл."
            };
        }
        catch (Exception ex)
        {
            return new WokUsersLoadResult
            {
                Users = [],
                Source = path,
                Warning = ex.Message
            };
        }
    }

    private static WokUserDto MapReader(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetString(reader.GetOrdinal("id")),
        Email = reader.IsDBNull(reader.GetOrdinal("email")) ? string.Empty : reader.GetString(reader.GetOrdinal("email")),
        Role = reader.IsDBNull(reader.GetOrdinal("role")) ? string.Empty : reader.GetString(reader.GetOrdinal("role")),
        FirstName = reader.IsDBNull(reader.GetOrdinal("first_name")) ? string.Empty : reader.GetString(reader.GetOrdinal("first_name")),
        LastName = reader.IsDBNull(reader.GetOrdinal("last_name")) ? string.Empty : reader.GetString(reader.GetOrdinal("last_name")),
        Company = reader.IsDBNull(reader.GetOrdinal("company")) ? string.Empty : reader.GetString(reader.GetOrdinal("company")),
        Phone = reader.IsDBNull(reader.GetOrdinal("phone")) ? string.Empty : reader.GetString(reader.GetOrdinal("phone")),
        JobTitle = reader.IsDBNull(reader.GetOrdinal("job_title")) ? string.Empty : reader.GetString(reader.GetOrdinal("job_title")),
        Status = reader.IsDBNull(reader.GetOrdinal("status")) ? "active" : reader.GetString(reader.GetOrdinal("status")),
        PasswordHash = HasColumn(reader, "password_hash") && !reader.IsDBNull(reader.GetOrdinal("password_hash"))
            ? reader.GetString(reader.GetOrdinal("password_hash"))
            : string.Empty,
        CreatedAt = reader.IsDBNull(reader.GetOrdinal("created_at"))
            ? null
            : reader.GetFieldValue<DateTime>(reader.GetOrdinal("created_at"))
    };

    private static bool HasColumn(NpgsqlDataReader reader, string name)
    {
        try
        {
            return reader.GetOrdinal(name) >= 0;
        }
        catch
        {
            return false;
        }
    }

    private static List<WokUserDto> ParseUsersArray(JsonElement usersEl)
    {
        var list = new List<WokUserDto>();
        if (usersEl.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in usersEl.EnumerateArray())
        {
            var dto = new WokUserDto
            {
                Id = GetString(item, "id"),
                Email = GetString(item, "email"),
                Role = GetString(item, "role"),
                FirstName = GetString(item, "firstName", "first_name"),
                LastName = GetString(item, "lastName", "last_name"),
                Company = GetString(item, "company"),
                Phone = GetString(item, "phone"),
                JobTitle = GetString(item, "jobTitle", "job_title"),
                Status = GetString(item, "status"),
                PasswordHash = GetString(item, "passwordHash", "password_hash"),
                CreatedAt = GetDate(item, "createdAt", "created_at")
            };
            if (string.IsNullOrWhiteSpace(dto.Status))
            {
                dto.Status = "active";
            }

            if (string.IsNullOrWhiteSpace(dto.Email) && string.IsNullOrWhiteSpace(dto.Id))
            {
                continue;
            }

            list.Add(dto);
        }

        return list;
    }

    private static List<WokUserDto> SortUsers(IEnumerable<WokUserDto> list) =>
        list
            .OrderBy(x => x.LastName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.FirstName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Email, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static bool IsLoginAllowed(WokUserDto user) =>
        !string.Equals(user.Status, "rejected", StringComparison.OrdinalIgnoreCase);

    private static AuthUser ToAuthUser(WokUserDto u) => new()
    {
        Login = u.Email,
        DisplayName = u.DisplayName,
        Role = string.Equals(u.Role, "admin", StringComparison.OrdinalIgnoreCase)
            ? AuthRoles.HeadAdmin
            : "User"
    };

    internal static bool VerifyPasswordHash(string? hash, string plain)
    {
        if (string.IsNullOrWhiteSpace(hash) || plain is null)
        {
            return false;
        }

        if (hash.StartsWith("$argon2", StringComparison.Ordinal))
        {
            try
            {
                return Argon2.Verify(hash, plain);
            }
            catch
            {
                return false;
            }
        }

        // Старые записи WOK: bcrypt или plaintext.
        if (hash.StartsWith("$2", StringComparison.Ordinal))
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(plain, hash);
            }
            catch
            {
                return false;
            }
        }

        return string.Equals(hash, plain, StringComparison.Ordinal);
    }

    private static string GetString(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
            {
                return p.GetString()?.Trim() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static DateTime? GetDate(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (!el.TryGetProperty(name, out var p))
            {
                continue;
            }

            if (p.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(p.GetString(), out var dt))
            {
                return dt;
            }
        }

        return null;
    }
}
