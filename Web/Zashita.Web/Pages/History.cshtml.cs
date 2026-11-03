using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Ppe;
using Zashita.Core.PrintHistory;
using Zashita.Core.Training.Journals;
using Zashita.Web.Services;

namespace Zashita.Web.Pages;

[RequireLogin]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class HistoryModel : PageModel
{
    private readonly SavedSiosCardsStore _cards;
    private readonly DocumentPrintHistoryStore _prints;
    private readonly TrainingPrintHistoryStore _legacyJournalPrints;
    private readonly TrainingJournalHistoryStore _journalVersions;
    private readonly OrganizationStore _orgs;

    public HistoryModel(
        SavedSiosCardsStore cards,
        DocumentPrintHistoryStore prints,
        TrainingPrintHistoryStore legacyJournalPrints,
        TrainingJournalHistoryStore journalVersions,
        OrganizationStore orgs)
    {
        _cards = cards;
        _prints = prints;
        _legacyJournalPrints = legacyJournalPrints;
        _journalVersions = journalVersions;
        _orgs = orgs;
    }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? To { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Org { get; set; }

    /// <summary>all | ppe | journal | med | risks | sout | training | other</summary>
    [BindProperty(SupportsGet = true)]
    public string? Type { get; set; }

    public IReadOnlyList<UnifiedPrintRow> Items { get; private set; } = [];
    public IReadOnlyList<string> Organizations { get; private set; } = [];
    public string SessionOrganization { get; private set; } = string.Empty;

    public void OnGet()
    {
        ViewData["ActiveModule"] = "history";
        ViewData["Title"] = "История печати";
        ViewData["SectionTitle"] = "История печати";

        SessionOrganization = _orgs.GetSelectedOrganization(HttpContext);
        Type = NormalizeType(Type);

        var from = (From ?? DateTime.Today.AddYears(-3)).Date;
        var to = (To ?? DateTime.Today).Date;
        if (to < from)
        {
            (from, to) = (to, from);
        }

        From = from;
        To = to;

        var rows = new List<UnifiedPrintRow>();
        if (Type is "all" or "ppe")
        {
            rows.AddRange(LoadPpeRows());
        }

        if (Type is "all" or "journal")
        {
            rows.AddRange(LoadDocumentRows("journal"));
            rows.AddRange(LoadLegacyJournalPrints());
            rows.AddRange(LoadJournalVersionRows());
        }

        if (Type is "all" or "med")
        {
            rows.AddRange(LoadDocumentRows("med"));
        }

        if (Type is "all" or "risks")
        {
            rows.AddRange(LoadDocumentRows("risks"));
        }

        if (Type is "all" or "sout")
        {
            rows.AddRange(LoadDocumentRows("sout"));
        }

        if (Type is "all" or "training")
        {
            rows.AddRange(LoadDocumentRows("training"));
        }

        if (Type == "all")
        {
            rows.AddRange(LoadDocumentRows("other"));
        }

        Organizations = rows
            .Select(r => r.Organization.Trim())
            .Where(o => o.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(o => o, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var filtered = rows
            .Where(r => string.IsNullOrWhiteSpace(Org) ||
                        string.IsNullOrWhiteSpace(r.Organization) ||
                        string.Equals(r.Organization.Trim(), Org, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.At.Date >= from && r.At.Date <= to)
            .Where(r => MatchesQuery(r, Q))
            .GroupBy(r => $"{r.DeleteSource}|{r.DeleteId}|{r.CategoryKind}|{r.Title}|{r.At:yyyyMMddHHmm}|{r.Summary}",
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(r => r.At)
            .ToList();

        Items = filtered;
        ViewData["Subtitle"] = $"Показано: {Items.Count}";
    }

    public IActionResult OnPostDelete(string? id, string? source)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            switch ((source ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "ppe":
                    _cards.Delete(id);
                    break;
                case "print":
                    _prints.Delete(id);
                    break;
                case "legacy":
                    _legacyJournalPrints.Delete(id);
                    break;
                case "journal-version":
                    DeleteJournalVersion(id);
                    break;
                default:
                    // fallback: пробуем все хранилища по id
                    if (!_prints.Delete(id) && !_legacyJournalPrints.Delete(id))
                    {
                        _cards.Delete(id);
                    }

                    break;
            }
        }

        return RedirectToPage(new
        {
            q = Q,
            from = From?.ToString("yyyy-MM-dd"),
            to = To?.ToString("yyyy-MM-dd"),
            org = Org,
            type = Type
        });
    }

    private void DeleteJournalVersion(string id)
    {
        // формат: KindName:version
        var parts = id.Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var version))
        {
            return;
        }

        if (!Enum.TryParse<TrainingJournalKind>(parts[0], ignoreCase: true, out var kind))
        {
            return;
        }

        _journalVersions.Delete(kind, version);
    }

    private IEnumerable<UnifiedPrintRow> LoadPpeRows()
    {
        foreach (var card in _cards.LoadAll())
        {
            var at = card.UpdatedAt != default ? card.UpdatedAt.ToLocalTime() : card.CreatedAt.ToLocalTime();
            if (at == default)
            {
                at = DateTime.Now;
            }

            var people = card.Employees.Count == 0
                ? "—"
                : string.Join("; ", card.Employees.Take(3)) + (card.Employees.Count > 3 ? "…" : "");

            yield return new UnifiedPrintRow(
                At: at,
                Category: "СИЗ",
                CategoryKind: "ppe",
                Title: string.IsNullOrWhiteSpace(card.DocumentName) ? "Карточка СИЗ" : card.DocumentName,
                Organization: card.Organization,
                Detail: string.IsNullOrWhiteSpace(card.CardNumber) ? card.Profession : $"№ {card.CardNumber} · {card.Profession}",
                Summary: people,
                OpenUrl: $"/Ppe/SiosCard/Preview?id={Uri.EscapeDataString(card.Id)}",
                DeleteId: card.Id,
                DeleteSource: "ppe");
        }
    }

    private IEnumerable<UnifiedPrintRow> LoadDocumentRows(string category)
    {
        foreach (var item in _prints.Query(category: category))
        {
            yield return new UnifiedPrintRow(
                At: item.PrintedAt,
                Category: CategoryLabel(item.Category),
                CategoryKind: item.Category,
                Title: item.Title,
                Organization: item.Organization,
                Detail: string.IsNullOrWhiteSpace(item.Detail) ? "—" : item.Detail,
                Summary: string.IsNullOrWhiteSpace(item.Summary) ? "—" : item.Summary,
                OpenUrl: item.OpenUrl,
                DeleteId: item.Id,
                DeleteSource: "print");
        }
    }

    private IEnumerable<UnifiedPrintRow> LoadLegacyJournalPrints()
    {
        foreach (var item in _legacyJournalPrints.Query())
        {
            yield return new UnifiedPrintRow(
                At: item.PrintedAt,
                Category: "Журнал",
                CategoryKind: "journal",
                Title: item.Title,
                Organization: item.Organization,
                Detail: string.IsNullOrWhiteSpace(item.BriefingType) ? "—" : item.BriefingType,
                Summary: string.IsNullOrWhiteSpace(item.Summary) ? $"Строк: {item.EntryCount}" : item.Summary,
                OpenUrl: $"/Training/Journal?title={Uri.EscapeDataString(item.Title)}",
                DeleteId: item.Id,
                DeleteSource: "legacy");
        }
    }

    private IEnumerable<UnifiedPrintRow> LoadJournalVersionRows()
    {
        foreach (var def in TrainingJournalRegistry.Journals)
        {
            foreach (var entry in _journalVersions.Get(def.Kind))
            {
                yield return new UnifiedPrintRow(
                    At: entry.SavedAt,
                    Category: "Журнал",
                    CategoryKind: "journal",
                    Title: def.Title,
                    Organization: SessionOrganization,
                    Detail: string.IsNullOrWhiteSpace(entry.BriefingType) ? $"версия {entry.Version}" : entry.BriefingType!,
                    Summary: string.IsNullOrWhiteSpace(entry.Summary)
                        ? $"{entry.DateFrom:dd.MM.yyyy} — {entry.DateTo:dd.MM.yyyy}"
                        : entry.Summary,
                    OpenUrl: $"/Training/Journal?title={Uri.EscapeDataString(def.Title)}",
                    DeleteId: $"{def.Kind}:{entry.Version}",
                    DeleteSource: "journal-version");
            }
        }
    }

    private static string CategoryLabel(string category) => category.ToLowerInvariant() switch
    {
        "ppe" => "СИЗ",
        "journal" => "Журнал",
        "med" => "Медосмотр",
        "risks" => "Риски",
        "sout" => "СОУТ",
        "training" => "Обучение",
        _ => "Документ"
    };

    private static string NormalizeType(string? type) =>
        (type ?? "all").Trim().ToLowerInvariant() switch
        {
            "ppe" or "сиз" or "card" or "cards" => "ppe",
            "journal" or "журнал" or "journals" => "journal",
            "med" or "медосмотр" => "med",
            "risks" or "риски" => "risks",
            "sout" or "соут" => "sout",
            "training" or "обучение" => "training",
            _ => "all"
        };

    private static bool MatchesQuery(UnifiedPrintRow row, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var s = query.Trim();
        return row.Title.Contains(s, StringComparison.OrdinalIgnoreCase)
               || row.Organization.Contains(s, StringComparison.OrdinalIgnoreCase)
               || row.Detail.Contains(s, StringComparison.OrdinalIgnoreCase)
               || row.Summary.Contains(s, StringComparison.OrdinalIgnoreCase)
               || row.Category.Contains(s, StringComparison.OrdinalIgnoreCase);
    }

    public sealed record UnifiedPrintRow(
        DateTime At,
        string Category,
        string CategoryKind,
        string Title,
        string Organization,
        string Detail,
        string Summary,
        string? OpenUrl,
        string? DeleteId,
        string DeleteSource);
}
