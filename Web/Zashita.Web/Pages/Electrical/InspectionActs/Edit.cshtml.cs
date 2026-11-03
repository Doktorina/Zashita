using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Compliance;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Electrical.InspectionActs;

[RequireLogin]
public class EditModel : PageModel
{
    private readonly InspectionActStore _store;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;

    public EditModel(
        InspectionActStore store,
        OrganizationStore orgs,
        OrganizationEmployeesService employees)
    {
        _store = store;
        _orgs = orgs;
        _employees = employees;
    }

    [BindProperty(SupportsGet = true, Name = "id")]
    public string? Id { get; set; }

    public InspectionActRecord Document { get; set; } = new();
    public bool IsNew => string.IsNullOrWhiteSpace(Id);
    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<string> EmployeeNames { get; private set; } = [];

    public IActionResult OnGet()
    {
        LoadPage();
        if (!IsNew && (string.IsNullOrWhiteSpace(Document.Id) || !string.Equals(Document.Id, Id, StringComparison.Ordinal)))
        {
            return RedirectToPage("/Electrical/InspectionActs");
        }

        if (IsNew)
        {
            Document.Number = _store.SuggestNextNumber(Organization);
            Document.Date = DateTime.Now;
            Document.Organization = Organization;
            Document.Violations = [new InspectionActViolationLine()];
        }

        return Page();
    }

    public IActionResult OnPostSave() => SaveDocument(post: false, close: false);

    public IActionResult OnPostPost() => SaveDocument(post: true, close: false);

    public IActionResult OnPostPostAndClose() => SaveDocument(post: true, close: true);

    public IActionResult OnPostDelete()
    {
        if (!string.IsNullOrWhiteSpace(Id))
        {
            _store.Delete(Id);
        }

        return RedirectToPage("/Electrical/InspectionActs");
    }

    private IActionResult SaveDocument(bool post, bool close)
    {
        var existing = string.IsNullOrWhiteSpace(Id) ? null : _store.GetById(Id);
        var document = BuildDocumentFromForm();
        if (string.IsNullOrWhiteSpace(document.Number))
        {
            document.Number = _store.SuggestNextNumber(document.Organization);
        }

        document.IsPosted = post || (existing?.IsPosted ?? false);
        _store.Upsert(document);

        return close
            ? RedirectToPage("/Electrical/InspectionActs", new { printId = document.Id })
            : RedirectToPage("/Electrical/InspectionActs/Edit", new { id = document.Id });
    }

    private InspectionActRecord BuildDocumentFromForm()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        var existing = string.IsNullOrWhiteSpace(Id) ? null : _store.GetById(Id);

        _ = DateTime.TryParse(Request.Form["date"].ToString(), out var date);
        _ = int.TryParse(Request.Form["photoCount"].ToString(), out var photoCount);

        var number = Request.Form["number"].ToString()?.Trim() ?? string.Empty;
        var violations = new List<InspectionActViolationLine>();
        var descriptions = Request.Form["violationDescription"];
        var regulations = Request.Form["violationRegulations"];
        var locations = Request.Form["violationLocation"];
        var deadlines = Request.Form["violationDeadline"];

        for (var i = 0; i < descriptions.Count; i++)
        {
            var desc = descriptions[i]?.Trim() ?? string.Empty;
            var npa = i < regulations.Count ? regulations[i]?.Trim() ?? string.Empty : string.Empty;
            var place = i < locations.Count ? locations[i]?.Trim() ?? string.Empty : string.Empty;
            var deadlineRaw = i < deadlines.Count ? deadlines[i]?.Trim() ?? string.Empty : string.Empty;
            DateTime? deadline = DateTime.TryParse(deadlineRaw, out var parsedDeadline) ? parsedDeadline : null;

            if (string.IsNullOrWhiteSpace(desc) &&
                string.IsNullOrWhiteSpace(npa) &&
                string.IsNullOrWhiteSpace(place) &&
                deadline is null)
            {
                continue;
            }

            violations.Add(new InspectionActViolationLine
            {
                Description = desc,
                Regulations = npa,
                Location = place,
                Deadline = deadline
            });
        }

        if (violations.Count == 0)
        {
            violations.Add(new InspectionActViolationLine());
        }

        return new InspectionActRecord
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id!,
            Organization = Organization,
            Number = number,
            Date = date == default ? DateTime.Now : date,
            ConstructionObject = Request.Form["constructionObject"].ToString()?.Trim() ?? string.Empty,
            Contractor = Request.Form["contractor"].ToString()?.Trim() ?? string.Empty,
            RepresentativeTitle = Request.Form["representativeTitle"].ToString()?.Trim() ?? string.Empty,
            RepresentativeName = Request.Form["representativeName"].ToString()?.Trim() ?? string.Empty,
            InspectorTitle = Request.Form["inspectorTitle"].ToString()?.Trim() ?? "Специалист службы охраны труда",
            InspectorName = Request.Form["inspectorName"].ToString()?.Trim() ?? string.Empty,
            PhotoCount = Math.Max(0, photoCount),
            Violations = violations,
            IsPosted = existing?.IsPosted ?? false,
            PrintOverrides = existing?.PrintOverrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow
        };
    }

    private void LoadPage()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        EmployeeNames = _employees.GetEmployeeNames(Organization);
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = IsNew ? "Акт проверки. Новый" : "Акт проверки";
        ViewData["Subtitle"] = Organization;

        if (!IsNew && !string.IsNullOrWhiteSpace(Id))
        {
            Document = _store.GetById(Id) ?? new InspectionActRecord();
        }
    }
}
