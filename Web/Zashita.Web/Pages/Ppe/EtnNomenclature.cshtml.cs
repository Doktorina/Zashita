using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Ppe;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Ppe;

[RequireLogin]
public class EtnNomenclatureModel : PageModel
{
    private readonly EtnNomenclatureStore _store;

    public EtnNomenclatureModel(EtnNomenclatureStore store) => _store = store;

    public List<PpeNomenclatureItem> Items { get; set; } = [];
    public IReadOnlyList<string> ProtectionTypes { get; private set; } = [];
    public string? Message { get; private set; }
    public string? ErrorMessage { get; private set; }

    [BindProperty]
    public IFormFile? UploadFile { get; set; }

    public void OnGet()
    {
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;
        LoadEditor();
    }

    public IActionResult OnPostSave()
    {
        Items = ReadItemsFromForm();
        _store.SaveAll(Items);
        Message = $"Сохранено позиций: {Items.Count}.";
        LoadEditor();
        return Page();
    }

    public IActionResult OnPostAddRow()
    {
        Items = _store.LoadForEditor();
        Items.Add(new PpeNomenclatureItem
        {
            Category = "СИЗ",
            ProtectionType = _store.GetProtectionTypes().FirstOrDefault() ?? "Одежда специальная защитная",
            Name = "",
            Norm = "1 шт.",
            NormParagraph = ""
        });
        ProtectionTypes = BuildProtectionTypes(Items);
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;
        return Page();
    }

    public IActionResult OnPostDeleteSelected()
    {
        var deleteFlags = Request.Form["DeleteRow"];
        var toDelete = new HashSet<int>();
        foreach (var flag in deleteFlags)
        {
            if (int.TryParse(flag, out var idx))
            {
                toDelete.Add(idx);
            }
        }

        var names = Request.Form["Name"];
        var list = new List<PpeNomenclatureItem>();
        for (var i = 0; i < names.Count; i++)
        {
            if (toDelete.Contains(i))
            {
                continue;
            }

            var item = ReadItemAt(i);
            if (item is not null)
            {
                list.Add(item);
            }
        }

        _store.SaveAll(list);
        Message = toDelete.Count > 0
            ? $"Удалено строк: {toDelete.Count}. Осталось: {list.Count}."
            : "Отметьте строки для удаления.";
        LoadEditor();
        return Page();
    }

    public IActionResult OnPostUpload()
    {
        if (UploadFile is null || UploadFile.Length == 0)
        {
            ErrorMessage = "Выберите файл Excel (.xlsx).";
            LoadEditor();
            return Page();
        }

        var ext = Path.GetExtension(UploadFile.FileName);
        if (!string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Нужен файл формата .xlsx.";
            LoadEditor();
            return Page();
        }

        using var stream = UploadFile.OpenReadStream();
        Items = _store.ImportFromStream(stream, UploadFile.FileName, replaceExisting: true);
        if (Items.Count == 0)
        {
            ErrorMessage =
                $"Файл «{UploadFile.FileName}» не распознан (0 строк). Нужен .xlsx с колонками: тип СИЗ, наименование, норма, пункт ЕТН.";
            LoadEditor();
            return Page();
        }

        Message = $"Импортировано и сохранено: {Items.Count} позиций. Повторная загрузка не нужна.";
        ProtectionTypes = BuildProtectionTypes(Items);
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;
        return Page();
    }

    private void LoadEditor()
    {
        Items = _store.LoadForEditor();
        ProtectionTypes = BuildProtectionTypes(Items);
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;
    }

    private static IReadOnlyList<string> BuildProtectionTypes(IEnumerable<PpeNomenclatureItem> items) =>
        items
            .Select(i => i.DisplayProtectionType)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private List<PpeNomenclatureItem> ReadItemsFromForm()
    {
        var names = Request.Form["Name"];
        var list = new List<PpeNomenclatureItem>();
        for (var i = 0; i < names.Count; i++)
        {
            var item = ReadItemAt(i);
            if (item is not null)
            {
                list.Add(item);
            }
        }

        return list;
    }

    private PpeNomenclatureItem? ReadItemAt(int i)
    {
        var categories = Request.Form["Category"];
        var protectionTypes = Request.Form["ProtectionType"];
        var names = Request.Form["Name"];
        var norms = Request.Form["Norm"];
        var paragraphs = Request.Form["NormParagraph"];

        if (i >= names.Count)
        {
            return null;
        }

        var name = names[i].ToString()?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var protectionType = i < protectionTypes.Count ? protectionTypes[i].ToString()?.Trim() ?? "" : "";
        var category = i < categories.Count ? categories[i].ToString() ?? "СИЗ" : "СИЗ";
        if (string.IsNullOrWhiteSpace(category) || category is not ("СИЗ" or "ДСИЗ"))
        {
            category = protectionType.Contains("дерматолог", StringComparison.OrdinalIgnoreCase) ||
                       protectionType.Equals("ДСИЗ", StringComparison.OrdinalIgnoreCase) ||
                       protectionType.Contains("очища", StringComparison.OrdinalIgnoreCase)
                ? "ДСИЗ"
                : "СИЗ";
        }

        return new PpeNomenclatureItem
        {
            Category = category,
            ProtectionType = protectionType,
            Name = name,
            Norm = i < norms.Count && !string.IsNullOrWhiteSpace(norms[i]) ? norms[i].ToString()! : "По норме",
            NormParagraph = i < paragraphs.Count ? paragraphs[i].ToString() ?? "" : ""
        };
    }
}
