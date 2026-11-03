using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Sout.Preparation;

[RequireLogin]
public class TemplatesModel : PageModel
{
    private static readonly JsonSerializerOptions EmployeeJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SoutPreparationStore _store;
    private readonly WorkplaceStore _workplaces;
    private readonly WorkplaceAssignmentStore _assignments;
    private readonly SoutAnnualPlanStore _annualPlans;
    private readonly SoutCatalogStore _catalog;
    private readonly SoutCommissionStore _commissions;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;
    private readonly OrganizationRequisitesStore _requisites;

    public TemplatesModel(
        SoutPreparationStore store,
        WorkplaceStore workplaces,
        WorkplaceAssignmentStore assignments,
        SoutAnnualPlanStore annualPlans,
        SoutCatalogStore catalog,
        SoutCommissionStore commissions,
        OrganizationStore orgs,
        OrganizationEmployeesService employees,
        OrganizationRequisitesStore requisites)
    {
        _store = store;
        _workplaces = workplaces;
        _assignments = assignments;
        _annualPlans = annualPlans;
        _catalog = catalog;
        _commissions = commissions;
        _orgs = orgs;
        _employees = employees;
        _requisites = requisites;
    }

    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Preview { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public string ActiveTab { get; private set; } = "hub";
    public bool IsHub => string.Equals(ActiveTab, "hub", StringComparison.OrdinalIgnoreCase);
    public string DocumentId { get; private set; } = string.Empty;
    public string DocumentNumber { get; private set; } = string.Empty;
    public string PreviewUrl { get; private set; } = string.Empty;
    public string PreviewHtml { get; private set; } = string.Empty;
    public string EmployeeLookupJson { get; private set; } = "[]";
    public IReadOnlyList<string> EmployeeNames { get; private set; } = [];
    public IReadOnlyList<SoutTemplateWorkplaceRow> WorkplaceRows { get; private set; } = [];
    public HashSet<string> SelectedWorkplaceIds { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool HasExplicitSelection { get; private set; }
    public bool ShowPreview { get; private set; }
    public string? Message { get; private set; }

    public IActionResult OnGet()
    {
        LoadPage();
        if (!IsHub && !string.Equals(ActiveTab, SoutPreparationPrintModes.InfoNeeded, StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToPage("/Sout/Preparation/Templates");
        }

        return Page();
    }

    public IActionResult OnPostSaveWorkplaces(string[]? selectedWorkplaceIds)
    {
        Tab = SoutPreparationPrintModes.InfoNeeded;
        LoadPage();
        var document = _store.GetById(DocumentId);
        if (document is null)
        {
            TempData["TemplatesMessage"] = "Документ подготовки СОУТ не найден.";
            return RedirectToPage(new { tab = SoutPreparationPrintModes.InfoNeeded });
        }

        document.SelectedWorkplaceIds = (selectedWorkplaceIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _store.Upsert(document);

        if (document.SelectedWorkplaceIds.Count == 0)
        {
            TempData["TemplatesMessage"] = "Отметьте хотя бы одно рабочее место и нажмите «Сформировать предпечать».";
            return RedirectToPage(new { tab = SoutPreparationPrintModes.InfoNeeded });
        }

        TempData["TemplatesMessage"] =
            $"Выбрано рабочих мест: {document.SelectedWorkplaceIds.Count}. Данные подтянуты из карт СОУТ — проверьте таблицу предпечати.";
        return RedirectToPage(new { tab = SoutPreparationPrintModes.InfoNeeded, preview = 1 });
    }

    public IActionResult OnPostSavePreview()
    {
        Tab = SoutPreparationPrintModes.InfoNeeded;
        LoadPage();
        var document = _store.GetById(DocumentId);
        if (document is null)
        {
            TempData["TemplatesMessage"] = "Документ подготовки СОУТ не найден.";
            return RedirectToPage(new { tab = SoutPreparationPrintModes.InfoNeeded });
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
            document.PrintOverrides[key] = value;
        }

        _store.Upsert(document);
        TempData["TemplatesMessage"] = "Изменения в форме сохранены.";
        return RedirectToPage(new { tab = SoutPreparationPrintModes.InfoNeeded, preview = 1 });
    }

    private void LoadPage()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        SoutOrganizationBootstrap.Ensure(Organization, _workplaces, _assignments, _store, _annualPlans);
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Шаблоны для печати";
        ViewData["Subtitle"] = Organization;
        Message = TempData["TemplatesMessage"] as string;

        var rawTab = Tab?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawTab) ||
            string.Equals(rawTab, "hub", StringComparison.OrdinalIgnoreCase))
        {
            ActiveTab = "hub";
        }
        else
        {
            ActiveTab = SoutPreparationPrintModes.Normalize(rawTab) switch
            {
                SoutPreparationPrintModes.InfoNeeded => SoutPreparationPrintModes.InfoNeeded,
                _ => "hub"
            };
        }

        var document = EnsureDocument();
        DocumentId = document.Id;
        DocumentNumber = document.Number;
        if (!IsHub)
        {
            PreviewUrl = $"/Sout/Preparation/Preview?id={Uri.EscapeDataString(DocumentId)}&mode={Uri.EscapeDataString(SoutPreparationPrintModes.InfoNeeded)}";
            LoadWorkplaceRows(document);
            HasExplicitSelection = document.SelectedWorkplaceIds.Count > 0;
            SelectedWorkplaceIds = document.SelectedWorkplaceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Шаг 2 только после явного «Сформировать предпечать» (?preview=1) или уже сохранённого выбора с preview.
            ShowPreview = Preview == 1 && HasExplicitSelection;
            if (ShowPreview)
            {
                BuildPreview(document);
            }
        }
    }

    private SoutPreparationDocument EnsureDocument()
    {
        var document = _store.LoadByOrganization(Organization).FirstOrDefault();
        if (document is not null)
        {
            return document;
        }

        document = new SoutPreparationDocument
        {
            Organization = Organization,
            Number = _store.SuggestNextNumber(),
            Date = DateTime.Now,
            ContractorOrganization = string.Empty
        };
        _store.Upsert(document);
        return document;
    }

    private void LoadWorkplaceRows(SoutPreparationDocument document)
    {
        var candidates = SoutPreparationDataBuilder.GetCandidateWorkplaces(
            document,
            _workplaces.LoadTreeByOrganization(Organization),
            onlyWithoutSout: false);
        WorkplaceRows = candidates
            .Select(wp => new SoutTemplateWorkplaceRow(
                wp.Id,
                wp.WorkplaceNumber,
                wp.Name,
                wp.Department,
                wp.Profession,
                wp.ConditionsClass,
                SoutStatusBuilder.ResolvePlanYearDisplay(wp),
                (wp.SoutCard?.EmployeeIds?.Count ?? 0) > 0))
            .ToList();
    }

    private void BuildPreview(SoutPreparationDocument document)
    {
        var organization = string.IsNullOrWhiteSpace(document.Organization)
            ? Organization
            : document.Organization;
        var workplaces = _workplaces.LoadTreeByOrganization(organization);
        var assignmentDocs = _assignments.LoadDocumentsByOrganization(organization);
        var equipment = _catalog.LoadEquipment(organization);
        var materials = _catalog.LoadMaterials(organization);
        var employees = _employees.LoadForReport(organization);
        var commissions = _commissions.Load(organization);

        var rows = SoutPreparationDataBuilder.BuildInfoNeededRows(
            document,
            workplaces,
            assignmentDocs,
            equipment,
            materials,
            employees,
            document.SelectedWorkplaceIds);

        var lookup = SoutPreparationDataBuilder.BuildEmployeeLookup(
            employees,
            workplaces,
            assignmentDocs,
            organization);
        EmployeeLookupJson = JsonSerializer.Serialize(lookup, EmployeeJsonOpts);
        EmployeeNames = lookup.Select(x => x.Name).ToList();

        var commission = SoutPreparationDataBuilder.ResolveCommission(document, commissions, employees);
        var orgHeader = SoutPreparationDataBuilder.ResolveOrgHeader(
            organization,
            _requisites.Get(organization),
            employees);
        PreviewHtml = SoutPreparationPrintBuilder.Build(
            document,
            rows,
            SoutPreparationPrintModes.InfoNeeded,
            commission,
            orgHeader);
    }
}

public sealed record SoutTemplateWorkplaceRow(
    string Id,
    string WorkplaceNumber,
    string Name,
    string Department,
    string Profession,
    string ConditionsClass,
    string PlanYear,
    bool HasCardEmployees);
