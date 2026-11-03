using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Incidents;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Incidents;

[RequireLogin]
public class WorkplaceModel : PageModel
{
    private readonly WorkplaceAccidentStore _store;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesStore _employees;

    public WorkplaceModel(WorkplaceAccidentStore store, OrganizationStore orgs, OrganizationEmployeesStore employees)
    {
        _store = store;
        _orgs = orgs;
        _employees = employees;
    }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<WorkplaceAccidentRecord> Rows { get; private set; } = [];
    public IReadOnlyList<string> Departments { get; private set; } = [];
    public string? Message { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? EditId { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Create { get; set; }

    [BindProperty] public bool Posted { get; set; }
    [BindProperty] public DateTime DocumentDate { get; set; } = DateTime.Today;
    [BindProperty] public string Number { get; set; } = string.Empty;
    [BindProperty] public string Department { get; set; } = string.Empty;
    [BindProperty] public string Place { get; set; } = string.Empty;
    [BindProperty] public string IncidentType { get; set; } = "Несчастный случай";
    [BindProperty] public string Victims { get; set; } = string.Empty;
    [BindProperty] public DateTime? IncidentDateTime { get; set; }
    [BindProperty] public string Circumstances { get; set; } = string.Empty;
    [BindProperty] public string MeasuresTaken { get; set; } = string.Empty;
    [BindProperty] public string Comment { get; set; } = string.Empty;

    public void OnGet()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "несчастные-случаи";
        ViewData["IsModulePage"] = true;
        Rows = _store.LoadByOrganization(Organization);
        Departments = DepartmentSelect.Merge(
            DepartmentSelect.FromEmployees(_employees, Organization),
            Rows.Select(r => r.Department));

        if (Create && string.IsNullOrWhiteSpace(EditId))
        {
            Number = _store.SuggestNextNumber();
        }

        LoadEditRow();
    }

    public IActionResult OnPostSave()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        if (string.IsNullOrWhiteSpace(Number) || string.IsNullOrWhiteSpace(Victims))
        {
            Message = "Укажите номер и пострадавших.";
            OnGet();
            return Page();
        }

        var id = string.IsNullOrWhiteSpace(EditId) ? Guid.NewGuid().ToString("N") : EditId;
        var existing = _store.LoadAll().FirstOrDefault(r => r.Id == id);
        _store.Upsert(new WorkplaceAccidentRecord
        {
            Id = id,
            Posted = Posted,
            DocumentDate = DocumentDate.Date,
            Number = Number.Trim(),
            Organization = Organization,
            Department = Department.Trim(),
            Place = Place.Trim(),
            IncidentType = IncidentType.Trim(),
            Victims = Victims.Trim(),
            IncidentDateTime = IncidentDateTime,
            Circumstances = Circumstances.Trim(),
            MeasuresTaken = MeasuresTaken.Trim(),
            Comment = Comment.Trim(),
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
        Department = row.Department;
        Place = row.Place;
        IncidentType = row.IncidentType;
        Victims = row.Victims;
        IncidentDateTime = row.IncidentDateTime;
        Circumstances = row.Circumstances;
        MeasuresTaken = row.MeasuresTaken;
        Comment = row.Comment;
    }
}
