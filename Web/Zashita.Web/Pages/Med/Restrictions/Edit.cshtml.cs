using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Med;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med.Restrictions;

[RequireLogin]
public class EditModel : PageModel
{
    private readonly MedRestrictionStore _store;
    private readonly MedContextService _med;

    public EditModel(MedRestrictionStore store, MedContextService med)
    {
        _store = store;
        _med = med;
    }

    [BindProperty(SupportsGet = true, Name = "id")]
    public string? Id { get; set; }

    public MedRestrictionDocument Document { get; set; } = new();
    public bool IsNew => string.IsNullOrWhiteSpace(Id);
    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<string> EmployeeNames { get; private set; } = [];

    public IActionResult OnGet()
    {
        LoadPage();
        if (!IsNew && (string.IsNullOrWhiteSpace(Document.Id) || !string.Equals(Document.Id, Id, StringComparison.Ordinal)))
        {
            return RedirectToPage("/Med/Restrictions");
        }

        if (IsNew)
        {
            Document.Number = _store.SuggestNextNumber();
            Document.Date = DateTime.Now;
            Document.Lines = [new MedRestrictionLine()];
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

        return RedirectToPage("/Med/Restrictions");
    }

    private IActionResult SaveDocument(bool post, bool close)
    {
        Organization = _med.GetOrganization(HttpContext);
        var existing = string.IsNullOrWhiteSpace(Id) ? null : _store.GetById(Id);
        var document = BuildDocumentFromForm();
        if (string.IsNullOrWhiteSpace(document.Number) || document.Number == "<Авто>")
        {
            document.Number = _store.SuggestNextNumber();
        }

        if (string.IsNullOrWhiteSpace(document.EmployeeName))
        {
            ViewData["ActiveModule"] = "медосмотры";
            ViewData["IsModulePage"] = true;
            Document = document;
            EmployeeNames = _med.GetEmployeeNames(Organization);
            return Page();
        }

        document.IsPosted = post || (existing?.IsPosted ?? false);
        _store.Upsert(document);

        if (close)
        {
            return RedirectToPage("/Med/Restrictions", new { printId = document.Id });
        }

        return RedirectToPage(new { id = document.Id });
    }

    private MedRestrictionDocument BuildDocumentFromForm()
    {
        Organization = _med.GetOrganization(HttpContext);
        var existing = string.IsNullOrWhiteSpace(Id) ? null : _store.GetById(Id);
        var document = existing ?? new MedRestrictionDocument
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id
        };

        document.Organization = Organization;
        document.Number = Request.Form["number"].ToString()?.Trim() ?? document.Number;
        if (DateTime.TryParse(Request.Form["date"], out var date))
        {
            document.Date = date;
        }

        document.EmployeeName = Request.Form["employeeName"].ToString()?.Trim() ?? string.Empty;
        document.Profession = Request.Form["profession"].ToString()?.Trim() ?? string.Empty;
        document.City = Request.Form["city"].ToString()?.Trim() ?? string.Empty;
        document.MedicalInstitution = Request.Form["medicalInstitution"].ToString()?.Trim() ?? string.Empty;
        document.Basis = Request.Form["basis"].ToString()?.Trim() ?? string.Empty;
        document.Diseases = Request.Form["diseases"].ToString()?.Trim() ?? string.Empty;
        document.AdditionalInfo = Request.Form["additionalInfo"].ToString()?.Trim() ?? string.Empty;
        if (DateTime.TryParse(Request.Form["suspensionFrom"], out var suspensionFrom))
        {
            document.SuspensionFrom = suspensionFrom.Date;
        }
        else
        {
            document.SuspensionFrom = document.Date.Date;
        }

        if (string.IsNullOrWhiteSpace(document.Profession))
        {
            var employee = _med.GetEmployees(Organization)
                .FirstOrDefault(e => string.Equals(e.Name?.Trim(), document.EmployeeName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(employee?.Profession))
            {
                document.Profession = employee.Profession.Trim();
            }
        }

        var lineIds = Request.Form["lineId"];
        var factors = Request.Form["factorOrWorkType"];
        var itemNumbers = Request.Form["itemNumber"];
        var presents = Request.Form["isPresent"];
        var reviewDates = Request.Form["reviewDate"];
        var comments = Request.Form["lineComment"];
        var lines = new List<MedRestrictionLine>();

        for (var i = 0; i < factors.Count; i++)
        {
            var factor = factors[i].ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(factor))
            {
                continue;
            }

            DateTime? reviewDate = null;
            if (i < reviewDates.Count && DateTime.TryParse(reviewDates[i], out var parsed))
            {
                reviewDate = parsed.Date;
            }

            lines.Add(new MedRestrictionLine
            {
                Id = i < lineIds.Count && !string.IsNullOrWhiteSpace(lineIds[i])
                    ? lineIds[i].ToString()!
                    : Guid.NewGuid().ToString("N"),
                FactorOrWorkType = factor,
                ItemNumber = i < itemNumbers.Count ? itemNumbers[i].ToString()?.Trim() ?? string.Empty : string.Empty,
                IsPresent = i < presents.Count && presents[i].ToString() == "true",
                ReviewDate = reviewDate,
                Comment = i < comments.Count ? comments[i].ToString()?.Trim() ?? string.Empty : string.Empty
            });
        }

        document.Lines = lines;
        document.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return document;
    }

    private void LoadPage()
    {
        Organization = _med.GetOrganization(HttpContext);
        _med.EnsureInitialized(Organization);
        ViewData["ActiveModule"] = "медосмотры";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = IsNew ? "Ограничения физ. лиц. Новый" : "Ограничения физ. лиц";
        ViewData["Subtitle"] = Organization;

        Document = !IsNew ? _store.GetById(Id!) ?? new MedRestrictionDocument() : new MedRestrictionDocument();
        if (Document.Lines.Count == 0)
        {
            Document.Lines = [new MedRestrictionLine()];
        }

        if (string.IsNullOrWhiteSpace(Document.Profession) && !string.IsNullOrWhiteSpace(Document.EmployeeName))
        {
            var employee = _med.GetEmployees(Organization)
                .FirstOrDefault(e => string.Equals(e.Name?.Trim(), Document.EmployeeName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(employee?.Profession))
            {
                Document.Profession = employee.Profession.Trim();
            }
        }

        if (Document.SuspensionFrom is null)
        {
            Document.SuspensionFrom = Document.Date.Date;
        }

        EmployeeNames = _med.GetEmployeeNames(Organization);
    }
}
