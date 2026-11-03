using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Incidents;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Incidents;

[RequireLogin]
public class InvestigationsModel : PageModel
{
    private readonly AccidentInvestigationStore _store;
    private readonly WorkplaceAccidentStore _accidents;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesStore _employees;

    public InvestigationsModel(
        AccidentInvestigationStore store,
        WorkplaceAccidentStore accidents,
        OrganizationStore orgs,
        OrganizationEmployeesStore employees)
    {
        _store = store;
        _accidents = accidents;
        _orgs = orgs;
        _employees = employees;
    }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<AccidentInvestigationRecord> Rows { get; private set; } = [];
    public IReadOnlyList<WorkplaceAccidentRecord> SourceAccidents { get; private set; } = [];
    public IReadOnlyList<string> Departments { get; private set; } = [];
    public string? Message { get; private set; }

    [BindProperty(SupportsGet = true)] public string? EditId { get; set; }
    [BindProperty(SupportsGet = true)] public bool Create { get; set; }
    [BindProperty] public bool Posted { get; set; }
    [BindProperty] public DateTime DocumentDate { get; set; } = DateTime.Today;
    [BindProperty] public string Number { get; set; } = string.Empty;
    [BindProperty] public string? SourceAccidentId { get; set; }
    [BindProperty] public string SourceAccidentNumber { get; set; } = string.Empty;
    [BindProperty] public DateTime? AccidentDateTime { get; set; }
    [BindProperty] public string Victims { get; set; } = string.Empty;
    [BindProperty] public string Department { get; set; } = string.Empty;
    [BindProperty] public string Circumstances { get; set; } = string.Empty;
    [BindProperty] public string Conclusion { get; set; } = string.Empty;

    public void OnGet()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "несчастные-случаи";
        ViewData["IsModulePage"] = true;
        Rows = _store.LoadByOrganization(Organization);
        SourceAccidents = _accidents.LoadByOrganization(Organization);
        Departments = DepartmentSelect.Merge(
            DepartmentSelect.FromEmployees(_employees, Organization),
            Rows.Select(r => r.Department),
            SourceAccidents.Select(a => a.Department));
        if (Create && string.IsNullOrWhiteSpace(EditId))
        {
            Number = _store.SuggestNextNumber();
        }

        LoadEditRow();
    }

    public IActionResult OnPostSave()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        if (string.IsNullOrWhiteSpace(Number))
        {
            Message = "Укажите номер расследования.";
            OnGet();
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(SourceAccidentId))
        {
            var src = _accidents.LoadAll().FirstOrDefault(a => a.Id == SourceAccidentId);
            if (src is not null)
            {
                SourceAccidentNumber = src.Number;
                if (!AccidentDateTime.HasValue)
                {
                    AccidentDateTime = src.IncidentDateTime;
                }

                if (string.IsNullOrWhiteSpace(Victims))
                {
                    Victims = src.Victims;
                }
            }
        }

        var id = string.IsNullOrWhiteSpace(EditId) ? Guid.NewGuid().ToString("N") : EditId;
        var existing = _store.LoadAll().FirstOrDefault(r => r.Id == id);
        _store.Upsert(new AccidentInvestigationRecord
        {
            Id = id,
            Posted = Posted,
            DocumentDate = DocumentDate.Date,
            Number = Number.Trim(),
            SourceAccidentId = SourceAccidentId,
            SourceAccidentNumber = SourceAccidentNumber.Trim(),
            AccidentDateTime = AccidentDateTime,
            Victims = Victims.Trim(),
            Department = Department.Trim(),
            Organization = Organization,
            Circumstances = Circumstances.Trim(),
            Conclusion = Conclusion.Trim(),
            CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
            History = existing?.History ?? []
        });

        return RedirectToPage(new { editId = id });
    }

    public IActionResult OnPostDelete(string deleteId)
    {
        if (!string.IsNullOrWhiteSpace(deleteId))
        {
            _store.Delete(deleteId);
        }

        return RedirectToPage();
    }

    private void LoadEditRow()
    {
        if (string.IsNullOrWhiteSpace(EditId))
        {
            return;
        }

        var row = Rows.FirstOrDefault(r => r.Id == EditId) ?? _store.LoadAll().FirstOrDefault(r => r.Id == EditId);
        if (row is null)
        {
            return;
        }

        Posted = row.Posted;
        DocumentDate = row.DocumentDate;
        Number = row.Number;
        SourceAccidentId = row.SourceAccidentId;
        SourceAccidentNumber = row.SourceAccidentNumber;
        AccidentDateTime = row.AccidentDateTime;
        Victims = row.Victims;
        Department = row.Department;
        Circumstances = row.Circumstances;
        Conclusion = row.Conclusion;
    }
}
