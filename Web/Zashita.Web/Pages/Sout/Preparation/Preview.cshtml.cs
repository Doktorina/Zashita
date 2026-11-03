using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Sout.Preparation;

[RequireLogin]
public class PreviewModel : PageModel
{
    private static readonly JsonSerializerOptions EmployeeJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SoutPreparationStore _store;
    private readonly WorkplaceStore _workplaces;
    private readonly WorkplaceAssignmentStore _assignments;
    private readonly SoutCatalogStore _catalog;
    private readonly SoutCommissionStore _commissions;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;
    private readonly OrganizationRequisitesStore _requisites;

    public PreviewModel(
        SoutPreparationStore store,
        WorkplaceStore workplaces,
        WorkplaceAssignmentStore assignments,
        SoutCatalogStore catalog,
        SoutCommissionStore commissions,
        OrganizationStore orgs,
        OrganizationEmployeesService employees,
        OrganizationRequisitesStore requisites)
    {
        _store = store;
        _workplaces = workplaces;
        _assignments = assignments;
        _catalog = catalog;
        _commissions = commissions;
        _orgs = orgs;
        _employees = employees;
        _requisites = requisites;
    }

    public string Id { get; private set; } = string.Empty;
    public string Mode { get; private set; } = SoutPreparationPrintModes.Equipment;
    public string Title { get; private set; } = string.Empty;
    public string FormTitle { get; private set; } = string.Empty;
    public string PreviewHtml { get; private set; } = string.Empty;
    public string EmployeeLookupJson { get; private set; } = "[]";
    public IReadOnlyList<string> EmployeeNames { get; private set; } = [];
    public SoutPreparationDocument? Document { get; private set; }
    public bool NotFound { get; private set; }
    public string? Message { get; private set; }
    public bool AutoPrint { get; private set; }

    public IActionResult OnGet(string id, string? mode, int? autoprint)
    {
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;
        AutoPrint = autoprint == 1;

        if (string.IsNullOrWhiteSpace(id))
        {
            NotFound = true;
            return Page();
        }

        Id = id;
        Mode = SoutPreparationPrintModes.Normalize(mode);
        Document = _store.GetById(id);
        if (Document is null)
        {
            NotFound = true;
            return Page();
        }

        FormTitle = SoutPreparationPrintModes.Title(Mode);
        Title = $"{Document.Number} от {Document.Date:dd.MM.yyyy HH:mm:ss}";
        ViewData["Title"] = " ";
        PreviewHtml = BuildPreviewHtml(Document, Mode);
        return Page();
    }

    public IActionResult OnPostSave(string id, string? mode)
    {
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;

        if (string.IsNullOrWhiteSpace(id))
        {
            NotFound = true;
            return Page();
        }

        Id = id;
        Mode = SoutPreparationPrintModes.Normalize(mode);
        Document = _store.GetById(id);
        if (Document is null)
        {
            NotFound = true;
            return Page();
        }

        var keys = Request.Form["overrideKey"];
        var values = Request.Form["overrideValue"];
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i].ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var value = i < values.Count ? values[i].ToString() ?? string.Empty : string.Empty;
            Document.PrintOverrides[key] = value;
        }

        _store.Upsert(Document);
        Message = "Изменения в форме сохранены.";
        FormTitle = SoutPreparationPrintModes.Title(Mode);
        Title = $"{Document.Number} от {Document.Date:dd.MM.yyyy HH:mm:ss}";
        ViewData["Title"] = " ";
        PreviewHtml = BuildPreviewHtml(Document, Mode);
        return Page();
    }

    private string BuildPreviewHtml(SoutPreparationDocument document, string mode)
    {
        var organization = string.IsNullOrWhiteSpace(document.Organization)
            ? _orgs.GetSelectedOrganization(HttpContext)
            : document.Organization;

        var workplaces = _workplaces.LoadTreeByOrganization(organization);
        var assignmentDocs = _assignments.LoadDocumentsByOrganization(organization);
        var equipment = _catalog.LoadEquipment(organization);
        var materials = _catalog.LoadMaterials(organization);
        var commissions = _commissions.Load(organization);
        var employees = _employees.LoadForReport(organization);

        var onlyWithoutSout = mode is SoutPreparationPrintModes.WorkplacesList;

        var rows = mode == SoutPreparationPrintModes.InfoNeeded
            ? SoutPreparationDataBuilder.BuildInfoNeededRows(
                document,
                workplaces,
                assignmentDocs,
                equipment,
                materials,
                employees,
                document.SelectedWorkplaceIds)
            : SoutPreparationDataBuilder.BuildRows(
                document,
                workplaces,
                assignmentDocs,
                equipment,
                materials,
                employees,
                onlyWithoutSout,
                document.SelectedWorkplaceIds);

        if (mode == SoutPreparationPrintModes.InfoNeeded)
        {
            var lookup = SoutPreparationDataBuilder.BuildEmployeeLookup(
                employees,
                workplaces,
                assignmentDocs,
                organization);
            EmployeeLookupJson = JsonSerializer.Serialize(lookup, EmployeeJsonOpts);
            EmployeeNames = lookup.Select(x => x.Name).ToList();
        }
        else
        {
            EmployeeLookupJson = "[]";
            EmployeeNames = [];
        }

        var commission = SoutPreparationDataBuilder.ResolveCommission(document, commissions, employees);
        var orgHeader = SoutPreparationDataBuilder.ResolveOrgHeader(
            organization,
            _requisites.Get(organization),
            employees);
        return SoutPreparationPrintBuilder.Build(document, rows, mode, commission, orgHeader);
    }
}
