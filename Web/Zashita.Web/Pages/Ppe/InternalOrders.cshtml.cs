using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Ppe;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Ppe;

[RequireLogin]
public class InternalOrdersModel : PageModel
{
    private readonly PpeInternalOrdersStore _store;
    private readonly OrganizationStore _orgs;

    public InternalOrdersModel(PpeInternalOrdersStore store, OrganizationStore orgs)
    {
        _store = store;
        _orgs = orgs;
    }

    public string Organization { get; private set; } = string.Empty;
    public string? Message { get; private set; }
    public IReadOnlyList<PpeInternalOrderRecord> Rows { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string? EditId { get; set; }
    [BindProperty] public string Number { get; set; } = string.Empty;
    [BindProperty] public DateTime Date { get; set; } = DateTime.Today;
    [BindProperty] public string Department { get; set; } = string.Empty;
    [BindProperty] public string Status { get; set; } = "Черновик";
    [BindProperty] public string Comment { get; set; } = string.Empty;

    public void OnGet()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;
        Rows = _store.LoadForOrganization(Organization);

        if (!string.IsNullOrWhiteSpace(EditId))
        {
            var row = _store.GetById(EditId);
            if (row is not null)
            {
                Number = row.Number;
                Date = row.Date;
                Department = row.Department;
                Status = row.Status;
                Comment = row.Comment;
            }
        }
    }

    public IActionResult OnPostSave()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        var id = string.IsNullOrWhiteSpace(EditId) ? Guid.NewGuid().ToString("N") : EditId;
        _store.Upsert(new PpeInternalOrderRecord(
            id,
            Organization,
            Number.Trim(),
            Date.Date,
            Department.Trim(),
            string.IsNullOrWhiteSpace(Status) ? "Черновик" : Status.Trim(),
            Comment.Trim(),
            DateTime.UtcNow));
        Message = "Внутренний заказ сохранён.";
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
}
