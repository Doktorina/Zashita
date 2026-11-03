using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med;

[RequireLogin]
public class SetupModel : PageModel
{
    public IActionResult OnGet() =>
        Redirect("/module/медосмотры#med-setup");
}
