using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Web.Services;

namespace Zashita.Web.Pages;

public class LogoutModel : PageModel
{
    public IActionResult OnGet()
    {
        HttpContext.Session.Remove(AuthConstants.SessionKey);
        HttpContext.Session.Remove(AuthConstants.UserNameKey);
        HttpContext.Session.Remove(AuthConstants.RoleKey);
        Response.Cookies.Delete(AuthConstants.CookieName);
        Response.Cookies.Delete(AuthConstants.RoleCookieName);
        return RedirectToPage("/Login");
    }
}
