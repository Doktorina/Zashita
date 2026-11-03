using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Incidents;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Incidents;

[RequireLogin]
public class MicrotraumaInvestigationsModel : PageModel
{
    private readonly MicrotraumaInvestigationStore _store;
    private readonly MicrotraumaStore _microtraumas;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesStore _employees;

    public MicrotraumaInvestigationsModel(
        MicrotraumaInvestigationStore store,
        MicrotraumaStore microtraumas,
        OrganizationStore orgs,
        OrganizationEmployeesStore employees)
    {
        _store = store;
        _microtraumas = microtraumas;
        _orgs = orgs;
        _employees = employees;
    }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<MicrotraumaInvestigationRecord> Rows { get; private set; } = [];
    public IReadOnlyList<MicrotraumaRecord> SourceMicrotraumas { get; private set; } = [];
    public IReadOnlyList<string> Departments { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string? EditId { get; set; }
    [BindProperty(SupportsGet = true)] public bool Create { get; set; }
    [BindProperty] public bool Posted { get; set; }
    [BindProperty] public DateTime DocumentDate { get; set; } = DateTime.Today;
    [BindProperty] public string Number { get; set; } = string.Empty;
    [BindProperty] public string? SourceMicrotraumaId { get; set; }
    [BindProperty] public string SourceMicrotraumaNumber { get; set; } = string.Empty;
    [BindProperty] public DateTime? EventDateTime { get; set; }
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
        SourceMicrotraumas = _microtraumas.LoadByOrganization(Organization);
        Departments = DepartmentSelect.Merge(
            DepartmentSelect.FromEmployees(_employees, Organization),
            Rows.Select(r => r.Department),
            SourceMicrotraumas.Select(m => m.Department));
        if (Create && string.IsNullOrWhiteSpace(EditId))
        {
            Number = _store.SuggestNextNumber();
        }

        LoadEditRow();
    }

    public IActionResult OnPostSave()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        if (!string.IsNullOrWhiteSpace(SourceMicrotraumaId))
        {
            var src = _microtraumas.LoadAll().FirstOrDefault(m => m.Id == SourceMicrotraumaId);
            if (src is not null)
            {
                SourceMicrotraumaNumber = src.Number;
                EventDateTime ??= src.EventDateTime;
                if (string.IsNullOrWhiteSpace(Victims))
                {
                    Victims = src.Victims;
                }
            }
        }

        var id = string.IsNullOrWhiteSpace(EditId) ? Guid.NewGuid().ToString("N") : EditId;
        var existing = _store.LoadAll().FirstOrDefault(r => r.Id == id);
        _store.Upsert(new MicrotraumaInvestigationRecord
        {
            Id = id,
            Posted = Posted,
            DocumentDate = DocumentDate.Date,
            Number = Number.Trim(),
            SourceMicrotraumaId = SourceMicrotraumaId,
            SourceMicrotraumaNumber = SourceMicrotraumaNumber.Trim(),
            EventDateTime = EventDateTime,
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
        SourceMicrotraumaId = row.SourceMicrotraumaId;
        SourceMicrotraumaNumber = row.SourceMicrotraumaNumber;
        EventDateTime = row.EventDateTime;
        Victims = row.Victims;
        Department = row.Department;
        Circumstances = row.Circumstances;
        Conclusion = row.Conclusion;
    }
}
