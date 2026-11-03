using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Sout.Workplaces;

[RequireLogin]
public class CardPreviewModel : PageModel
{
    private readonly WorkplaceStore _store;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;

    public CardPreviewModel(WorkplaceStore store, OrganizationStore orgs, OrganizationEmployeesService employees)
    {
        _store = store;
        _orgs = orgs;
        _employees = employees;
    }

    public string Id { get; private set; } = string.Empty;
    public string CardNumber { get; private set; } = string.Empty;
    public string Organization { get; private set; } = string.Empty;
    public string PreviewHtml { get; private set; } = string.Empty;
    public bool NotFound { get; private set; }

    public void OnGet(string id)
    {
        ViewData["ActiveModule"] = "специальная-оценка";
        if (string.IsNullOrWhiteSpace(id))
        {
            NotFound = true;
            return;
        }

        var workplace = _store.GetById(id.Trim());
        if (workplace is null || workplace.IsGroup)
        {
            NotFound = true;
            return;
        }

        if (!_orgs.SelectOrganizationForRecord(HttpContext, workplace.Organization))
        {
            NotFound = true;
            return;
        }

        Organization = workplace.Organization;
        Id = workplace.Id;
        CardNumber = string.IsNullOrWhiteSpace(workplace.SoutReportNumber) ? "—" : workplace.SoutReportNumber;
        if (SoutCardPrintBuilder.EnsurePreparedDate(workplace))
        {
            workplace.ModifiedAt = DateTime.UtcNow;
            _store.Upsert(workplace);
        }

        PreviewHtml = SoutCardPrintBuilder.Build(workplace, Organization, _employees.LoadForReport(Organization));
    }
}
