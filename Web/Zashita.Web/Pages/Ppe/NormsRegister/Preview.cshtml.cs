using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Ppe;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Ppe.NormsRegister;

[RequireLogin]
public class PreviewModel : PageModel
{
    private readonly IssueStandardsStore _standards;
    private readonly PpeNomenclatureStore _nomenclature;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;
    private readonly PpeNormsRegisterPrintOverridesStore _overrides;

    public PreviewModel(
        IssueStandardsStore standards,
        PpeNomenclatureStore nomenclature,
        OrganizationStore orgs,
        OrganizationEmployeesService employees,
        PpeNormsRegisterPrintOverridesStore overrides)
    {
        _standards = standards;
        _nomenclature = nomenclature;
        _orgs = orgs;
        _employees = employees;
        _overrides = overrides;
    }

    [BindProperty(SupportsGet = true)]
    public string? FilterOrg { get; set; }

    public string PreviewHtml { get; private set; } = string.Empty;
    public string? Message { get; private set; }

    public IActionResult OnGet()
    {
        LoadPreview();
        return Page();
    }

    public IActionResult OnPostSave()
    {
        var orgKey = ResolveOrgKey();
        var keys = Request.Form["overrideKey"];
        var values = Request.Form["overrideValue"];
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i].ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            map[key] = i < values.Count ? values[i].ToString() ?? string.Empty : string.Empty;
        }

        _overrides.Save(orgKey, map);
        Message = "Изменения в форме сохранены.";
        LoadPreview();
        return Page();
    }

    private void LoadPreview()
    {
        var organization = _orgs.GetSelectedOrganization(HttpContext);
        if (string.IsNullOrWhiteSpace(FilterOrg))
        {
            FilterOrg = organization;
        }

        var allStandards = _standards.LoadAll()
            .Where(s => s.Items.Any(x => !string.IsNullOrWhiteSpace(x)))
            .ToList();
        var nomenclature = _nomenclature.LoadForEditor(allStandards);
        var filtered = string.IsNullOrWhiteSpace(FilterOrg)
            ? allStandards
            : allStandards.Where(s => OrganizationMatcher.Matches(FilterOrg, s.Organization));

        var orgKey = ResolveOrgKey();
        var departments = BuildDepartmentByProfession(orgKey);
        var rows = PpeReportBuilder.BuildNormsRegister(filtered, nomenclature, departments);
        var overrides = _overrides.Load(orgKey);
        PreviewHtml = PpeNormsRegisterPrintBuilder.BuildBody(orgKey, rows, overrides);
    }

    private IReadOnlyDictionary<string, string> BuildDepartmentByProfession(string organization)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var employee in _employees.Load(organization))
        {
            var profession = employee.Profession?.Trim() ?? string.Empty;
            var department = employee.Department?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(profession) || string.IsNullOrWhiteSpace(department))
            {
                continue;
            }

            map.TryAdd(profession, department);
        }

        return map;
    }

    private string ResolveOrgKey() =>
        string.IsNullOrWhiteSpace(FilterOrg)
            ? _orgs.GetSelectedOrganization(HttpContext)
            : FilterOrg.Trim();
}
