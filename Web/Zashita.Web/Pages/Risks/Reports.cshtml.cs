using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks;

[RequireLogin]
public class ReportsModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public IReadOnlyList<ReportEntry> Entries { get; private set; } = [];

    public void OnGet()
    {
        ViewData["ActiveModule"] = "риски";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Отчеты по рискам";
        ViewData["Subtitle"] = "Оценка и управление рисками";

        var all = new List<ReportEntry>
        {
            new("Отчет по рискам",
                "Реестр опасностей, оценка и мероприятия по иерархии оргструктуры",
                "/Risks/Reports/Status"),
            new("График идентификации опасностей и оценки рисков в подразделениях",
                "Формируется из отчёта по рискам или перечня рабочих мест — таблица по месяцам",
                "/Risks/IdentificationSchedule"),
            new("Перечень рабочих мест, подлежащих оценке профессиональных рисков",
                "Печать из документа подготовки — РМ из справочника «Рабочие места»",
                "/Risks/Preparation"),
            new("Перечень объектов без оценки рисков", "Объекты, не проходившие оценку рисков", "/Risks/Reports/WithoutAssessment"),
            new("Мероприятия по снижению рисков", "План и факт мероприятий", null)
        };

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var query = Q.Trim();
            all = all
                .Where(item => item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                               item.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        Entries = all;
    }

    public sealed record ReportEntry(string Title, string Description, string? Href);
}
