using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Suot;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Suot;

[RequireLogin]
public class FactsModel : PageModel
{
    private readonly SuotIndicatorFactStore _facts;
    private readonly SuotIndicatorStore _indicators;
    private readonly OrganizationStore _organizations;
    private readonly OrganizationEmployeesStore _employees;

    public FactsModel(SuotIndicatorFactStore facts, SuotIndicatorStore indicators, OrganizationStore organizations, OrganizationEmployeesStore employees)
    {
        _facts = facts;
        _indicators = indicators;
        _organizations = organizations;
        _employees = employees;
    }

    public IReadOnlyList<SuotIndicatorFact> Rows { get; private set; } = [];
    public IReadOnlyList<SuotIndicator> IndicatorOptions { get; private set; } = [];
    public IReadOnlyList<string> Departments { get; private set; } = [];
    public SuotIndicatorFact? Selected { get; private set; }
    public string Organization { get; private set; } = string.Empty;
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public bool IsViewMode { get; private set; }
    public bool IsEditMode { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? ViewId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? EditId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? DateFrom { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? DateTo { get; set; }

    [BindProperty]
    public string IndicatorId { get; set; } = string.Empty;

    [BindProperty]
    public string Department { get; set; } = string.Empty;

    [BindProperty]
    public DateTime PeriodStart { get; set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

    [BindProperty]
    public DateTime PeriodEnd { get; set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month,
        DateTime.DaysInMonth(DateTime.Today.Year, DateTime.Today.Month));

    [BindProperty]
    public decimal Value { get; set; }

    [BindProperty]
    public string Comment { get; set; } = string.Empty;

    public void OnGet()
    {
        ViewData["ActiveModule"] = "система-управления-охраной-труда";
        ViewData["IsModulePage"] = true;
        Organization = _organizations.GetSelectedOrganization(HttpContext);
        IndicatorOptions = _indicators.LoadAll()
            .Where(i => !i.IsGroup)
            .OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        Rows = _facts.LoadFiltered(Organization, DateFrom, DateTo);
        Departments = DepartmentSelect.Merge(
            DepartmentSelect.FromEmployees(_employees, Organization),
            Rows.Select(r => r.Department));
        ResolveMessage();

        if (!string.IsNullOrWhiteSpace(ViewId))
        {
            IsViewMode = true;
            LoadSelected(ViewId);
            return;
        }

        if (!string.IsNullOrWhiteSpace(EditId))
        {
            IsEditMode = true;
            LoadSelected(EditId);
        }
    }

    public IActionResult OnPostSave()
    {
        Organization = _organizations.GetSelectedOrganization(HttpContext);
        var id = string.IsNullOrWhiteSpace(EditId) ? Guid.NewGuid().ToString("N") : EditId;
        _facts.Upsert(new SuotIndicatorFact
        {
            Id = id,
            IndicatorId = IndicatorId,
            Organization = Organization,
            Department = Department.Trim(),
            PeriodStart = PeriodStart.Date,
            PeriodEnd = PeriodEnd.Date,
            Value = Value,
            Comment = Comment.Trim()
        });

        return RedirectToPage(new
        {
            viewId = id,
            dateFrom = DateFrom?.ToString("yyyy-MM-dd"),
            dateTo = DateTo?.ToString("yyyy-MM-dd"),
            message = "saved"
        });
    }

    public IActionResult OnPostDelete(string deleteId)
    {
        if (!string.IsNullOrWhiteSpace(deleteId))
        {
            _facts.Delete(deleteId);
        }

        return RedirectToPage(new
        {
            dateFrom = DateFrom?.ToString("yyyy-MM-dd"),
            dateTo = DateTo?.ToString("yyyy-MM-dd"),
            message = "deleted"
        });
    }

    public string ResolveIndicatorName(string indicatorId) =>
        IndicatorOptions.FirstOrDefault(i => i.Id == indicatorId)?.Name ??
        _indicators.GetById(indicatorId)?.Name ??
        indicatorId;

    public string ActiveId => ViewId ?? EditId ?? string.Empty;

    private void LoadSelected(string id)
    {
        var row = Rows.FirstOrDefault(r => r.Id == id) ??
                  _facts.LoadAll().FirstOrDefault(r => r.Id == id);
        if (row is null)
        {
            return;
        }

        Selected = row;
        IndicatorId = row.IndicatorId;
        Department = row.Department;
        PeriodStart = row.PeriodStart;
        PeriodEnd = row.PeriodEnd;
        Value = row.Value;
        Comment = row.Comment;
    }

    private void ResolveMessage()
    {
        var flag = Request.Query["message"].ToString();
        Message = flag switch
        {
            "saved" => "Запись сохранена.",
            "deleted" => "Запись удалена.",
            _ => Message
        };
    }
}
