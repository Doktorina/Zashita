using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks.Documents;

[RequireLogin]
public class PrintSelectionModel : PageModel
{
    private readonly RiskWorkplaceStore _workplaces;
    private readonly RiskPreparationStore _preparation;
    private readonly RiskHazardCatalogStore _catalog;
    private readonly RiskHazardPrintSelectionStore _hazardSelection;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;
    private readonly RisksWorkspaceContext _risksWorkspace;

    public PrintSelectionModel(
        RiskWorkplaceStore workplaces,
        RiskPreparationStore preparation,
        RiskHazardCatalogStore catalog,
        RiskHazardPrintSelectionStore hazardSelection,
        OrganizationStore orgs,
        OrganizationEmployeesService employees,
        RisksWorkspaceContext risksWorkspace)
    {
        _workplaces = workplaces;
        _preparation = preparation;
        _catalog = catalog;
        _hazardSelection = hazardSelection;
        _orgs = orgs;
        _employees = employees;
        _risksWorkspace = risksWorkspace;
    }

    [BindProperty(SupportsGet = true)]
    public string? Kind { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Ids { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PrepId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ObjectId { get; set; }

    public string Title { get; private set; } = "Печать";
    public string PreviewHtml { get; private set; } = string.Empty;
    public string BackUrl { get; private set; } = "/Risks/Documents";

    public void OnGet()
    {
        var organization = _orgs.GetSelectedOrganization(HttpContext);
        var objectId = _risksWorkspace.ResolveObjectId(HttpContext, organization, ObjectId);
        ObjectId = objectId;
        var objectName = _risksWorkspace.DisplayName(organization, objectId);

        var selected = (Ids ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (string.Equals(Kind, "hazards", StringComparison.OrdinalIgnoreCase))
        {
            Title = $"Реестр опасностей — {organization} · {objectName}";
            var purpose = string.Equals(Request.Query["purpose"], "measures", StringComparison.OrdinalIgnoreCase)
                ? "measures"
                : "registry";
            BackUrl = string.IsNullOrWhiteSpace(objectId)
                ? $"/Risks/Documents/SelectHazards?purpose={purpose}"
                : $"/Risks/Documents/SelectHazards?objectId={Uri.EscapeDataString(objectId)}&purpose={purpose}";
            var catalog = _catalog.LoadActive();
            if (selected.Count == 0)
            {
                var saved = _hazardSelection.TryGetSelectedIds(organization, objectId);
                selected = saved is not null
                    ? saved.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    : RisksWorkspaceContext.IsAllObjects(objectId)
                        ? RiskHazardPrintSelectionBuilder.CollectUsedCatalogIds(
                            organization,
                            _workplaces.LoadAll(),
                            catalog)
                        : RiskHazardPrintSelectionBuilder.CollectUsedCatalogIds(
                            organization,
                            _workplaces.LoadAll(),
                            catalog,
                            RisksWorkspaceContext.ProjectionObjectId(objectId));
            }

            var staff = _employees.Load(organization);
            PreviewHtml = RiskHazardCatalogPrintBuilder.Build(
                organization,
                catalog,
                selected,
                chairmanName: null,
                employees: staff);
            return;
        }

        Title = $"Перечень РМ для ОПР — {organization} · {objectName}";
        var backQs = new List<string>();
        if (!string.IsNullOrWhiteSpace(PrepId))
        {
            backQs.Add($"prepId={Uri.EscapeDataString(PrepId)}");
        }

        backQs.Add($"objectId={Uri.EscapeDataString(objectId)}");
        BackUrl = "/Risks/Documents/SelectWorkplaces?" + string.Join("&", backQs);

        var prep = !string.IsNullOrWhiteSpace(PrepId)
            ? _preparation.GetById(PrepId)
            : null;
        prep ??= new RiskPreparationDocument
        {
            Organization = organization,
            Number = "—",
            Date = DateTime.Today
        };
        if (string.IsNullOrWhiteSpace(prep.Organization))
        {
            prep.Organization = organization;
        }

        PreviewHtml = RiskPreparationPrintBuilder.BuildWorkplacesListSelected(
            prep,
            _workplaces.LoadAll(),
            selected,
            _employees.Load(organization));
    }
}
