using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks;

[RequireLogin]
public class UnscheduledOrdersModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Department { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PrintId { get; set; }

    public IActionResult OnGet()
    {
        var qs = new List<string> { "tab=order" };
        if (!string.IsNullOrWhiteSpace(PrintId))
        {
            qs.Add($"printId={Uri.EscapeDataString(PrintId)}");
        }

        if (!string.IsNullOrWhiteSpace(Department))
        {
            qs.Add($"department={Uri.EscapeDataString(Department)}");
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            qs.Add($"q={Uri.EscapeDataString(Q)}");
        }

        return Redirect("/Risks/Documents?" + string.Join("&", qs));
    }
}
