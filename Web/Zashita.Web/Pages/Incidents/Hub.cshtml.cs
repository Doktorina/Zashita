using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Incidents;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Incidents;

[RequireLogin]
public class HubModel : PageModel
{
    private readonly WorkplaceAccidentStore _accidents;
    private readonly AccidentInvestigationStore _investigations;
    private readonly MicrotraumaStore _microtraumas;
    private readonly MicrotraumaInvestigationStore _microInvestigations;
    private readonly OrganizationStore _orgs;

    public HubModel(
        WorkplaceAccidentStore accidents,
        AccidentInvestigationStore investigations,
        MicrotraumaStore microtraumas,
        MicrotraumaInvestigationStore microInvestigations,
        OrganizationStore orgs)
    {
        _accidents = accidents;
        _investigations = investigations;
        _microtraumas = microtraumas;
        _microInvestigations = microInvestigations;
        _orgs = orgs;
    }

    public string Organization { get; private set; } = string.Empty;
    public IncidentHubStats Stats { get; private set; } = new();
    public IReadOnlyList<WorkplaceAccidentRecord> RecentAccidents { get; private set; } = [];

    public void OnGet()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "несчастные-случаи";
        ViewData["IsModulePage"] = true;
        Stats = IncidentDashboardBuilder.Build(
            Organization,
            _accidents.LoadAll(),
            _investigations.LoadAll(),
            _microtraumas.LoadAll(),
            _microInvestigations.LoadAll(),
            DateTime.Today);
        RecentAccidents = _accidents.LoadByOrganization(Organization).Take(5).ToList();
    }
}
