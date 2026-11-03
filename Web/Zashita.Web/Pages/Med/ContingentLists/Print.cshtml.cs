using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Med;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med.ContingentLists;

[RequireLogin]
public class PrintModel : PageModel
{
    private readonly MedFactorStore _factors;
    private readonly MedStandardStore _standards;
    private readonly MedContingentOverridesStore _overrides;
    private readonly MedContextService _med;
    private readonly OrganizationRequisitesStore _requisites;

    public PrintModel(
        MedFactorStore factors,
        MedStandardStore standards,
        MedContingentOverridesStore overrides,
        MedContextService med,
        OrganizationRequisitesStore requisites)
    {
        _factors = factors;
        _standards = standards;
        _overrides = overrides;
        _med = med;
        _requisites = requisites;
    }

    [BindProperty(SupportsGet = true)]
    public string? Kind { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public string Title { get; private set; } = "Печать";
    public string PreviewHtml { get; private set; } = string.Empty;
    public string ActiveKind { get; private set; } = "contingents";
    public string? Message { get; private set; }

    public void OnGet() => LoadPage();

    public IActionResult OnPostSave(string? kind)
    {
        Organization = _med.GetOrganization(HttpContext);
        Kind = kind;
        ActiveKind = string.Equals(Kind, "named", StringComparison.OrdinalIgnoreCase) ? "named" : "contingents";

        var map = new Dictionary<string, string>(
            _overrides.LoadPrintSettings(Organization).Overrides,
            StringComparer.OrdinalIgnoreCase);
        var keys = Request.Form["overrideKey"];
        var values = Request.Form["overrideValue"];
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i].ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            map[key] = i < values.Count ? values[i].ToString() ?? string.Empty : string.Empty;
        }

        _overrides.SavePrintOverrides(Organization, map);
        Message = "Изменения сохранены.";
        LoadPage();
        return Page();
    }

    private void LoadPage()
    {
        Organization = _med.GetOrganization(HttpContext);
        _med.EnsureInitialized(Organization);
        ActiveKind = string.Equals(Kind, "named", StringComparison.OrdinalIgnoreCase) ? "named" : "contingents";

        var employees = _med.GetEmployees(Organization);
        var printSettings = _overrides.LoadPrintSettings(Organization);
        var requisites = _requisites.Get(Organization);
        var contingent = MedContingentListBuilder.Build(
            employees,
            _med.GetExams(Organization),
            _factors.LoadAll(),
            _overrides.Load(Organization),
            _standards.LoadByOrganization(Organization));

        if (ActiveKind == "named")
        {
            Title = $"Поименный список (ЛПУ) — {Organization}";
            var named = MedContingentNamedListBuilder.Build(
                contingent.Rows,
                employees,
                _med.GetExams(Organization),
                _overrides.LoadNamed(Organization));
            PreviewHtml = MedContingentPrintBuilder.BuildNamedListHtml(
                Organization, named, printSettings, employees);
        }
        else
        {
            Title = $"Список контингентов — {Organization}";
            PreviewHtml = MedContingentPrintBuilder.BuildContingentsHtml(
                Organization,
                contingent.Rows,
                contingent.Totals,
                employees,
                printSettings,
                requisites);
        }
    }
}
