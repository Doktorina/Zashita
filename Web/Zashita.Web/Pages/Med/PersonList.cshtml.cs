using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med;

/// <summary>Совместимость: /Med/PersonList перенаправляет на «Списки контингентов».</summary>
[RequireLogin]
public class PersonListModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Department { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool InGroup { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PrintId { get; set; }

    public IActionResult OnGet() =>
        RedirectToPage("/Med/ContingentLists", new
        {
            tab = ContingentListsModel.TabNamed,
            department = Department,
            q = Q,
            inGroup = InGroup,
            printId = PrintId
        });

    public IActionResult OnPost() => OnGet();

    public IActionResult OnPostCreate() => OnGet();

    public IActionResult OnPostDelete() => OnGet();
}
