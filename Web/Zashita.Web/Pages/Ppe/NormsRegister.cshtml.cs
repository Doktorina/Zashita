using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Ppe;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Ppe;

[RequireLogin]
public class NormsRegisterModel : PageModel
{
    private readonly IssueStandardsStore _standards;
    private readonly PpeNomenclatureStore _nomenclature;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;

    public NormsRegisterModel(
        IssueStandardsStore standards,
        PpeNomenclatureStore nomenclature,
        OrganizationStore orgs,
        OrganizationEmployeesService employees)
    {
        _standards = standards;
        _nomenclature = nomenclature;
        _orgs = orgs;
        _employees = employees;
    }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<string> Organizations { get; private set; } = [];
    public IReadOnlyList<PpeReportBuilder.NormsRow> Rows { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? FilterOrg { get; set; }

    public void OnGet()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;
        LoadRows();
    }

    public IActionResult OnGetPrint()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        if (string.IsNullOrWhiteSpace(FilterOrg))
        {
            FilterOrg = Organization;
        }

        return RedirectToPage("/Ppe/NormsRegister/Preview", new { filterOrg = FilterOrg });
    }

    private void LoadRows()
    {
        var allStandards = _standards.LoadAll()
            .Where(s => s.Items.Any(x => !string.IsNullOrWhiteSpace(x)))
            .ToList();
        var nomenclature = _nomenclature.LoadForEditor(allStandards);

        Organizations = allStandards
            .Select(s => (s.Organization ?? "").Trim())
            .Where(o => o.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(o => o, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (string.IsNullOrWhiteSpace(FilterOrg))
        {
            FilterOrg = Organization;
        }

        var filtered = string.IsNullOrWhiteSpace(FilterOrg)
            ? allStandards
            : allStandards.Where(s => OrganizationMatcher.Matches(FilterOrg, s.Organization));

        var departments = BuildDepartmentByProfession(FilterOrg ?? Organization);
        Rows = PpeReportBuilder.BuildNormsRegister(filtered, nomenclature, departments);
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
}
