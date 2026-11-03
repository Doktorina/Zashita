using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Sout;

/// <summary>Закрепление сотрудников убрано — редирект на отчёт по СОУТ.</summary>
[RequireLogin]
public class EditModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Sout/Status");

    public IActionResult OnPost() => RedirectToPage("/Sout/Status");
}
