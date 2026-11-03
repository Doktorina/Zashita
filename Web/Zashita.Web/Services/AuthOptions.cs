namespace Zashita.Web.Services;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Устаревшее: один логин (если Users пуст).</summary>
    public string Login { get; set; } = "admin";

    /// <summary>Устаревшее: один пароль (если Users пуст).</summary>
    public string Password { get; set; } = "123";

    /// <summary>Учётные записи для входа в веб-интерфейс.</summary>
    public List<AuthUser> Users { get; set; } = [];

    public IReadOnlyList<AuthUser> ResolveUsers()
    {
        if (Users.Count > 0)
        {
            return Users;
        }

        return [new AuthUser { Login = Login, Password = Password, DisplayName = "Администратор", Role = AuthRoles.HeadAdmin }];
    }

    public AuthUser? TryAuthenticate(string login, string password)
    {
        foreach (var user in ResolveUsers())
        {
            if (string.Equals(user.Login, login, StringComparison.OrdinalIgnoreCase) &&
                user.Password == password)
            {
                return user;
            }
        }

        return null;
    }
}

public sealed class AuthUser
{
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "Пользователь";
    public string Role { get; set; } = string.Empty;
}

public static class AuthConstants
{
    public const string SessionKey = "ZashitaAuth";
    public const string UserNameKey = "ZashitaUserName";
    public const string RoleKey = "ZashitaUserRole";
    public const string CookieName = "Zashita.Auth";
    public const string RoleCookieName = "Zashita.Role";
}
