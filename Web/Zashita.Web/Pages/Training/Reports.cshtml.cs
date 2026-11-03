using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training;

[RequireLogin]
public class ReportsModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public IReadOnlyList<TrainingJournalSection> Sections { get; private set; } = [];

    public void OnGet()
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        var all = TrainingReportCatalog.BuildJournalHubEntries();
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            all = all
                .Where(e => e.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                            || e.Description.Contains(q, StringComparison.OrdinalIgnoreCase)
                            || e.Section.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        Sections = TrainingReportCatalog.BuildJournalSections(all);
    }
}
