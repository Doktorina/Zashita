namespace Zashita.Web.Services;

public static class AuthRoles
{
  public const string HeadAdmin = "HeadAdmin";
}

public static class AuthHelper
{
  public static bool IsAuthenticated(HttpContext http) =>
    http.Session.GetString(AuthConstants.SessionKey) == "1"
    || http.Request.Cookies[AuthConstants.CookieName] == "1";

  public static string? GetRole(HttpContext http)
  {
    var fromSession = http.Session.GetString(AuthConstants.RoleKey);
    if (!string.IsNullOrWhiteSpace(fromSession))
    {
      return fromSession.Trim();
    }

    var fromCookie = http.Request.Cookies[AuthConstants.RoleCookieName];
    return string.IsNullOrWhiteSpace(fromCookie) ? null : fromCookie.Trim();
  }

  public static bool IsHeadAdmin(HttpContext http) =>
    IsAuthenticated(http)
    && string.Equals(GetRole(http), AuthRoles.HeadAdmin, StringComparison.OrdinalIgnoreCase);

  /// <summary>
  /// Управление скрытием организаций: любой вошедший пользователь
  /// (как «Переименовать» / «Удалить» на странице «Организации»).
  /// </summary>
  public static bool CanManageOrganizationVisibility(HttpContext http) => IsAuthenticated(http);
}
