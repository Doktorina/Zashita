using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Sout.Planned;

[RequireLogin]
public class EditModel : PageModel
{
    private readonly SoutAnnualPlanStore _store;
    private readonly WorkplaceStore _workplaces;
    private readonly WorkplaceAssignmentStore _assignments;
    private readonly SoutPreparationStore _preparation;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;

    public EditModel(
        SoutAnnualPlanStore store,
        WorkplaceStore workplaces,
        WorkplaceAssignmentStore assignments,
        SoutPreparationStore preparation,
        OrganizationStore orgs,
        OrganizationEmployeesService employees)
    {
        _store = store;
        _workplaces = workplaces;
        _assignments = assignments;
        _preparation = preparation;
        _orgs = orgs;
        _employees = employees;
    }

    [BindProperty(SupportsGet = true, Name = "id")]
    public string? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PlanYear { get; set; } = DateTime.Today.Year;

    public SoutAnnualPlanDocument Document { get; set; } = new();
    public bool IsNew => string.IsNullOrWhiteSpace(Id);
    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<string> Departments { get; private set; } = [];

    public IActionResult OnGet()
    {
        LoadPage();
        SoutOrganizationBootstrap.Ensure(Organization, _workplaces, _assignments, _preparation, _store);
        if (!IsNew && (string.IsNullOrWhiteSpace(Document.Id) || !string.Equals(Document.Id, Id, StringComparison.Ordinal)))
        {
            return RedirectToPage("/Sout/Planned");
        }

        if (IsNew)
        {
            Document = SoutAnnualPlanBuilder.Build(
                Organization,
                PlanYear,
                string.Empty,
                _workplaces.LoadTreeByOrganization(Organization),
                _assignments.LoadDocumentsByOrganization(Organization),
                _employees.Load(Organization));
            Document.Number = _store.SuggestNextNumber();
        }

        return Page();
    }

    public IActionResult OnPostSave() => SaveDocument(post: false, close: false);

    public IActionResult OnPostPost() => SaveDocument(post: true, close: false);

    public IActionResult OnPostPostAndClose() => SaveDocument(post: true, close: true);

    public IActionResult OnPostRegenerate()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        var year = int.TryParse(Request.Form["planYear"], out var parsedYear) ? parsedYear : DateTime.Today.Year;
        var department = Request.Form["department"].ToString()?.Trim();
        var existing = string.IsNullOrWhiteSpace(Id) ? null : _store.GetById(Id);
        var regenerated = SoutAnnualPlanBuilder.Build(
            Organization,
            year,
            department,
            _workplaces.LoadTreeByOrganization(Organization),
            _assignments.LoadDocumentsByOrganization(Organization),
            _employees.Load(Organization));
        if (existing is not null)
        {
            regenerated.Id = existing.Id;
            regenerated.Number = existing.Number;
            regenerated.Date = existing.Date;
            regenerated.Status = existing.Status;
            regenerated.PlanType = existing.PlanType;
            regenerated.Scenario = string.IsNullOrWhiteSpace(existing.Scenario)
                ? regenerated.Scenario
                : existing.Scenario;
            regenerated.ApprovedByAuthor = existing.ApprovedByAuthor;
            regenerated.GeneralRows = existing.GeneralRows.Count > 0 ? existing.GeneralRows : regenerated.GeneralRows;
        }
        else
        {
            regenerated.Number = _store.SuggestNextNumber();
        }

        _store.Upsert(regenerated);
        return RedirectToPage("/Sout/Planned/Edit", new { id = regenerated.Id });
    }

    public IActionResult OnPostDelete()
    {
        if (!string.IsNullOrWhiteSpace(Id))
        {
            _store.Delete(Id);
        }

        return RedirectToPage("/Sout/Planned");
    }

    private IActionResult SaveDocument(bool post, bool close)
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        var existing = string.IsNullOrWhiteSpace(Id) ? null : _store.GetById(Id);
        var document = BuildDocumentFromForm(existing);
        if (string.IsNullOrWhiteSpace(document.Number))
        {
            document.Number = _store.SuggestNextNumber();
        }

        document.IsPosted = post || (existing?.IsPosted ?? false);
        document.Status = document.IsPosted ? "Утверждён" : document.Status;
        _store.Upsert(document);

        if (close)
        {
            return RedirectToPage("/Sout/Planned");
        }

        return RedirectToPage("/Sout/Planned/Edit", new { id = document.Id });
    }

    private SoutAnnualPlanDocument BuildDocumentFromForm(SoutAnnualPlanDocument? existing)
    {
        _ = DateTime.TryParse(Request.Form["date"], out var date);
        _ = DateTime.TryParse(Request.Form["periodStart"], out var periodStart);
        _ = DateTime.TryParse(Request.Form["periodEnd"], out var periodEnd);
        _ = int.TryParse(Request.Form["planYear"], out var year);
        var rowIds = Request.Form["generalRowId"];
        var assessmentTypes = Request.Form["assessmentType"];
        var contractors = Request.Form["generalContractor"];
        var detailIds = Request.Form["detailRowId"];
        var employees = Request.Form["employee"];
        var personNames = Request.Form["personName"];
        var professions = Request.Form["profession"];
        var previousDates = Request.Form["previousDate"];
        var nextDates = Request.Form["nextDate"];
        var plannedDates = Request.Form["plannedDate"];
        var detailContractors = Request.Form["detailContractor"];
        var workplaceIds = Request.Form["workplaceId"];
        var workplaceNumbers = Request.Form["workplaceNumber"];
        var departments = Request.Form["detailDepartment"];

        var generalRows = new List<SoutAnnualPlanGeneralRow>();
        for (var i = 0; i < assessmentTypes.Count; i++)
        {
            var type = assessmentTypes[i].ToString()?.Trim() ?? string.Empty;
            var contractor = i < contractors.Count ? contractors[i].ToString()?.Trim() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(type) && string.IsNullOrWhiteSpace(contractor))
            {
                continue;
            }

            generalRows.Add(new SoutAnnualPlanGeneralRow
            {
                Id = i < rowIds.Count && !string.IsNullOrWhiteSpace(rowIds[i]) ? rowIds[i].ToString()! : Guid.NewGuid().ToString("N"),
                AssessmentType = type,
                Contractor = contractor
            });
        }

        var detailRows = new List<SoutAnnualPlanDetailRow>();
        for (var i = 0; i < professions.Count; i++)
        {
            var profession = professions[i].ToString()?.Trim() ?? string.Empty;
            var employee = i < employees.Count ? employees[i].ToString()?.Trim() ?? string.Empty : string.Empty;
            var personName = i < personNames.Count ? personNames[i].ToString()?.Trim() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(profession) &&
                string.IsNullOrWhiteSpace(employee) &&
                string.IsNullOrWhiteSpace(personName))
            {
                continue;
            }

            detailRows.Add(new SoutAnnualPlanDetailRow
            {
                Id = i < detailIds.Count && !string.IsNullOrWhiteSpace(detailIds[i]) ? detailIds[i].ToString()! : Guid.NewGuid().ToString("N"),
                Employee = employee,
                PersonName = personName,
                Profession = profession,
                PreviousDate = ParseDate(previousDates, i),
                NextDate = ParseDate(nextDates, i),
                PlannedDate = ParseDate(plannedDates, i),
                Contractor = i < detailContractors.Count ? detailContractors[i].ToString()?.Trim() ?? string.Empty : string.Empty,
                WorkplaceId = i < workplaceIds.Count ? workplaceIds[i].ToString()?.Trim() ?? string.Empty : string.Empty,
                WorkplaceNumber = i < workplaceNumbers.Count ? workplaceNumbers[i].ToString()?.Trim() ?? string.Empty : string.Empty,
                Department = i < departments.Count ? departments[i].ToString()?.Trim() ?? string.Empty : string.Empty
            });
        }

        var number = Request.Form["number"].ToString()?.Trim() ?? string.Empty;
        if (string.Equals(number, "<Авто>", StringComparison.OrdinalIgnoreCase))
        {
            number = string.Empty;
        }

        return new SoutAnnualPlanDocument
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id!,
            Organization = Organization,
            Number = number,
            Date = date == default ? DateTime.Now : date,
            PeriodStart = periodStart == default ? new DateTime(year <= 0 ? DateTime.Today.Year : year, 1, 1) : periodStart,
            PeriodEnd = periodEnd == default ? new DateTime(year <= 0 ? DateTime.Today.Year : year, 12, 31) : periodEnd,
            PlanYear = year <= 0 ? DateTime.Today.Year : year,
            PlanType = Request.Form["planType"].ToString()?.Trim() ?? "Офис",
            Scenario = Request.Form["scenario"].ToString()?.Trim() ?? string.Empty,
            Department = Request.Form["department"].ToString()?.Trim() ?? string.Empty,
            Status = Request.Form["status"].ToString()?.Trim() ?? "В подготовке",
            ApprovedByAuthor = Request.Form["approvedByAuthor"] == "true",
            IsPosted = existing?.IsPosted ?? false,
            GeneralRows = generalRows.Count > 0 ? generalRows : [new SoutAnnualPlanGeneralRow()],
            DetailRows = detailRows,
            CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow
        };
    }

    private static DateTime? ParseDate(Microsoft.Extensions.Primitives.StringValues values, int index)
    {
        if (index >= values.Count)
        {
            return null;
        }

        return DateTime.TryParse(values[index].ToString(), out var parsed) ? parsed : null;
    }

    private void LoadPage()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = IsNew ? "Годовой план. Новый" : "Годовой план";
        ViewData["Subtitle"] = Organization;
        Departments = _workplaces.LoadTreeByOrganization(Organization)
            .Where(item => item.IsGroup)
            .Select(item => item.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (!IsNew)
        {
            Document = _store.GetById(Id!) ?? new SoutAnnualPlanDocument();
            PlanYear = Document.PlanYear;
        }
    }
}
