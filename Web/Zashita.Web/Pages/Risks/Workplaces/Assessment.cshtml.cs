using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks.Workplaces;

[RequireLogin]
public class AssessmentModel : PageModel
{
    private const string Dash = "—";

    private readonly RiskWorkplaceStore _store;
    private readonly RiskHazardCatalogStore _catalog;
    private readonly RiskAssessmentObjectStore _objects;
    private readonly OrganizationStore _orgs;
    private readonly RiskCardDocxImportService _wordImport;
    private readonly RisksWorkspaceContext _risksWorkspace;

    public AssessmentModel(
        RiskWorkplaceStore store,
        RiskHazardCatalogStore catalog,
        RiskAssessmentObjectStore objects,
        OrganizationStore orgs,
        RiskCardDocxImportService wordImport,
        RisksWorkspaceContext risksWorkspace)
    {
        _store = store;
        _catalog = catalog;
        _objects = objects;
        _orgs = orgs;
        _wordImport = wordImport;
        _risksWorkspace = risksWorkspace;
    }

    public string Id { get; private set; } = string.Empty;
    public WorkplaceRecord Workplace { get; private set; } = new();
    public List<WorkplaceRiskLine> Lines { get; private set; } = [];
    public IReadOnlyList<RiskHazardCatalogItem> Catalog { get; private set; } = [];
    public string CatalogJson { get; private set; } = "[]";
    public IReadOnlyList<string> SeverityOptions { get; } = WorkplaceRiskScale.SeverityOptions;
    public IReadOnlyList<string> ProbabilityOptions { get; } = WorkplaceRiskScale.ProbabilityOptions;
    public IReadOnlyList<string> ResidualRiskOptions { get; } = WorkplaceRiskScale.ResidualRiskOptions;
    public bool NotFound { get; private set; }
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public bool HasProfessionTemplate { get; private set; }
    public int ProfessionTemplateLineCount { get; private set; }
    public string ProfessionTemplateSources { get; private set; } = string.Empty;
    public string SelectedObjectId { get; private set; } = RiskAssessmentObjectIds.OrganizationWide;
    public string SelectedObjectName { get; private set; } = RiskAssessmentObjectIds.OrganizationWideName;
    public bool IsOrganizationWideObject =>
        !RisksWorkspaceContext.ShouldFilterMembership(SelectedObjectId);
    private string DataObjectId => RisksWorkspaceContext.ProjectionObjectId(SelectedObjectId);

    [BindProperty(SupportsGet = true)]
    public string? Flow { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ObjectId { get; set; }

    public bool IsChangesFlow =>
        string.Equals(Flow, "changes", StringComparison.OrdinalIgnoreCase);

    public IActionResult OnGet(string id) => LoadPage(id);

    private static void ApplyWorkplaceLines(WorkplaceRecord workplace, List<WorkplaceRiskLine> lines)
    {
        workplace.RiskLines = lines;
        workplace.RiskAssessmentNotConducted = false;
    }

    public IActionResult OnPostSave(string id)
    {
        if (!TryMutateAssessment(id, workplace =>
            {
                var lines = ReadLinesFromForm(workplace);
                ApplyWorkplaceLines(workplace, lines);
                ApplyCardHeaderFromForm(workplace, lines);
            }, out var edited))
        {
            NotFound = true;
            return Page();
        }

        TempData["Message"] =
            $"Сохранено строк: {edited!.RiskLines?.Count ?? 0}. Дата оценки: {(edited.RiskAssessmentDate?.ToString("dd.MM.yyyy") ?? "—")}.";
        return RedirectToAssessment(id);
    }

    public IActionResult OnPostSaveChanges(string id)
    {
        if (!TryMutateAssessment(id, workplace =>
            {
                var lines = ReadLinesFromForm(workplace);
                ApplyWorkplaceLines(workplace, lines);
                ApplyCardHeaderFromForm(workplace, lines);
                SyncCatalogFromLines(lines, workplace.Profession);
                workplace.RiskLines = lines;
            }, out var edited))
        {
            NotFound = true;
            return Page();
        }

        TempData["Message"] =
            $"Изменения сохранены в карте «{edited!.Name}» и в реестре опасностей ({edited.RiskLines?.Count ?? 0} поз.).";
        return RedirectToPage("/Risks/Changes/Workplaces");
    }

    public IActionResult OnPostMarkNotConducted(string id)
    {
        if (!TryMutateAssessment(id, workplace =>
            {
                var rowCount = Math.Max(1, workplace.RiskLines?.Count ?? 0);
                var lines = new List<WorkplaceRiskLine>();
                for (var i = 0; i < rowCount; i++)
                {
                    lines.Add(CreateDashLine());
                }

                workplace.RiskLines = lines;
                workplace.RiskAssessmentDate = null;
                workplace.RiskAssessmentNotConducted = true;
                workplace.OverallRisk = Dash;
            }, out var edited))
        {
            NotFound = true;
            return Page();
        }

        TempData["Message"] = $"«{edited!.Name}»: оценка не проведена (прочерки). Объект в перечне без оценки рисков.";
        return RedirectToPage("/Risks/Reports/WithoutAssessment");
    }

    public IActionResult OnPostImportWord(string id, List<IFormFile>? wordFiles, bool overwriteExisting = false)
    {
        ResolveSelectedObject();
        var stored = ResolveStoredWorkplace(id);
        if (stored is null)
        {
            NotFound = true;
            return Page();
        }

        var workplace = RiskObjectAssessmentScope.ProjectForObject(stored, DataObjectId);
        var files = (wordFiles ?? [])
            .Where(f => f is { Length: > 0 })
            .ToList();
        if (files.Count == 0)
        {
            TempData["Message"] = "Выберите один или несколько файлов Word (.docx).";
            TempData["IsError"] = "1";
            return RedirectToAssessment(id);
        }

        if (files.Any(f => !string.Equals(Path.GetExtension(f.FileName), ".docx", StringComparison.OrdinalIgnoreCase)))
        {
            TempData["Message"] = "Нужны файлы .docx (не скан и не .doc).";
            TempData["IsError"] = "1";
            return RedirectToAssessment(id);
        }

        try
        {
            var payloads = new List<(Stream Stream, string FileName, MemoryStream Keep)>();
            try
            {
                foreach (var f in files)
                {
                    var ms = new MemoryStream();
                    f.CopyTo(ms);
                    ms.Position = 0;
                    payloads.Add((ms, f.FileName, ms));
                }

                var (ok, message, _) = _wordImport.ApplyMatchingCardToWorkplaceFromFiles(
                    workplace,
                    payloads.Select(p => (p.Stream, p.FileName)).ToList(),
                    overwriteExisting,
                    persist: false);
                RiskObjectAssessmentScope.SaveAssessment(stored, workplace, DataObjectId);
                _store.Upsert(stored);
                TempData["Message"] = message;
                if (!ok)
                {
                    TempData["IsError"] = "1";
                }
            }
            finally
            {
                foreach (var p in payloads)
                {
                    p.Keep.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            TempData["Message"] = $"Не удалось разобрать Word: {ex.Message}";
            TempData["IsError"] = "1";
        }

        return RedirectToAssessment(id);
    }

    public IActionResult OnPostInsertProfessionTemplate(string id)
    {
        ResolveSelectedObject();
        var stored = ResolveStoredWorkplace(id);
        if (stored is null)
        {
            NotFound = true;
            return Page();
        }

        var workplace = RiskObjectAssessmentScope.ProjectForObject(stored, DataObjectId);
        var all = _store.LoadAll();
        var objectNames = LoadObjectNames(stored.Organization);
        var preview = RiskProfessionTemplateBuilder.BuildPreview(all, workplace, DataObjectId, objectNames);
        if (!preview.Available)
        {
            TempData["Message"] =
                "Нет заполненных рисков по этой профессии (ни на других объектах, ни в других юрлицах) — шаблон недоступен.";
            TempData["IsError"] = "1";
            return RedirectToAssessment(id);
        }

        var template = RiskProfessionTemplateBuilder.BuildTemplateLines(all, workplace, DataObjectId, objectNames);
        var current = ReadLinesFromForm(workplace);
        if (current.Count == 0)
        {
            current = workplace.RiskLines ?? [];
        }

        var before = current.Count(l =>
            !string.IsNullOrWhiteSpace(l.HazardTitle) && l.HazardTitle is not ("-" or "—" or "–"));
        var merged = RiskProfessionTemplateBuilder.MergeIntoExisting(current, template);
        ApplyWorkplaceLines(workplace, merged);
        if (workplace.RiskAssessmentDate is null)
        {
            workplace.RiskAssessmentDate = DateTime.Today;
        }

        RiskObjectAssessmentScope.SaveAssessment(stored, workplace, DataObjectId);
        _store.Upsert(stored);

        var sources = preview.Organizations.Count == 0
            ? "других карт"
            : string.Join(", ", preview.Organizations);
        TempData["Message"] =
            $"Вставлено шаблонных рисков по профессии: было {before}, стало {merged.Count} (источники: {sources}).";
        return RedirectToAssessment(id);
    }

    private IReadOnlyDictionary<string, string> LoadObjectNames(string? organization)
    {
        var org = (organization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return _objects.Load(org)
            .Where(o => !string.IsNullOrWhiteSpace(o.Id))
            .GroupBy(o => o.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (g.First().Name ?? string.Empty).Trim(),
                StringComparer.OrdinalIgnoreCase);
    }

    private bool TryMutateAssessment(string id, Action<WorkplaceRecord> mutate, out WorkplaceRecord? edited)
    {
        edited = null;
        ResolveSelectedObject();
        var stored = ResolveStoredWorkplace(id);
        if (stored is null)
        {
            return false;
        }

        edited = RiskObjectAssessmentScope.ProjectForObject(stored, DataObjectId);
        mutate(edited);
        RiskObjectAssessmentScope.SaveAssessment(stored, edited, DataObjectId);
        _store.Upsert(stored);
        return true;
    }

    private void SyncCatalogFromLines(IReadOnlyList<WorkplaceRiskLine> lines, string profession)
    {
        var catalog = _catalog.LoadAll();
        var byId = catalog.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var byTitle = catalog
            .Where(x => !string.IsNullOrWhiteSpace(x.Title))
            .GroupBy(x => x.Title.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var hint = ProfessionMatcher.Normalize(profession);
        var changed = false;

        foreach (var line in lines)
        {
            var title = (line.HazardTitle ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(title) || IsDash(title))
            {
                continue;
            }

            RiskHazardCatalogItem? item = null;
            if (!string.IsNullOrWhiteSpace(line.HazardCatalogId) &&
                byId.TryGetValue(line.HazardCatalogId, out var byCatalogId))
            {
                item = byCatalogId;
            }
            else if (byTitle.TryGetValue(title, out var byName))
            {
                item = byName;
            }

            if (item is null)
            {
                item = new RiskHazardCatalogItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Title = title,
                    IsActive = true
                };
                catalog.Add(item);
                byId[item.Id] = item;
                byTitle[title] = item;
            }

            if (!IsDash(line.HazardNumber) && !string.IsNullOrWhiteSpace(line.HazardNumber))
            {
                item.Number = line.HazardNumber.Trim();
            }

            item.Title = title;
            if (!IsDash(line.ControlMeasures) && !string.IsNullOrWhiteSpace(line.ControlMeasures))
            {
                item.DefaultMeasures = line.ControlMeasures.Trim();
            }

            if (!string.IsNullOrWhiteSpace(hint) &&
                (string.IsNullOrWhiteSpace(item.ProfessionHint) ||
                 !item.ProfessionHint.Contains(hint, StringComparison.OrdinalIgnoreCase)))
            {
                item.ProfessionHint = string.IsNullOrWhiteSpace(item.ProfessionHint)
                    ? hint
                    : $"{item.ProfessionHint},{hint}";
            }

            line.HazardCatalogId = item.Id;
            changed = true;
        }

        if (changed)
        {
            _catalog.SaveAll(catalog);
        }
    }

    private static bool IsDash(string? value)
    {
        var t = (value ?? string.Empty).Trim();
        return t.Length == 0 || t is "-" or "—" or "–";
    }

    private WorkplaceRecord? ResolveStoredWorkplace(string id)
    {
        var organization = _orgs.GetSelectedOrganization(HttpContext);
        var workplace = _store.GetById(id);
        if (workplace is null || workplace.IsGroup ||
            !string.Equals(workplace.Organization, organization, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return workplace;
    }

    private void ResolveSelectedObject()
    {
        var organization = _orgs.GetSelectedOrganization(HttpContext);
        SelectedObjectId = _risksWorkspace.ResolveObjectId(HttpContext, organization, ObjectId);
        SelectedObjectName = _risksWorkspace.DisplayName(organization, SelectedObjectId);
        ObjectId = SelectedObjectId;
    }

    private IActionResult RedirectToAssessment(string id) =>
        RedirectToPage(new { id, flow = Flow, objectId = SelectedObjectId });

    private void ApplyCardHeaderFromForm(WorkplaceRecord workplace, List<WorkplaceRiskLine> lines)
    {
        workplace.RiskCardNumber = Request.Form["riskCardNumber"].ToString()?.Trim() ?? string.Empty;
        workplace.OverallRisk = Request.Form["overallRisk"].ToString()?.Trim() ?? string.Empty;
        workplace.RiskPeriodicity = Request.Form["riskPeriodicity"].ToString()?.Trim() ?? string.Empty;

        // Пустое поле даты = очистить (раньше fallback возвращал старое значение).
        var rawDate = Request.Form["riskAssessmentDate"].ToString()?.Trim() ?? string.Empty;
        workplace.RiskAssessmentDate = string.IsNullOrWhiteSpace(rawDate)
            ? null
            : ParseAssessmentDate(rawDate);
    }

    private List<WorkplaceRiskLine> ReadLinesFromForm(WorkplaceRecord workplace)
    {
        var lineIds = Request.Form["lineId"];
        var catalogIds = Request.Form["hazardCatalogId"];
        var numbers = Request.Form["number"];
        var titles = Request.Form["hazardTitle"];
        var sources = Request.Form["source"];
        var measures = Request.Form["measures"];
        var severities = Request.Form["severity"];
        var probabilities = Request.Form["probability"];
        var residuals = Request.Form["residualRisk"];
        var deletes = Request.Form["deleteLine"];

        var lines = new List<WorkplaceRiskLine>();
        for (var i = 0; i < titles.Count; i++)
        {
            if (i < deletes.Count && string.Equals(deletes[i].ToString(), "true", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var title = At(titles, i);
            var number = At(numbers, i);
            var catalogId = At(catalogIds, i);
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(number) && string.IsNullOrWhiteSpace(catalogId))
            {
                continue;
            }

            var lineId = At(lineIds, i);
            var existing = workplace.RiskLines?.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(lineId) &&
                string.Equals(x.Id, lineId, StringComparison.OrdinalIgnoreCase));

            var severity = WorkplaceRiskScale.MapSeverity(At(severities, i)) ?? At(severities, i);
            var probability = WorkplaceRiskScale.MapProbability(At(probabilities, i)) ?? At(probabilities, i);
            var score = WorkplaceRiskScale.ComputeScore(severity, probability);
            var scoreText = score?.ToString() ?? string.Empty;
            var zone = score is { } s ? WorkplaceRiskScale.ZoneForScore(s) : string.Empty;

            lines.Add(new WorkplaceRiskLine
            {
                Id = string.IsNullOrWhiteSpace(lineId) ? Guid.NewGuid().ToString("N") : lineId,
                HazardCatalogId = catalogId,
                HazardNumber = number,
                HazardTitle = title,
                Source = At(sources, i),
                Consequences = existing?.Consequences ?? string.Empty,
                WhoMaySuffer = existing?.WhoMaySuffer ?? string.Empty,
                InitialRisk = scoreText,
                ControlMeasures = At(measures, i),
                ResidualRisk = At(residuals, i),
                Responsible = existing?.Responsible ?? string.Empty,
                Severity = severity,
                Probability = probability,
                Significance = zone
            });
        }

        return lines;
    }

    private static DateTime? ParseAssessmentDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateTime.TryParseExact(
                text.Trim(),
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var iso))
        {
            return iso.Date;
        }

        return DateTime.TryParse(
            text,
            System.Globalization.CultureInfo.GetCultureInfo("ru-RU"),
            System.Globalization.DateTimeStyles.None,
            out var any)
            ? any.Date
            : null;
    }

    private static WorkplaceRiskLine CreateDashLine() => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        HazardNumber = Dash,
        HazardTitle = Dash,
        Source = Dash,
        Consequences = Dash,
        WhoMaySuffer = Dash,
        InitialRisk = Dash,
        ControlMeasures = Dash,
        ResidualRisk = Dash,
        Responsible = Dash,
        Severity = Dash,
        Probability = Dash,
        Significance = Dash
    };

    private static bool NormalizeLineScales(IList<WorkplaceRiskLine> lines)
    {
        var any = false;
        foreach (var line in lines)
        {
            var lineChanged = false;
            var sev = WorkplaceRiskScale.MapSeverity(line.Severity);
            if (sev is not null && !string.Equals(sev, line.Severity, StringComparison.Ordinal))
            {
                line.Severity = sev;
                lineChanged = true;
            }

            var prob = WorkplaceRiskScale.MapProbability(line.Probability);
            if (prob is not null && !string.Equals(prob, line.Probability, StringComparison.Ordinal))
            {
                line.Probability = prob;
                lineChanged = true;
            }

            if (!lineChanged)
            {
                continue;
            }

            var score = WorkplaceRiskScale.ComputeScore(line.Severity, line.Probability);
            if (score is { } s)
            {
                line.InitialRisk = s.ToString();
                line.Significance = WorkplaceRiskScale.ZoneForScore(s);
            }

            any = true;
        }

        return any;
    }

    private IActionResult LoadPage(string id)
    {
        ViewData["ActiveModule"] = "риски";
        ViewData["IsModulePage"] = true;
        Message = TempData["Message"] as string;
        IsError = string.Equals(TempData["IsError"] as string, "1", StringComparison.Ordinal);
        ResolveSelectedObject();

        if (string.IsNullOrWhiteSpace(id))
        {
            NotFound = true;
            return Page();
        }

        var stored = ResolveStoredWorkplace(id);
        if (stored is null)
        {
            NotFound = true;
            return Page();
        }

        Id = id;
        var workplace = RiskObjectAssessmentScope.ProjectForObject(stored, DataObjectId);
        if (_wordImport.EnsureLinesLinkedToCatalog(workplace, persist: false))
        {
            RiskObjectAssessmentScope.SaveAssessment(stored, workplace, DataObjectId);
            _store.Upsert(stored);
            stored = ResolveStoredWorkplace(id) ?? stored;
            workplace = RiskObjectAssessmentScope.ProjectForObject(stored, DataObjectId);
        }

        Workplace = workplace;
        Lines = workplace.RiskLines?.Count > 0 ? workplace.RiskLines : [];
        if (NormalizeLineScales(Lines))
        {
            RiskObjectAssessmentScope.SaveAssessment(stored, workplace, DataObjectId);
            _store.Upsert(stored);
        }

        Catalog = _catalog.LoadActive();
        CatalogJson = JsonSerializer.Serialize(Catalog.Select(c => new
        {
            id = c.Id,
            number = c.Number,
            title = c.Title,
            measures = c.DefaultMeasures
        }));

        var templatePreview = RiskProfessionTemplateBuilder.BuildPreview(
            _store.LoadAll(),
            workplace,
            DataObjectId,
            LoadObjectNames(workplace.Organization));
        HasProfessionTemplate = templatePreview.Available;
        ProfessionTemplateLineCount = templatePreview.LineCount;
        ProfessionTemplateSources = templatePreview.Organizations.Count == 0
            ? string.Empty
            : string.Join(", ", templatePreview.Organizations);

        ViewData["Title"] = IsChangesFlow
            ? "Внесение изменений — карта оценки рисков"
            : "Карта идентификации опасностей и оценки рисков";
        ViewData["Subtitle"] = IsChangesFlow
            ? $"Шаг 3 из 3 · {workplace.Name}"
            : $"{workplace.Name} · {SelectedObjectName} · {workplace.Organization}";
        return Page();
    }

    private static string At(Microsoft.Extensions.Primitives.StringValues values, int index) =>
        index < values.Count ? values[index].ToString()?.Trim() ?? string.Empty : string.Empty;
}
