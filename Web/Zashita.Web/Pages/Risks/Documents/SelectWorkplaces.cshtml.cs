using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks.Documents;

[RequireLogin]
public class SelectWorkplacesModel : PageModel
{
    private readonly RiskWorkplaceStore _workplaces;
    private readonly RiskPreparationStore _preparation;
    private readonly RiskAssessmentObjectStore _objects;
    private readonly OrganizationStore _orgs;
    private readonly RisksWorkspaceContext _risksWorkspace;

    public SelectWorkplacesModel(
        RiskWorkplaceStore workplaces,
        RiskPreparationStore preparation,
        RiskAssessmentObjectStore objects,
        OrganizationStore orgs,
        RisksWorkspaceContext risksWorkspace)
    {
        _workplaces = workplaces;
        _preparation = preparation;
        _objects = objects;
        _orgs = orgs;
        _risksWorkspace = risksWorkspace;
    }

    [BindProperty(SupportsGet = true)]
    public string? PrepId { get; set; }

    /// <summary>workplaces — перечень РМ; cards — печать карт идентификации/оценки.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Purpose { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ObjectId { get; set; }

    [BindProperty]
    public List<string> SelectedIds { get; set; } = [];

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<WorkplaceRecord> Rows { get; private set; } = [];
    public HashSet<string> PrecheckedIds { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<RiskAssessmentObject> AssessmentObjects { get; private set; } = [];
    public string SelectedObjectId { get; private set; } = RiskAssessmentObjectIds.OrganizationWide;
    public RiskAssessmentObject? SelectedObject =>
        AssessmentObjects.FirstOrDefault(o =>
            string.Equals(o.Id, SelectedObjectId, StringComparison.OrdinalIgnoreCase));
    public string SelectedObjectDisplayName =>
        _risksWorkspace.DisplayName(Organization, SelectedObjectId);
    public string? Message { get; private set; }
    public bool IsCardsPurpose =>
        string.Equals(Purpose, "cards", StringComparison.OrdinalIgnoreCase);

    public void OnGet()
    {
        Load();
    }

    public IActionResult OnPostPrint()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        AssessmentObjects = _objects.Load(Organization);
        SelectedObjectId = _risksWorkspace.ResolveObjectId(HttpContext, Organization, ObjectId);

        if (SelectedIds.Count == 0)
        {
            Message = "Отметьте хотя бы одну профессию / рабочее место.";
            Load();
            return Page();
        }

        var ids = string.Join(",", SelectedIds.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (IsCardsPurpose)
        {
            return RedirectToPage("/Risks/Documents/OrgDocPreview", new
            {
                kind = "identification-cards",
                ids,
                objectId = SelectedObjectId
            });
        }

        return RedirectToPage("/Risks/Documents/PrintSelection", new
        {
            kind = "workplaces",
            ids,
            prepId = PrepId,
            objectId = SelectedObjectId
        });
    }

    private void Load()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "риски";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = IsCardsPurpose
            ? "Карты идентификации — выбор РМ"
            : "Перечень РМ для ОПР — выбор";
        ViewData["Subtitle"] = Organization;

        var rows = _workplaces.LoadTreeByOrganization(Organization)
            .Where(w => !w.IsGroup && w.IsUsed && !RiskPrintChrome.IsIndividualEntrepreneurWorkplace(w))
            .OrderBy(w => w.Department, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(w => w.Profession, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        AssessmentObjects = _objects.Load(Organization);
        SelectedObjectId = _risksWorkspace.ResolveObjectId(HttpContext, Organization, ObjectId);
        ObjectId = SelectedObjectId;
        rows = RisksWorkspaceContext.ApplyWorkplaceScope(
            rows,
            SelectedObjectId,
            SelectedObject,
            obj => _objects.Upsert(obj));
        Rows = rows;

        var today = DateTime.Today;
        if (IsCardsPurpose)
        {
            // Карты: по умолчанию все профессии объекта.
            PrecheckedIds = rows.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            // Перечень РМ: отмечены только профессии с оценкой на этом объекте.
            PrecheckedIds = rows
                .Where(r => RiskAssessmentStatusBuilder.HasValidRiskAssessment(r, today))
                .Select(r => r.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

}
