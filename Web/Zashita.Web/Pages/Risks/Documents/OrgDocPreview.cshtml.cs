using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks.Documents;

[RequireLogin]
public class OrgDocPreviewModel : PageModel
{
    private readonly RiskWorkplaceStore _workplaces;
    private readonly RiskHazardCatalogStore _catalog;
    private readonly RiskHazardPrintSelectionStore _hazardSelection;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;
    private readonly RiskRegulationTemplateStore _regulationTemplates;
    private readonly RiskAssessmentObjectStore _objects;
    private readonly RisksWorkspaceContext _risksWorkspace;

    public OrgDocPreviewModel(
        RiskWorkplaceStore workplaces,
        RiskHazardCatalogStore catalog,
        RiskHazardPrintSelectionStore hazardSelection,
        OrganizationStore orgs,
        OrganizationEmployeesService employees,
        RiskRegulationTemplateStore regulationTemplates,
        RiskAssessmentObjectStore objects,
        RisksWorkspaceContext risksWorkspace)
    {
        _workplaces = workplaces;
        _catalog = catalog;
        _hazardSelection = hazardSelection;
        _orgs = orgs;
        _employees = employees;
        _regulationTemplates = regulationTemplates;
        _objects = objects;
        _risksWorkspace = risksWorkspace;
    }

    [BindProperty(SupportsGet = true)]
    public string? Kind { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Ids { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ObjectId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? OrgName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Manager { get; set; }

    public string Title { get; private set; } = "Предпросмотр";
    public string PreviewHtml { get; private set; } = string.Empty;
    public string Organization { get; private set; } = string.Empty;
    public string KindNormalized { get; private set; } = string.Empty;
    public bool ShowOrgEditBar { get; private set; }
    public string? SelectionHint { get; private set; }

    public void OnGet()
    {
        var sessionOrg = _orgs.GetSelectedOrganization(HttpContext);
        var visible = _orgs.GetVisibleOrganizations(HttpContext);
        var knownOrgs = _orgs.GetNonHiddenOrganizations();
        Organization = !string.IsNullOrWhiteSpace(sessionOrg)
            ? sessionOrg
            : visible.FirstOrDefault() ?? string.Empty;

        KindNormalized = RiskOrgDocumentsPrintBuilder.Normalize(Kind);
        var kind = KindNormalized;
        var printOrg = !string.IsNullOrWhiteSpace(OrgName) ? OrgName.Trim() : Organization;
        Title = $"{RiskOrgDocumentsPrintBuilder.Title(kind)} — {(string.IsNullOrWhiteSpace(printOrg) ? "организация не выбрана" : printOrg)}";
        ShowOrgEditBar = kind is RiskOrgDocumentsPrintBuilder.TitlePage
            or RiskOrgDocumentsPrintBuilder.Regulation
            or RiskOrgDocumentsPrintBuilder.Methodology;

        var selected = (Ids ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var assessmentObjects = string.IsNullOrWhiteSpace(Organization) ? [] : _objects.Load(Organization);
        var resolvedObjectId = _risksWorkspace.ResolveObjectId(HttpContext, Organization, ObjectId);
        ObjectId = resolvedObjectId;
        var resolvedObjectName = _risksWorkspace.DisplayName(Organization, resolvedObjectId);

        var hazards = _catalog.LoadActive();
        if (kind == RiskOrgDocumentsPrintBuilder.Measures)
        {
            hazards = FilterHazardsForMeasuresPrint(hazards, selected, printOrg, resolvedObjectId);
            SelectionHint = string.IsNullOrWhiteSpace(SelectionHint)
                ? $"Объект: {resolvedObjectName}"
                : $"{SelectionHint} · объект: {resolvedObjectName}";
        }

        var workplaces = string.IsNullOrWhiteSpace(Organization)
            ? []
            : _workplaces.LoadTreeByOrganization(Organization);
        if (selected.Count > 0 && kind == RiskOrgDocumentsPrintBuilder.IdentificationCards)
        {
            workplaces = workplaces.Where(w => selected.Contains(w.Id)).ToList();
        }

        if (kind == RiskOrgDocumentsPrintBuilder.IdentificationCards)
        {
            var selectedObject = assessmentObjects.FirstOrDefault(o =>
                string.Equals(o.Id, resolvedObjectId, StringComparison.OrdinalIgnoreCase));
            workplaces = RisksWorkspaceContext.ApplyWorkplaceScope(
                workplaces,
                resolvedObjectId,
                selectedObject,
                obj => _objects.Upsert(obj));
            SelectionHint = string.IsNullOrWhiteSpace(SelectionHint)
                ? $"Объект: {resolvedObjectName}"
                : $"{SelectionHint} · объект: {resolvedObjectName}";
        }

        var employees = string.IsNullOrWhiteSpace(Organization)
            ? []
            : _employees.Load(Organization);
        if (string.IsNullOrWhiteSpace(Manager) && ShowOrgEditBar)
        {
            Manager = RiskPrintChrome.ResolveDirectorSignInitials(Organization, employees);
        }

        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(printOrg))
        {
            overrides["org-name"] = printOrg;
            overrides["assess-object"] = printOrg;
        }

        if (!string.IsNullOrWhiteSpace(Manager))
        {
            overrides["org-manager"] = Manager.Trim();
        }

        // «Объект строительства» на титуле — официальное название выбранного объекта (как в договоре).
        var titleObject = assessmentObjects.FirstOrDefault(o =>
            string.Equals(o.Id, RisksWorkspaceContext.ProjectionObjectId(resolvedObjectId), StringComparison.OrdinalIgnoreCase));
        var constructionName = (titleObject?.OfficialName ?? string.Empty).Trim();
        if (constructionName.Length > 0)
        {
            overrides["construction-object"] = constructionName;
        }

        string? regulationHtml = null;
        if (kind is RiskOrgDocumentsPrintBuilder.Regulation or RiskOrgDocumentsPrintBuilder.Methodology)
        {
            var templateOrg = ResolveRegulationTemplateOrganization(printOrg, Organization);
            regulationHtml = _regulationTemplates.GetHtmlOrDefault(templateOrg);
            regulationHtml = RiskRegulationTemplateStore.ReplaceHardcodedOrganizations(
                regulationHtml,
                new[] { templateOrg, Organization, printOrg }.Concat(knownOrgs));
        }

        PreviewHtml = RiskOrgDocumentsPrintBuilder.Build(
            kind,
            printOrg,
            employees,
            hazards,
            workplaces,
            overrides,
            regulationHtml);
    }

    /// <summary>
    /// Перечень мер: только выбранные опасности (ids в URL, сохранённый выбор или из карт РМ).
    /// </summary>
    private List<RiskHazardCatalogItem> FilterHazardsForMeasuresPrint(
        IReadOnlyList<RiskHazardCatalogItem> active,
        HashSet<string> queryIds,
        string organization,
        string objectId)
    {
        var activeById = active
            .Where(h => !string.IsNullOrWhiteSpace(h.Id))
            .ToDictionary(h => h.Id, StringComparer.OrdinalIgnoreCase);

        HashSet<string> filterIds;
        if (queryIds.Count > 0)
        {
            filterIds = queryIds;
            SelectionHint = $"По выбору: {queryIds.Count(id => activeById.ContainsKey(id))} опасн.";
        }
        else
        {
            var saved = _hazardSelection.TryGetSelectedIds(organization, objectId);
            if (saved is not null)
            {
                filterIds = saved.ToHashSet(StringComparer.OrdinalIgnoreCase);
                SelectionHint =
                    filterIds.Count == 0
                        ? "Сохранён пустой выбор — перечень пуст. Откройте «Реестр опасностей (выбор)»."
                        : $"По сохранённому выбору: {filterIds.Count(id => activeById.ContainsKey(id))} опасн.";
            }
            else
            {
                filterIds = RisksWorkspaceContext.IsAllObjects(objectId)
                    ? RiskHazardPrintSelectionBuilder.CollectUsedCatalogIds(
                        organization,
                        _workplaces.LoadAll(),
                        active)
                    : RiskHazardPrintSelectionBuilder.CollectUsedCatalogIds(
                        organization,
                        _workplaces.LoadAll(),
                        active,
                        RisksWorkspaceContext.ProjectionObjectId(objectId));
                SelectionHint =
                    filterIds.Count == 0
                        ? "Нет опасностей в картах РМ — отметьте их в «Реестр опасностей (выбор)»."
                        : $"По опасностям из карт РМ: {filterIds.Count} опасн.";
            }
        }

        if (filterIds.Count == 0)
        {
            return [];
        }

        return RiskHazardNumberComparer.OrderByNumber(
            active.Where(h => filterIds.Contains(h.Id)));
    }

    private string ResolveRegulationTemplateOrganization(string printOrg, string sessionOrg)
    {
        if (!string.IsNullOrWhiteSpace(printOrg) && _regulationTemplates.HasCustomTemplate(printOrg))
        {
            return printOrg.Trim();
        }

        if (!string.IsNullOrWhiteSpace(sessionOrg) && _regulationTemplates.HasCustomTemplate(sessionOrg))
        {
            return sessionOrg.Trim();
        }

        return !string.IsNullOrWhiteSpace(sessionOrg) ? sessionOrg.Trim() : printOrg.Trim();
    }
}
