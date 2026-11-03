using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Training.Journals;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training;

[RequireLogin]
public class PrintHistoryModel : PageModel
{
    private readonly TrainingPrintHistoryStore _history;
    private readonly OrganizationStore _orgs;

    public PrintHistoryModel(TrainingPrintHistoryStore history, OrganizationStore orgs)
    {
        _history = history;
        _orgs = orgs;
    }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? PrintedFrom { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? PrintedTo { get; set; }

    [BindProperty(SupportsGet = true)]
    public TrainingJournalKind? Kind { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Organization { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? BriefingType { get; set; }

    public IReadOnlyList<TrainingPrintHistoryRecord> Items { get; private set; } = [];
    public IReadOnlyList<TrainingJournalDefinition> Journals { get; } = TrainingJournalRegistry.Journals;
    public IReadOnlyList<string> BriefingTypeOptions { get; } = TrainingBriefingTypes.Options;
    public string SessionOrganization { get; private set; } = string.Empty;

    public IActionResult OnGet()
    {
        return RedirectToPage("/History", new
        {
            q = Q,
            from = PrintedFrom?.ToString("yyyy-MM-dd"),
            to = PrintedTo?.ToString("yyyy-MM-dd"),
            org = Organization,
            type = "journal"
        });
    }
}
