using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks;

[RequireLogin]
public class HazardCatalogModel : PageModel
{
    private readonly RiskHazardCatalogStore _store;

    public HazardCatalogModel(RiskHazardCatalogStore store) => _store = store;

    [BindProperty(SupportsGet = true)]
    public string? Flow { get; set; }

    public bool IsChangesFlow =>
        string.Equals(Flow, "changes", StringComparison.OrdinalIgnoreCase);

    public List<RiskHazardCatalogItem> Items { get; private set; } = [];
    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    public void OnGet()
    {
        ViewData["ActiveModule"] = "риски";
        ViewData["IsModulePage"] = true;
        if (IsChangesFlow)
        {
            ViewData["Title"] = "Внесение изменений — реестр опасностей";
            ViewData["Subtitle"] = "Шаг 1 из 3";
        }

        Items = _store.LoadAll();
        Message = TempData["Message"] as string;
    }

    public IActionResult OnPostSave()
    {
        SaveFromForm();
        TempData["Message"] = $"Сохранено позиций: {_store.LoadAll().Count}.";
        return IsChangesFlow
            ? RedirectToPage(new { flow = "changes" })
            : RedirectToPage();
    }

    public IActionResult OnPostSaveAndNext()
    {
        SaveFromForm();
        TempData["Message"] = "Реестр сохранён. Выберите рабочее место.";
        return RedirectToPage("/Risks/Changes/Workplaces");
    }

    private void SaveFromForm()
    {
        var ids = Request.Form["id"];
        var numbers = Request.Form["number"];
        var titles = Request.Form["title"];
        var measures = Request.Form["measures"];
        var hints = Request.Form["professionHint"];
        var actives = Request.Form["active"];

        var existingById = _store.LoadAll()
            .ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

        var list = new List<RiskHazardCatalogItem>();
        for (var i = 0; i < titles.Count; i++)
        {
            var title = titles[i].ToString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var id = i < ids.Count ? ids[i].ToString()?.Trim() ?? "" : "";
            if (string.IsNullOrWhiteSpace(id))
            {
                id = Guid.NewGuid().ToString("N");
            }

            var activeRaw = i < actives.Count ? actives[i].ToString() ?? "0" : "0";
            var isActive = activeRaw is "1" or "true" or "on";
            existingById.TryGetValue(id, out var previous);

            list.Add(new RiskHazardCatalogItem
            {
                Id = id,
                Number = i < numbers.Count ? numbers[i].ToString()?.Trim() ?? "" : "",
                Title = title,
                DefaultWhoMaySuffer = previous?.DefaultWhoMaySuffer ?? "работник",
                DefaultMeasures = i < measures.Count ? measures[i].ToString()?.Trim() ?? "" : "",
                DefaultInitialRisk = previous?.DefaultInitialRisk ?? "Средний",
                DefaultResidualRisk = previous?.DefaultResidualRisk ?? "Низкий",
                ProfessionHint = i < hints.Count ? hints[i].ToString()?.Trim() ?? "" : "",
                IsActive = isActive
            });
        }

        _store.SaveAll(RiskHazardNumberComparer.OrderByNumber(list));
    }
}
