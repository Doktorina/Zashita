using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Search;

[RequireLogin]
public class IndexModel : PageModel
{
    private readonly GlobalSearchService _search;

    public IndexModel(GlobalSearchService search)
    {
        _search = search;
    }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public string? Query => Q?.Trim();
    public IReadOnlyList<SearchHit> Results { get; private set; } = [];

    public void OnGet()
    {
        if (string.IsNullOrWhiteSpace(Query))
        {
            return;
        }

        Results = _search.Search(Query, 50);
    }
}
