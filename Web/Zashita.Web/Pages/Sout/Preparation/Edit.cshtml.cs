using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Sout.Preparation;

[RequireLogin]
public class EditModel : PageModel
{
    private readonly SoutPreparationStore _store;
    private readonly OrganizationStore _orgs;

    public EditModel(SoutPreparationStore store, OrganizationStore orgs)
    {
        _store = store;
        _orgs = orgs;
    }

    [BindProperty(SupportsGet = true, Name = "id")]
    public string? Id { get; set; }

    public SoutPreparationDocument Document { get; set; } = new();
    public bool IsNew => string.IsNullOrWhiteSpace(Id);
    public string Organization { get; private set; } = string.Empty;
    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    public IActionResult OnGet()
    {
        LoadPage();
        if (!IsNew && (string.IsNullOrWhiteSpace(Document.Id) || !string.Equals(Document.Id, Id, StringComparison.Ordinal)))
        {
            return RedirectToPage("/Sout/Preparation");
        }

        if (IsNew)
        {
            Document.Number = _store.SuggestNextNumber();
            Document.Date = DateTime.Now;
            Document.StartDate = DateTime.Today;
            Document.EndDate = DateTime.Today.AddMonths(1);
            Document.ContractorOrganization = "ООО \"ЭКСПЕРТЭГИДА\"";
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

        return RedirectToPage("/Sout/Preparation");
    }

    private IActionResult SaveDocument(bool post, bool close)
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        var existing = string.IsNullOrWhiteSpace(Id) ? null : _store.GetById(Id);
        var document = BuildDocumentFromForm();
        if (string.IsNullOrWhiteSpace(document.Number))
        {
            document.Number = _store.SuggestNextNumber();
        }

        document.IsPosted = post || (existing?.IsPosted ?? false);
        _store.Upsert(document);

        if (close)
        {
            return RedirectToPage("/Sout/Preparation");
        }

        return RedirectToPage("/Sout/Preparation/Edit", new { id = document.Id });
    }

    private SoutPreparationDocument BuildDocumentFromForm()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        var existing = string.IsNullOrWhiteSpace(Id)
            ? null
            : _store.GetById(Id);

        _ = DateTime.TryParse(Request.Form["date"].ToString(), out var date);
        DateTime? startDate = DateTime.TryParse(Request.Form["startDate"].ToString(), out var start) ? start : null;
        DateTime? endDate = DateTime.TryParse(Request.Form["endDate"].ToString(), out var end) ? end : null;

        var number = Request.Form["number"].ToString()?.Trim() ?? string.Empty;
        if (string.Equals(number, "<Авто>", StringComparison.OrdinalIgnoreCase))
        {
            number = string.Empty;
        }

        return new SoutPreparationDocument
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id!,
            Organization = Organization,
            Number = number,
            Date = date == default ? DateTime.Now : date,
            StartDate = startDate,
            EndDate = endDate,
            Department = Request.Form["department"].ToString()?.Trim() ?? string.Empty,
            ContractorOrganization = Request.Form["contractorOrganization"].ToString()?.Trim() ?? string.Empty,
            IsPosted = existing?.IsPosted ?? false,
            PrintOverrides = existing?.PrintOverrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow
        };
    }

    private void LoadPage()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = IsNew
            ? "Подготовка проведения СОУТ. Новый"
            : "Подготовка проведения СОУТ";
        ViewData["Subtitle"] = Organization;

        if (!IsNew)
        {
            Document = _store.GetById(Id!) ?? new SoutPreparationDocument();
        }
    }
}
