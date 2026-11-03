using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Med;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med.WorkTypes;

[RequireLogin]
public class EditModel : PageModel
{
    private readonly MedWorkTypeStore _store;
    private readonly MedFactorStore _factors;
    private readonly MedContextService _med;

    public EditModel(MedWorkTypeStore store, MedFactorStore factors, MedContextService med)
    {
        _store = store;
        _factors = factors;
        _med = med;
    }

    [BindProperty(SupportsGet = true)] public string? Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? ParentId { get; set; }
    [BindProperty(SupportsGet = true)] public bool IsGroup { get; set; }

    public MedWorkTypeNode Record { get; set; } = new();
    public bool IsNew => string.IsNullOrWhiteSpace(Id);
    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<MedWorkTypeNode> GroupOptions { get; private set; } = [];
    public IReadOnlyList<string> FactorSetOptions { get; private set; } = [];
    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    public IActionResult OnGet()
    {
        if (!LoadPage())
        {
            return RedirectToPage("/Med/WorkTypes");
        }

        if (IsNew)
        {
            Record = new MedWorkTypeNode
            {
                Id = Guid.NewGuid().ToString("N"),
                ParentId = string.IsNullOrWhiteSpace(ParentId) ? null : ParentId.Trim(),
                IsGroup = IsGroup,
                Code = _store.SuggestNextCode()
            };
        }
        else
        {
            var existing = _store.GetById(Id!);
            if (existing is null)
            {
                return RedirectToPage("/Med/WorkTypes");
            }

            Record = existing;
            IsGroup = existing.IsGroup;
        }

        return Page();
    }

    public IActionResult OnPostSave() => Save(close: false);
    public IActionResult OnPostSaveAndClose() => Save(close: true);

    private IActionResult Save(bool close)
    {
        if (!LoadPage())
        {
            return RedirectToPage("/Med/WorkTypes");
        }

        Record = BindFromForm();
        IsGroup = Record.IsGroup;

        if (string.IsNullOrWhiteSpace(Record.Name))
        {
            Message = "Поле «Наименование» не заполнено.";
            IsError = true;
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Record.Code) || Record.Code == "<Авто>")
        {
            Record.Code = _store.SuggestNextCode();
        }

        _store.Upsert(Record);

        if (close)
        {
            return RedirectToPage("/Med/WorkTypes");
        }

        Id = Record.Id;
        Message = "Записано.";
        return Page();
    }

    private bool LoadPage()
    {
        Organization = _med.GetOrganization(HttpContext);
        GroupOptions = _store.LoadAll().Where(n => n.IsGroup).OrderBy(n => n.Name).ToList();
        FactorSetOptions = _factors.LoadAll()
            .Where(n => n.IsGroup)
            .Select(n => n.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();
        ViewData["ActiveModule"] = "медосмотры";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = IsNew
            ? (IsGroup ? "Группа видов работ (создание)" : "Вид работ (создание)")
            : (Record.IsGroup ? Record.Name : "Вид работ");
        ViewData["Subtitle"] = Organization;
        return true;
    }

    private MedWorkTypeNode BindFromForm()
    {
        var id = Request.Form["recordId"].ToString();
        if (string.IsNullOrWhiteSpace(id))
        {
            id = Guid.NewGuid().ToString("N");
        }

        var existing = _store.GetById(id);
        var record = existing ?? new MedWorkTypeNode { Id = id };
        record.IsGroup = string.Equals(Request.Form["isGroup"], "true", StringComparison.OrdinalIgnoreCase);
        record.ParentId = string.IsNullOrWhiteSpace(Request.Form["parentId"]) ? null : Request.Form["parentId"].ToString();
        record.Name = Request.Form["name"].ToString()?.Trim() ?? "";
        record.Code = Request.Form["code"].ToString()?.Trim() ?? record.Code;
        record.ItemNumber = Request.Form["itemNumber"].ToString()?.Trim() ?? "";
        record.Description = Request.Form["description"].ToString()?.Trim() ?? "";
        record.FactorSetName = Request.Form["factorSetName"].ToString()?.Trim() ?? "";
        return record;
    }
}
