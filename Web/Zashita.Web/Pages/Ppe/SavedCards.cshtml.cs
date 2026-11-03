using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Ppe;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Ppe;

[RequireLogin]
public class SavedCardsModel : PageModel
{
    private readonly SavedSiosCardsStore _cards;
    private readonly OrganizationStore _orgs;

    public SavedCardsModel(SavedSiosCardsStore cards, OrganizationStore orgs)
    {
        _cards = cards;
        _orgs = orgs;
    }

    public IReadOnlyList<SavedSiosCardRecord> Cards { get; private set; } = [];
    public IReadOnlyList<string> Organizations { get; private set; } = [];
    public int TotalCount { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? To { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Org { get; set; }

    public void OnGet()
    {
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;

        var all = _cards.LoadAll();
        TotalCount = all.Count;
        Organizations = all
            .Select(c => c.Organization.Trim())
            .Where(o => o.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(o => o, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selectedOrg = _orgs.GetSelectedOrganization(HttpContext);
        if (string.IsNullOrWhiteSpace(Org) && Organizations.Any(o => string.Equals(o, selectedOrg, StringComparison.OrdinalIgnoreCase)))
        {
            Org = selectedOrg;
        }

        var from = (From ?? DateTime.Today.AddYears(-3)).Date;
        var to = (To ?? DateTime.Today).Date;
        if (to < from) (from, to) = (to, from);

        Cards = all
            .Where(c => string.IsNullOrWhiteSpace(Org) || string.Equals(c.Organization.Trim(), Org, StringComparison.OrdinalIgnoreCase))
            .Where(c =>
            {
                var created = c.CreatedAt != default ? c.CreatedAt.ToLocalTime().Date : c.UpdatedAt.ToLocalTime().Date;
                return created >= from && created <= to;
            })
            .Where(c => MatchesQuery(c, Q))
            .OrderByDescending(c => c.UpdatedAt)
            .ToList();
    }

    public IActionResult OnPostDelete(string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            _cards.Delete(id);
            var sessionState = SiosCardWizardSession.Load(HttpContext.Session);
            if (string.Equals(sessionState.SavedCardId, id, StringComparison.Ordinal))
            {
                SiosCardWizardSession.Clear(HttpContext.Session);
            }
        }

        return RedirectToPage(new { q = Q, from = From?.ToString("yyyy-MM-dd"), to = To?.ToString("yyyy-MM-dd"), org = Org });
    }

    private static bool MatchesQuery(SavedSiosCardRecord card, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var haystack = string.Join(" ",
            card.Id, card.CardNumber, card.Department, card.Organization, card.DocumentName,
            card.Profession, string.Join(" ", card.Employees), card.ResponsiblePerson);
        return haystack.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
