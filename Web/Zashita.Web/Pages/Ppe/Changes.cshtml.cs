using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Ppe;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Ppe;

[RequireLogin]
public class ChangesModel : PageModel
{
    private readonly PpeChangesStore _store;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesStore _employees;

    public ChangesModel(PpeChangesStore store, OrganizationStore orgs, OrganizationEmployeesStore employees)
    {
        _store = store;
        _orgs = orgs;
        _employees = employees;
    }

    public string Organization { get; private set; } = string.Empty;
    public string? Message { get; private set; }
    public IReadOnlyList<string> Departments { get; private set; } = [];
    public IReadOnlyList<PpeActRecord> Rows { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public bool ShowAllOrgs { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? FilterDepartment { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? EditId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PrintId { get; set; }

    public string? ActivePrintId =>
        !string.IsNullOrWhiteSpace(PrintId) ? PrintId :
        !string.IsNullOrWhiteSpace(EditId) ? EditId :
        null;

    public PpeActRecord? SelectedPrintRecord { get; private set; }

    [BindProperty(SupportsGet = true)]
    public bool Create { get; set; }

    [BindProperty] public string Number { get; set; } = string.Empty;
    [BindProperty] public DateTime Date { get; set; } = DateTime.Today;
    [BindProperty] public string Department { get; set; } = string.Empty;
    [BindProperty] public string Employee { get; set; } = string.Empty;
    [BindProperty] public string Position { get; set; } = "Мастер участка";
    [BindProperty] public string ApproverPosition { get; set; } = string.Empty;
    [BindProperty] public string ApproverName { get; set; } = string.Empty;
    [BindProperty] public string CommissionHead { get; set; } = string.Empty;
    [BindProperty] public string CommissionHeadPosition { get; set; } = string.Empty;
    [BindProperty] public List<string> CommissionMemberPosition { get; set; } = [];
    [BindProperty] public List<string> CommissionMemberName { get; set; } = [];
    [BindProperty] public string OrderNumber { get; set; } = "___";
    [BindProperty] public string SuitabilityDecision { get; set; } = "не пригодны к дальнейшей эксплуатации (утилизация)";
    [BindProperty] public List<string> LineName { get; set; } = [];
    [BindProperty] public List<string> LineQuantity { get; set; } = [];
    [BindProperty] public List<string> LineIssueDate { get; set; } = [];
    [BindProperty] public List<string> LineWearPercent { get; set; } = [];
    [BindProperty] public List<string> LineConclusion { get; set; } = [];

    public void OnGet()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;

        var rows = _store.LoadAll();
        Departments = DepartmentSelect.Merge(
            DepartmentSelect.FromEmployees(_employees, Organization),
            rows.Select(x => x.Department));

        if (!ShowAllOrgs)
        {
            rows = rows.Where(x =>
                    string.IsNullOrWhiteSpace(x.Organization) ||
                    string.Equals(x.Organization.Trim(), Organization, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (string.Equals(FilterDepartment, "__empty__", StringComparison.OrdinalIgnoreCase))
        {
            rows = rows.Where(x => string.IsNullOrWhiteSpace(x.Department)).ToList();
        }
        else if (!string.IsNullOrWhiteSpace(FilterDepartment))
        {
            rows = rows.Where(x => x.Department.Contains(FilterDepartment, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var token = Search.Trim();
            rows = rows.Where(x =>
                    x.Number.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                    x.Department.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                    x.Organization.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                    x.Employee.Contains(token, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        Rows = rows.OrderByDescending(x => x.Date).ToList();
        if (!string.IsNullOrWhiteSpace(ActivePrintId))
        {
            SelectedPrintRecord = _store.GetById(ActivePrintId);
        }

        LoadEditRow();
    }

    public IActionResult OnPostSave()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        if (string.IsNullOrWhiteSpace(Employee))
        {
            Message = "Укажите сотрудника.";
            OnGet();
            Create = true;
            return Page();
        }

        var id = string.IsNullOrWhiteSpace(EditId) ? Guid.NewGuid().ToString("N") : EditId;
        var existing = _store.GetById(id);
        var lines = BuildLines(Date);
        if (lines.Count == 0)
        {
            Message = "Добавьте хотя бы одну строку СИЗ.";
            OnGet();
            Create = true;
            return Page();
        }

        _store.Upsert(new PpeActRecord
        {
            Id = id,
            Organization = Organization,
            Number = string.IsNullOrWhiteSpace(Number) ? _store.SuggestNextNumber(Organization) : Number.Trim(),
            Date = Date == default ? DateTime.Today : Date.Date,
            Department = (Department ?? string.Empty).Trim(),
            Employee = Employee.Trim(),
            Position = (Position ?? string.Empty).Trim(),
            ApproverPosition = (ApproverPosition ?? string.Empty).Trim(),
            ApproverName = (ApproverName ?? string.Empty).Trim(),
            CommissionHead = (CommissionHead ?? string.Empty).Trim(),
            CommissionHeadPosition = (CommissionHeadPosition ?? string.Empty).Trim(),
            CommissionMembers = BuildCommissionMembers(),
            OrderNumber = (OrderNumber ?? string.Empty).Trim(),
            SuitabilityDecision = (SuitabilityDecision ?? string.Empty).Trim(),
            Lines = lines,
            CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow
        });

        return RedirectToPage(new { editId = id, printId = id, showAllOrgs = ShowAllOrgs ? "true" : null });
    }

    public IActionResult OnPostDelete(string deleteId)
    {
        if (!string.IsNullOrWhiteSpace(deleteId))
        {
            _store.Delete(deleteId);
        }

        return RedirectToPage();
    }

    private List<PpeActLine> BuildLines(DateTime fallbackDate)
    {
        var list = new List<PpeActLine>();
        var count = new[] { LineName.Count, LineQuantity.Count, LineIssueDate.Count, LineWearPercent.Count, LineConclusion.Count }.Max();
        for (var i = 0; i < count; i++)
        {
            var name = i < LineName.Count ? (LineName[i] ?? string.Empty).Trim() : string.Empty;
            if (name.Length == 0)
            {
                continue;
            }

            var qty = i < LineQuantity.Count && decimal.TryParse(LineQuantity[i], out var q) ? q : 1m;
            var issueDate = i < LineIssueDate.Count && DateTime.TryParse(LineIssueDate[i], out var d) ? d.Date : fallbackDate.Date;
            var wear = i < LineWearPercent.Count && int.TryParse(LineWearPercent[i], out var w) ? Math.Clamp(w, 0, 100) : 0;
            var conclusion = i < LineConclusion.Count ? (LineConclusion[i] ?? string.Empty).Trim() : string.Empty;
            list.Add(new PpeActLine
            {
                Name = name,
                Quantity = qty <= 0 ? 1 : qty,
                IssueDate = issueDate,
                WearPercent = wear,
                Conclusion = conclusion
            });
        }

        return list;
    }

    private void LoadEditRow()
    {
        if (string.IsNullOrWhiteSpace(EditId))
        {
            EnsureLineRows();
            EnsureCommissionRows();
            if (Create)
            {
                Number = _store.SuggestNextNumber(Organization);
                Date = DateTime.Today;
            }

            return;
        }

        var row = _store.GetById(EditId);
        if (row is null)
        {
            EnsureLineRows();
            EnsureCommissionRows();
            return;
        }

        Number = row.Number;
        Date = row.Date;
        Department = row.Department;
        Employee = row.Employee;
        Position = row.Position;
        ApproverPosition = row.ApproverPosition;
        ApproverName = row.ApproverName;
        CommissionHead = row.CommissionHead;
        CommissionHeadPosition = row.CommissionHeadPosition;
        CommissionMemberPosition = row.CommissionMembers.Select(x => x.Position).ToList();
        CommissionMemberName = row.CommissionMembers.Select(x => x.Name).ToList();
        OrderNumber = row.OrderNumber;
        SuitabilityDecision = row.SuitabilityDecision;
        LineName = row.Lines.Select(x => x.Name).ToList();
        LineQuantity = row.Lines.Select(x => x.Quantity.ToString("0.###")).ToList();
        LineIssueDate = row.Lines.Select(x => x.IssueDate.ToString("yyyy-MM-dd")).ToList();
        LineWearPercent = row.Lines.Select(x => x.WearPercent.ToString()).ToList();
        LineConclusion = row.Lines.Select(x => x.Conclusion).ToList();
        EnsureLineRows();
        EnsureCommissionRows();
    }

    private List<PpeActCommissionMember> BuildCommissionMembers()
    {
        var list = new List<PpeActCommissionMember>();
        var count = Math.Max(CommissionMemberPosition.Count, CommissionMemberName.Count);
        for (var i = 0; i < count; i++)
        {
            var position = i < CommissionMemberPosition.Count ? (CommissionMemberPosition[i] ?? string.Empty).Trim() : string.Empty;
            var name = i < CommissionMemberName.Count ? (CommissionMemberName[i] ?? string.Empty).Trim() : string.Empty;
            if (position.Length == 0 && name.Length == 0)
            {
                continue;
            }

            list.Add(new PpeActCommissionMember
            {
                Position = position,
                Name = name
            });
        }

        return list;
    }

    private void EnsureCommissionRows()
    {
        while (CommissionMemberPosition.Count < 3) CommissionMemberPosition.Add(string.Empty);
        while (CommissionMemberName.Count < 3) CommissionMemberName.Add(string.Empty);
    }

    private void EnsureLineRows()
    {
        while (LineName.Count < 1) LineName.Add(string.Empty);
        while (LineQuantity.Count < 1) LineQuantity.Add("1");
        while (LineIssueDate.Count < 1) LineIssueDate.Add(Date == default ? DateTime.Today.ToString("yyyy-MM-dd") : Date.ToString("yyyy-MM-dd"));
        while (LineWearPercent.Count < 1) LineWearPercent.Add("0");
        while (LineConclusion.Count < 1) LineConclusion.Add(SuitabilityDecision);
    }
}
