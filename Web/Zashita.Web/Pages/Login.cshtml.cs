using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Zashita.Web.Services;
using Zashita.Web.Services.Connections;

namespace Zashita.Web.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class LoginModel : PageModel
{
    private readonly AuthOptions _auth;
    private readonly WokUsersService _wokUsers;
    private readonly SiteAccessStore _access;

    public LoginModel(IOptions<AuthOptions> auth, WokUsersService wokUsers, SiteAccessStore access)
    {
        _auth = auth.Value;
        _wokUsers = wokUsers;
        _access = access;
    }

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty]
    public ForgotInput Forgot { get; set; } = new();

    /// <summary>login | forgot</summary>
    public string Screen { get; set; } = "login";

    public bool ForgotAwaitingCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string? InfoMessage { get; set; }

    public IActionResult OnGet(string? screen, string? email)
    {
        if (IsAuthenticated())
        {
            return RedirectToPage("/Index");
        }

        if (string.Equals(screen, "forgot", StringComparison.OrdinalIgnoreCase))
        {
            Screen = "forgot";
            if (!string.IsNullOrWhiteSpace(email))
            {
                Forgot.Email = email.Trim();
            }
        }
        else
        {
            Screen = "login";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl, CancellationToken ct)
    {
        Screen = "login";

        // Локальный главный админ (appsettings) — всегда может войти.
        var localAdmin = _auth.TryAuthenticate(Input.Login, Input.Password);
        if (localAdmin is not null)
        {
            return SignIn(localAdmin, returnUrl);
        }

        // Локально добавленный пользователь с коронкой.
        var localUser = _access.TryAuthenticateLocal(Input.Login, Input.Password);
        if (localUser is not null)
        {
            return SignIn(localUser, returnUrl);
        }

        // Пользователь WOK: пароль верный, но нужна коронка в белом списке.
        var wokUser = await _wokUsers.TryAuthenticateAsync(Input.Login, Input.Password, ct);
        if (wokUser is not null)
        {
            if (!_access.HasCrown(wokUser.Login) && !_access.HasCrown(Input.Login))
            {
                ErrorMessage = "Нет доступа к этому сайту. Обратитесь к администратору (нужна «коронка» в списке пользователей).";
                return Page();
            }

            return SignIn(wokUser, returnUrl);
        }

        ErrorMessage = "Неверный логин или пароль.";
        return Page();
    }

    public async Task<IActionResult> OnPostForgotAsync(CancellationToken ct)
    {
        Screen = "forgot";
        ForgotAwaitingCode = Forgot.HasCodeStep;

        var result = await _wokUsers.ForgotPasswordAsync(
            Forgot.Email,
            Forgot.HasCodeStep ? Forgot.Code : null,
            Forgot.HasCodeStep ? Forgot.NewPassword : null,
            ct);

        if (!result.Success)
        {
            ErrorMessage = result.Message;
            return Page();
        }

        if (result.NeedsVerification && !Forgot.HasCodeStep)
        {
            ForgotAwaitingCode = true;
            InfoMessage = result.Message;
            return Page();
        }

        if (Forgot.HasCodeStep && result.Success && !result.NeedsVerification)
        {
            Screen = "login";
            ForgotAwaitingCode = false;
            Input.Login = Forgot.Email.Trim();
            InfoMessage = result.Message;
            return Page();
        }

        ForgotAwaitingCode = result.NeedsVerification;
        InfoMessage = result.Message;
        return Page();
    }

    private IActionResult SignIn(AuthUser user, string? returnUrl)
    {
        var role = string.IsNullOrWhiteSpace(user.Role) ? AuthRoles.HeadAdmin : user.Role.Trim();
        HttpContext.Session.SetString(AuthConstants.SessionKey, "1");
        HttpContext.Session.SetString(AuthConstants.UserNameKey, user.DisplayName);
        HttpContext.Session.SetString(AuthConstants.RoleKey, role);
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            MaxAge = TimeSpan.FromHours(8)
        };
        Response.Cookies.Append(AuthConstants.CookieName, "1", cookieOptions);
        Response.Cookies.Append(AuthConstants.RoleCookieName, role, cookieOptions);

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToPage("/Index");
    }

    private bool IsAuthenticated() =>
        HttpContext.Session.GetString(AuthConstants.SessionKey) == "1"
        || Request.Cookies[AuthConstants.CookieName] == "1";

    public sealed class LoginInput
    {
        [Required]
        public string Login { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public sealed class ForgotInput
    {
        public string Email { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;

        /// <summary>Скрытое поле: уже запросили код и показываем шаг подтверждения.</summary>
        public bool HasCodeStep { get; set; }
    }
}
