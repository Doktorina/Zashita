using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks.Documents;

[RequireLogin]
public class SelectHazardsModel : PageModel
{
    private readonly RiskHazardCatalogStore _catalog;
    private readonly RiskWorkplaceStore _workplaces;
    private readonly RiskHazardPrintSelectionStore _selection;
    private readonly RiskAssessmentObjectStore _objects;
    private readonly OrganizationStore _orgs;
    private readonly RisksWorkspaceContext _risksWorkspace;

    public SelectHazardsModel(
        RiskHazardCatalogStore catalog,
        RiskWorkplaceStore workplaces,
        RiskHazardPrintSelectionStore selection,
        RiskAssessmentObjectStore objects,
        OrganizationStore orgs,
        RisksWorkspaceContext risksWorkspace)
    {
        _catalog = catalog;
        _workplaces = workplaces;
        _selection = selection;
        _objects = objects;
        _orgs = orgs;
        _risksWorkspace = risksWorkspace;
    }

    [BindProperty(SupportsGet = true)]
    public string? ObjectId { get; set; }

    /// <summary>registry | measures — какой документ открываем с этой страницы выбора.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Purpose { get; set; }

    [BindProperty]
    public List<string> SelectedIds { get; set; } = [];

    public bool IsMeasuresPurpose =>
        string.Equals(NormalizePurpose(Purpose), "measures", StringComparison.OrdinalIgnoreCase);

    public string PageHeading => IsMeasuresPurpose
        ? "Перечень мер управления — выбор для печати"
        : "Реестр опасностей — выбор для печати";

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<RiskHazardCatalogItem> Rows { get; private set; } = [];
    public HashSet<string> CheckedIds { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> UsedInCardsIds { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<RiskAssessmentObject> AssessmentObjects { get; private set; } = [];
    public string SelectedObjectId { get; private set; } = RiskAssessmentObjectIds.OrganizationWide;
    public RiskAssessmentObject? SelectedObject =>
        AssessmentObjects.FirstOrDefault(o =>
            string.Equals(o.Id, SelectedObjectId, StringComparison.OrdinalIgnoreCase));
    public string SelectedObjectDisplayName =>
        _risksWorkspace.DisplayName(Organization, SelectedObjectId);
    public bool HasSavedSelection { get; private set; }
    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    public void OnGet()
    {
        Message = TempData["Message"] as string;
        IsError = string.Equals(TempData["IsError"] as string, "1", StringComparison.Ordinal);
        Load();
    }

    public IActionResult OnPostSave()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ResolveObject();
        _selection.SaveSelectedIds(Organization, SelectedObjectId, SelectedIds);
        TempData["Message"] =
            SelectedIds.Count == 0
                ? "Сохранено: ни одна опасность не выбрана (в печати будет пусто, пока снова не отметите)."
                : $"Сохранён выбор: {SelectedIds.Count} опасн.";
        return RedirectToPage(new { objectId = SelectedObjectId, purpose = NormalizePurpose(Purpose) });
    }

    public IActionResult OnPostResetToCards()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ResolveObject();
        var catalog = _catalog.LoadActive();
        var used = CollectUsedInCards(catalog);
        _selection.SaveSelectedIds(Organization, SelectedObjectId, used);
        TempData["Message"] =
            used.Count == 0
                ? "По картам оценки рисков опасностей не найдено — выбор очищен."
                : $"Выбор сброшен к картам: отмечено {used.Count} опасн.";
        return RedirectToPage(new { objectId = SelectedObjectId, purpose = NormalizePurpose(Purpose) });
    }

    private void ResolveObject()
    {
        AssessmentObjects = _objects.Load(Organization);
        SelectedObjectId = _risksWorkspace.ResolveObjectId(HttpContext, Organization, ObjectId);
        ObjectId = SelectedObjectId;
    }

    private HashSet<string> CollectUsedInCards(IReadOnlyList<RiskHazardCatalogItem> catalog) =>
        RisksWorkspaceContext.IsAllObjects(SelectedObjectId)
            ? RiskHazardPrintSelectionBuilder.CollectUsedCatalogIds(
                Organization,
                _workplaces.LoadAll(),
                catalog)
            : RiskHazardPrintSelectionBuilder.CollectUsedCatalogIds(
                Organization,
                _workplaces.LoadAll(),
                catalog,
                RisksWorkspaceContext.ProjectionObjectId(SelectedObjectId));

    private void Load()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "риски";
        ViewData["IsModulePage"] = true;
        Purpose = NormalizePurpose(Purpose);
        ViewData["Title"] = PageHeading;
        ViewData["Subtitle"] = Organization;
        ResolveObject();
        Rows = RiskHazardNumberComparer.OrderByNumber(_catalog.LoadActive());

        UsedInCardsIds = CollectUsedInCards(Rows);

        var saved = _selection.TryGetSelectedIds(Organization, SelectedObjectId);
        HasSavedSelection = saved is not null;
        if (saved is not null)
        {
            var active = Rows.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            CheckedIds = saved.Where(active.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            CheckedIds = UsedInCardsIds;
        }
    }

    private static string NormalizePurpose(string? purpose)
    {
        var p = (purpose ?? string.Empty).Trim().ToLowerInvariant();
        return p is "measures" or "меры" or "mera" ? "measures" : "registry";
    }

}
