using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Suot;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Suot;

[RequireLogin]
public class IndicatorsModel : PageModel
{
    private readonly SuotIndicatorStore _store;

    public IndicatorsModel(SuotIndicatorStore store)
    {
        _store = store;
    }

    public IReadOnlyList<SuotIndicatorTreeRow> TreeRows { get; private set; } = [];
    public IReadOnlyList<SuotIndicator> Groups { get; private set; } = [];
    public SuotIndicator? Selected { get; private set; }
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public bool ShowBulkPanel { get; private set; }
    public bool IsViewMode { get; private set; }
    public bool IsEditMode { get; private set; }
    public bool IsCreateMode { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? ViewId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? EditId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? FilterDirection { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? FilterType { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Bulk { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool NewGroup { get; set; }

    [BindProperty]
    public string[] SelectedIds { get; set; } = [];

    [BindProperty]
    public string Code { get; set; } = string.Empty;

    [BindProperty]
    public string Name { get; set; } = string.Empty;

    [BindProperty]
    public string ShortName { get; set; } = string.Empty;

    [BindProperty]
    public string? ParentId { get; set; }

    [BindProperty]
    public bool IsGroup { get; set; }

    [BindProperty]
    public string IndicatorType { get; set; } = "Количественный";

    [BindProperty]
    public string Unit { get; set; } = "шт";

    [BindProperty]
    public string ActivityDirection { get; set; } = string.Empty;

    [BindProperty]
    public string Description { get; set; } = string.Empty;

    [BindProperty]
    public string FormulaDescription { get; set; } = string.Empty;

    [BindProperty]
    public string Formula { get; set; } = string.Empty;

    [BindProperty]
    public bool BulkApplyType { get; set; }

    [BindProperty]
    public string? BulkIndicatorType { get; set; }

    [BindProperty]
    public bool BulkApplyDirection { get; set; }

    [BindProperty]
    public string? BulkActivityDirection { get; set; }

    [BindProperty]
    public bool BulkApplyUnit { get; set; }

    [BindProperty]
    public string? BulkUnit { get; set; }

    [BindProperty]
    public bool BulkApplyGroup { get; set; }

    [BindProperty]
    public string? BulkParentId { get; set; }

    public void OnGet()
    {
        ViewData["ActiveModule"] = "система-управления-охраной-труда";
        ViewData["IsModulePage"] = true;
        ShowBulkPanel = Bulk;
        LoadRows();

        if (NewGroup)
        {
            IsCreateMode = true;
            IsGroup = true;
            return;
        }

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
            return;
        }

        IsCreateMode = true;
    }

    public IActionResult OnPostSave()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            IsError = true;
            Message = "Укажите наименование показателя.";
            OnGet();
            return Page();
        }

        var id = string.IsNullOrWhiteSpace(EditId) ? Guid.NewGuid().ToString("N") : EditId;
        _store.Upsert(new SuotIndicator
        {
            Id = id,
            Code = Code,
            Name = Name.Trim(),
            ShortName = string.IsNullOrWhiteSpace(ShortName) ? Name.Trim() : ShortName.Trim(),
            ParentId = string.IsNullOrWhiteSpace(ParentId) ? null : ParentId,
            IsGroup = IsGroup,
            IndicatorType = IndicatorType,
            Unit = Unit,
            ActivityDirection = ActivityDirection,
            Description = Description.Trim(),
            FormulaDescription = FormulaDescription.Trim(),
            Formula = Formula.Trim()
        });

        return RedirectToPage(new
        {
            viewId = id,
            filterDirection = FilterDirection,
            filterType = FilterType,
            search = Search,
            message = "saved"
        });
    }

    public IActionResult OnPostDelete(string deleteId)
    {
        if (!string.IsNullOrWhiteSpace(deleteId))
        {
            _store.Delete(deleteId);
        }

        return RedirectToPage(new
        {
            filterDirection = FilterDirection,
            filterType = FilterType,
            search = Search,
            message = "deleted"
        });
    }

    public IActionResult OnPostBulk()
    {
        if (SelectedIds.Length == 0)
        {
            IsError = true;
            Message = "Выберите хотя бы один показатель.";
            Bulk = true;
            OnGet();
            return Page();
        }

        _store.BulkUpdate(SelectedIds, item =>
        {
            if (BulkApplyType && !string.IsNullOrWhiteSpace(BulkIndicatorType))
            {
                item.IndicatorType = BulkIndicatorType;
            }

            if (BulkApplyDirection && !string.IsNullOrWhiteSpace(BulkActivityDirection))
            {
                item.ActivityDirection = BulkActivityDirection;
            }

            if (BulkApplyUnit && !string.IsNullOrWhiteSpace(BulkUnit))
            {
                item.Unit = BulkUnit;
            }

            if (BulkApplyGroup)
            {
                item.ParentId = string.IsNullOrWhiteSpace(BulkParentId) ? null : BulkParentId;
            }
        });

        return RedirectToPage(new
        {
            filterDirection = FilterDirection,
            filterType = FilterType,
            search = Search,
            message = "bulk"
        });
    }

    private void LoadRows()
    {
        var all = _store.LoadAll();
        Groups = all.Where(i => i.IsGroup).OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        IEnumerable<SuotIndicator> query = all;
        if (!string.IsNullOrWhiteSpace(FilterDirection))
        {
            query = query.Where(i => string.Equals(i.ActivityDirection, FilterDirection, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(FilterType))
        {
            query = query.Where(i => string.Equals(i.IndicatorType, FilterType, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            query = query.Where(i =>
                i.Name.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                i.Code.Contains(Search, StringComparison.OrdinalIgnoreCase));
        }

        TreeRows = SuotIndicatorTreeBuilder.Build(query);
        ResolveMessage();
    }

    private void LoadSelected(string id)
    {
        var row = _store.GetById(id);
        if (row is null)
        {
            return;
        }

        Selected = row;
        Code = row.Code;
        Name = row.Name;
        ShortName = row.ShortName;
        ParentId = row.ParentId;
        IsGroup = row.IsGroup;
        IndicatorType = row.IndicatorType;
        Unit = row.Unit;
        ActivityDirection = row.ActivityDirection;
        Description = row.Description;
        FormulaDescription = row.FormulaDescription;
        Formula = row.Formula;
    }

    private void ResolveMessage()
    {
        var flag = Request.Query["message"].ToString();
        Message = flag switch
        {
            "saved" => "Показатель сохранён.",
            "deleted" => "Показатель удалён.",
            "bulk" => "Выделенные показатели обновлены.",
            _ => Message
        };
    }

    public string? ResolveGroupName(string? parentId)
    {
        if (string.IsNullOrWhiteSpace(parentId))
        {
            return null;
        }

        return Groups.FirstOrDefault(g => g.Id == parentId)?.Name
            ?? _store.GetById(parentId)?.Name;
    }

    public string ActiveId => ViewId ?? EditId ?? string.Empty;
}
