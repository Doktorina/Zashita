using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Med;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med;

[RequireLogin]
public class RestrictionsModel : PageModel
{
    private readonly MedContextService _med;
    private readonly MedExamStore _exams;
    private readonly MedStandardStore _standards;
    private readonly MedDirectionStore _directions;
    private readonly OrganizationEmployeesService _employees;
    private readonly MedPhysicalRestrictionNotesStore _notes;

    public RestrictionsModel(
        MedContextService med,
        MedExamStore exams,
        MedStandardStore standards,
        MedDirectionStore directions,
        OrganizationEmployeesService employees,
        MedPhysicalRestrictionNotesStore notes)
    {
        _med = med;
        _exams = exams;
        _standards = standards;
        _directions = directions;
        _employees = employees;
        _notes = notes;
    }

    [BindProperty(SupportsGet = true)]
    public string? Employee { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<MedPhysicalRestrictionRow> Rows { get; private set; } = [];
    public IReadOnlyList<string> Employees { get; private set; } = [];
    public string? Message { get; private set; }

    public void OnGet()
    {
        Message = TempData["Message"] as string;
        LoadRows();
    }

    public IActionResult OnPostSave()
    {
        Organization = _med.GetOrganization(HttpContext);
        _med.EnsureInitialized(Organization);

        var names = Request.Form["employeeName"];
        var restrictions = Request.Form["workRestriction"];
        var count = Math.Min(names.Count, restrictions.Count);
        var notesByEmployee = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < count; i++)
        {
            var name = names[i]!.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            notesByEmployee[name] = restrictions[i]!.Trim();
        }

        _notes.SaveOrganization(Organization, notesByEmployee);
        TempData["Message"] = "Ограничения по работе сохранены.";
        return RedirectToPage(new { employee = Employee, q = Q });
    }

    private void LoadRows()
    {
        Organization = _med.GetOrganization(HttpContext);
        _med.EnsureInitialized(Organization);
        ViewData["ActiveModule"] = "медосмотры";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Ограничения физ. лиц";
        ViewData["Subtitle"] = Organization;

        var statusRows = MedStatusReportCleanup.Clean(LoadStatusRows());
        var noteMap = _notes.LoadMap(Organization)
            .ToDictionary(
                pair => MedPhysicalRestrictionsBuilder.PersonKey(Organization, pair.Key),
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);

        var rows = MedPhysicalRestrictionsBuilder.Build(statusRows, noteMap);

        if (!string.IsNullOrWhiteSpace(Employee))
        {
            var employee = Employee.Trim();
            rows = rows
                .Where(row => row.EmployeeName.Contains(employee, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var query = Q.Trim();
            rows = rows
                .Where(row =>
                    row.EmployeeName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    row.Profession.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    row.Factors.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    row.WorkRestriction.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        Rows = rows;
        Employees = _med.GetEmployeeNames(Organization);
    }

    private IReadOnlyList<MedStatusRow> LoadStatusRows()
    {
        var employees = _employees.LoadForMedReport(Organization)
            .Select(e => (Organization, e))
            .ToList();
        var exams = _exams.LoadByOrganization(Organization);
        var standards = _standards.LoadByOrganization(Organization);
        var directionKeys = MedDirectionMatcher.BuildIssuedKeysForOrganizations(
            _directions.LoadAll(),
            [Organization]);

        var synced = MedStandardAssignmentBuilder.CollapseDuplicateExams(
            MedStandardAssignmentBuilder.ExpandExamsFromStandards(employees, exams, standards));
        return MedStatusBuilder.Build(employees, synced, DateTime.Today, directionKeys);
    }
}
