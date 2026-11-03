using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med;

[RequireLogin]
public class HubModel : PageModel
{
    public IActionResult OnGet()
    {
        ViewData["ActiveModule"] = "медосмотры";
        ViewData["IsModulePage"] = true;
        return Redirect("/module/медосмотры");
    }
}
