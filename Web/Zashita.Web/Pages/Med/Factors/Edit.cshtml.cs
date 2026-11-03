using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Med;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med.Factors;

[RequireLogin]
public class EditModel : PageModel
{
    private readonly MedFactorStore _store;
    private readonly MedContextService _med;

    public EditModel(MedFactorStore store, MedContextService med)
    {
        _store = store;
        _med = med;
    }

    [BindProperty(SupportsGet = true)] public string? Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? ParentId { get; set; }
    [BindProperty(SupportsGet = true)] public bool IsGroup { get; set; }

    public MedFactorNode Record { get; set; } = new();
    public bool IsNew => string.IsNullOrWhiteSpace(Id);
    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<MedFactorNode> GroupOptions { get; private set; } = [];
    public IReadOnlyList<string> PeriodicityOptions { get; } = MedPeriodicity.CatalogOptions;
    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    public IActionResult OnGet()
    {
        if (!LoadPage())
        {
            return RedirectToPage("/Med/Factors");
        }

        if (IsNew)
        {
            Record = new MedFactorNode
            {
                Id = Guid.NewGuid().ToString("N"),
                ParentId = string.IsNullOrWhiteSpace(ParentId) ? null : ParentId.Trim(),
                IsGroup = IsGroup,
                Code = _store.SuggestNextCode(),
                IsActive = true
            };
        }
        else
        {
            var existing = _store.GetById(Id!);
            if (existing is null)
            {
                return RedirectToPage("/Med/Factors");
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
            return RedirectToPage("/Med/Factors");
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
            return RedirectToPage("/Med/Factors");
        }

        Id = Record.Id;
        Message = "Записано.";
        return Page();
    }

    private bool LoadPage()
    {
        Organization = _med.GetOrganization(HttpContext);
        GroupOptions = _store.LoadAll()
            .Where(n => n.IsGroup)
            .OrderBy(n => CanonicalGroupOrder(n.ItemNumber))
            .ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        ViewData["ActiveModule"] = "медосмотры";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = IsNew
            ? (IsGroup ? "Группа факторов (создание)" : "Фактор / вид работ (создание)")
            : (Record.IsGroup ? Record.Name : "Фактор / вид работ");
        ViewData["Subtitle"] = Organization;
        return true;
    }

    private MedFactorNode BindFromForm()
    {
        var id = Request.Form["recordId"].ToString();
        if (string.IsNullOrWhiteSpace(id))
        {
            id = Guid.NewGuid().ToString("N");
        }

        var existing = _store.GetById(id);
        var record = existing ?? new MedFactorNode { Id = id };
        record.IsGroup = string.Equals(Request.Form["isGroup"], "true", StringComparison.OrdinalIgnoreCase);
        record.ParentId = string.IsNullOrWhiteSpace(Request.Form["parentId"]) ? null : Request.Form["parentId"].ToString();
        record.ItemNumber = Request.Form["itemNumber"].ToString()?.Trim() ?? "";
        record.Name = Request.Form["name"].ToString()?.Trim() ?? "";
        record.Code = Request.Form["code"].ToString()?.Trim() ?? record.Code;
        record.Periodicity = Request.Form["periodicity"].ToString()?.Trim() ?? "Никогда";
        record.Description = Request.Form["description"].ToString()?.Trim() ?? "";
        record.Kind = Request.Form["kind"].ToString()?.Trim() ?? "";
        record.OrderType = Request.Form["orderType"].ToString()?.Trim() ?? "";
        record.OrderNumber = Request.Form["orderNumber"].ToString()?.Trim() ?? "";
        record.PointNumber = Request.Form["pointNumber"].ToString()?.Trim() ?? "";
        record.ExternalLink = Request.Form["externalLink"].ToString()?.Trim() ?? "";
        record.IsActive = Request.Form["isActive"] == "true";
        record.ConsiderConditionsClass = Request.Form["considerConditionsClass"] == "true";
        record.EffectiveFrom = DateTime.TryParse(Request.Form["effectiveFrom"], out var date) ? date : null;
        record.Doctors = Request.Form["doctor"].Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.ToString()!.Trim()).ToList();
        record.Examinations = Request.Form["examination"].Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.ToString()!.Trim()).ToList();
        record.Contraindications = Request.Form["contraindication"].Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.ToString()!.Trim()).ToList();
        return record;
    }

    private static int CanonicalGroupOrder(string? itemNumber)
    {
        return itemNumber?.Trim().ToUpperInvariant() switch
        {
            "I" => 1,
            "II" => 2,
            "III" => 3,
            "IV" => 4,
            "V" => 5,
            "VI" => 6,
            _ => 100
        };
    }
}
