using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training.Kinds;

[RequireLogin]
public class EditModel : PageModel
{
    private readonly TrainingKindStore _store;
    private readonly OrganizationStore _orgs;

    public EditModel(TrainingKindStore store, OrganizationStore orgs)
    {
        _store = store;
        _orgs = orgs;
    }

    [BindProperty(SupportsGet = true)]
    public string? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ParentId { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool IsGroup { get; set; }

    public TrainingKindNode Record { get; set; } = new();
    public bool IsNew => string.IsNullOrWhiteSpace(Id);
    public IReadOnlyList<TrainingKindNode> GroupOptions { get; private set; } = [];
    public IReadOnlyList<string> SectionOptions { get; private set; } = TrainingKindCatalogDefaults.SectionOptions;
    public IReadOnlyList<string> PersonnelCategoryOptions => TrainingKindCatalogDefaults.PersonnelCategoryOptions;
    public IReadOnlyList<string> TypeOptions => TrainingKindCatalogDefaults.TypeOptions;
    public string Organization { get; private set; } = string.Empty;
    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    public IActionResult OnGet()
    {
        LoadShell();
        var all = _store.Load();
        GroupOptions = all.Where(node => node.IsGroup && !node.MarkedForDeletion).ToList();
        SectionOptions = TrainingKindCatalogDefaults.MergeSectionOptions(all.Select(n => n.Section));

        if (IsNew)
        {
            Record = new TrainingKindNode
            {
                Id = Guid.NewGuid().ToString("N"),
                ParentId = string.IsNullOrWhiteSpace(ParentId) ? null : ParentId.Trim(),
                IsGroup = IsGroup,
                Type = "Обучение",
                Section = "Охрана труда",
                Periodicity = IsGroup ? string.Empty : "1 раз в год",
                Code = _store.SuggestNextCode(all)
            };
            return Page();
        }

        var existing = _store.Find(Id!);
        if (existing is null)
        {
            return RedirectToPage("/Training/Kinds");
        }

        Record = existing;
        IsGroup = existing.IsGroup;
        return Page();
    }

    public IActionResult OnPostSave() => Save(close: false);

    public IActionResult OnPostSaveAndClose() => Save(close: true);

    private IActionResult Save(bool close)
    {
        LoadShell();
        var all = _store.Load();
        GroupOptions = all.Where(node => node.IsGroup && !node.MarkedForDeletion).ToList();
        var formId = Request.Form["Id"].ToString()?.Trim()
                     ?? Request.Form["recordId"].ToString()?.Trim()
                     ?? Id?.Trim();
        var previous = !string.IsNullOrWhiteSpace(formId) ? _store.Find(formId) : null;
        var previousSection = previous?.Section?.Trim();
        Record = BindFromForm(all);

        if (string.IsNullOrWhiteSpace(Record.Name))
        {
            Message = "Укажите наименование.";
            IsError = true;
            SectionOptions = TrainingKindCatalogDefaults.MergeSectionOptions(
                all.Select(n => n.Section).Append(Record.Section));
            return Page();
        }

        _store.Upsert(Record);

        if (Record.IsGroup && !string.IsNullOrWhiteSpace(Record.Section))
        {
            var list = _store.Load();
            if (TrainingKindCatalogDefaults.PropagateSectionToDescendants(
                    list, Record.Id, Record.Section.Trim(), previousSection))
            {
                _store.Save(list);
            }
        }

        all = _store.Load();
        SectionOptions = TrainingKindCatalogDefaults.MergeSectionOptions(all.Select(n => n.Section));
        GroupOptions = all.Where(node => node.IsGroup && !node.MarkedForDeletion).ToList();

        if (close)
        {
            TempData["Message"] = "Вид обучения записан.";
            return RedirectToPage("/Training/Kinds");
        }

        Id = Record.Id;
        IsGroup = Record.IsGroup;
        Message = "Записано.";
        return Page();
    }

    private void LoadShell()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = IsGroup ? "Вид обучения (группа)" : "Вид обучения";
        ViewData["Subtitle"] = string.IsNullOrWhiteSpace(Organization) ? "Все организации" : Organization;
    }

    private TrainingKindNode BindFromForm(IReadOnlyList<TrainingKindNode> all)
    {
        var formId = Request.Form["Id"].ToString()?.Trim()
                     ?? Request.Form["recordId"].ToString()?.Trim()
                     ?? Id?.Trim();
        var existing = !string.IsNullOrWhiteSpace(formId) ? _store.Find(formId) : null;
        var record = existing ?? new TrainingKindNode
        {
            Id = string.IsNullOrWhiteSpace(formId) ? Guid.NewGuid().ToString("N") : formId,
            Code = _store.SuggestNextCode(all)
        };

        record.IsGroup = string.Equals(Request.Form["isGroup"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
        record.ParentId = string.IsNullOrWhiteSpace(Request.Form["parentId"])
            ? null
            : Request.Form["parentId"].ToString()?.Trim();
        var code = Request.Form["code"].ToString()?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(code) && !code.Contains('<'))
        {
            record.Code = code;
        }

        record.Name = Request.Form["name"].ToString()?.Trim() ?? string.Empty;
        record.Type = Request.Form["type"].ToString()?.Trim() ?? "Обучение";
        record.Periodicity = Request.Form["periodicity"].ToString()?.Trim() ?? string.Empty;
        var section = Request.Form["section"].ToString()?.Trim() ?? string.Empty;
        record.Section = string.IsNullOrWhiteSpace(section) ? "Охрана труда" : section;
        record.PersonnelCategory = Request.Form["personnelCategory"].ToString()?.Trim() ?? string.Empty;
        return record;
    }
}
