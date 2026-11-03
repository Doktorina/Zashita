using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Training.Journals;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training.Journal;

[RequireLogin]
public class PreviewModel : PageModel
{
    private readonly TrainingJournalStore _journalStore;
    private readonly TrainingPrintHistoryStore _printHistory;
    private readonly OrganizationStore _orgs;

    public PreviewModel(
        TrainingJournalStore journalStore,
        TrainingPrintHistoryStore printHistory,
        OrganizationStore orgs)
    {
        _journalStore = journalStore;
        _printHistory = printHistory;
        _orgs = orgs;
    }

    [BindProperty(SupportsGet = true)]
    public TrainingJournalKind Kind { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool? ShowTitlePage { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool AutoPrint { get; set; }

    public string PreviewHtml { get; private set; } = string.Empty;
    public string JournalTitle { get; private set; } = "Журнал";
    public string? Message { get; private set; }
    public string BackUrl { get; private set; } = "/Training/Reports";

    public IActionResult OnGet()
    {
        if (!TryLoad(out var definition))
        {
            return RedirectToPage("/Training/Reports");
        }

        LoadPreview(definition);
        RecordPrint(definition);
        return Page();
    }

    public IActionResult OnPostSave()
    {
        if (!TryLoad(out var definition))
        {
            return RedirectToPage("/Training/Reports");
        }

        var keys = Request.Form["overrideKey"];
        var values = Request.Form["overrideValue"];
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i].ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            map[key] = i < values.Count ? values[i].ToString() ?? string.Empty : string.Empty;
        }

        // Не сохраняем подсказку из бланка вместо названия организации
        if (map.TryGetValue("org", out var orgOv) &&
            orgOv.Contains("поле которое заполняется", StringComparison.OrdinalIgnoreCase))
        {
            map.Remove("org");
        }

        if (map.TryGetValue("object", out var objOv) &&
            (objOv.Contains("поле которое заполняется", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(objOv.Trim(), "Объект", StringComparison.OrdinalIgnoreCase) ||
             IsProfessionLikeOverride(objOv, Kind)))
        {
            map.Remove("object");
        }

        _journalStore.SavePrintOverrides(Kind, map);
        Message = "Изменения в форме сохранены.";
        LoadPreview(definition);
        return Page();
    }

    private bool TryLoad(out TrainingJournalDefinition definition)
    {
        try
        {
            definition = TrainingJournalRegistry.Get(Kind);
            JournalTitle = definition.Title;
            BackUrl = $"/Training/Journal?title={Uri.EscapeDataString(definition.Title)}";
            return true;
        }
        catch
        {
            definition = null!;
            return false;
        }
    }

    private void LoadPreview(TrainingJournalDefinition definition)
    {
        var saved = _journalStore.Load(Kind);
        var show = ShowTitlePage ?? saved.Params.ShowTitlePage;
        var sessionOrg = _orgs.GetSelectedOrganization(HttpContext);
        var p = saved.Params;
        if (string.IsNullOrWhiteSpace(p.Organization))
        {
            p = p with { Organization = sessionOrg };
        }

        var overrides = saved.PrintOverrides
            .Where(kv =>
            {
                if (kv.Key.Equals("org", StringComparison.OrdinalIgnoreCase) &&
                    kv.Value.Contains("поле которое заполняется", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (kv.Key.Equals("object", StringComparison.OrdinalIgnoreCase) &&
                    (kv.Value.Contains("поле которое заполняется", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(kv.Value.Trim(), "Объект", StringComparison.OrdinalIgnoreCase) ||
                     IsProfessionLikeOverride(kv.Value, Kind)))
                {
                    return false;
                }

                return true;
            })
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        PreviewHtml = TrainingJournalPrintBuilder.BuildPreviewBody(
            definition,
            saved.Entries,
            p,
            show,
            overrides);
    }

    private bool IsProfessionLikeOverride(string? value, TrainingJournalKind kind)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        var entries = _journalStore.Load(kind).Entries;
        return entries.Any(e =>
            (!string.IsNullOrWhiteSpace(e.Profession) &&
             string.Equals(text, e.Profession.Trim(), StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(e.Workplace) &&
             string.Equals(text, e.Workplace.Trim(), StringComparison.OrdinalIgnoreCase) &&
             string.Equals(e.Workplace.Trim(), e.Profession?.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    private void RecordPrint(TrainingJournalDefinition definition)
    {
        var saved = _journalStore.Load(Kind);
        var p = saved.Params;
        var org = string.IsNullOrWhiteSpace(p.Organization)
            ? _orgs.GetSelectedOrganization(HttpContext)
            : p.Organization;
        var contentCount = saved.Entries.Count(e => !string.IsNullOrWhiteSpace(e.EmployeeName));

        _printHistory.Append(new TrainingPrintHistoryRecord
        {
            PrintedAt = DateTime.Now,
            Kind = definition.Kind,
            Title = definition.Title,
            Organization = org,
            BriefingType = p.BriefingType,
            DateFrom = p.DateFrom,
            DateTo = p.DateTo,
            EntryCount = contentCount,
            Summary = $"Предпросмотр печати · строк: {contentCount}"
        });
    }
}
