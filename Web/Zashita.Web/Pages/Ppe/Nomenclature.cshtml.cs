using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Ppe;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Ppe;

[RequireLogin]
public class NomenclatureModel : PageModel
{
    private readonly PpeNomenclatureStore _store;
    private readonly IssueStandardsStore _standards;

    public NomenclatureModel(PpeNomenclatureStore store, IssueStandardsStore standards)
    {
        _store = store;
        _standards = standards;
    }

    public List<PpeNomenclatureItem> Items { get; set; } = [];
    public IReadOnlyList<string> ProtectionTypes { get; private set; } = [];
    public string? Message { get; private set; }

    public void OnGet()
    {
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;
        Items = _store.LoadForEditor(_standards.LoadAll());
        ProtectionTypes = _store.GetProtectionTypes(_standards.LoadAll());
    }

    public IActionResult OnPostSave()
    {
        Items = ReadItemsFromForm();
        Items = _store.EnrichForEditor(Items);
        _store.SaveAll(Items);
        Message = "Номенклатура сохранена.";
        ProtectionTypes = Items
            .Select(i => i.DisplayProtectionType)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;
        return Page();
    }

    public IActionResult OnPostAddRow()
    {
        Items = _store.LoadForEditor(_standards.LoadAll());
        Items.Add(new PpeNomenclatureItem
        {
            Category = "СИЗ",
            ProtectionType = ProtectionTypes.FirstOrDefault() ?? "Одежда специальная защитная",
            Name = "",
            Norm = "По норме"
        });
        ProtectionTypes = _store.GetProtectionTypes(_standards.LoadAll());
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;
        return Page();
    }

    public IActionResult OnPostImportAppendix1()
    {
        Items = _store.ImportFromAppendix1();
        ProtectionTypes = _store.GetProtectionTypes();
        Message = $"Загружено из Приложения 1: {Items.Count} позиций.";
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;
        return Page();
    }

    private List<PpeNomenclatureItem> ReadItemsFromForm()
    {
        var categories = Request.Form["Category"];
        var protectionTypes = Request.Form["ProtectionType"];
        var names = Request.Form["Name"];
        var norms = Request.Form["Norm"];
        var paragraphs = Request.Form["NormParagraph"];
        var list = new List<PpeNomenclatureItem>();
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i].ToString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name)) continue;
            var protectionType = i < protectionTypes.Count ? protectionTypes[i].ToString()?.Trim() ?? "" : "";
            var category = i < categories.Count ? categories[i].ToString() ?? "СИЗ" : "СИЗ";
            if (string.IsNullOrWhiteSpace(category) || category is not ("СИЗ" or "ДСИЗ"))
            {
                category = protectionType.Contains("дерматолог", StringComparison.OrdinalIgnoreCase) ||
                           protectionType.Equals("ДСИЗ", StringComparison.OrdinalIgnoreCase) ||
                           protectionType.Contains("очища", StringComparison.OrdinalIgnoreCase) ||
                           protectionType.Contains("смыва", StringComparison.OrdinalIgnoreCase) ||
                           protectionType.Contains("защитного типа", StringComparison.OrdinalIgnoreCase)
                    ? "ДСИЗ"
                    : "СИЗ";
            }

            if (PpeProtectionTypeResolver.IsGenericType(protectionType))
            {
                protectionType = string.Empty;
            }

            list.Add(new PpeNomenclatureItem
            {
                Category = category,
                ProtectionType = protectionType,
                Name = name,
                Norm = i < norms.Count && !string.IsNullOrWhiteSpace(norms[i]) ? norms[i].ToString()! : "По норме",
                NormParagraph = i < paragraphs.Count ? paragraphs[i].ToString() ?? "" : ""
            });
        }

        return list;
    }
}
