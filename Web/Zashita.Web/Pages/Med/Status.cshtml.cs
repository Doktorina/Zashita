using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Med;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med;

[RequireLogin]
public class StatusModel : PageModel
{
    private readonly MedContextService _med;
    private readonly MedExamStore _exams;
    private readonly MedConclusionScanStore _scans;
    private readonly MedPersonListStore _personLists;
    private readonly MedStandardStore _standards;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;
    private readonly MedDirectionStore _directions;

    private static readonly object RowsCacheGate = new();
    private static string _rowsCacheKey = string.Empty;
    private static DateTime _rowsCacheUtc = DateTime.MinValue;
    private static List<MedStatusRow>? _rowsCache;
    private static Dictionary<string, string>? _hrCache;
    private static bool _standardsMissingCache;

    internal static void InvalidateRowsCache()
    {
        lock (RowsCacheGate)
        {
            _rowsCache = null;
            _hrCache = null;
            _rowsCacheKey = string.Empty;
            _rowsCacheUtc = DateTime.MinValue;
        }
    }

    public StatusModel(
        MedContextService med,
        MedExamStore exams,
        MedConclusionScanStore scans,
        MedPersonListStore personLists,
        MedStandardStore standards,
        OrganizationStore orgs,
        OrganizationEmployeesService employees,
        MedDirectionStore directions)
    {
        _med = med;
        _exams = exams;
        _scans = scans;
        _personLists = personLists;
        _standards = standards;
        _orgs = orgs;
        _employees = employees;
        _directions = directions;
    }

    [BindProperty]
    public DateTime ReportDate { get; set; } = DateTime.Today;

    [BindProperty(SupportsGet = true)]
    public string? SelectedOrganization { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Department { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Profession { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ExamType { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; }

    /// <summary>После загрузки скана — прокрутить к этой строке осмотра.</summary>
    [BindProperty(SupportsGet = true)]
    public string? FocusExamId { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<string> OrganizationOptions { get; private set; } = [];
    public IReadOnlyList<string> DepartmentOptions { get; private set; } = [];
    public IReadOnlyList<string> ProfessionOptions { get; private set; } = [];
    public IReadOnlyList<string> ExamTypeOptions { get; private set; } = [];
    public IReadOnlyList<string> StatusOptions { get; private set; } = MedExamStatuses.ReportFilterOptions;
    public IReadOnlyList<string> ConclusionOptions { get; private set; } = MedStatusBuilder.ConclusionOptions;
    public IReadOnlyList<MedStatusRow> Rows { get; private set; } = [];
    public IReadOnlyList<MedStatusTreeLine> TreeLines { get; private set; } = [];
    /// <summary>Люди в отчёте: человек×организация (одно ФИО в двух ЮЛ = 2).</summary>
    public int PeopleCount { get; private set; }
    public Dictionary<string, string> HrStatusByName { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool StandardsMissing { get; private set; }
    public string? Message { get; private set; }

    public void OnGet()
    {
        Message = TempData["Message"] as string;
        LoadReport();
    }

    public IActionResult OnPostSave()
    {
        var formRows = ParseExamFormRows(Request.Form);
        if (formRows.Count == 0)
        {
            TempData["Message"] = "Нет строк для сохранения.";
            return RedirectToPage(FilterRoute());
        }

        var existing = _exams.LoadAll().ToDictionary(x => x.Id, StringComparer.Ordinal);
        var existingByKey = existing.Values
            .GroupBy(MedStandardAssignmentBuilder.BuildExamKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.ExamDate).First(), StringComparer.OrdinalIgnoreCase);
        var toSave = new List<MedExamRecord>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in formRows)
        {
            if (string.IsNullOrWhiteSpace(row.EmployeeName))
            {
                continue;
            }

            var examType = row.IsPsychiatric
                ? "Психиатрическое освидетельствование"
                : MedStandardAssignmentBuilder.NormalizeExamType(row.ExamType);
            var itemNumber = row.IsPsychiatric
                ? string.Empty
                : MedItemNumber.Normalize(row.ItemNumber);
            var periodicity = row.IsPsychiatric
                ? MedPeriodicity.OnceOnHire
                : MedPeriodicity.Normalize(row.Periodicity);

            var status = row.Status;
            if (string.IsNullOrWhiteSpace(status))
            {
                status = row.ExamDate.HasValue ? MedExamStatuses.Passed : string.Empty;
            }

            if (!row.ExamDate.HasValue &&
                string.IsNullOrWhiteSpace(row.Conclusion) &&
                string.IsNullOrWhiteSpace(itemNumber) &&
                string.IsNullOrWhiteSpace(row.Id) &&
                !row.IsPsychiatric)
            {
                continue;
            }

            var logicalKey = MedStandardAssignmentBuilder.BuildExamKey(
                row.Organization, row.EmployeeName, examType, itemNumber);
            if (!seenKeys.Add(logicalKey))
            {
                continue;
            }

            var record = ResolveExamForSave(row.Id, logicalKey, examType, existing, existingByKey);
            var roster = _employees.FindByName(row.Organization, row.EmployeeName);
            record.Organization = row.Organization;
            record.Department = !string.IsNullOrWhiteSpace(roster?.Department)
                ? roster!.Department
                : row.Department;
            record.Profession = ProfessionMatcher.Normalize(
                !string.IsNullOrWhiteSpace(roster?.Profession)
                    ? roster!.Profession
                    : row.Profession);
            record.EmployeeName = row.EmployeeName;
            record.ExamType = examType;
            record.ItemNumber = itemNumber;
            record.Periodicity = row.IsPsychiatric
                ? MedPeriodicity.OnceOnHire
                : MedPeriodicity.ApplyUnder21Override(roster?.BirthDate ?? record.BirthDate, periodicity);
            record.Conclusion = row.Conclusion;
            if (string.Equals(row.Conclusion, "Не пройден", StringComparison.OrdinalIgnoreCase))
            {
                record.Status = "Не пройден";
            }
            else if (string.Equals(row.Conclusion, "Годен", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(row.Conclusion, "Ограниченно годен", StringComparison.OrdinalIgnoreCase))
            {
                record.Status = row.ExamDate.HasValue ? "Пройден" : status;
            }
            else if (string.Equals(row.Conclusion, "Не годен", StringComparison.OrdinalIgnoreCase))
            {
                record.Status = row.ExamDate.HasValue ? "Пройден" : status;
            }
            else
            {
                record.Status = status;
            }

            record.BirthDate = roster?.BirthDate ?? record.BirthDate;

            if (row.ExamDate.HasValue)
            {
                record.ExamDate = row.ExamDate.Value;
                record.NextExamDate = row.IsPsychiatric
                    ? null
                    : MedPeriodicity.NextExamDate(row.ExamDate.Value, record.Periodicity) ?? row.NextExamDate;
            }
            else
            {
                record.ExamDate = DateTime.MinValue;
                record.NextExamDate = row.IsPsychiatric ? null : row.NextExamDate;
            }

            toSave.Add(record);
        }

        if (toSave.Count == 0)
        {
            TempData["Message"] = "Нет изменений для сохранения.";
            return RedirectToPage(FilterRoute());
        }

        _med.UpsertExams(toSave);
        var purged = _exams.PurgeDuplicateExams();
        InvalidateRowsCache();
        TempData["Message"] = purged > 0
            ? $"Даты и медзаключения сохранены. Убрано дублей факторов: {purged}."
            : "Даты и медзаключения сохранены.";
        return RedirectToPage(FilterRoute());
    }

    private static MedExamRecord ResolveExamForSave(
        string id,
        string logicalKey,
        string examType,
        IReadOnlyDictionary<string, MedExamRecord> existing,
        IReadOnlyDictionary<string, MedExamRecord> existingByKey)
    {
        if (!string.IsNullOrWhiteSpace(id) &&
            existing.TryGetValue(id, out var found) &&
            ExamTypesMatch(found.ExamType, examType))
        {
            return found;
        }

        if (existingByKey.TryGetValue(logicalKey, out var byKey) &&
            ExamTypesMatch(byKey.ExamType, examType))
        {
            return byKey;
        }

        return new MedExamRecord { Id = ResolvePersistId(id) };
    }

    private static string ResolvePersistId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.StartsWith("plist-", StringComparison.OrdinalIgnoreCase))
        {
            return Guid.NewGuid().ToString("N");
        }

        return id;
    }

    private static bool ExamTypesMatch(string? left, string? right) =>
        string.Equals(
            MedStandardAssignmentBuilder.NormalizeExamType(left),
            MedStandardAssignmentBuilder.NormalizeExamType(right),
            StringComparison.OrdinalIgnoreCase);

    private sealed record ExamFormRow(
        string Id,
        string Organization,
        string Department,
        string Profession,
        string EmployeeName,
        string ExamType,
        string ItemNumber,
        string Periodicity,
        DateTime? ExamDate,
        DateTime? NextExamDate,
        string Conclusion,
        string Status,
        bool IsPsychiatric);

    private static List<ExamFormRow> ParseExamFormRows(IFormCollection form)
    {
        var ids = form["examId"];
        var count = ids.Count;
        var rows = new List<ExamFormRow>(count);
        for (var i = 0; i < count; i++)
        {
            var rawExamType = At(form["examType"], i);
            var isPsychiatric = string.Equals(At(form["isPsychiatric"], i), "true", StringComparison.OrdinalIgnoreCase) ||
                                MedStandardAssignmentBuilder.IsPsychiatricKind(rawExamType);
            rows.Add(new ExamFormRow(
                Id: At(ids, i),
                Organization: At(form["organization"], i),
                Department: At(form["department"], i),
                Profession: At(form["profession"], i),
                EmployeeName: At(form["employeeName"], i),
                ExamType: rawExamType,
                ItemNumber: At(form["itemNumber"], i),
                Periodicity: At(form["periodicity"], i),
                ExamDate: ParseDate(At(form["examDate"], i)),
                NextExamDate: ParseDate(At(form["nextExamDate"], i)),
                Conclusion: At(form["conclusion"], i),
                Status: At(form["status"], i),
                IsPsychiatric: isPsychiatric));
        }

        return rows;
    }

    public async Task<IActionResult> OnPostUploadConclusionScanAsync(
        string examId,
        string organization,
        string rowDepartment,
        string rowProfession,
        string employeeName,
        string rowExamType,
        string itemNumber,
        IFormFile? scanFile)
    {
        if (scanFile is null || scanFile.Length == 0)
        {
            TempData["Message"] = "Выберите файл скана (PDF или изображение).";
            return RedirectToPage(AfterImportRoute(examId));
        }

        var focusId = (examId ?? string.Empty).Trim();
        try
        {
            var record = ResolveExamRecord(
                examId, organization, rowDepartment, rowProfession, employeeName, rowExamType, itemNumber);
            focusId = record.Id;
            var oldScan = record.ConclusionScanFileName;

            await using var upload = scanFile.OpenReadStream();
            using var ms = new MemoryStream();
            await upload.CopyToAsync(ms);

            ms.Position = 0;
            var storedName = _scans.Save(record.Id, ms, scanFile.FileName);
            if (!string.IsNullOrWhiteSpace(oldScan) &&
                !string.Equals(oldScan, storedName, StringComparison.OrdinalIgnoreCase))
            {
                _scans.Delete(oldScan);
            }

            record.ConclusionScanFileName = storedName;
            // Сначала сохраняем скан на строке загрузки — дальше даты по совпавшим факторам.
            _exams.Upsert(record);

            // PDF (текст или OCR-скан) / картинка: Годен/Не годен + дата выдачи заключения + факторы.
            ms.Position = 0;
            var (inferred, examDate, conclusionFactors) =
                RecognizeConclusionScan(ms, Path.GetExtension(scanFile.FileName ?? string.Empty));

            // Если из потока не вышло — ещё раз с уже сохранённого файла (OCR иногда капризничает на MemoryStream).
            if (examDate is null && string.IsNullOrWhiteSpace(inferred) && conclusionFactors.Count == 0)
            {
                var savedPath = _scans.GetFullPath(storedName);
                if (savedPath is not null)
                {
                    await using var saved = System.IO.File.OpenRead(savedPath);
                    using var retry = new MemoryStream();
                    await saved.CopyToAsync(retry);
                    retry.Position = 0;
                    (inferred, examDate, conclusionFactors) =
                        RecognizeConclusionScan(retry, Path.GetExtension(savedPath));
                    if (examDate is { Year: >= 1900 } || !string.IsNullOrWhiteSpace(inferred) || conclusionFactors.Count > 0)
                    {
                        ms.SetLength(0);
                        retry.Position = 0;
                        await retry.CopyToAsync(ms);
                    }
                }
            }

            var applyResult = (Filled: 0, Created: 0);
            if (examDate is { Year: >= 1900 } || !string.IsNullOrWhiteSpace(inferred) || conclusionFactors.Count > 0)
            {
                ms.Position = 0;
                applyResult = ApplyConclusionToMatchingFactors(
                    record, inferred, examDate, conclusionFactors, storedName, ms, scanFile.FileName);
            }

            InvalidateRowsCache();

            var parts = new List<string> { "Скан загружен" };
            if (!string.IsNullOrWhiteSpace(inferred))
            {
                parts.Add($"заключение «{inferred}»");
            }

            if (examDate is { Year: >= 1900 })
            {
                parts.Add($"дата выдачи заключения {examDate:dd.MM.yyyy}");
            }

            if (conclusionFactors.Count > 0)
            {
                parts.Add($"факторы из заключения: {string.Join(", ", conclusionFactors)}");
            }

            if (applyResult.Filled > 0)
            {
                parts.Add($"заполнено строк: {applyResult.Filled}");
            }

            if (applyResult.Created > 0)
            {
                parts.Add($"создано карточек факторов: {applyResult.Created}");
            }

            if (parts.Count > 1)
            {
                TempData["Message"] = string.Join(". ", parts) + ".";
            }
            else
            {
                TempData["Message"] =
                    "Скан загружен. Результат/дату не распознали — заполните вручную (нужен читаемый скан; на сервере — tesseract с русским).";
            }
        }
        catch (Exception ex)
        {
            TempData["Message"] = "Не удалось загрузить скан: " + ex.Message;
            TempData["IsError"] = "1";
        }

        // После импорта заключения фильтры не выставляем — полный отчёт; остаёмся у той же строки.
        return RedirectToPage(AfterImportRoute(focusId));
    }

    public IActionResult OnGetDownloadConclusionScan(string examId) =>
        OnGetPreviewConclusionScan(examId);

    /// <summary>Открыть скан в браузере (предпросмотр), без принудительного скачивания.</summary>
    public IActionResult OnGetPreviewConclusionScan(string examId)
    {
        if (string.IsNullOrWhiteSpace(examId))
        {
            return NotFound();
        }

        var record = _exams.LoadAll().FirstOrDefault(x => string.Equals(x.Id, examId, StringComparison.Ordinal));
        var path = _scans.GetFullPath(record?.ConclusionScanFileName);
        if (path is null)
        {
            return NotFound();
        }

        var fileName = Path.GetFileName(record!.ConclusionScanFileName);
        var contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };

        Response.Headers.ContentDisposition = $"inline; filename=\"{fileName}\"";
        return PhysicalFile(path, contentType);
    }

    public IActionResult OnPostDeleteConclusionScan(
        string examId,
        string organization,
        string rowDepartment,
        string rowProfession,
        string employeeName,
        string rowExamType,
        string itemNumber)
    {
        var record = ResolveExamRecord(
            examId, organization, rowDepartment, rowProfession, employeeName, rowExamType, itemNumber);
        if (!string.IsNullOrWhiteSpace(record.ConclusionScanFileName))
        {
            _scans.Delete(record.ConclusionScanFileName);
            record.ConclusionScanFileName = string.Empty;
            _exams.Upsert(record);
            InvalidateRowsCache();
            TempData["Message"] = "Скан заключения удалён.";
        }

        return RedirectToPage(AfterImportRoute(record.Id));
    }

    private MedExamRecord ResolveExamRecord(
        string examId,
        string organization,
        string department,
        string profession,
        string employeeName,
        string examType,
        string itemNumber)
    {
        var existing = _exams.LoadAll();
        var byId = existing.FirstOrDefault(x => string.Equals(x.Id, examId, StringComparison.Ordinal));
        if (byId is not null)
        {
            return byId;
        }

        examType = MedStandardAssignmentBuilder.NormalizeExamType(examType);
        itemNumber = MedItemNumber.Normalize(itemNumber);
        if (MedStandardAssignmentBuilder.IsPsychiatricKind(examType))
        {
            examType = "Психиатрическое освидетельствование";
            itemNumber = string.Empty;
        }

        var key = MedStandardAssignmentBuilder.BuildExamKey(organization, employeeName, examType, itemNumber);
        var byKey = existing
            .GroupBy(MedStandardAssignmentBuilder.BuildExamKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.ExamDate).First(), StringComparer.OrdinalIgnoreCase)
            .GetValueOrDefault(key);
        if (byKey is not null)
        {
            return byKey;
        }

        var persistId = string.IsNullOrWhiteSpace(examId) ||
                        examId.StartsWith("plist-", StringComparison.OrdinalIgnoreCase)
            ? Guid.NewGuid().ToString("N")
            : examId;

        var roster = _employees.FindByName(organization, employeeName);
        return new MedExamRecord
        {
            Id = persistId,
            Organization = organization,
            Department = !string.IsNullOrWhiteSpace(roster?.Department) ? roster!.Department : department,
            Profession = ProfessionMatcher.Normalize(
                !string.IsNullOrWhiteSpace(roster?.Profession) ? roster!.Profession : profession),
            EmployeeName = employeeName,
            ExamType = examType,
            ItemNumber = itemNumber,
            Periodicity = MedStandardAssignmentBuilder.IsPsychiatricKind(examType)
                ? MedPeriodicity.OnceOnHire
                : MedPeriodicity.OnceAYear,
            Status = string.Empty,
            BirthDate = roster?.BirthDate
        };
    }

    private static (string? Conclusion, DateTime? ExamDate, IReadOnlyList<string> ItemNumbers)
        RecognizeConclusionScan(Stream content, string? extension)
    {
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        var r = MedConclusionScanReader.Read(content, extension);
        return (r.Conclusion, r.IssueDate, r.ItemNumbers);
    }

    /// <summary>
    /// Проставляет заключение/дату:
    /// 1) всегда в строку, куда загрузили скан;
    /// 2) во все совпавшие факторы отчёта;
    /// 3) для пунктов из заключения, которых ещё нет — создаёт карточки
    ///    с данными строки загрузки (организация, ФИО, профессия, тип МО…).
    /// Если пункты в PDF не распознаны — заполняет все факторы человека (кроме психиатрии).
    /// </summary>
    private (int Filled, int Created) ApplyConclusionToMatchingFactors(
        MedExamRecord primary,
        string? inferredConclusion,
        DateTime? examDate,
        IReadOnlyList<string> conclusionFactors,
        string primaryScanFileName,
        MemoryStream scanContent,
        string originalFileName)
    {
        var factorList = (conclusionFactors ?? [])
            .Select(MedItemNumber.Normalize)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var matchSet = new HashSet<string>(factorList, StringComparer.OrdinalIgnoreCase);

        var all = CollectPersonNonPsychiatricExams(primary)
            .Select(e =>
                string.Equals(e.Id, primary.Id, StringComparison.OrdinalIgnoreCase) ? primary : e)
            .ToList();
        if (!all.Any(e => string.Equals(e.Id, primary.Id, StringComparison.OrdinalIgnoreCase)))
        {
            all.Add(primary);
        }

        // Строка загрузки — всегда. Если у неё нет пункта, а в заключении есть —
        // берём первый фактор заключения на эту строку (не плодим пустую + ту же карточку).
        if (string.IsNullOrWhiteSpace(MedItemNumber.Normalize(primary.ItemNumber))
            && factorList.Count > 0
            && !MedStandardAssignmentBuilder.IsPsychiatricKind(primary.ExamType))
        {
            primary.ItemNumber = factorList[0];
        }

        var targets = new List<MedExamRecord> { primary };
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { primary.Id };
        var coveredFactors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var primaryItem = MedItemNumber.Normalize(primary.ItemNumber);
        if (primaryItem.Length > 0)
        {
            coveredFactors.Add(primaryItem);
        }

        foreach (var e in all)
        {
            if (string.Equals(e.Id, primary.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var item = MedItemNumber.Normalize(e.ItemNumber);
            var take = matchSet.Count == 0
                || (item.Length > 0 && matchSet.Contains(item));
            if (!take || !seenIds.Add(e.Id))
            {
                continue;
            }

            targets.Add(e);
            if (item.Length > 0)
            {
                coveredFactors.Add(item);
            }
        }

        var created = 0;
        // Пункты из заключения, которых ещё нет у человека — новые карточки.
        if (matchSet.Count > 0)
        {
            var examTypeForCreate = MedStandardAssignmentBuilder.IsPsychiatricKind(primary.ExamType)
                ? "Периодический"
                : MedStandardAssignmentBuilder.NormalizeExamType(primary.ExamType);
            if (string.IsNullOrWhiteSpace(examTypeForCreate)
                || examTypeForCreate.Contains("предваритель", StringComparison.OrdinalIgnoreCase))
            {
                // Предварительное заключение обычно кладём в периодический ряд отчёта.
                examTypeForCreate = "Периодический";
            }

            foreach (var factor in factorList)
            {
                if (coveredFactors.Contains(factor))
                {
                    continue;
                }

                var createdExam = new MedExamRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Organization = primary.Organization,
                    Department = primary.Department,
                    Profession = primary.Profession,
                    EmployeeName = primary.EmployeeName,
                    ExamType = examTypeForCreate,
                    ItemNumber = factor,
                    Periodicity = string.IsNullOrWhiteSpace(primary.Periodicity)
                        ? MedPeriodicity.OnceAYear
                        : primary.Periodicity,
                    BirthDate = primary.BirthDate,
                    Status = string.Empty
                };
                targets.Add(createdExam);
                coveredFactors.Add(factor);
                created++;
            }
        }

        var batch = new List<MedExamRecord>(targets.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            ApplyConclusionFields(target, inferredConclusion, examDate);

            if (string.Equals(target.Id, primary.Id, StringComparison.OrdinalIgnoreCase))
            {
                target.ConclusionScanFileName = primaryScanFileName;
            }
            else
            {
                try
                {
                    scanContent.Position = 0;
                    var oldScan = target.ConclusionScanFileName;
                    var siblingScan = _scans.Save(target.Id, scanContent, originalFileName);
                    if (!string.IsNullOrWhiteSpace(oldScan)
                        && !string.Equals(oldScan, siblingScan, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(oldScan, primaryScanFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        _scans.Delete(oldScan);
                    }

                    target.ConclusionScanFileName = siblingScan;
                }
                catch
                {
                    // Дата/заключение важнее скана — продолжаем без копии файла.
                }
            }

            if (seen.Add(target.Id) || string.IsNullOrWhiteSpace(target.Id))
            {
                batch.Add(target);
            }
        }

        _exams.UpsertMany(batch);
        return (batch.Count, created);
    }

    private List<MedExamRecord> CollectPersonNonPsychiatricExams(MedExamRecord primary)
    {
        var org = (primary.Organization ?? string.Empty).Trim();
        var name = (primary.EmployeeName ?? string.Empty).Trim();
        if (org.Length == 0 || name.Length == 0)
        {
            return [];
        }

        var employeePairs = _employees.LoadPairsForMedReport([org]).ToList();
        var exams = _exams.LoadByOrganization(org);
        var standards = _standards.LoadByOrganization(org);
        var expanded = MedStandardAssignmentBuilder.CollapseDuplicateExams(
            MedStandardAssignmentBuilder.ExpandExamsFromStandards(employeePairs, exams, standards));

        return expanded
            .Where(e => MedOrganizationFilter.Matches(org, e.Organization)
                        || OrganizationMatcher.Matches(org, e.Organization))
            .Where(e => EmployeeNameMatcher.Matches(e.EmployeeName, name))
            .Where(e => !MedStandardAssignmentBuilder.IsPsychiatricKind(e.ExamType))
            .ToList();
    }

    private static void ApplyConclusionFields(
        MedExamRecord record,
        string? inferredConclusion,
        DateTime? examDate)
    {
        if (examDate is { Year: >= 1900 })
        {
            record.ExamDate = examDate.Value.Date;
            record.RecalculateNextExamDate();
        }

        if (string.IsNullOrWhiteSpace(inferredConclusion))
        {
            return;
        }

        record.Conclusion = inferredConclusion;
        if (string.Equals(inferredConclusion, "Не пройден", StringComparison.OrdinalIgnoreCase))
        {
            record.Status = "Не пройден";
        }
        else if (record.ExamDate.Year >= 1900)
        {
            record.Status = "Пройден";
        }
    }

    private object FilterRoute() => new
    {
        reportDate = ReportDate.ToString("yyyy-MM-dd"),
        selectedOrganization = SelectedOrganization,
        department = Department,
        profession = Profession,
        examType = ExamType,
        statusFilter = StatusFilter
    };

    /// <summary>После загрузки/удаления скана — без фильтров; focusExamId — вернуться к строке.</summary>
    private object AfterImportRoute(string? focusExamId = null) => new
    {
        reportDate = ReportDate.ToString("yyyy-MM-dd"),
        selectedOrganization = SelectedOrganization,
        focusExamId = string.IsNullOrWhiteSpace(focusExamId) ? null : focusExamId.Trim()
    };

    private void LoadReport()
    {
        ReportDate = DateTime.Today;
        string? selectedOrganization = null;
        _orgs.ApplyReportOrganizationFilter(HttpContext, ref selectedOrganization, allowAll: true);
        SelectedOrganization = selectedOrganization;
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        OrganizationOptions = _orgs.GetVisibleOrganizations(HttpContext);
        ViewData["ActiveModule"] = "медосмотры";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Отчет по медосмотрам";

        var selectedOrg = SelectedOrganization?.Trim() ?? string.Empty;
        ViewData["Subtitle"] = string.IsNullOrWhiteSpace(selectedOrg) ? "Все организации" : selectedOrg;
        ViewData["Organization"] = string.IsNullOrWhiteSpace(selectedOrg) ? Organization : selectedOrg;

        var orgsToLoad = string.IsNullOrWhiteSpace(selectedOrg)
            ? _employees.ResolveOrganizationsForAllReports(
                OrganizationOptions,
                _orgs.IsOrganizationHidden).ToList()
            : [selectedOrg];

        var employeePairs = _employees.LoadPairsForMedReport(orgsToLoad).ToList();

        var cacheKey = $"{selectedOrg}|{ReportDate:yyyy-MM-dd}|{employeePairs.Count}";

        List<MedStatusRow> allRows;
        lock (RowsCacheGate)
        {
            if (_rowsCache is not null
                && _hrCache is not null
                && string.Equals(_rowsCacheKey, cacheKey, StringComparison.Ordinal)
                && DateTime.UtcNow - _rowsCacheUtc < TimeSpan.FromMinutes(5))
            {
                allRows = _rowsCache;
                HrStatusByName = new Dictionary<string, string>(_hrCache, StringComparer.OrdinalIgnoreCase);
                StandardsMissing = _standardsMissingCache;
            }
            else
            {
                HrStatusByName = EmployeeHrStatus.BuildMap(employeePairs.Select(p => p.Employee));

                var examsBefore = string.IsNullOrWhiteSpace(selectedOrg)
                    ? _exams.LoadAll()
                    : _exams.LoadByOrganization(selectedOrg);
                var exams = MergePersonListFallbackExams(examsBefore, selectedOrg);

                var standards = string.IsNullOrWhiteSpace(selectedOrg)
                    ? _standards.LoadAll()
                    : _standards.LoadByOrganization(selectedOrg);
                StandardsMissing = standards.Count == 0;
                var synced = MedStandardAssignmentBuilder.CollapseDuplicateExams(
                    MedStandardAssignmentBuilder.ExpandExamsFromStandards(employeePairs, exams, standards));

                var directionKeys = MedDirectionMatcher.BuildIssuedKeysForOrganizations(
                    _directions.LoadAll(),
                    orgsToLoad);

                var roster = EmployeeRosterIndex.Build(employeePairs);
                allRows = MedStatusReportCleanup.Clean(
                    MedStatusBuilder.Build(employeePairs, synced, ReportDate.Date, directionKeys),
                    roster);

                _rowsCacheKey = cacheKey;
                _rowsCacheUtc = DateTime.UtcNow;
                _rowsCache = allRows;
                _hrCache = new Dictionary<string, string>(HrStatusByName, StringComparer.OrdinalIgnoreCase);
                _standardsMissingCache = StandardsMissing;
            }
        }

        DepartmentOptions = DistinctSorted(allRows.Select(r => r.Department));
        ProfessionOptions = DistinctSorted(allRows.Select(r => ProfessionMatcher.Normalize(r.Profession)));
        ExamTypeOptions = DistinctSorted(allRows.Select(r => r.ExamType));

        Rows = allRows
            .Where(r => string.IsNullOrWhiteSpace(Department) ||
                        r.Department.Contains(Department, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrWhiteSpace(Profession) ||
                        ProfessionMatcher.Matches(Profession, r.Profession) ||
                        r.Profession.Contains(Profession, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrWhiteSpace(ExamType) ||
                        r.ExamType.Contains(ExamType, StringComparison.OrdinalIgnoreCase))
            .Where(r => MatchesStatusFilter(r, StatusFilter, ReportDate.Date))
            .ToList();

        TreeLines = MedStatusBuilder.BuildTree(Rows);
        // Один счётчик со всеми отчётами: штат человек×ЮЛ (не строки осмотров).
        PeopleCount = StaffHeadcount.CountPersonOrgCards(
            employeePairs
                .Where(p => string.IsNullOrWhiteSpace(Department)
                            || (p.Employee.Department ?? string.Empty)
                                .Contains(Department, StringComparison.OrdinalIgnoreCase))
                .Where(p => string.IsNullOrWhiteSpace(Profession)
                            || ProfessionMatcher.Matches(Profession, p.Employee.Profession)
                            || (p.Employee.Profession ?? string.Empty)
                                .Contains(Profession, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Фильтр статуса. Важно: не использовать Contains —
    /// «Не пройден» содержит подстроку «пройден».
    /// </summary>
    private static bool MatchesStatusFilter(MedStatusRow row, string? filter, DateTime asOf)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        var display = (row.DisplayStatus ?? string.Empty).Trim();
        var status = (row.Status ?? string.Empty).Trim();
        var conclusion = (row.Conclusion ?? string.Empty).Trim();

        if (string.Equals(filter, MedExamStatuses.Passed, StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(display, MedExamStatuses.Passed, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(filter, MedExamStatuses.NotPassed, StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(display, MedExamStatuses.NotPassed, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(status, MedExamStatuses.NotPassed, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(conclusion, MedExamStatuses.NotPassed, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(filter, MedExamStatuses.Planned, StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(display, MedExamStatuses.Planned, StringComparison.OrdinalIgnoreCase);
        }

        if (MedExamStatuses.IsOverdueFilter(filter))
        {
            return string.Equals(display, MedExamStatuses.Overdue, StringComparison.OrdinalIgnoreCase)
                   || MedStatusTraffic.IsOverdue(row, asOf);
        }

        return string.Equals(display, filter, StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, filter, StringComparison.OrdinalIgnoreCase)
               || string.Equals(conclusion, filter, StringComparison.OrdinalIgnoreCase);
    }

    private List<MedExamRecord> MergePersonListFallbackExams(
        IReadOnlyList<MedExamRecord> exams,
        string selectedOrg)
    {
        var merged = MedStandardAssignmentBuilder.CollapseDuplicateExams(exams).ToList();
        var keys = new HashSet<string>(
            merged.Select(MedStandardAssignmentBuilder.BuildExamKey),
            StringComparer.OrdinalIgnoreCase);

        var docs = string.IsNullOrWhiteSpace(selectedOrg)
            ? _personLists.LoadAll()
            : _personLists.LoadByOrganization(selectedOrg);

        foreach (var doc in docs)
        {
            foreach (var line in doc.Lines)
            {
                var name = line.EmployeeName?.Trim() ?? string.Empty;
                var item = MedItemNumber.Normalize(line.ItemNumber);
                if (!EmployeeNameMatcher.IsPlausiblePersonName(name) || string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                var examType = MedStandardAssignmentBuilder.NormalizeExamType(doc.ExamType);
                var key = MedStandardAssignmentBuilder.BuildExamKey(doc.Organization, name, examType, item);
                if (!keys.Add(key))
                {
                    continue;
                }

                // Не добавляем предварительный/психиатрию, если периодический уже есть.
                if ((MedStandardAssignmentBuilder.IsPreliminaryKind(examType) ||
                     MedStandardAssignmentBuilder.IsPsychiatricKind(examType)) &&
                    keys.Contains(MedStandardAssignmentBuilder.BuildExamKey(
                        doc.Organization, name, "Периодический", item)))
                {
                    continue;
                }

                merged.Add(new MedExamRecord
                {
                    Id = $"plist-{doc.Id}-{line.Id}",
                    Organization = doc.Organization,
                    EmployeeName = name,
                    Profession = ProfessionMatcher.Normalize(line.Profession ?? string.Empty),
                    Department = line.Workplace,
                    ExamType = examType,
                    ExamDate = doc.Date.Year >= 1900 ? doc.Date.Date : DateTime.MinValue,
                    Status = string.Empty,
                    ItemNumber = item,
                    Periodicity = MedPeriodicity.Normalize(line.Periodicity),
                    BirthDate = line.BirthDate,
                    Conclusion = string.Empty
                });
            }
        }

        return MedStandardAssignmentBuilder.CollapseDuplicateExams(merged);
    }

    private static string At(Microsoft.Extensions.Primitives.StringValues values, int index) =>
        index < values.Count ? values[index].ToString()?.Trim() ?? string.Empty : string.Empty;

    private static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
        {
            return iso.Date;
        }

        if (DateTime.TryParseExact(text.Trim(), "dd.MM.yyyy", CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out var ru))
        {
            return ru.Date;
        }

        return DateTime.TryParse(text, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out var any)
            ? any.Date
            : null;
    }

    private static List<string> DistinctSorted(IEnumerable<string> values) =>
        values
            .Where(v => !string.IsNullOrWhiteSpace(v) && v != "—")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}
