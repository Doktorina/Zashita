using System.Text.Json;
using System.Text.RegularExpressions;
using Zashita.Core.Employees;

namespace Zashita.Core.Med;

/// <summary>
/// Импорт заключений медосмотра из Word → строки отчёта по медосмотрам.
/// </summary>
public sealed class MedConclusionDocxImportService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static readonly TimeSpan DraftTtl = TimeSpan.FromHours(8);

    private readonly MedDataPaths _paths;
    private readonly MedExamStore _exams;
    private readonly MedStandardStore _standards;

    public MedConclusionDocxImportService(
        MedDataPaths paths,
        MedExamStore exams,
        MedStandardStore standards)
    {
        _paths = paths;
        _exams = exams;
        _standards = standards;
    }

    public MedConclusionImportDraft CreateDraftFromFiles(
        string organization,
        IReadOnlyList<(Stream Stream, string FileName)> files,
        IReadOnlyList<OrganizationEmployee> employees)
    {
        var all = new List<ParsedMedConclusion>();
        var names = new List<string>();
        foreach (var (stream, fileName) in files)
        {
            all.AddRange(ParseAny(stream, fileName));
            names.Add(Path.GetFileName(fileName));
        }

        return CreateDraftFromConclusions(
            organization,
            all,
            employees,
            names.Count == 0 ? "файлы" : string.Join(", ", names.Take(5)) + (names.Count > 5 ? "…" : ""));
    }

    /// <summary>Разбор .docx или .pdf с заключениями.</summary>
    public static IReadOnlyList<ParsedMedConclusion> ParseAny(Stream stream, string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? string.Empty);
        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return MedConclusionPdfImportParser.Parse(stream, fileName);
        }

        return MedConclusionDocxImportParser.Parse(stream, fileName);
    }

    public MedConclusionImportApplyResult ImportFilesAuto(
        string organization,
        IReadOnlyList<(Stream Stream, string FileName)> files,
        bool overwriteExisting,
        IReadOnlyList<OrganizationEmployee> employees)
    {
        var draft = CreateDraftFromFiles(organization, files, employees);
        if (draft.Items.Count == 0)
        {
            return new MedConclusionImportApplyResult
            {
                Messages =
                [
                    "В файлах не найдено заключений медосмотра. Нужен PDF/Word с текстом формы «ЗАКЛЮЧЕНИЕ … МЕДИЦИНСКОГО ОСМОТРА» и заполненным ФИО."
                ]
            };
        }

        var selected = new HashSet<string>(draft.Items.Select(i => i.ItemKey), StringComparer.OrdinalIgnoreCase);
        var overwrite = overwriteExisting
            ? new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(
                draft.Items.Where(i => !i.HasExistingData).Select(i => i.ItemKey),
                StringComparer.OrdinalIgnoreCase);

        var result = Apply(draft, selected, overwrite, employees);
        result.Messages.Insert(0, $"Файлов: {files.Count}, заключений: {draft.Items.Count}.");
        DeleteDraft(draft.Id);
        return result;
    }

    public MedConclusionImportDraft? LoadDraft(string draftId)
    {
        if (string.IsNullOrWhiteSpace(draftId))
        {
            return null;
        }

        var path = DraftPath(draftId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<MedConclusionImportDraft>(File.ReadAllText(path), JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    public void SaveDraft(MedConclusionImportDraft draft)
    {
        Directory.CreateDirectory(_paths.ConclusionImportDraftsFolder);
        File.WriteAllText(DraftPath(draft.Id), JsonSerializer.Serialize(draft, JsonOpts));
    }

    public void DeleteDraft(string draftId)
    {
        var path = DraftPath(draftId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public MedConclusionImportApplyResult Apply(
        MedConclusionImportDraft draft,
        IReadOnlySet<string> selectedKeys,
        IReadOnlySet<string> overwriteKeys,
        IReadOnlyList<OrganizationEmployee> employees)
    {
        var result = new MedConclusionImportApplyResult();
        var org = draft.Organization?.Trim() ?? string.Empty;
        var orgExams = _exams.LoadByOrganization(org).ToList();
        var standards = _standards.LoadByOrganization(org);
        var employeePairs = employees
            .Select(e => (org, e))
            .ToList();
        var reportRows = MedStandardAssignmentBuilder.CollapseDuplicateExams(
            MedStandardAssignmentBuilder.ExpandExamsFromStandards(employeePairs, orgExams, standards));
        var toUpsert = new List<MedExamRecord>();
        var upsertedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in draft.Items)
        {
            if (!selectedKeys.Contains(item.ItemKey))
            {
                result.Skipped++;
                continue;
            }

            var card = item.Conclusion;
            card.EmployeeName = MedConclusionDocxImportParser.CleanImportedPersonName(card.EmployeeName);
            if (string.IsNullOrWhiteSpace(card.EmployeeName))
            {
                result.Skipped++;
                result.Messages.Add("Пропуск: в заключении нет ФИО.");
                continue;
            }

            var employee = FindEmployee(card, employees);
            // В отчёт всегда ФИО из штата, если нашли; иначе очищенное из файла.
            var personName = employee?.Name?.Trim() ?? card.EmployeeName.Trim();
            var profession = !string.IsNullOrWhiteSpace(employee?.Profession)
                ? employee!.Profession.Trim()
                : card.Profession;
            var department = !string.IsNullOrWhiteSpace(employee?.Department)
                ? employee!.Department.Trim()
                : card.Department;

            var personExams = orgExams
                .Where(e => EmployeeNameMatcher.Matches(e.EmployeeName, personName))
                .ToList();

            if (item.HasExistingData && !overwriteKeys.Contains(item.ItemKey) && !item.OverwriteExisting)
            {
                var dated = personExams.Count(e => e.ExamDate.Year >= 2000 && !string.IsNullOrWhiteSpace(e.Conclusion));
                if (dated > 0)
                {
                    result.Skipped++;
                    result.Messages.Add($"{personName}: уже есть данные — отметьте «перезаписать».");
                    continue;
                }
            }

            var personReport = reportRows
                .Where(e => EmployeeNameMatcher.Matches(e.EmployeeName, personName))
                .Where(e => !MedStandardAssignmentBuilder.IsPsychiatricKind(e.ExamType))
                .ToList();

            var reportFactorSet = new HashSet<string>(
                personReport
                    .Select(e => MedItemNumber.Normalize(e.ItemNumber))
                    .Where(x => x.Length > 0),
                StringComparer.OrdinalIgnoreCase);

            var conclusionFactors = card.ItemNumbers
                .Select(MedItemNumber.Normalize)
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Совпадение факторов заключения и строк отчёта.
            List<string> factors;
            if (conclusionFactors.Count > 0 && reportFactorSet.Count > 0)
            {
                factors = conclusionFactors
                    .Where(f => reportFactorSet.Contains(f))
                    .ToList();
                if (factors.Count == 0)
                {
                    result.Skipped++;
                    result.Messages.Add(
                        $"{personName}: факторы из заключения ({string.Join(", ", conclusionFactors)}) не совпали с отчётом.");
                    continue;
                }
            }
            else if (conclusionFactors.Count > 0)
            {
                // В отчёте ещё нет строк — пишем по факторам заключения (тип — из отчёта/стандарта, не «Предварительный»).
                factors = conclusionFactors;
            }
            else if (reportFactorSet.Count > 0)
            {
                // В заключении пункты не распознаны — заполняем все факторы человека в отчёте.
                factors = reportFactorSet.ToList();
            }
            else
            {
                factors = [string.Empty];
            }

            var preferredExamType = MedStandardAssignmentBuilder.NormalizeExamType(card.ExamType);
            var conclusion = string.IsNullOrWhiteSpace(card.Conclusion)
                ? MedConclusionDocxImportParser.MapConclusion(card.ResultText)
                : card.Conclusion;
            var examDate = card.ConclusionDate ?? DateTime.Today;
            var updated = 0;
            var created = 0;

            foreach (var factor in factors)
            {
                var itemNorm = MedItemNumber.Normalize(factor);
                var existing = FindReportExamForFactor(personReport, personExams, itemNorm, preferredExamType);

                if (existing is not null)
                {
                    ApplyToExam(
                        existing,
                        org,
                        personName,
                        profession,
                        department,
                        preferredExamType,
                        itemNorm,
                        examDate,
                        conclusion,
                        card.BirthDate,
                        preserveExamType: true);
                    if (string.IsNullOrWhiteSpace(existing.Id) || upsertedIds.Add(existing.Id))
                    {
                        toUpsert.Add(existing);
                    }

                    if (!personExams.Contains(existing))
                    {
                        personExams.Add(existing);
                        orgExams.Add(existing);
                    }

                    updated++;
                }
                else
                {
                    // Не создаём «лишний» пункт только из заключения, если отчёт уже знает другие факторы.
                    if (!string.IsNullOrWhiteSpace(itemNorm) &&
                        reportFactorSet.Count > 0 &&
                        !reportFactorSet.Contains(itemNorm))
                    {
                        continue;
                    }

                    var examTypeForCreate = ResolveExamTypeForCreate(personReport, preferredExamType);
                    var createdExam = new MedExamRecord
                    {
                        Organization = org,
                        EmployeeName = personName,
                        Profession = ProfessionMatcher.Normalize(profession),
                        Department = department?.Trim() ?? string.Empty,
                        ExamType = examTypeForCreate,
                        ItemNumber = itemNorm,
                        Periodicity = MedPeriodicity.OnceAYear,
                        ExamDate = examDate.Date,
                        Conclusion = conclusion,
                        BirthDate = card.BirthDate,
                        Status = StatusFromConclusion(conclusion)
                    };
                    createdExam.RecalculateNextExamDate();
                    toUpsert.Add(createdExam);
                    orgExams.Add(createdExam);
                    personExams.Add(createdExam);
                    personReport.Add(createdExam);
                    created++;
                }
            }

            result.Applied += updated;
            result.Created += created;
            result.Messages.Add(
                $"{personName}: обновлено {updated}, создано {created} · {conclusion} · {examDate:dd.MM.yyyy}" +
                (factors.Count > 0 && factors[0].Length > 0
                    ? $" · факторы {string.Join(", ", factors)}"
                    : string.Empty));
        }

        if (toUpsert.Count > 0)
        {
            _exams.UpsertMany(toUpsert);
        }

        return result;
    }

    /// <summary>
    /// Находит строку отчёта по пункту фактора.
    /// Предварительное заключение не должно уводить дату в отдельный «Предварительный» ряд —
    /// пишем в периодическую (или уже существующую) строку отчёта.
    /// </summary>
    private static MedExamRecord? FindReportExamForFactor(
        IReadOnlyList<MedExamRecord> personReport,
        IReadOnlyList<MedExamRecord> personExams,
        string itemNorm,
        string preferredExamType)
    {
        static IEnumerable<MedExamRecord> ByItem(IEnumerable<MedExamRecord> source, string item) =>
            source.Where(e =>
                !MedStandardAssignmentBuilder.IsPsychiatricKind(e.ExamType) &&
                MedItemNumber.EqualsNormalized(e.ItemNumber, item));

        if (!string.IsNullOrWhiteSpace(itemNorm))
        {
            var fromReport = PreferExam(ByItem(personReport, itemNorm), preferredExamType);
            if (fromReport is not null)
            {
                return fromReport;
            }

            var fromStore = PreferExam(ByItem(personExams, itemNorm), preferredExamType);
            if (fromStore is not null)
            {
                return fromStore;
            }

            return null;
        }

        return PreferExam(
            personReport.Where(e => !MedStandardAssignmentBuilder.IsPsychiatricKind(e.ExamType)),
            preferredExamType)
            ?? PreferExam(
                personExams.Where(e => !MedStandardAssignmentBuilder.IsPsychiatricKind(e.ExamType)),
                preferredExamType);
    }

    private static MedExamRecord? PreferExam(IEnumerable<MedExamRecord> candidates, string preferredExamType)
    {
        var list = candidates.ToList();
        if (list.Count == 0)
        {
            return null;
        }

        var preferred = MedStandardAssignmentBuilder.NormalizeExamType(preferredExamType);
        var exact = list.FirstOrDefault(e =>
            string.Equals(
                MedStandardAssignmentBuilder.NormalizeExamType(e.ExamType),
                preferred,
                StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        // Заключение «предварительное» → строка отчёта обычно «периодическая».
        var periodic = list.FirstOrDefault(e =>
            MedStandardAssignmentBuilder.IsStrictPeriodicKind(e.ExamType));
        if (periodic is not null)
        {
            return periodic;
        }

        return list[0];
    }

    private static string ResolveExamTypeForCreate(
        IReadOnlyList<MedExamRecord> personReport,
        string preferredExamType)
    {
        var fromReport = personReport
            .Where(e => !MedStandardAssignmentBuilder.IsPsychiatricKind(e.ExamType))
            .Select(e => MedStandardAssignmentBuilder.NormalizeExamType(e.ExamType))
            .FirstOrDefault(t => MedStandardAssignmentBuilder.IsStrictPeriodicKind(t));
        if (!string.IsNullOrWhiteSpace(fromReport))
        {
            return fromReport!;
        }

        // Не плодим «Предварительный», если в отчёте уже есть любой другой вид.
        var any = personReport
            .Where(e => !MedStandardAssignmentBuilder.IsPsychiatricKind(e.ExamType))
            .Select(e => MedStandardAssignmentBuilder.NormalizeExamType(e.ExamType))
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(any))
        {
            return any!;
        }

        // Нет строк отчёта: предварительное заключение всё равно кладём как периодический ряд —
        // иначе Expand создаст пустой периодический поверх датированного предварительного.
        if (MedStandardAssignmentBuilder.IsPreliminaryKind(preferredExamType))
        {
            return "Периодический";
        }

        return MedStandardAssignmentBuilder.NormalizeExamType(preferredExamType);
    }

    private MedConclusionImportDraft CreateDraftFromConclusions(
        string organization,
        IReadOnlyList<ParsedMedConclusion> conclusions,
        IReadOnlyList<OrganizationEmployee> employees,
        string sourceFileName)
    {
        var orgExams = _exams.LoadByOrganization(organization);
        var draft = new MedConclusionImportDraft
        {
            Organization = organization,
            SourceFileName = sourceFileName
        };

        foreach (var card in conclusions)
        {
            card.EmployeeName = MedConclusionDocxImportParser.CleanImportedPersonName(card.EmployeeName);
            var employee = FindEmployee(card, employees);
            var personName = employee?.Name?.Trim() ?? card.EmployeeName.Trim();
            if (employee is not null)
            {
                card.EmployeeName = employee.Name.Trim();
            }

            var personExams = orgExams
                .Where(e => EmployeeNameMatcher.Matches(e.EmployeeName, personName))
                .ToList();
            var hasData = personExams.Any(e =>
                e.ExamDate.Year >= 2000 && !string.IsNullOrWhiteSpace(e.Conclusion));

            draft.Items.Add(new MedConclusionImportDraftItem
            {
                Conclusion = card,
                Selected = employee is not null || !string.IsNullOrWhiteSpace(card.EmployeeName),
                MatchedEmployeeName = employee?.Name,
                MatchNote = employee is null
                    ? (string.IsNullOrWhiteSpace(card.EmployeeName)
                        ? "Нет ФИО в файле"
                        : $"ФИО из файла: {card.EmployeeName} (в штате не найден — запись по имени из заключения)")
                    : $"Штат: {employee.Name}",
                HasExistingData = hasData,
                MatchedExamCount = personExams.Count
            });
        }

        SaveDraft(draft);
        CleanupOldDrafts();
        return draft;
    }

    private static void ApplyToExam(
        MedExamRecord exam,
        string organization,
        string personName,
        string? profession,
        string? department,
        string examType,
        string itemNumber,
        DateTime examDate,
        string conclusion,
        DateTime? birthDate,
        bool preserveExamType = false)
    {
        exam.Organization = organization;
        exam.EmployeeName = personName;
        if (!string.IsNullOrWhiteSpace(profession))
        {
            exam.Profession = ProfessionMatcher.Normalize(profession);
        }

        if (!string.IsNullOrWhiteSpace(department))
        {
            exam.Department = department.Trim();
        }

        // Не переписываем вид осмотра строки отчёта (периодический) на «предварительный» из бланка.
        if (!preserveExamType || string.IsNullOrWhiteSpace(exam.ExamType))
        {
            exam.ExamType = MedStandardAssignmentBuilder.NormalizeExamType(examType);
        }

        if (!string.IsNullOrWhiteSpace(itemNumber))
        {
            exam.ItemNumber = itemNumber;
        }

        exam.ExamDate = examDate.Date;
        exam.Conclusion = conclusion;
        exam.Status = StatusFromConclusion(conclusion);
        if (birthDate is not null)
        {
            exam.BirthDate = birthDate;
        }

        exam.RecalculateNextExamDate();
    }

    private static string StatusFromConclusion(string conclusion)
    {
        if (string.Equals(conclusion, "Не годен", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(conclusion, "Не пройден", StringComparison.OrdinalIgnoreCase))
        {
            return MedExamStatuses.NotPassed;
        }

        if (string.IsNullOrWhiteSpace(conclusion))
        {
            return MedExamStatuses.Planned;
        }

        return MedExamStatuses.Passed;
    }

    private static OrganizationEmployee? FindEmployee(
        ParsedMedConclusion card,
        IReadOnlyList<OrganizationEmployee> employees)
    {
        if (string.IsNullOrWhiteSpace(card.EmployeeName))
        {
            return null;
        }

        var matches = employees
            .Where(e => EmployeeNameMatcher.Matches(e.Name, card.EmployeeName))
            .ToList();
        if (matches.Count == 0)
        {
            return null;
        }

        if (matches.Count == 1 || string.IsNullOrWhiteSpace(card.Profession))
        {
            return matches[0];
        }

        var byProf = matches.FirstOrDefault(e =>
            ProfessionMatcher.Matches(e.Profession, card.Profession));
        return byProf ?? matches[0];
    }

    private string DraftPath(string draftId)
    {
        var safe = Regex.Replace(draftId, @"[^a-zA-Z0-9]", string.Empty);
        if (safe.Length == 0)
        {
            safe = Guid.NewGuid().ToString("N");
        }

        return Path.Combine(_paths.ConclusionImportDraftsFolder, $"{safe}.json");
    }

    private void CleanupOldDrafts()
    {
        try
        {
            var cutoff = DateTime.UtcNow - DraftTtl;
            foreach (var file in Directory.EnumerateFiles(_paths.ConclusionImportDraftsFolder, "*.json"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // ignore
        }
    }
}
