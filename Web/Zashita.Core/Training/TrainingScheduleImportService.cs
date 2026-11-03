using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Zashita.Core.Employees;

namespace Zashita.Core.Training;

public sealed class TrainingScheduleImportResult
{
    public int UpdatedCells { get; set; }
    public int MatchedOrganizations { get; set; }
    public int AutoAssignedStandards { get; set; }
    public int CreatedStandards { get; set; }
    public int EnrichedStandards { get; set; }
    public int PreservedFilledCells { get; set; }
    public int PersonExtrasUpdated { get; set; }
    public List<string> Warnings { get; } = [];
    public List<string> MissingInReport { get; } = [];
    public List<string> ExtraInReport { get; } = [];
    public List<string> ProfessionMismatch { get; } = [];
    public List<string> UnmatchedColumns { get; } = [];

    public string FormatMessage()
    {
        var sb = new StringBuilder();
        sb.Append($"Импорт графика: обновлено ячеек — {UpdatedCells}");
        if (MatchedOrganizations > 0)
        {
            sb.Append($", организаций — {MatchedOrganizations}");
        }

        if (CreatedStandards > 0)
        {
            sb.Append($", стандартов создано — {CreatedStandards}");
        }

        if (EnrichedStandards > 0)
        {
            sb.Append($", программ в стандарты — {EnrichedStandards}");
        }

        if (AutoAssignedStandards > 0)
        {
            sb.Append($", стандартов назначено — {AutoAssignedStandards}");
        }

        if (PreservedFilledCells > 0)
        {
            sb.Append($", уже заполненные не трогали — {PreservedFilledCells}");
        }

        sb.Append('.');

        // Длинные списки ФИО / «компания не найдена» не засоряют экран — только счётчики.
        AppendCount(sb, "нет в отчёте", MissingInReport.Count);
        AppendCount(sb, "лишние в отчёте", ExtraInReport.Count);
        AppendCount(sb, "профессия не совпала", ProfessionMismatch.Count);
        AppendCount(sb, "колонки без совпадения", UnmatchedColumns.Count);

        var companyNotFound = Warnings.Count(w =>
            w.Contains("Компания не найдена", StringComparison.OrdinalIgnoreCase));
        var otherWarnings = Warnings.Count - companyNotFound;
        AppendCount(sb, "листы без компании", companyNotFound);
        AppendCount(sb, "прочие замечания", otherWarnings);

        return sb.ToString();
    }

    private static void AppendCount(StringBuilder sb, string title, int count)
    {
        if (count <= 0)
            {
                return;
            }

            sb.Append(' ');
            sb.Append(title);
        sb.Append(" — ");
        sb.Append(count);
            sb.Append('.');
    }
}

/// <summary>
/// Импорт Excel-графика: сопоставляет компанию/человека/программу, пишет протоколы.
/// Уже существующие стандарты не меняет (только заполняет ячейки отчёта);
/// если стандарта нет — создаёт по колонкам файла и назначает.
/// </summary>
public sealed class TrainingScheduleImportService
{
    private readonly TrainingConductStore _conduct;
    private readonly TrainingStandardsStore _standards;
    private readonly TrainingPersonStandardAssignmentStore _assignments;
    private readonly TrainingKindStore _kinds;

    public TrainingScheduleImportService(
        TrainingConductStore conduct,
        TrainingStandardsStore standards,
        TrainingPersonStandardAssignmentStore assignments,
        TrainingKindStore kinds)
    {
        _conduct = conduct;
        _standards = standards;
        _assignments = assignments;
        _kinds = kinds;
    }

    public TrainingScheduleImportResult Import(
        TrainingScheduleExcelParseResult parsed,
        IReadOnlyList<string> organizationCatalog,
        Func<string, IReadOnlyList<OrganizationEmployee>> loadEmployees,
        Func<string, IReadOnlyList<TrainingStatusRow>> loadReportRows,
        bool forArchive = false,
        string? targetOrganization = null,
        bool overwriteExisting = false)
    {
        var result = new TrainingScheduleImportResult();
        foreach (var w in parsed.Warnings)
        {
            result.Warnings.Add(w);
        }

        if (parsed.Sheets.Count == 0)
        {
            return result;
        }

        var targetOrg = (targetOrganization ?? string.Empty).Trim();
        var list = _conduct.LoadAll();
        var kinds = _kinds.Load();
        var standards = _standards.LoadAll();
        // Стандарты, которые уже были до импорта — только назначение и ячейки, состав не трогаем.
        var preExistingStandardIds = standards
            .Select(s => s.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assignmentList = _assignments.LoadAll();
        var assignmentMap = assignmentList
            .GroupBy(a => TrainingReportBuilder.AssignmentKey(a.Organization, a.Person, a.Profession))
            .ToDictionary(g => g.Key, g => g.First().StandardId, StringComparer.OrdinalIgnoreCase);

        var kindProgramNames = kinds
            .Where(k => !k.IsGroup && !k.MarkedForDeletion && !string.IsNullOrWhiteSpace(k.Name))
            .Select(k => k.Name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var skippedOtherOrgs = 0;
        foreach (var sheet in parsed.Sheets)
        {
            var org = ResolveOrg(sheet, organizationCatalog);
            if (string.IsNullOrWhiteSpace(org))
            {
                result.Warnings.Add(
                    $"Компания не найдена: «{sheet.OrganizationHint}» (лист «{sheet.SheetName}»).");
                continue;
            }

            // Импорт только для открытого юрлица — иначе файл с десятками листов подвешивает систему.
            if (!string.IsNullOrWhiteSpace(targetOrg)
                && !IsSameOrganization(org, targetOrg))
            {
                skippedOtherOrgs++;
                continue;
            }

            result.MatchedOrganizations++;
            var employees = loadEmployees(org) ?? [];
            var reportRows = (loadReportRows(org) ?? []).ToList();
            ImportSheet(
                sheet,
                org,
                employees,
                reportRows,
                list,
                standards,
                assignmentMap,
                assignmentList,
                kinds,
                kindProgramNames,
                preExistingStandardIds,
                result,
                forArchive,
                overwriteExisting);
        }

        if (skippedOtherOrgs > 0)
        {
            result.Warnings.Add(
                string.IsNullOrWhiteSpace(targetOrg)
                    ? $"пропущено листов других организаций — {skippedOtherOrgs}"
                    : $"импорт только для «{targetOrg}»: пропущено других листов — {skippedOtherOrgs}");
        }

        if (!string.IsNullOrWhiteSpace(targetOrg)
            && result.MatchedOrganizations == 0
            && skippedOtherOrgs == 0
            && parsed.Sheets.Count > 0)
        {
            result.Warnings.Add(
                $"В файле нет листа для «{targetOrg}». Откройте нужную организацию или проверьте название листа.");
        }

        if (result.CreatedStandards > 0 || result.EnrichedStandards > 0)
        {
            _standards.SaveAll(standards);
        }

        if (result.UpdatedCells > 0 || result.AutoAssignedStandards > 0)
        {
            _conduct.SaveAll(list);
        }

        if (result.AutoAssignedStandards > 0 || result.PersonExtrasUpdated > 0)
            {
                _assignments.SaveAll(assignmentList);
        }

        return result;
    }

    private static bool IsSameOrganization(string resolved, string target)
    {
        if (string.Equals(resolved.Trim(), target.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return OrganizationMatcher.Matches(resolved, target)
               || OrganizationMatcher.Matches(target, resolved);
    }

    private static string? ResolveOrg(TrainingScheduleExcelSheet sheet, IReadOnlyList<string> catalog)
    {
        foreach (var hint in new[] { sheet.OrganizationHint, sheet.SheetName })
        {
            var normalizedHint = TrainingScheduleExcelParser.NormalizeOrgAliasesInText(hint);
            var resolved = OrganizationMatcher.ResolveOrganizationName(normalizedHint, catalog)
                           ?? OrganizationMatcher.ResolveOrganizationName(hint, catalog);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        // Доп. эвристика: ядро имени листа внутри каталога («ССтрой» → «ООО «СтандартСтрой»»)
        var core = OrganizationMatcher.GetCoreName(
            TrainingScheduleExcelParser.NormalizeOrgAliasesInText(sheet.SheetName) ?? string.Empty);
        if (core.Length < 3)
        {
            core = OrganizationMatcher.GetCoreName(
                TrainingScheduleExcelParser.NormalizeOrgAliasesInText(sheet.OrganizationHint) ?? string.Empty);
        }

        if (core.Length < 3)
        {
            core = OrganizationMatcher.GetCoreName(sheet.SheetName ?? string.Empty);
        }

        if (core.Length >= 3)
        {
            var byCore = catalog.FirstOrDefault(c =>
            {
                var candidateCore = OrganizationMatcher.GetCoreName(c);
                return string.Equals(candidateCore, core, StringComparison.OrdinalIgnoreCase)
                       || candidateCore.Contains(core, StringComparison.OrdinalIgnoreCase)
                       || core.Contains(candidateCore, StringComparison.OrdinalIgnoreCase);
            });
            if (!string.IsNullOrWhiteSpace(byCore))
            {
                return byCore;
            }
        }

        return null;
    }

    private void ImportSheet(
        TrainingScheduleExcelSheet sheet,
        string organization,
        IReadOnlyList<OrganizationEmployee> employees,
        IReadOnlyList<TrainingStatusRow> reportRows,
        List<TrainingConductRecord> conductList,
        List<TrainingStandard> standards,
        Dictionary<string, string> assignmentMap,
        List<TrainingPersonStandardAssignment> assignmentList,
        IReadOnlyList<TrainingKindNode> kinds,
        IReadOnlyList<string> kindProgramNames,
        HashSet<string> preExistingStandardIds,
        TrainingScheduleImportResult result,
        bool forArchive,
        bool overwriteExisting)
    {
        var orgStandards = TrainingReportBuilder.StandardsForOrganization(standards, organization).ToList();
        var programCatalog = BuildProgramCatalog(reportRows, orgStandards, kindProgramNames);
        var employedNames = employees
            .Where(EmployeeHrStatus.IsEmployed)
            .Select(e => (e.Name ?? string.Empty).Trim())
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var columnToPrograms = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in sheet.People.SelectMany(p => p.CellsByColumnKey.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // Высота без номера группы в заголовке — сопоставление только по ячейке.
            if (string.Equals(key, "height", StringComparison.OrdinalIgnoreCase))
            {
                columnToPrograms[key] = [];
                continue;
            }

            var targets = ResolveProgramsForColumn(key, programCatalog);
            if (targets.Count == 0)
            {
                targets = DefaultProgramsForColumn(key);
            }

            if (targets.Count == 0)
            {
                result.UnmatchedColumns.Add($"«{organization}»: {DescribeColumn(key)}");
                continue;
            }

            columnToPrograms[key] = targets;
            foreach (var program in targets)
            {
                if (!programCatalog.Contains(program, StringComparer.OrdinalIgnoreCase))
                {
                    programCatalog.Add(program);
                }
            }
        }

        var sheetPrograms = columnToPrograms.Values
            .SelectMany(x => x)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (sheetPrograms.Count == 0 && programCatalog.Count == 0)
        {
            result.Warnings.Add($"«{organization}»: в файле нет колонок программ обучения.");
            return;
        }

        var reportPeople = reportRows
            .Select(r => r.Person.Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var rosterPeople = employees
            .Select(e => e.Name.Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var matchPeople = reportPeople.Count > 0 ? reportPeople : rosterPeople;
        if (matchPeople.Count == 0)
        {
            matchPeople = sheet.People.Select(p => p.Person.Trim()).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        var snilsIndex = BuildSnilsIndex(employees, matchPeople);
        var employeesByName = employees
            .GroupBy(e => e.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var createdByProfession = new Dictionary<string, TrainingStandard>(StringComparer.OrdinalIgnoreCase);
        var createdByProgramSet = new Dictionary<string, TrainingStandard>(StringComparer.OrdinalIgnoreCase);
        var matchedFilePeople = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Стандарт «Индивидуальный предприниматель» — только самому ИП по ФИО из названия орг.
        var employeeTuples = employees
            .Select(e => (organization, e.Name, e.Profession ?? "", e.Department ?? ""))
            .ToList();
        TrainingIndividualEntrepreneurStandards.EnsureForOrganization(
            organization,
            _standards,
            _assignments,
            employeeTuples,
            kinds,
            out var ieEnsured);
        if (ieEnsured)
        {
            standards.Clear();
            standards.AddRange(_standards.LoadAll());
            assignmentList.Clear();
            assignmentList.AddRange(_assignments.LoadAll());
            assignmentMap.Clear();
            foreach (var a in assignmentList)
            {
                assignmentMap[TrainingReportBuilder.AssignmentKey(a.Organization, a.Person, a.Profession)] =
                    a.StandardId;
            }

            orgStandards = TrainingReportBuilder.StandardsForOrganization(standards, organization).ToList();
        }

        foreach (var excelPerson in sheet.People)
        {
            var reportPerson = MatchPerson(excelPerson, matchPeople, snilsIndex);
            if (reportPerson is null)
            {
                // Человек есть в файле, но нет в штате/отчёте — пропускаем с предупреждением
                result.MissingInReport.Add($"«{organization}»: {excelPerson.Person}");
                continue;
            }

            matchedFilePeople.Add(reportPerson);
            employeesByName.TryGetValue(reportPerson, out var employeeRows);
            employeeRows ??= [];
            var (excelProfession, objectFromProfession) =
                ProfessionMatcher.SplitProfessionAndObject(excelPerson.Profession);

            // Профессия в файле должна совпасть с профессией в программе (без текста в скобках-объекте).
            OrganizationEmployee? employee = null;
            if (!string.IsNullOrWhiteSpace(excelProfession))
            {
                employee = employeeRows.FirstOrDefault(e =>
                    ProfessionMatcher.MatchesForImport(e.Profession, excelProfession));
                if (employee is null && employeeRows.Count > 0)
                {
                    var programProf = employeeRows
                        .Select(e => (e.Profession ?? string.Empty).Trim())
                        .FirstOrDefault(p => p.Length > 0 && p != "—")
                        ?? string.Empty;
                    result.ProfessionMismatch.Add(
                        $"«{organization}»: {reportPerson} — в программе «{programProf}», в файле «{excelProfession}»");
                    continue;
                }

                // Нет в штате JSON, но есть в отчёте — сверяем с профессией строк отчёта.
                if (employee is null && employeeRows.Count == 0)
                {
                    var reportProfessions = reportRows
                        .Where(r => string.Equals(r.Person, reportPerson, StringComparison.OrdinalIgnoreCase))
                        .Select(r => (r.Profession ?? string.Empty).Trim())
                        .Where(p => p.Length > 0 && p != "—")
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (reportProfessions.Count > 0
                        && !reportProfessions.Any(p => ProfessionMatcher.MatchesForImport(p, excelProfession)))
                    {
                        result.ProfessionMismatch.Add(
                            $"«{organization}»: {reportPerson} — в программе «{reportProfessions[0]}», в файле «{excelProfession}»");
                        continue;
                    }
                }
            }

            employee ??= employeeRows.FirstOrDefault();

            var isIePerson = OrganizationMatcher.IsIndividualEntrepreneurPerson(
                organization, reportPerson, employee?.Profession);
            var profession = !string.IsNullOrWhiteSpace(employee?.Profession)
                ? employee!.Profession.Trim()
                : excelProfession;
            if (isIePerson)
            {
                // ИП всегда на стандарте «Индивидуальный предприниматель», не «Сотрудники».
                profession = TrainingIndividualEntrepreneurStandards.ProfessionName;
            }
            else if (string.IsNullOrWhiteSpace(profession) || profession == "—")
            {
                profession = "Сотрудники";
            }

            var department = !string.IsNullOrWhiteSpace(employee?.Department)
                ? employee!.Department.Trim()
                : reportRows.FirstOrDefault(r =>
                      string.Equals(r.Person, reportPerson, StringComparison.OrdinalIgnoreCase))
                  ?.Department ?? "—";
            if (isIePerson && (string.IsNullOrWhiteSpace(department) || department == "—"))
            {
                department = TrainingIndividualEntrepreneurStandards.ProfessionName;
            }

            // Колонка «Ж» / ячейка «ж» → галочка «Журнал» у сотрудника.
            if (excelPerson.HasJournalMarker)
            {
                if (ApplyPersonJournalFlag(assignmentList, organization, reportPerson, profession))
                {
                    result.PersonExtrasUpdated++;
                }
            }

            // «Архитектор (Офис БЦ Демидов)» → поле «объект».
            if (!string.IsNullOrWhiteSpace(objectFromProfession))
            {
                if (ApplyPersonObjectNote(
                        assignmentList, organization, reportPerson, profession, objectFromProfession))
                {
                    result.PersonExtrasUpdated++;
                }
            }

            // Автоназначение / создание стандарта по профессии — чтобы в отчёте появились строки программ
            var standard = TrainingReportBuilder.ResolveStandard(
                standards, assignmentMap, organization, reportPerson, profession, department);
            // Стандарт ИП — только самому предпринимателю, не наёмным.
            if (standard is not null
                && IsIndividualEntrepreneurStandard(standard)
                && !isIePerson)
            {
                standard = null;
            }

            if (standard is null)
            {
                standard = orgStandards
                    .Select(s => (S: s, Score: ProfessionScore(s, profession)))
                    .Where(x => x.Score > 0)
                    .Where(x => !IsIndividualEntrepreneurStandard(x.S) || isIePerson)
                    .OrderByDescending(x => x.Score)
                    .Select(x => x.S)
                    .FirstOrDefault();
            }

            if (standard is null && isIePerson)
            {
                standard = orgStandards.FirstOrDefault(IsIndividualEntrepreneurStandard);
            }

            if (standard is null)
            {
                var professionKey = ProfessionMatcher.Normalize(profession);
                    // Только программы из заполненных ячеек (пустые / «-» не добавляем).
                    var programsForPerson = CollectProgramsForPerson(excelPerson, columnToPrograms)
                        .Where(p => IsAllowedAutoStandardProgram(p, kindProgramNames))
                        .ToList();

                    if (programsForPerson.Count > 0)
                {
                    var programSetKey = BuildProgramSetKey(programsForPerson);

                    // 1) Уже создали в этом импорте стандарт с тем же набором программ.
                    if (createdByProgramSet.TryGetValue(programSetKey, out var byPrograms))
                    {
                        standard = EnsureProfessionOnStandard(byPrograms, profession);
                        if (!AreSameProfessionList(standard, byPrograms)
                            || !string.Equals(standard.Name, byPrograms.Name, StringComparison.Ordinal))
                        {
                            ReplaceStandardInLists(standards, orgStandards, standard);
                            createdByProgramSet[programSetKey] = standard;
                        }

                        createdByProfession[professionKey] = standard;
                    }
                    // 2) Та же профессия уже получила стандарт в этом импорте.
                    else if (createdByProfession.TryGetValue(professionKey, out standard))
                    {
                        createdByProgramSet[programSetKey] = standard;
                    }
                    // 3) В организации уже есть стандарт с тем же набором программ — берём как есть, не меняем.
                    else if (TryFindStandardByProgramSet(
                                 orgStandards, programsForPerson, isIePerson, out var existingBySet))
                    {
                        standard = existingBySet;
                        createdByProgramSet[programSetKey] = standard;
                        createdByProfession[professionKey] = standard;
                    }
                    else
                    {
                        standard = CreateStandardFromImport(organization, profession, programsForPerson);
                        standards.Add(standard);
                        orgStandards.Add(standard);
                        createdByProfession[professionKey] = standard;
                        createdByProgramSet[programSetKey] = standard;
                        result.CreatedStandards++;
                        result.Warnings.Add(
                            $"«{organization}»: создан стандарт «{standard.Name}» ({programsForPerson.Count} прогр.).");
                    }
                }
            }

            var canMutateStandard = standard is not null
                && !preExistingStandardIds.Contains(standard.Id);

            if (canMutateStandard)
            {
                // Дописываем программы только в стандарты, созданные этим импортом.
                var added = EnrichStandardFromFilledColumns(
                    ref standard!,
                    excelPerson,
                    columnToPrograms,
                    kindProgramNames,
                    programCatalog);
                if (added > 0)
                {
                    result.EnrichedStandards += added;
                    result.Warnings.Add(
                        $"«{organization}»: стандарт «{standard.Name}» дополнен ({added} прогр.).");
                    ReplaceStandardInLists(standards, orgStandards, standard);
                }
                }

            if (standard is not null)
            {

                var alreadyAssigned = assignmentMap.TryGetValue(
                    TrainingReportBuilder.AssignmentKey(organization, reportPerson, profession),
                    out var assignedId)
                    && string.Equals(assignedId, standard.Id, StringComparison.OrdinalIgnoreCase);
                if (!alreadyAssigned)
                {
                    UpsertAssignment(assignmentList, assignmentMap, organization, reportPerson, profession, standard.Id);
                    result.AutoAssignedStandards++;
                }

                foreach (var item in (standard.Items ?? []).Where(i => !string.IsNullOrWhiteSpace(i)))
                {
                    if (!programCatalog.Contains(item.Trim(), StringComparer.OrdinalIgnoreCase))
                    {
                        programCatalog.Add(item.Trim());
                    }
                }
            }

            var personReportRows = reportRows
                .Where(r => string.Equals(r.Person, reportPerson, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var (columnKey, cellText) in excelPerson.CellsByColumnKey)
            {
                var effectiveKey = ResolveEffectiveColumnKey(columnKey, cellText, out var keyWarning);
                if (!string.IsNullOrWhiteSpace(keyWarning))
                {
                    result.Warnings.Add($"«{organization}» / {reportPerson}: {keyWarning}");
                }

                var isOtCore = IsOtCoreProtocolColumn(effectiveKey) || IsOtCoreProtocolColumn(columnKey);
                // Зелёная заливка ячейки программы → «движение протокола: готов» (любая программная колонка).
                var markReady = excelPerson.GreenFillColumnKeys.Contains(columnKey)
                                || excelPerson.GreenFillColumnKeys.Contains(effectiveKey);
                var markUpd = isOtCore && excelPerson.HasUpdLetter;
                var protocolMovement = markReady ? "готов" : null;
                var updStatus = markUpd ? "УПД есть" : null;

                if (!columnToPrograms.TryGetValue(effectiveKey, out var targetPrograms))
                {
                    // Высота без группы в заголовке: программы только из текста ячейки ниже.
                    if (effectiveKey.StartsWith("height", StringComparison.OrdinalIgnoreCase))
                    {
                        targetPrograms = [];
                    }
                    else
                {
                    // Колонка могла стать сопоставимой после автоназначения стандарта
                    // или после уточнения программы по тексту ячейки.
                    var late = ResolveProgramsForColumn(effectiveKey, programCatalog);
                        if (late.Count == 0 && !columnKey.Equals(effectiveKey, StringComparison.OrdinalIgnoreCase))
                        {
                            late = ResolveProgramsForColumn(columnKey, programCatalog);
                        }

                    if (late.Count == 0)
                    {
                            // Зелёная заливка без сопоставленной программы — по уже существующим строкам человека.
                            if (markReady || markUpd)
                            {
                                ApplyGreenMarkersToPersonPrograms(
                                    conductList,
                                    organization,
                                    reportPerson,
                                    protocolMovement,
                                    updStatus,
                                    forArchive,
                                    employedNames,
                                    overwriteExisting,
                                    result);
                            }

                        continue;
                    }

                    targetPrograms = late;
                    columnToPrograms[effectiveKey] = targetPrograms;
                    }
                }

                // ПБ: после уточнения ключа по ячейке всегда пересчитываем программу —
                // кэш колонки мог держать «Проведение инструктажей» для общего fire.
                if (effectiveKey.StartsWith("fire", StringComparison.OrdinalIgnoreCase))
                {
                    targetPrograms = ResolveProgramsForColumn(effectiveKey, programCatalog);
                    if (targetPrograms.Count == 0)
                    {
                        if (markReady || markUpd)
                        {
                            ApplyGreenMarkersToPersonPrograms(
                                conductList,
                                organization,
                                reportPerson,
                                protocolMovement,
                                updStatus,
                                forArchive,
                                employedNames,
                                overwriteExisting,
                                result);
                        }

                        continue;
                    }

                    columnToPrograms[effectiveKey] = targetPrograms;
                }

                var parsed = TrainingScheduleExcelParser.TryParseProtocolCell(
                    cellText, out var protocol, out var trainingDate);
                if (!parsed)
                {
                    // Повторный импорт: стираем ранее затянутые пометки «обучен в… Основное место».
                    if (IsOtCoreProtocolColumn(effectiveKey)
                        && TrainingScheduleExcelParser.IsEmploymentOrVenueNoteCell(cellText))
                    {
                        var clearTargets = targetPrograms.Count > 0
                            ? targetPrograms
                            : ResolveProgramsForColumn(effectiveKey, programCatalog);
                        foreach (var programName in clearTargets)
                        {
                            ClearVenueNoteProtocol(conductList, organization, reportPerson, programName, result);
                        }
                    }

                    // Зелёная заливка / «а» без распознанного протокола — всё равно ставим метки.
                    if (markReady || markUpd)
                    {
                        var markerTargets = targetPrograms.Count > 0
                            ? targetPrograms
                            : ResolveProgramsForColumn(effectiveKey, programCatalog);
                        foreach (var programName in markerTargets)
                        {
                            if (ApplyOtMarkersToConduct(
                                    conductList,
                                    organization,
                                    reportPerson,
                                    programName,
                                    protocolMovement,
                                    updStatus,
                                    forArchive,
                                    employedNames,
                                    overwriteExisting,
                                    out var preservedMarkers))
                            {
                                result.UpdatedCells++;
                            }
                            else if (preservedMarkers)
                            {
                                result.PreservedFilledCells++;
                            }
                        }
                    }

                    continue;
                }

                // А/Б/В/СИЗ/ПП: не тащим пометки «обучен Ресурсом… Основное место» как протокол.
                if (IsOtCoreProtocolColumn(effectiveKey))
                {
                    if (TrainingScheduleExcelParser.IsEmploymentOrVenueNoteCell(cellText)
                        || TrainingScheduleExcelParser.IsEmploymentOrVenueNoteCell(protocol))
                    {
                        var clearTargets = targetPrograms.Count > 0
                            ? targetPrograms
                            : ResolveProgramsForColumn(effectiveKey, programCatalog);
                        foreach (var programName in clearTargets)
                        {
                            ClearVenueNoteProtocol(conductList, organization, reportPerson, programName, result);
                        }

                        // Дату из пометки места не пишем — это не факт прохождения программы.
                        // Зелёную заливку / «а» всё равно проставляем.
                        if (markReady || markUpd)
                        {
                            var markerTargets = clearTargets.Count > 0
                                ? clearTargets
                                : ResolveProgramsForColumn(effectiveKey, programCatalog);
                            foreach (var programName in markerTargets)
                            {
                                if (ApplyOtMarkersToConduct(
                                        conductList,
                                        organization,
                                        reportPerson,
                                        programName,
                                        protocolMovement,
                                        updStatus,
                                        forArchive,
                                        employedNames,
                                        overwriteExisting,
                                        out var preservedMarkers))
                                {
                                    result.UpdatedCells++;
                                }
                                else if (preservedMarkers)
                                {
                                    result.PreservedFilledCells++;
                                }
                            }
                        }

                        continue;
                    }
                }

                // Дата приёма — только дата, без номера протокола
                if (string.Equals(effectiveKey, "hire", StringComparison.OrdinalIgnoreCase))
                {
                    protocol = string.Empty;
                    if (!trainingDate.HasValue)
                    {
                        continue;
                    }
                }

                // В: вариант программы зависит от даты обучения.
                if (string.Equals(effectiveKey, "v", StringComparison.OrdinalIgnoreCase))
                {
                    targetPrograms = ResolveProgramVForDate(trainingDate, programCatalog);
                }

                // Высота: «1 гр» / «2 гр» / «3 гр» из текста ячейки (важнее заголовка колонки).
                if (effectiveKey.StartsWith("height", StringComparison.OrdinalIgnoreCase))
                {
                    targetPrograms = ResolveHeightProgramsForCell(effectiveKey, cellText, programCatalog);
                    if (targetPrograms.Count == 0)
                    {
                        if (!string.IsNullOrWhiteSpace(protocol)
                            || trainingDate.HasValue)
                        {
                            result.Warnings.Add(
                                $"«{organization}» / {reportPerson}: работы на высоте — укажите группу в ячейке («1 гр», «2 гр» или «3 гр»), иначе протокол не записывается.");
                        }

                        continue;
                    }

                    // Убираем тот же протокол с других групп высоты (раньше размазывалось на 1/2/3).
                    ClearHeightProtocolFromOtherGroups(
                        conductList,
                        organization,
                        reportPerson,
                        protocol,
                        targetPrograms,
                        result);
                }

                foreach (var programName in targetPrograms)
                {
                    // Квалификации из свободных колонок Excel не пишем человеку,
                    // если их нет в его стандарте (иначе «слесарь» попадает к бурильщику).
                    if (standard is not null
                        && !IsCoreImportColumn(effectiveKey)
                        && !standard.Items.Any(i =>
                            string.Equals(i, programName, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    if (canMutateStandard
                        && IsCoreImportColumn(effectiveKey)
                        && TryAddProgramToStandard(ref standard!, programName))
                    {
                        result.EnrichedStandards++;
                        if (!programCatalog.Contains(programName, StringComparer.OrdinalIgnoreCase))
                        {
                            programCatalog.Add(programName);
                        }

                        ReplaceStandardInLists(standards, orgStandards, standard);
                    }

                    var row = personReportRows.FirstOrDefault(r =>
                        string.Equals(r.Program, programName, StringComparison.OrdinalIgnoreCase));

                    row ??= new TrainingStatusRow(
                        organization,
                        string.IsNullOrWhiteSpace(department) ? "—" : department,
                        string.IsNullOrWhiteSpace(profession) ? "—" : profession,
                        reportPerson,
                        programName,
                        "—",
                        "—",
                        TrainingProgramCatalog.DefaultPeriodicity(programName),
                        null,
                        null,
                        string.Empty,
                        "pink",
                        null,
                        standard?.Id,
                        standard?.Name);

                    if (ApplyToConduct(
                            conductList,
                            row,
                            protocol,
                            trainingDate,
                            standards,
                            assignmentMap,
                            kinds,
                            forArchive,
                            employedNames,
                            overwriteExisting,
                            out var preservedFilled,
                            protocolMovement,
                            updStatus))
                    {
                        result.UpdatedCells++;
                    }
                    else if (preservedFilled)
                    {
                        result.PreservedFilledCells++;
                    }

                    // Повторный импорт: то же удостоверение могло раньше лежать
                    // на другой программе ПБ — снимаем оттуда.
                    if (effectiveKey.StartsWith("fire", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(protocol)
                        && ClearMisplacedFireProtocol(
                            conductList,
                            organization,
                            reportPerson,
                            protocol,
                            programName))
                    {
                        result.UpdatedCells++;
                    }

                    // Высота: ячейка «2 гр» / «3 гр» важнее колонки — снимаем тот же
                    // протокол с чужих групп (иначе 1-я строка остаётся с «2 гр»).
                    if (effectiveKey.StartsWith("height", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(protocol)
                        && ClearMisplacedHeightProtocol(
                            conductList,
                            organization,
                            reportPerson,
                            protocol,
                            programName))
                    {
                        result.UpdatedCells++;
                    }
                }
            }

            // Зелёные ячейки без текста (ключ есть в GreenFill, но не в Cells).
            foreach (var greenKey in excelPerson.GreenFillColumnKeys)
            {
                if (excelPerson.CellsByColumnKey.ContainsKey(greenKey))
                {
                    continue;
                }

                var greenPrograms = columnToPrograms.TryGetValue(greenKey, out var gp)
                    ? gp
                    : ResolveProgramsForColumn(greenKey, programCatalog);
                if (greenPrograms.Count == 0)
                {
                    continue;
                }

                foreach (var programName in greenPrograms)
                {
                    if (ApplyOtMarkersToConduct(
                            conductList,
                            organization,
                            reportPerson,
                            programName,
                            "готов",
                            IsOtCoreProtocolColumn(greenKey) && excelPerson.HasUpdLetter ? "УПД есть" : null,
                            forArchive,
                            employedNames,
                            overwriteExisting,
                            out _))
                    {
                        result.UpdatedCells++;
                    }
                }
            }

            // Буква «а» в строке → УПД есть по уже существующим А/Б/В/СИЗ/ПП этого человека.
            if (excelPerson.HasUpdLetter)
            {
                foreach (var rec in conductList
                             .Where(c =>
                                 string.Equals(c.Person, reportPerson, StringComparison.OrdinalIgnoreCase)
                                 && (string.IsNullOrWhiteSpace(c.Organization)
                                     || OrganizationMatcher.Matches(c.Organization, organization)
                                     || string.Equals(c.Organization, organization, StringComparison.OrdinalIgnoreCase))
                                 && IsOtCoreProgramName(c.Program))
                             .ToList())
                {
                    if (ApplyOtMarkersToConduct(
                            conductList,
                            organization,
                            reportPerson,
                            rec.Program,
                            protocolMovement: null,
                            updStatus: "УПД есть",
                            forArchive,
                            employedNames,
                            overwriteExisting,
                            out _))
                    {
                        result.UpdatedCells++;
                    }
                }
            }
        }

        foreach (var person in reportPeople)
        {
            if (!matchedFilePeople.Contains(person))
            {
                result.ExtraInReport.Add($"«{organization}»: {person}");
            }
        }
    }

    private static List<string> BuildProgramCatalog(
        IReadOnlyList<TrainingStatusRow> reportRows,
        IReadOnlyList<TrainingStandard> orgStandards,
        IReadOnlyList<string> kindProgramNames)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in reportRows)
        {
            if (!string.IsNullOrWhiteSpace(r.Program))
            {
                set.Add(r.Program.Trim());
            }
        }

        foreach (var s in orgStandards)
        {
            foreach (var item in s.Items)
            {
                if (!string.IsNullOrWhiteSpace(item))
                {
                    set.Add(item.Trim());
                }
            }
        }

        foreach (var name in kindProgramNames)
        {
            set.Add(name);
        }

        // Типовые составные названия, если их нет в справочнике
        set.Add("Программы А+Б+СИЗ+ПП");
        set.Add("Программа В");
        set.Add(CanonicalHeightProgram(1));
        set.Add(CanonicalHeightProgram(2));
        set.Add(CanonicalHeightProgram(3));
        set.Add("Вводный инструктаж по охране труда");
        set.Add("Вводный инструктаж по пожарной безопасности");
        set.Add("Первичный инструктаж на рабочем месте по пожарной безопасности");
        return set.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// С 01.09.2026 программа В переходит на 3 года; до этой даты — вариант «на 1 год».
    /// </summary>
    private static readonly DateTime ProgramVThreeYearFrom = new(2026, 9, 1);

    /// <summary>
    /// Колонка Excel → список программ отчёта.
    /// А/Б/СИЗ/ПП — по одной программе; В выбирается по дате ячейки при импорте.
    /// </summary>
    private static List<string> ResolveProgramsForColumn(string columnKey, IReadOnlyList<string> programCatalog)
    {
        var key = (columnKey ?? string.Empty).Trim().ToLowerInvariant();
        if (key is "hire")
        {
            return MatchHireBriefingPrograms(programCatalog);
        }

        if (key is "a")
        {
            return [CanonicalOtProgram(programCatalog, "Программа А", MatchesProgramA)];
        }

        if (key is "b")
        {
            return [CanonicalOtProgram(programCatalog, "Программа Б", MatchesProgramB)];
        }

        if (key is "ab")
        {
            // Редкий объединённый столбец А+Б — две отдельные программы.
            return
            [
                CanonicalOtProgram(programCatalog, "Программа А", MatchesProgramA),
                CanonicalOtProgram(programCatalog, "Программа Б", MatchesProgramB)
            ];
        }

        if (key is "siz")
        {
            return [CanonicalOtProgram(programCatalog, "Программа СИЗ", MatchesProgramSiz)];
        }

        if (key is "pp")
        {
            return [CanonicalOtProgram(programCatalog, "Программа ПП", MatchesProgramPp)];
        }

        if (key is "v")
        {
            // Конкретный вариант (1 год / 3 года) выбирается по дате обучения в ячейке.
            return
            [
                CanonicalOtProgram(programCatalog, "Программа В (на 1 год)", MatchesProgramVOneYear),
                CanonicalOtProgram(programCatalog, "Программа В (на 3 года)", MatchesProgramVThreeYear)
            ];
        }

        if (key is "height" || key.StartsWith("height:", StringComparison.Ordinal))
        {
            return ResolveHeightProgramsForCell(key, cellText: null, programCatalog);
        }

        // ПБ: Excel → канон справочника (протокол/дата пишутся в эту программу).
        // «Инструктажи по ПБ» → «Проведение инструктажей по ПБ»
        if (key is "fire:briefings" or "fire:conducting")
        {
            return [ResolveFireBriefingsProgram(programCatalog)];
        }

        // «Ответственный за обеспечение ПБ» → «Обучение по программе «Ответственный…»»
        if (key is "fire:responsible")
        {
            return [ResolveFireResponsibleProgram(programCatalog)];
        }

        // «Специалист по пожарной профилактике» → то же имя в справочнике
        if (key is "fire:specialist")
        {
            return
            [
                CanonicalOtProgram(
                    programCatalog,
                    "Специалист по пожарной профилактике",
                    MatchesFireSpecialist)
            ];
        }

        var matched = MatchProgramsForColumn(key, programCatalog);
        if (matched.Count == 0)
        {
            return DefaultProgramsForColumn(key);
        }

        if (key.StartsWith("fire", StringComparison.Ordinal))
        {
            var preferred = PickBestFireProgram(key, matched);
            if (preferred is not null)
            {
                return [preferred];
            }

            // Общий «fire» без подтипа — не угадываем инструктажи; берём канон.
            var defaults = DefaultProgramsForColumn(key);
            if (defaults.Count > 0)
            {
                return defaults;
            }
        }

        return [PickBestProgram(key, matched)];
    }

    private static string CanonicalOtProgram(
        IReadOnlyList<string> catalog,
        string canonical,
        Func<string, bool> match)
    {
        var exact = catalog.FirstOrDefault(p =>
            string.Equals(p, canonical, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var soft = catalog
            .Where(p => match(NormalizeProgramName(p)))
            .OrderBy(p => p.Length)
            .FirstOrDefault();
        return soft ?? canonical;
    }

    /// <summary>
    /// Канон: «Обучение по программе «Ответственный за обеспечение пожарной безопасности»».
    /// В Excel колонка короче — «Ответственный за обеспечение ПБ».
    /// </summary>
    private const string FireResponsibleCanonicalName =
        "Обучение по программе «Ответственный за обеспечение пожарной безопасности»";

    private const string FireBriefingsCanonicalName = "Проведение инструктажей по ПБ";

    private static string ResolveFireBriefingsProgram(IReadOnlyList<string> catalog)
    {
        var exact = catalog.FirstOrDefault(p =>
            string.Equals(p, FireBriefingsCanonicalName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var soft = catalog
            .Where(p =>
            {
                var n = NormalizeProgramName(p);
                return MatchesFireConducting(n) || MatchesFireBriefings(n);
            })
            .OrderByDescending(p => NormalizeProgramName(p).Contains("проведен", StringComparison.Ordinal))
            .ThenBy(p => p.Length)
            .FirstOrDefault();
        return soft ?? FireBriefingsCanonicalName;
    }

    private static string ResolveFireResponsibleProgram(IReadOnlyList<string> catalog)
    {
        var exact = catalog.FirstOrDefault(p =>
            string.Equals(p, FireResponsibleCanonicalName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        // Без кавычек / с обычными кавычками — тоже считаем совпадением.
        var normalizedCanon = NormalizeFireResponsibleTitle(FireResponsibleCanonicalName);
        var byNorm = catalog.FirstOrDefault(p =>
            string.Equals(NormalizeFireResponsibleTitle(p), normalizedCanon, StringComparison.OrdinalIgnoreCase));
        if (byNorm is not null)
        {
            return byNorm;
        }

        // Предпочитаем полное «Обучение по программе…», затем более длинное имя.
        var soft = catalog
            .Where(p => MatchesFireResponsible(NormalizeProgramName(p)))
            .OrderByDescending(p =>
                NormalizeProgramName(p).Contains("обучен", StringComparison.Ordinal)
                && NormalizeProgramName(p).Contains("программ", StringComparison.Ordinal))
            .ThenByDescending(p => p.Length)
            .FirstOrDefault();
        return soft ?? FireResponsibleCanonicalName;
    }

    private static string NormalizeFireResponsibleTitle(string? name)
    {
        var t = NormalizeProgramName(name ?? string.Empty);
        t = t.Replace('«', ' ').Replace('»', ' ').Replace('"', ' ').Replace('\'', ' ');
        return Regex.Replace(t, @"\s+", " ").Trim();
    }

    private static string NormalizeProgramName(string program)
    {
        var p = program.ToLowerInvariant().Replace('ё', 'е');
        return Regex.Replace(p, @"\s+", " ").Trim();
    }

    /// <summary>Канонические имена программ, если в справочнике ещё нет подходящих.</summary>
    private static List<string> DefaultProgramsForColumn(string columnKey)
    {
        var key = (columnKey ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "hire" => MatchHireBriefingPrograms([]),
            "a" => ["Программа А"],
            "b" => ["Программа Б"],
            "ab" => ["Программа А", "Программа Б"],
            "siz" => ["Программа СИЗ"],
            "pp" => ["Программа ПП"],
            "v" => ["Программа В (на 1 год)", "Программа В (на 3 года)"],
            // Без группы в заголовке — только из ячейки; не подставляем «1 группа» по умолчанию.
            "height" => [],
            "height:1" => [CanonicalHeightProgram(1)],
            "height:2" => [CanonicalHeightProgram(2)],
            "height:3" => [CanonicalHeightProgram(3)],
            "fire" => ["Обучение мерам пожарной безопасности"],
            "fire:responsible" => [FireResponsibleCanonicalName],
            // Excel: «Инструктажи по ПБ 24 часа» → «Проведение инструктажей по ПБ».
            "fire:briefings" => ["Проведение инструктажей по ПБ"],
            "fire:conducting" => ["Проведение инструктажей по ПБ"],
            "fire:intro" => ["Вводный инструктаж по пожарной безопасности"],
            "fire:primary" => ["Первичный инструктаж на рабочем месте по пожарной безопасности"],
            "fire:repeat" => ["Повторный противопожарный инструктаж на рабочем месте"],
            "fire:specialist" => ["Специалист по пожарной профилактике"],
            "fire:training" => ["Обучение мерам пожарной безопасности"],
            "electrical" => ["Электробезопасность"],
            "slinger" => ["Стропальщик"],
            "sout" => ["СОУТ"],
            "industrial" => ["Промышленная безопасность"],
            "prof" => ["Профессиональное обучение"],
            _ when key.StartsWith("pb:", StringComparison.Ordinal) =>
                [FormatDefaultPbProgramName(key[3..])],
            _ when key.StartsWith("h:", StringComparison.Ordinal) =>
                [HumanizeHeaderTitle(key[2..])],
            _ => []
        };
    }

    private static string FormatDefaultPbProgramName(string code)
    {
        var normalized = NormalizePbCodeToken(code);
        return string.IsNullOrWhiteSpace(normalized)
            ? "Промышленная безопасность"
            : normalized;
    }

    private static string HumanizeHeaderTitle(string raw)
    {
        var t = (raw ?? string.Empty).Trim();
        if (t.Length == 0)
        {
            return "Программа обучения";
        }

        return char.ToUpper(t[0], CultureInfo.GetCultureInfo("ru-RU")) + t[1..];
    }

    private static List<string> CollectProgramsForPerson(
        TrainingScheduleExcelPersonRow excelPerson,
        IReadOnlyDictionary<string, List<string>> columnToPrograms)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (columnKey, cellText) in excelPerson.CellsByColumnKey)
        {
            var effectiveKey = ResolveEffectiveColumnKey(columnKey, cellText, out _);
            if (!IsCoreImportColumn(effectiveKey))
            {
                continue;
            }

            // Пустая ячейка / «-» — программу в стандарт не добавляем.
            if (TrainingScheduleExcelParser.IsSkipCell(cellText))
            {
                continue;
            }

            if (!TrainingScheduleExcelParser.TryParseProtocolCell(cellText, out _, out var trainingDate)
                && string.IsNullOrWhiteSpace(cellText))
            {
                continue;
            }

            List<string> programs;
            if (string.Equals(effectiveKey, "v", StringComparison.OrdinalIgnoreCase))
            {
                programs = ResolveProgramVForDate(trainingDate, columnToPrograms.TryGetValue(effectiveKey, out var vCatalog)
                    ? vCatalog
                    : DefaultProgramsForColumn("v"));
            }
            else if (effectiveKey.StartsWith("height", StringComparison.OrdinalIgnoreCase))
            {
                var catalogHint = columnToPrograms.Values
                    .SelectMany(x => x)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                programs = ResolveHeightProgramsForCell(effectiveKey, cellText, catalogHint);
                if (programs.Count == 0)
                {
                    continue;
                }
            }
            else if (effectiveKey.StartsWith("fire", StringComparison.OrdinalIgnoreCase))
            {
                // Всегда по уточнённому ключу ячейки — не из кэша общего fire.
                var catalogHint = columnToPrograms.Values
                    .SelectMany(x => x)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                programs = ResolveProgramsForColumn(effectiveKey, catalogHint);
                if (programs.Count == 0)
                {
                    continue;
                }
            }
            else if (columnToPrograms.TryGetValue(effectiveKey, out programs!))
            {
                // ok
            }
            else
            {
                programs = ResolveProgramsForColumn(effectiveKey, []);
                if (programs.Count == 0)
                {
                    continue;
                }
            }

            foreach (var program in programs)
            {
                if (!string.IsNullOrWhiteSpace(program))
                {
                    set.Add(program.Trim());
                }
            }
        }

        return set.OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// Уточняет ключ колонки ПБ по тексту ячейки: курс в удостоверении важнее заголовка
    /// («Ответственный…» / «Проведение инструктажей…» в тексте → пишем в эту программу).
    /// </summary>
    private static string ResolveEffectiveColumnKey(string columnKey, string cellText, out string? warning)
    {
        warning = null;
        var key = (columnKey ?? string.Empty).Trim().ToLowerInvariant();
        if (!key.StartsWith("fire", StringComparison.Ordinal))
        {
            return key;
        }

        var inferred = TrainingScheduleExcelParser.InferFireColumnKeyFromCell(cellText);
        if (string.IsNullOrWhiteSpace(inferred))
        {
            return key;
        }

        if (string.Equals(key, inferred, StringComparison.OrdinalIgnoreCase))
        {
            return key;
        }

        // Всегда берём программу из текста удостоверения, если курс там назван явно.
        // Иначе «Ответственный… УЦ Специалист» из колонки «Инструктажи» попадал в
        // «Проведение инструктажей», а «…Проведение инструктажей 24 часа» — в «Специалист».
            var cellHours = TrainingScheduleExcelParser.TryExtractHours(cellText);
            var hoursNote = cellHours is { } h ? $", в ячейке {h} ч." : string.Empty;
            warning =
                $"программа по тексту ячейки — «{DescribeColumn(inferred)}» (колонка «{DescribeColumn(key)}»{hoursNote}); записано по ячейке";
            return inferred;
    }

    /// <returns>true, если программа добавлена; standard может быть заменён (новый record с Items).</returns>
    private static bool TryAddProgramToStandard(ref TrainingStandard standard, string programName)
    {
        var name = (programName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return false;
        }

        if (standard.Items is null)
        {
            standard = standard with { Items = [] };
        }

        var changed = false;
        if (MatchesHeight(name))
        {
            // Ячейка «3 гр…» → только 3 группа в стандарте, остальные группы высоты снимаем.
            changed = RemoveOtherHeightGroupsFromStandard(standard, name);
        }

        if (standard.Items.Any(i => string.Equals(i, name, StringComparison.OrdinalIgnoreCase)
                                    || OtProgramFamilyEquals(i, name)))
        {
            return changed;
        }

        standard.Items.Add(name);
        return true;
    }

    /// <summary>Оставляет в стандарте только выбранную группу высоты.</summary>
    private static bool RemoveOtherHeightGroupsFromStandard(TrainingStandard standard, string keepProgram)
    {
        if (standard.Items is null || standard.Items.Count == 0)
        {
            return false;
        }

        var keepRank = HeightGroupRank(keepProgram);
        if (keepRank is < 1 or > 3)
        {
            return false;
        }

        var removed = standard.Items.RemoveAll(i =>
            MatchesHeight(i ?? string.Empty)
            && !string.Equals(i, keepProgram, StringComparison.OrdinalIgnoreCase)
            && HeightGroupRank(i ?? string.Empty) is >= 1 and <= 3
            && HeightGroupRank(i ?? string.Empty) != keepRank);
        return removed > 0;
    }

    /// <summary>«Программа А» и «Программы А» — одна и та же строка стандарта.</summary>
    private static bool OtProgramFamilyEquals(string left, string right)
    {
        var a = NormalizeProgramName(left);
        var b = NormalizeProgramName(right);
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        static string? OtKey(string p)
        {
            if (MatchesProgramA(p)) return "a";
            if (MatchesProgramB(p)) return "b";
            if (MatchesProgramSiz(p) && !MatchesCompositeOt(p)) return "siz";
            if (MatchesProgramPp(p) && !MatchesCompositeOt(p)) return "pp";
            if (MatchesProgramVOneYear(p)) return "v1";
            if (MatchesProgramVThreeYear(p)) return "v3";
            if (MatchesHeight(p))
            {
                var g = HeightGroupRank(p);
                return g is >= 1 and <= 3 ? $"h{g}" : null;
            }

            // ПБ: не плодим «Инструктажи…» и «Проведение…» как две строки стандарта.
            if (MatchesFireConducting(p) || MatchesFireBriefings(p)) return "fire:brief";
            if (MatchesFireResponsible(p)) return "fire:resp";
            if (MatchesFireSpecialist(p)) return "fire:spec";

            return null;
        }

        var ka = OtKey(a);
        var kb = OtKey(b);
        return ka is not null && kb is not null && ka == kb;
    }

    /// <summary>
    /// Дополняет существующий стандарт программами из заполненных колонок Excel
    /// (повторный импорт — иначе строки в отчёте не появляются, пока программы не добавят вручную).
    /// </summary>
    private static int EnrichStandardFromFilledColumns(
        ref TrainingStandard standard,
        TrainingScheduleExcelPersonRow excelPerson,
        IReadOnlyDictionary<string, List<string>> columnToPrograms,
        IReadOnlyList<string> kindProgramNames,
        List<string> programCatalog)
    {
        var programs = CollectProgramsForPerson(excelPerson, columnToPrograms)
            .Where(p => IsAllowedAutoStandardProgram(p, kindProgramNames))
            .ToList();

        var added = 0;
        foreach (var program in programs)
        {
            if (!TryAddProgramToStandard(ref standard, program))
            {
                continue;
            }

            added++;
            if (!programCatalog.Contains(program, StringComparer.OrdinalIgnoreCase))
            {
                programCatalog.Add(program);
            }
        }

        return added;
    }

    private static void ReplaceStandardInLists(
        List<TrainingStandard> standards,
        List<TrainingStandard> orgStandards,
        TrainingStandard standard)
    {
        void Replace(List<TrainingStandard> list)
        {
            var idx = list.FindIndex(s => string.Equals(s.Id, standard.Id, StringComparison.Ordinal));
            if (idx >= 0)
            {
                list[idx] = standard;
            }
        }

        Replace(standards);
        Replace(orgStandards);
    }

    /// <summary>
    /// Программа В: до 01.09.2026 → «на 1 год», с 01.09.2026 → «на 3 года».
    /// Без даты обучения оставляем вариант «на 1 год» (как в колонке «В - 1 раз в год»).
    /// </summary>
    private static List<string> ResolveProgramVForDate(
        DateTime? trainingDate,
        IReadOnlyList<string> catalog)
    {
        var useThreeYear = trainingDate is { } d && d.Date >= ProgramVThreeYearFrom;
        var canonical = useThreeYear ? "Программа В (на 3 года)" : "Программа В (на 1 год)";
        var match = useThreeYear
            ? (Func<string, bool>)MatchesProgramVThreeYear
            : MatchesProgramVOneYear;
        return [CanonicalOtProgram(catalog, canonical, match)];
    }

    /// <summary>
    /// Колонки «ядра» охраны труда / ПБ — ими наполняем автостандарт.
    /// Свободные заголовки вроде «слесарь строительный» не входят: это не программы для любой профессии.
    /// </summary>
    private static bool IsCoreImportColumn(string? columnKey)
    {
        var key = (columnKey ?? string.Empty).Trim().ToLowerInvariant();
        if (key is "hire" or "a" or "ab" or "b" or "v" or "siz" or "pp"
            or "fire" or "height" or "electrical" or "industrial" or "sout" or "slinger" or "prof"
            || key.StartsWith("fire:", StringComparison.Ordinal)
            || key.StartsWith("height:", StringComparison.Ordinal))
        {
            return true;
        }

        if (key.StartsWith("pb:", StringComparison.Ordinal))
        {
            return true;
        }

        if (!key.StartsWith("h:", StringComparison.Ordinal))
        {
            return false;
        }

        var h = key[2..];
        return h.Contains("инструктаж", StringComparison.Ordinal)
               || h.Contains("программ", StringComparison.Ordinal)
               || h.Contains("сиз", StringComparison.Ordinal)
               || h.Contains("пожар", StringComparison.Ordinal)
               || h.Contains("высот", StringComparison.Ordinal)
               || h.Contains("электро", StringComparison.Ordinal)
               || h.Contains("стропаль", StringComparison.Ordinal)
               || h.Contains("вводн", StringComparison.Ordinal);
    }

    private static bool IsAllowedAutoStandardProgram(string program, IReadOnlyList<string> kindProgramNames)
    {
        var name = (program ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return false;
        }

        // Профессии/квалификации из Excel не кладём в стандарт автоматически.
        var lower = name.ToLowerInvariant().Replace('ё', 'е');
        if (LooksLikeProfessionQualificationTitle(lower))
        {
            return false;
        }

        // Канонические А/Б/В/СИЗ/ПП/ПБ/высота — всегда можно (как в справочнике видов).
        if (IsCanonicalImportProgram(name))
        {
            return true;
        }

        if (kindProgramNames.Count == 0)
        {
            return true;
        }

        if (kindProgramNames.Any(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Мягкое совпадение с видом обучения (полное имя в справочнике длиннее канона).
        return kindProgramNames.Any(k =>
            k.Contains(name, StringComparison.OrdinalIgnoreCase)
            || name.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCanonicalImportProgram(string program)
    {
        var p = NormalizeProgramName(program);
        return MatchesProgramA(p)
               || MatchesProgramB(p)
               || MatchesProgramVOneYear(p)
               || MatchesProgramVThreeYear(p)
               || MatchesProgramSiz(p)
               || MatchesProgramPp(p)
               || MatchesCompositeOt(p)
               || MatchesHeight(program)
               || MatchesAnyFire(p)
               || MatchesFireResponsible(p)
               || MatchesFireConducting(p)
               || MatchesFireBriefings(p)
               || IsHireBriefingProgram(program)
               || TrainingScheduleExcelParser.TryExtractIndustrialSafetyCode(program) is not null
               || (p.Contains("пром", StringComparison.Ordinal) && p.Contains("безопас", StringComparison.Ordinal));
    }

    private static bool LooksLikeProfessionQualificationTitle(string lower)
    {
        if (lower.Contains("инструктаж", StringComparison.Ordinal)
            || lower.Contains("программ", StringComparison.Ordinal)
            || lower.Contains("сиз", StringComparison.Ordinal)
            || lower.Contains("пожар", StringComparison.Ordinal)
            || lower.Contains("электробезопас", StringComparison.Ordinal)
            || lower.Contains("высот", StringComparison.Ordinal)
            || lower.Contains("соут", StringComparison.Ordinal)
            || lower.Contains("пром", StringComparison.Ordinal))
        {
            return false;
        }

        // «Слесарь строительный 3 разряд», «Бурильщик», «Стропальщик», «Рабочий люльки»…
        return lower.Contains("разряд", StringComparison.Ordinal)
               || lower.Contains("стропаль", StringComparison.Ordinal)
               || lower.Contains("люльк", StringComparison.Ordinal)
               || lower.Contains("профобуч", StringComparison.Ordinal)
               || lower.Contains("проф обуч", StringComparison.Ordinal)
               || Regex.IsMatch(lower, @"\b(слесарь|бурильщик|бетонщик|штукатур|плотник|маляр|сварщик|монтажник|машинист|арматурщик|каменщик|крановщик)\b");
    }

    private static TrainingStandard CreateStandardFromImport(
        string organization,
        string profession,
        IReadOnlyList<string> programs)
    {
        var cleanProfession = ProfessionMatcher.Normalize(profession);
        if (cleanProfession.Length == 0)
        {
            cleanProfession = profession.Trim();
        }

        var items = programs
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new TrainingStandard(
            Id: Guid.NewGuid().ToString("N"),
            Name: cleanProfession,
            Organization: organization.Trim(),
            Profession: cleanProfession,
            Items: items,
            CreatedAt: DateTime.UtcNow,
            Category: "Обучение",
            Professions: [cleanProfession],
            PersonnelCategory: TrainingKindCatalogDefaults.PersonnelCategoryOptions.FirstOrDefault() ?? "Рабочие",
            TrainingMethod: "Своими силами",
            Version: 1,
            EffectiveFrom: DateTime.Today);
    }

    /// <summary>
    /// Ключ набора программ: «Программа А» и «Программы А» считаются одной.
    /// Профессии с одинаковым ключом получают общий стандарт при импорте.
    /// </summary>
    private static string BuildProgramSetKey(IEnumerable<string> programs)
    {
        var keys = programs
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(ProgramFamilyKey)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);
        return string.Join("|", keys);
    }

    private static string ProgramFamilyKey(string program)
    {
        var p = NormalizeProgramName(program);
        if (MatchesProgramA(p)) return "a";
        if (MatchesProgramB(p)) return "b";
        if (MatchesProgramSiz(p) && !MatchesCompositeOt(p)) return "siz";
        if (MatchesProgramPp(p) && !MatchesCompositeOt(p)) return "pp";
        if (MatchesProgramVOneYear(p)) return "v1";
        if (MatchesProgramVThreeYear(p)) return "v3";
        if (MatchesHeight(p))
        {
            var g = HeightGroupRank(p);
            return g is >= 1 and <= 3 ? $"h{g}" : "height:" + p;
        }

        if (MatchesFireResponsible(p)) return "fire:responsible";
        if (p.Contains("проведен", StringComparison.Ordinal) && p.Contains("инструктаж", StringComparison.Ordinal)
            && (p.Contains("пожар", StringComparison.Ordinal) || p.Contains(" пб", StringComparison.Ordinal) || p.EndsWith("пб", StringComparison.Ordinal)))
        {
            return "fire:briefings";
        }

        if (p.Contains("обучен", StringComparison.Ordinal) && p.Contains("пожар", StringComparison.Ordinal))
        {
            return "fire:training";
        }

        return p;
    }

    private static bool TryFindStandardByProgramSet(
        IReadOnlyList<TrainingStandard> orgStandards,
        IReadOnlyList<string> programs,
        bool isIePerson,
        out TrainingStandard? standard)
    {
        standard = null;
        var want = BuildProgramSetKey(programs);
        if (string.IsNullOrWhiteSpace(want))
        {
            return false;
        }

        foreach (var candidate in orgStandards)
        {
            if (IsIndividualEntrepreneurStandard(candidate) && !isIePerson)
            {
                continue;
            }

            if (IsIndividualEntrepreneurStandard(candidate) && isIePerson)
            {
                // ИП не объединяем с наёмными по набору программ.
                continue;
            }

            var have = BuildProgramSetKey(candidate.Items ?? []);
            if (!string.Equals(have, want, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            standard = candidate;
            return true;
        }

        return false;
    }

    private static TrainingStandard EnsureProfessionOnStandard(TrainingStandard standard, string profession)
    {
        var clean = ProfessionMatcher.Normalize(profession);
        if (clean.Length == 0)
        {
            clean = profession.Trim();
        }

        if (clean.Length == 0)
        {
            return standard;
        }

        var list = standard.AllProfessions.ToList();
        if (list.Any(p => string.Equals(p, clean, StringComparison.OrdinalIgnoreCase)))
        {
            return standard with { Professions = list };
        }

        list.Add(clean);
        var name = list.Count <= 3
            ? string.Join(", ", list)
            : $"{list[0]} и ещё {list.Count - 1}";

        return standard with
        {
            Name = name,
            Profession = list[0],
            Professions = list
        };
    }

    private static bool AreSameProfessionList(TrainingStandard a, TrainingStandard b)
    {
        var left = a.AllProfessions;
        var right = b.AllProfessions;
        if (left.Count != right.Count)
        {
            return false;
        }

        return left.All(p => right.Any(q => string.Equals(p, q, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Колонка «В - 1 раз в год» → отдельная «Программа В» (если есть в стандарте/отчёте).
    /// </summary>
    private static List<string> MatchProgramV(IReadOnlyList<string> programs)
    {
        var hits = programs
            .Where(IsStandaloneProgramV)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p.Length)
            .ToList();

        if (hits.Count > 0)
        {
            return hits;
        }

        hits = MatchProgramsForColumn("v", programs)
            .Where(p => !p.Contains("вводн", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return hits.Count > 0 ? hits : ["Программа В"];
    }

    private static bool IsStandaloneProgramV(string program)
    {
        var p = program.ToLowerInvariant().Replace('ё', 'е');
        p = Regex.Replace(p, @"\s+", " ").Trim();
        if (p.Contains("вводн", StringComparison.Ordinal))
        {
            return false;
        }

        if (Regex.IsMatch(p, @"^программа\s+в\b", RegexOptions.IgnoreCase))
        {
            return true;
        }

        return Regex.IsMatch(p, @"^в\s*[-–.]?\s*1\b", RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Колонка «Работы на высоте - 1 гр, 2 гр…» → программы «работы на высоте …».
    /// </summary>
    private static List<string> MatchHeightPrograms(IReadOnlyList<string> programs)
    {
        var hits = programs
            .Where(MatchesHeight)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => HeightGroupRank(p))
            .ThenBy(p => p, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return hits.Count > 0
            ? hits
            : [CanonicalHeightProgram(1)];
    }

    /// <summary>
    /// По ключу колонки (height:2) и/или тексту ячейки («1 гр Протокол №…») —
    /// только нужная группа. Без указания группы не размазываем протокол по 1/2/3.
    /// </summary>
    private static List<string> ResolveHeightProgramsForCell(
        string? columnKey,
        string? cellText,
        IReadOnlyList<string> catalog)
    {
        var all = MatchHeightPrograms(catalog);
        var group = ResolveHeightGroup(columnKey, cellText);
        if (group is null)
        {
            // Раньше возвращали все группы — из‑за этого «1 гр» без распознавания
            // попадал сразу в 1, 2 и 3. Без группы не угадываем.
            return [];
        }

        var matched = all
            .Where(p => HeightGroupRank(p) == group.Value)
            .ToList();
        if (matched.Count > 0)
        {
            return matched;
        }

        return [CanonicalHeightProgram(group.Value)];
    }

    /// <summary>Группа: сначала из ячейки («2 гр…»), иначе из ключа колонки height:N.</summary>
    private static int? ResolveHeightGroup(string? columnKey, string? cellText)
    {
        var fromCell = InferHeightGroupFromCell(cellText);
        if (fromCell is not null)
        {
            return fromCell;
        }

        var key = (columnKey ?? string.Empty).Trim().ToLowerInvariant();
        if (key.StartsWith("height:", StringComparison.Ordinal)
            && int.TryParse(key["height:".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g)
            && g is >= 1 and <= 3)
        {
            return g;
        }

        return null;
    }

    /// <summary>«1 гр», «2 группа», «гр.3», «группа 1» в начале/теле удостоверения.</summary>
    internal static int? InferHeightGroupFromCell(string? cellText)
        => TrainingScheduleExcelParser.InferHeightGroupFromHeaderText(cellText);

    private static int HeightGroupRank(string program)
    {
        var p = program.ToLowerInvariant().Replace('ё', 'е');
        // Сначала «N группа» в названии программы — надёжнее, чем короткое «N гр».
        var named = Regex.Match(p, @"высот\w*.*?\b([123])\s*групп", RegexOptions.IgnoreCase);
        if (named.Success
            && int.TryParse(named.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fromName)
            && fromName is >= 1 and <= 3)
        {
            return fromName;
        }

        // Программы без слова «высота», но с «1 группа» / «2 гр».
        var bare = Regex.Match(p, @"\b([123])\s*групп", RegexOptions.IgnoreCase);
        if (bare.Success
            && int.TryParse(bare.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fromBare)
            && fromBare is >= 1 and <= 3)
        {
            return fromBare;
        }

        if (Regex.IsMatch(p, @"\b1\s*гр(?:\.|\b)|групп[аы]?\s*1\b|(?:^|[^\d])1\s*групп"))
        {
            return 1;
        }

        if (Regex.IsMatch(p, @"\b2\s*гр(?:\.|\b)|групп[аы]?\s*2\b|(?:^|[^\d])2\s*групп"))
        {
            return 2;
        }

        if (Regex.IsMatch(p, @"\b3\s*гр(?:\.|\b)|групп[аы]?\s*3\b|(?:^|[^\d])3\s*групп"))
        {
            return 3;
        }

        return 9;
    }

    private static string CanonicalHeightProgram(int group) => group switch
    {
        2 => "Работы на высоте 2 группа (бригадиры, ответственные исполнители по наряду)",
        3 => "Работы на высоте 3 группа (ответственные за организацию работ, выдающие наряды-допуски, преподаватели)",
        _ => "Работы на высоте 1 группа (в составе бригады)"
    };

    private static List<string> MatchHireBriefingPrograms(IReadOnlyList<string> programs)
    {
        var hits = new List<string>();
        foreach (var program in programs)
        {
            if (IsHireBriefingProgram(program))
            {
                hits.Add(program);
            }
        }

        // Если в каталоге нет точных имён — подставляем канонические
        if (hits.Count == 0)
        {
            return
            [
                "Вводный инструктаж по охране труда",
                "Вводный инструктаж по пожарной безопасности",
                "Первичный инструктаж на рабочем месте по пожарной безопасности"
            ];
        }

        return hits
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => HireBriefingRank(p))
            .ThenBy(p => p, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool IsHireBriefingProgram(string program)
    {
        var p = program.ToLowerInvariant().Replace('ё', 'е');
        p = Regex.Replace(p, @"\s+", " ").Trim();

        // Вводный инструктаж по охране труда (не пожарный)
        if (p.Contains("вводн", StringComparison.Ordinal)
            && (p.Contains("охране труда", StringComparison.Ordinal)
                || (p.Contains("охране", StringComparison.Ordinal) && p.Contains("труд", StringComparison.Ordinal)))
            && !p.Contains("пожар", StringComparison.Ordinal)
            && !p.Contains("противопожар", StringComparison.Ordinal))
        {
            return true;
        }

        // Вводный инструктаж по пожарной безопасности
        if (p.Contains("вводн", StringComparison.Ordinal)
            && (p.Contains("пожар", StringComparison.Ordinal) || p.Contains("противопожар", StringComparison.Ordinal)))
        {
            return true;
        }

        // Первичный инструктаж на рабочем месте по пожарной безопасности
        if (p.Contains("первичн", StringComparison.Ordinal)
            && (p.Contains("пожар", StringComparison.Ordinal) || p.Contains("противопожар", StringComparison.Ordinal))
            && (p.Contains("рабоч", StringComparison.Ordinal) || p.Contains("место", StringComparison.Ordinal)))
        {
            return true;
        }

        return false;
    }

    private static int HireBriefingRank(string program)
    {
        var p = program.ToLowerInvariant().Replace('ё', 'е');
        if (p.Contains("вводн", StringComparison.Ordinal)
            && !p.Contains("пожар", StringComparison.Ordinal)
            && !p.Contains("противопожар", StringComparison.Ordinal))
        {
            return 0;
        }

        if (p.Contains("вводн", StringComparison.Ordinal)
            && (p.Contains("пожар", StringComparison.Ordinal) || p.Contains("противопожар", StringComparison.Ordinal)))
        {
            return 1;
        }

        if (p.Contains("первичн", StringComparison.Ordinal))
        {
            return 2;
        }

        return 9;
    }

    /// <summary>Все программы А, Б, СИЗ, ПП (и составная А+Б+СИЗ+ПП) — даты в графике одинаковые.</summary>
    private static List<string> MatchAbSizPpPrograms(IReadOnlyList<string> programs)
    {
        var hits = new List<string>();
        foreach (var program in programs)
        {
            var p = program.ToLowerInvariant().Replace('ё', 'е');
            p = Regex.Replace(p, @"\s+", " ").Trim();
            if (MatchesAb(p) || MatchesB(p) || MatchesCompositeOt(p)
                || p.Contains("сиз", StringComparison.Ordinal)
                || p.Contains("первая помощ", StringComparison.Ordinal)
                || p.Contains("перв. помощ", StringComparison.Ordinal)
                || Regex.IsMatch(p, @"\bпп\b", RegexOptions.IgnoreCase))
            {
                // Не тащим «программу В»
                if (MatchesV(p) && !MatchesAb(p) && !MatchesCompositeOt(p)
                    && !p.Contains("сиз", StringComparison.Ordinal)
                    && !Regex.IsMatch(p, @"\bпп\b", RegexOptions.IgnoreCase)
                    && !p.Contains("первая помощ", StringComparison.Ordinal))
                {
                    continue;
                }

                hits.Add(program);
            }
        }

        if (hits.Count == 0)
        {
            return ["Программы А+Б+СИЗ+ПП"];
        }

        return hits.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string PickBestProgram(string columnKey, List<string> matched)
    {
        if (matched.Count == 1)
        {
            return matched[0];
        }

        var key = columnKey.Trim().ToLowerInvariant();
        // Для А/Б/СИЗ/ПП больше не схлопываем в составную «А+Б+СИЗ+ПП».

        if (key is "v")
        {
            var progV = matched.FirstOrDefault(p =>
                MatchesProgramVOneYear(NormalizeProgramName(p))
                || MatchesProgramVThreeYear(NormalizeProgramName(p))
                || p.Contains("программа в", StringComparison.OrdinalIgnoreCase));
            if (progV is not null)
            {
                return progV;
            }
        }

        if (key.StartsWith("fire", StringComparison.Ordinal))
        {
            var preferred = PickBestFireProgram(key, matched);
            if (preferred is not null)
            {
                return preferred;
            }
        }

        if (key.StartsWith("pb:", StringComparison.Ordinal))
        {
            var code = key[3..];
            var exact = matched
                .Select(p => (Program: p, Code: TrainingScheduleExcelParser.TryExtractIndustrialSafetyCode(p)))
                .Where(x => x.Code is not null
                            && string.Equals(x.Code, NormalizePbCodeToken(code), StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Program.Length)
                .Select(x => x.Program)
                .FirstOrDefault();
            if (exact is not null)
            {
                return exact;
            }
        }

        // Более короткое точное имя обычно лучше общего
        return matched.OrderBy(p => p.Length).First();
    }

    private static string? PickBestFireProgram(string key, List<string> matched)
    {
        static string Norm(string s) => s.ToLowerInvariant().Replace('ё', 'е');

        IEnumerable<string> Prefer(Func<string, bool> pred) =>
            matched.Where(p => pred(Norm(p))).OrderBy(p => p.Length);

        return key switch
        {
            "fire:responsible" => Prefer(n =>
                    n.Contains("ответственн")
                    && n.Contains("обучен")
                    && n.Contains("программ")).FirstOrDefault()
                ?? Prefer(n => n.Contains("ответственн")).OrderByDescending(p => p.Length).FirstOrDefault(),
            "fire:conducting" => Prefer(n => n.Contains("проведен") && n.Contains("инструктаж")).FirstOrDefault()
                ?? Prefer(n =>
                    (n.Contains("инструктаж") || n.Contains("инструктажи"))
                    && !n.Contains("ответственн")
                    && !n.Contains("вводн")
                    && !n.Contains("первичн")
                        && !n.Contains("повторн")
                        && !n.Contains("специалист"))
                    .FirstOrDefault(),
            // Excel «Инструктажи по ПБ» → в отчёте обычно «Проведение инструктажей по ПБ».
            "fire:briefings" => Prefer(n => n.Contains("проведен") && n.Contains("инструктаж")).FirstOrDefault()
                ?? Prefer(n =>
                        (n.Contains("инструктаж") || n.Contains("инструктажи"))
                        && !n.Contains("ответственн")
                        && !n.Contains("вводн")
                        && !n.Contains("первичн")
                        && !n.Contains("повторн")
                        && !n.Contains("специалист"))
                    .FirstOrDefault(),
            "fire:intro" => Prefer(n => n.Contains("вводн")).FirstOrDefault(),
            "fire:primary" => Prefer(n => n.Contains("первичн")).FirstOrDefault(),
            "fire:repeat" => Prefer(n => n.Contains("повторн")).FirstOrDefault(),
            "fire:specialist" => Prefer(n => n.Contains("специалист") && n.Contains("профилактик")).FirstOrDefault()
                ?? Prefer(n => n.Contains("специалист") && n.Contains("пожар")).FirstOrDefault(),
            "fire:training" => Prefer(n => n.Contains("обучен") && n.Contains("мер")).FirstOrDefault(),
            // Общий «Пожарная безопасность» без подтипа — не выбираем инструктажи наугад.
            "fire" => Prefer(n => n.Contains("обучен") && n.Contains("мер")).FirstOrDefault()
                      ?? Prefer(n => n.Contains("ответственн")).FirstOrDefault()
                      ?? Prefer(n => !n.Contains("инструктаж") && !n.Contains("проведен")).FirstOrDefault(),
            // null → ResolveProgramsForColumn возьмёт Default, не «Проведение инструктажей»
            _ => null
        };
    }

    /// <summary>
    /// Снимает то же удостоверение с других программ ПБ (ошибочный прошлый импорт).
    /// </summary>
    /// <returns>true, если хотя бы одна запись очищена (нужно сохранить conduct).</returns>
    private static bool ClearMisplacedFireProtocol(
        List<TrainingConductRecord> list,
        string organization,
        string person,
        string protocol,
        string keepProgram)
    {
        var proto = protocol.Trim();
        if (proto.Length < 8)
        {
            return false;
        }

        var cleared = false;
        foreach (var record in list)
        {
            if (record.IsArchivedSnapshot)
            {
                continue;
            }

            if (!string.Equals(record.Person, person, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(record.Organization)
                && !OrganizationMatcher.Matches(record.Organization, organization)
                && !string.Equals(record.Organization, organization, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // «Инструктажи…» и «Проведение…» — одна программа; не стираем «своё» удостоверение.
            if (string.Equals(record.Program, keepProgram, StringComparison.OrdinalIgnoreCase)
                || ProgramsEqualForConduct(record.Program, keepProgram))
            {
                continue;
            }

            var program = record.Program ?? string.Empty;
            var p = program.ToLowerInvariant().Replace('ё', 'е');
            if (!MatchesAnyFire(p))
            {
                continue;
            }

            if (!IsSameFireProtocol(proto, record.ProtocolNumber)
                && !IsSameFireProtocol(proto, record.CertificateNumber))
            {
                continue;
            }

            record.ProtocolNumber = string.Empty;
            record.CertificateNumber = string.Empty;
            record.TrainingDate = null;
            record.NextTrainingDate = null;
            if (string.IsNullOrWhiteSpace(record.Result)
                || !record.Result.Contains("не проводилось", StringComparison.OrdinalIgnoreCase))
            {
                record.Result = "Обучение не проводилось";
            }

            cleared = true;
        }

        return cleared;
    }

    /// <summary>
    /// Снимает тот же протокол высоты с других групп (ошибочный прошлый импорт /
    /// колонка «1 гр» с текстом «2 гр…»).
    /// </summary>
    private static bool ClearMisplacedHeightProtocol(
        List<TrainingConductRecord> list,
        string organization,
        string person,
        string protocol,
        string keepProgram)
    {
        var proto = protocol.Trim();
        if (proto.Length < 4)
        {
            return false;
        }

        var cleared = false;
        foreach (var record in list)
        {
            if (record.IsArchivedSnapshot)
            {
                continue;
            }

            if (!string.Equals(record.Person, person, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(record.Organization)
                && !OrganizationMatcher.Matches(record.Organization, organization)
                && !string.Equals(record.Organization, organization, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(record.Program, keepProgram, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!MatchesHeight(record.Program ?? string.Empty))
            {
                continue;
            }

            if (!IsSameHeightProtocol(proto, record.ProtocolNumber)
                && !IsSameHeightProtocol(proto, record.CertificateNumber))
            {
                continue;
            }

            record.ProtocolNumber = string.Empty;
            record.CertificateNumber = string.Empty;
            record.TrainingDate = null;
            record.NextTrainingDate = null;
            if (string.IsNullOrWhiteSpace(record.Result)
                || !record.Result.Contains("не проводилось", StringComparison.OrdinalIgnoreCase))
            {
                record.Result = "Обучение не проводилось";
            }

            cleared = true;
        }

        return cleared;
    }

    /// <summary>
    /// Тот же протокол высоты: полное совпадение или один номер после «№»,
    /// с игнорированием префикса «2 гр» / «3 группа».
    /// </summary>
    private static bool IsSameHeightProtocol(string incoming, string? stored)
    {
        if (string.IsNullOrWhiteSpace(incoming) || string.IsNullOrWhiteSpace(stored))
        {
            return false;
        }

        var a = StripHeightGroupPrefix(incoming);
        var b = StripHeightGroupPrefix(stored);
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsSameFireProtocol(a, b);
    }

    private static string StripHeightGroupPrefix(string text)
    {
        var t = (text ?? string.Empty).Trim();
        t = Regex.Replace(
            t,
            @"^\s*[123]\s*[-.]?\s*(?:я|й|ой|ая)?\s*гр(?:упп\w*)?\.?\s*",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return t.Trim();
    }

    /// <summary>Колонки А/Б/В/СИЗ/ПП — туда не пишем пометки места обучения.</summary>
    private static bool IsOtCoreProtocolColumn(string? columnKey)
    {
        var key = (columnKey ?? string.Empty).Trim().ToLowerInvariant();
        return key is "a" or "b" or "ab" or "v" or "siz" or "pp";
    }

    private static bool IsOtCoreProgramName(string? program)
    {
        if (string.IsNullOrWhiteSpace(program))
        {
            return false;
        }

        return MatchesProgramA(program)
               || MatchesProgramB(program)
               || MatchesProgramVOneYear(program)
               || MatchesProgramVThreeYear(program)
               || MatchesProgramSiz(program)
               || MatchesProgramPp(program);
    }

    /// <summary>
    /// Зелёная заливка / «а» без точного сопоставления колонки → метки по уже существующим
    /// строкам conduct этого человека (не создаём пустые записи).
    /// </summary>
    private static void ApplyGreenMarkersToPersonPrograms(
        List<TrainingConductRecord> conductList,
        string organization,
        string reportPerson,
        string? protocolMovement,
        string? updStatus,
        bool forArchive,
        IReadOnlySet<string>? employedNames,
        bool overwriteExisting,
        TrainingScheduleImportResult result)
    {
        if (string.IsNullOrWhiteSpace(protocolMovement) && string.IsNullOrWhiteSpace(updStatus))
        {
            return;
        }

        foreach (var rec in conductList
                     .Where(c =>
                         string.Equals(c.Person, reportPerson, StringComparison.OrdinalIgnoreCase)
                         && (string.IsNullOrWhiteSpace(c.Organization)
                             || OrganizationMatcher.Matches(c.Organization, organization)
                             || string.Equals(c.Organization, organization, StringComparison.OrdinalIgnoreCase))
                         && !string.IsNullOrWhiteSpace(c.Program))
                     .ToList())
        {
            if (ApplyOtMarkersToConduct(
                    conductList,
                    organization,
                    reportPerson,
                    rec.Program,
                    protocolMovement,
                    updStatus,
                    forArchive,
                    employedNames,
                    overwriteExisting,
                    out var preserved))
            {
                result.UpdatedCells++;
            }
            else if (preserved)
            {
                result.PreservedFilledCells++;
            }
        }
    }

    /// <summary>
    /// Зелёная заливка / буква «а» → движение протокола / УПД без изменения даты и номера.
    /// Не создаёт пустую запись conduct, только обновляет существующую.
    /// </summary>
    private static bool ApplyOtMarkersToConduct(
        List<TrainingConductRecord> list,
        string organization,
        string person,
        string programName,
        string? protocolMovement,
        string? updStatus,
        bool forArchive,
        IReadOnlySet<string>? employedNames,
        bool overwriteExisting,
        out bool preservedFilled)
    {
        preservedFilled = false;
        if (string.IsNullOrWhiteSpace(programName)
            || (string.IsNullOrWhiteSpace(protocolMovement) && string.IsNullOrWhiteSpace(updStatus)))
        {
            return false;
        }

        bool MatchesPersonProgram(TrainingConductRecord x) =>
            string.Equals(x.Person, person, StringComparison.OrdinalIgnoreCase)
            && ProgramsEqualForConduct(x.Program, programName)
            && (string.IsNullOrWhiteSpace(x.Organization)
                || OrganizationMatcher.Matches(x.Organization, organization)
                || string.Equals(x.Organization, organization, StringComparison.OrdinalIgnoreCase));

        var wantSnapshot = forArchive
                           && employedNames is not null
                           && employedNames.Contains((person ?? string.Empty).Trim());

        TrainingConductRecord? existing = null;
        if (forArchive)
        {
            if (wantSnapshot)
            {
                existing = list.FirstOrDefault(x => x.IsArchivedSnapshot && MatchesPersonProgram(x))
                           ?? list.FirstOrDefault(x => !x.IsArchivedSnapshot && MatchesPersonProgram(x));
            }
            else
            {
                existing = list.FirstOrDefault(x => !x.IsArchivedSnapshot && MatchesPersonProgram(x))
                           ?? list.FirstOrDefault(x => x.IsArchivedSnapshot && MatchesPersonProgram(x));
            }
        }
        else
        {
            existing = list.FirstOrDefault(x => !x.IsArchivedSnapshot && MatchesPersonProgram(x));
        }

        if (existing is null)
        {
            return false;
        }

        var fillOnlyEmpty = !overwriteExisting;
        return ApplyProtocolMarkers(existing, protocolMovement, updStatus, fillOnlyEmpty, ref preservedFilled);
    }

    private static bool ApplyProtocolMarkers(
        TrainingConductRecord existing,
        string? protocolMovement,
        string? updStatus,
        bool fillOnlyEmpty,
        ref bool preservedFilled)
    {
        var changed = false;

        if (!string.IsNullOrWhiteSpace(protocolMovement))
        {
            var next = TrainingConductValues.NormalizeProtocolMovement(protocolMovement);
            var current = TrainingConductValues.NormalizeProtocolMovement(existing.ProtocolMovement);
            if (!string.Equals(current, next, StringComparison.OrdinalIgnoreCase))
            {
                if (fillOnlyEmpty && !string.IsNullOrWhiteSpace(current))
                {
                    preservedFilled = true;
                }
                else
                {
                    existing.ProtocolMovement = next;
                    changed = true;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(updStatus))
        {
            var next = TrainingConductValues.NormalizeUpdStatus(updStatus);
            var current = TrainingConductValues.NormalizeUpdStatus(existing.UpdStatus);
            if (!string.Equals(current, next, StringComparison.OrdinalIgnoreCase))
            {
                if (fillOnlyEmpty && !string.IsNullOrWhiteSpace(current))
                {
                    preservedFilled = true;
                }
                else
                {
                    existing.UpdStatus = next;
                    changed = true;
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Одинаковое удостоверение/протокол ПБ (номер после «№» или полное совпадение текста).
    /// </summary>
    private static bool IsSameFireProtocol(string incoming, string? stored)
    {
        if (string.IsNullOrWhiteSpace(incoming) || string.IsNullOrWhiteSpace(stored))
        {
            return false;
        }

        var a = incoming.Trim();
        var b = stored.Trim();
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        static string? ExtractNumber(string text)
        {
            var m = Regex.Match(
                text,
                @"№\s*([A-Za-zА-Яа-я0-9][A-Za-zА-Яа-я0-9./\-–—]*)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!m.Success)
            {
                return null;
            }

            return Regex.Replace(m.Groups[1].Value, @"\s+", string.Empty).TrimEnd('.', ',');
        }

        var na = ExtractNumber(a);
        var nb = ExtractNumber(b);
        if (!string.IsNullOrWhiteSpace(na)
            && !string.IsNullOrWhiteSpace(nb)
            && string.Equals(na, nb, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Короткий хвост номера внутри длинного текста — только если длина ≥ 8, чтобы не цеплять «ПБ».
        if (b.Length >= 8 && a.Contains(b, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (a.Length >= 8 && b.Contains(a, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static int ProfessionScore(TrainingStandard standard, string profession)
    {
        var all = standard.AllProfessions;
        if (all.Count == 0)
        {
            return 0;
        }

        var best = 0;
        foreach (var p in all)
        {
            if (string.Equals(p, profession, StringComparison.OrdinalIgnoreCase))
            {
                return 100;
            }

            if (ProfessionMatcher.Matches(profession, p))
            {
                best = Math.Max(best, 50);
            }
        }

        return best;
    }

    private static bool IsIndividualEntrepreneurStandard(TrainingStandard standard)
    {
        static bool IsIe(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            return text.Contains("индивидуальн", StringComparison.OrdinalIgnoreCase)
                   && text.Contains("предпринимател", StringComparison.OrdinalIgnoreCase);
        }

        return IsIe(standard.Name)
               || IsIe(standard.Profession)
               || standard.AllProfessions.Any(IsIe);
    }

    private static void ClearVenueNoteProtocol(
        List<TrainingConductRecord> list,
        string organization,
        string person,
        string programName,
        TrainingScheduleImportResult result)
    {
        foreach (var record in list.Where(r =>
                     !r.IsArchivedSnapshot
                     && string.Equals(r.Person, person, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(r.Program, programName, StringComparison.OrdinalIgnoreCase)
                     && (string.IsNullOrWhiteSpace(r.Organization)
                         || OrganizationMatcher.Matches(r.Organization, organization)
                         || string.Equals(r.Organization, organization, StringComparison.OrdinalIgnoreCase))))
        {
            var protocol = record.ProtocolNumber ?? record.CertificateNumber ?? string.Empty;
            if (!TrainingScheduleExcelParser.IsEmploymentOrVenueNoteCell(protocol)
                && !TrainingScheduleExcelParser.IsEmploymentOrVenueNoteCell(record.Comment))
            {
                continue;
            }

            record.ProtocolNumber = string.Empty;
            record.CertificateNumber = string.Empty;
            if (record.TrainingDate is null)
            {
                record.Result = "Не проводилось";
                record.NextTrainingDate = null;
            }

            result.UpdatedCells++;
        }
    }

    /// <summary>
    /// Если в ячейке «2 гр Протокол…», тот же текст не должен оставаться у 1/3 группы.
    /// </summary>
    private static void ClearHeightProtocolFromOtherGroups(
        List<TrainingConductRecord> list,
        string organization,
        string person,
        string protocol,
        IReadOnlyList<string> keepPrograms,
        TrainingScheduleImportResult result)
    {
        if (string.IsNullOrWhiteSpace(protocol))
        {
            return;
        }

        var keep = keepPrograms
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var protocolNorm = protocol.Trim();

        foreach (var record in list.Where(r =>
                     !r.IsArchivedSnapshot
                     && string.Equals(r.Person, person, StringComparison.OrdinalIgnoreCase)
                     && MatchesHeight(r.Program ?? string.Empty)
                     && (string.IsNullOrWhiteSpace(r.Organization)
                         || OrganizationMatcher.Matches(r.Organization, organization)
                         || string.Equals(r.Organization, organization, StringComparison.OrdinalIgnoreCase))))
        {
            if (keep.Contains(record.Program ?? string.Empty))
            {
                continue;
            }

            var existing = (record.ProtocolNumber ?? record.CertificateNumber ?? string.Empty).Trim();
            if (existing.Length == 0)
            {
                continue;
            }

            // Тот же протокол / тот же текст «2 гр…» на чужой группе — убираем.
            if (!string.Equals(existing, protocolNorm, StringComparison.OrdinalIgnoreCase)
                && !existing.Contains(protocolNorm, StringComparison.OrdinalIgnoreCase)
                && !protocolNorm.Contains(existing, StringComparison.OrdinalIgnoreCase))
            {
                var cellGroup = TrainingScheduleExcelParser.InferHeightGroupFromHeaderText(protocolNorm);
                var recordGroup = HeightGroupRank(record.Program ?? string.Empty);
                if (cellGroup is null || recordGroup == cellGroup.Value)
                {
                    continue;
                }
            }

            record.ProtocolNumber = string.Empty;
            record.CertificateNumber = string.Empty;
            if (record.TrainingDate is null)
            {
                record.Result = "Не проводилось";
                record.NextTrainingDate = null;
            }

            result.UpdatedCells++;
        }
    }

    private static void UpsertAssignment(
        List<TrainingPersonStandardAssignment> list,
        Dictionary<string, string> map,
        string organization,
        string person,
        string profession,
        string standardId)
    {
        var key = TrainingReportBuilder.AssignmentKey(organization, person, profession);
        map[key] = standardId;
        var existing = list.FirstOrDefault(a =>
            string.Equals(
                TrainingReportBuilder.AssignmentKey(a.Organization, a.Person, a.Profession),
                key,
                StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.StandardId = standardId;
            return;
        }

        list.Add(new TrainingPersonStandardAssignment
        {
            Organization = organization,
            Person = person,
            Profession = profession,
            StandardId = standardId
        });
    }

    /// <summary>Галочка «Журнал» из колонки «Ж» графика (ячейка «ж»).</summary>
    private static bool ApplyPersonJournalFlag(
        List<TrainingPersonStandardAssignment> list,
        string organization,
        string person,
        string profession)
    {
        var key = TrainingReportBuilder.AssignmentKey(organization, person, profession);
        var existing = list.FirstOrDefault(a =>
            string.Equals(
                TrainingReportBuilder.AssignmentKey(a.Organization, a.Person, a.Profession),
                key,
                StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (existing.Journal)
            {
                return false;
            }

            existing.Journal = true;
            return true;
        }

        list.Add(new TrainingPersonStandardAssignment
        {
            Organization = organization,
            Person = person,
            Profession = profession,
            StandardId = string.Empty,
            Journal = true
        });
        return true;
    }

    /// <summary>«Архитектор (Офис БЦ Демидов)» → поле «объект» у сотрудника.</summary>
    private static bool ApplyPersonObjectNote(
        List<TrainingPersonStandardAssignment> list,
        string organization,
        string person,
        string profession,
        string objectNote)
    {
        var note = (objectNote ?? string.Empty).Trim();
        if (note.Length == 0)
        {
            return false;
        }

        var key = TrainingReportBuilder.AssignmentKey(organization, person, profession);
        var existing = list.FirstOrDefault(a =>
            string.Equals(
                TrainingReportBuilder.AssignmentKey(a.Organization, a.Person, a.Profession),
                key,
                StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (string.Equals(existing.ObjectNote?.Trim(), note, StringComparison.Ordinal))
            {
                return false;
            }

            existing.ObjectNote = note;
            return true;
        }

        list.Add(new TrainingPersonStandardAssignment
        {
            Organization = organization,
            Person = person,
            Profession = profession,
            StandardId = string.Empty,
            ObjectNote = note
        });
        return true;
    }

    /// <summary>
    /// «Инструктажи по ПБ» и «Проведение инструктажей по ПБ» — одна запись проведения.
    /// </summary>
    private static bool ProgramsEqualForConduct(string? left, string? right)
    {
        var a = (left ?? string.Empty).Trim();
        var b = (right ?? string.Empty).Trim();
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (OtProgramFamilyEquals(a, b))
        {
            return true;
        }

        var na = NormalizeProgramName(a);
        var nb = NormalizeProgramName(b);

        static bool IsFireBriefingsFamily(string n) =>
            (n.Contains("инструктаж", StringComparison.Ordinal) || n.Contains("инструктажи", StringComparison.Ordinal))
            && (n.Contains("пб", StringComparison.Ordinal)
                || n.Contains("пожар", StringComparison.Ordinal)
                || n.Contains("противопожар", StringComparison.Ordinal))
            && !n.Contains("ответственн", StringComparison.Ordinal)
            && !n.Contains("вводн", StringComparison.Ordinal)
            && !n.Contains("первичн", StringComparison.Ordinal)
            && !n.Contains("повторн", StringComparison.Ordinal)
            && !n.Contains("специалист", StringComparison.Ordinal);

        if (IsFireBriefingsFamily(na) && IsFireBriefingsFamily(nb))
        {
            return true;
        }

        static bool IsFireResponsibleFamily(string n) =>
            n.Contains("ответственн", StringComparison.Ordinal)
            && (n.Contains("пб", StringComparison.Ordinal)
                || n.Contains("пожар", StringComparison.Ordinal)
                || n.Contains("обеспечен", StringComparison.Ordinal));

        if (IsFireResponsibleFamily(na) && IsFireResponsibleFamily(nb))
        {
            return true;
        }

        static bool IsFireSpecialistFamily(string n) =>
            n.Contains("специалист", StringComparison.Ordinal)
            && (n.Contains("профилактик", StringComparison.Ordinal)
                || n.Contains("специалист по пожар", StringComparison.Ordinal));

        return IsFireSpecialistFamily(na) && IsFireSpecialistFamily(nb);
    }

    private static bool ApplyToConduct(
        List<TrainingConductRecord> list,
        TrainingStatusRow row,
        string protocol,
        DateTime? trainingDate,
        IReadOnlyList<TrainingStandard> standards,
        IReadOnlyDictionary<string, string> assignmentMap,
        IReadOnlyList<TrainingKindNode> kinds,
        bool forArchive,
        IReadOnlySet<string>? employedNames,
        bool overwriteExisting,
        out bool preservedFilled,
        string? protocolMovement = null,
        string? updStatus = null)
    {
        preservedFilled = false;
        TrainingConductRecord? existing = null;
        if (!string.IsNullOrWhiteSpace(row.ConductId))
        {
            existing = list.FirstOrDefault(x => string.Equals(x.Id, row.ConductId, StringComparison.Ordinal));
        }

        bool MatchesPersonProgram(TrainingConductRecord x) =>
            string.Equals(x.Person, row.Person, StringComparison.OrdinalIgnoreCase)
            && ProgramsEqualForConduct(x.Program, row.Program)
            && (string.IsNullOrWhiteSpace(x.Organization)
                || OrganizationMatcher.Matches(x.Organization, row.Organization)
                || string.Equals(x.Organization, row.Organization, StringComparison.OrdinalIgnoreCase));

        var wantSnapshot = forArchive
                           && employedNames is not null
                           && employedNames.Contains((row.Person ?? string.Empty).Trim());

        if (forArchive)
        {
            // Сначала снимок архива (переводы), затем обычная запись уволенного.
            if (wantSnapshot)
            {
            existing ??= list.FirstOrDefault(x => x.IsArchivedSnapshot && MatchesPersonProgram(x));
            existing ??= list.FirstOrDefault(x => !x.IsArchivedSnapshot && MatchesPersonProgram(x));
            }
            else
            {
                existing ??= list.FirstOrDefault(x => !x.IsArchivedSnapshot && MatchesPersonProgram(x));
                existing ??= list.FirstOrDefault(x => x.IsArchivedSnapshot && MatchesPersonProgram(x));
            }
        }
        else
        {
            existing ??= list.FirstOrDefault(x => !x.IsArchivedSnapshot && MatchesPersonProgram(x));
        }

        var isNew = existing is null;
        existing ??= new TrainingConductRecord { Id = Guid.NewGuid().ToString("N") };

        var fillOnlyEmpty = !overwriteExisting && !isNew;
        var hadIncomingData = !string.IsNullOrWhiteSpace(protocol) || trainingDate.HasValue
                              || !string.IsNullOrWhiteSpace(protocolMovement)
                              || !string.IsNullOrWhiteSpace(updStatus);

        // Без «перезаписать»: уже заполненная строка (протокол или дата) — протокол не трогаем,
        // но пустые «движение» / УПД из Excel-меток всё равно дописываем.
        if (fillOnlyEmpty && HasFilledTrainingData(existing))
        {
            var markerOnly = ApplyProtocolMarkers(
                existing, protocolMovement, updStatus, fillOnlyEmpty: true, ref preservedFilled);
            if (markerOnly)
            {
                return true;
            }

            if (hadIncomingData)
            {
                preservedFilled = true;
            }

            return false;
        }

        var changed = false;

        void SetText(string? next, Action<string> apply, string? current)
        {
            var value = (next ?? string.Empty).Trim();
            if (string.Equals((current ?? string.Empty).Trim(), value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            apply(value);
            changed = true;
        }

        SetText(row.Organization, v => existing.Organization = v, existing.Organization);
        SetText(row.Department, v => existing.Department = v, existing.Department);
        SetText(row.Profession, v => existing.Profession = v, existing.Profession);
        SetText(row.Person, v => existing.Person = v, existing.Person);
        SetText(row.Program, v => existing.Program = v, existing.Program);

        if (wantSnapshot && !existing.IsArchivedSnapshot)
        {
            existing.IsArchivedSnapshot = true;
            changed = true;
        }

        // Пустая строка или режим перезаписи — пишем протокол/дату из Excel.
        if (!string.IsNullOrWhiteSpace(protocol))
        {
            var p = protocol.Trim();
            if (!string.Equals((existing.ProtocolNumber ?? string.Empty).Trim(), p, StringComparison.OrdinalIgnoreCase)
                || !string.Equals((existing.CertificateNumber ?? string.Empty).Trim(), p, StringComparison.OrdinalIgnoreCase))
            {
                existing.ProtocolNumber = p;
                existing.CertificateNumber = p;
                changed = true;

                if (string.IsNullOrWhiteSpace(existing.Result)
                    || existing.Result.Contains("не проводилось", StringComparison.OrdinalIgnoreCase))
                {
                    existing.Result = row.Program.Contains("проверк", StringComparison.OrdinalIgnoreCase)
                        ? "Сдал"
                        : "Пройден";
                }
            }
        }

        if (trainingDate.HasValue)
        {
            var d = trainingDate.Value.Date;
            if (existing.TrainingDate?.Date != d)
            {
                existing.TrainingDate = d;
            changed = true;

            var standard = TrainingReportBuilder.ResolveStandard(
                standards,
                assignmentMap,
                row.Organization,
                row.Person,
                row.Profession);
            var periodicity = !string.IsNullOrWhiteSpace(row.Periodicity)
                ? row.Periodicity
                : TrainingProgramCatalog.ResolvePeriodicityForKind(
                    row.Program,
                    kinds,
                    standard?.PersonnelCategory,
                    standard?.ProgramPeriodicities);

                DateTime? expectedNext = TrainingConductValues.IsOneTimePeriodicity(periodicity)
                ? null
                    : d.AddMonths(TrainingProgramCatalog.PeriodMonthsFromPeriodicity(periodicity));

                existing.NextTrainingDate = expectedNext;

            if (string.IsNullOrWhiteSpace(existing.Result)
                || existing.Result.Contains("не проводилось", StringComparison.OrdinalIgnoreCase))
            {
                existing.Result = row.Program.Contains("проверк", StringComparison.OrdinalIgnoreCase)
                    ? "Сдал"
                    : "Пройден";
            }
        }
        }

        if (ApplyProtocolMarkers(existing, protocolMovement, updStatus, fillOnlyEmpty, ref preservedFilled))
        {
            changed = true;
        }

        if (!changed && !isNew)
        {
            if (fillOnlyEmpty && hadIncomingData)
            {
                preservedFilled = true;
            }

            return false;
        }

        if (!changed && isNew
            && string.IsNullOrWhiteSpace(protocol)
            && !trainingDate.HasValue)
        {
            return false;
        }

        var idx = list.FindIndex(x => string.Equals(x.Id, existing.Id, StringComparison.Ordinal));
        if (idx >= 0)
        {
            list[idx] = existing;
        }
        else
        {
            list.Add(existing);
        }

        return true;
    }

    private static bool HasFilledTrainingData(TrainingConductRecord record) =>
        !string.IsNullOrWhiteSpace(record.ProtocolNumber)
        || !string.IsNullOrWhiteSpace(record.CertificateNumber)
        || record.TrainingDate.HasValue;

    private static Dictionary<string, string> BuildSnilsIndex(
        IReadOnlyList<OrganizationEmployee> employees,
        IReadOnlyList<string> reportPeople)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var reportSet = new HashSet<string>(reportPeople, StringComparer.OrdinalIgnoreCase);
        foreach (var e in employees)
        {
            var name = (e.Name ?? string.Empty).Trim();
            if (reportSet.Count > 0 && !reportSet.Contains(name))
            {
                continue;
            }

            var snils = OneCEmployeeListParser.NormalizeSnils(e.Snils);
            if (snils.Length >= 11 && !map.ContainsKey(snils))
            {
                map[snils] = name;
            }
        }

        return map;
    }

    private static string? MatchPerson(
        TrainingScheduleExcelPersonRow excelPerson,
        IReadOnlyList<string> reportPeople,
        IReadOnlyDictionary<string, string> snilsToReportPerson)
    {
        var snils = OneCEmployeeListParser.NormalizeSnils(excelPerson.Snils);
        if (snils.Length >= 11 && snilsToReportPerson.TryGetValue(snils, out var bySnils))
        {
            return bySnils;
        }

        var target = NormalizeFio(excelPerson.Person);
        if (target.Length == 0)
        {
            return null;
        }

        string? exact = null;
        string? fuzzy = null;
        foreach (var person in reportPeople)
        {
            var n = NormalizeFio(person);
            if (n.Length == 0)
            {
                continue;
            }

            if (string.Equals(n, target, StringComparison.OrdinalIgnoreCase))
            {
                exact = person;
                break;
            }

            if (fuzzy is null && FioFuzzyEquals(n, target))
            {
                fuzzy = person;
            }
        }

        return exact ?? fuzzy;
    }

    private static bool FioFuzzyEquals(string left, string right)
    {
        var a = left.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var b = right.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        if (!string.Equals(a[0], b[0], StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (a.Length >= 2 && b.Length >= 2
            && string.Equals(a[1], b[1], StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        static string Initials(string[] parts)
        {
            if (parts.Length < 2)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            for (var i = 1; i < parts.Length; i++)
            {
                var p = parts[i].Trim('.');
                if (p.Length > 0)
                {
                    sb.Append(char.ToUpperInvariant(p[0]));
                }
            }

            return sb.ToString();
        }

        var ia = Initials(a);
        var ib = Initials(b);
        return ia.Length > 0 && string.Equals(ia, ib, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFio(string? raw)
    {
        var t = (raw ?? string.Empty).ToLowerInvariant().Replace('ё', 'е');
        t = Regex.Replace(t, @"\(.*?\)", " ");
        t = Regex.Replace(t, @"[^a-zа-я0-9.\s]+", " ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\s+", " ").Trim();
        return t;
    }

    internal static List<string> MatchProgramsForColumn(string columnKey, IReadOnlyList<string> programs)
    {
        var hits = new List<string>();
        foreach (var program in programs)
        {
            if (ProgramMatchesColumn(columnKey, program))
            {
                hits.Add(program);
            }
        }

        return hits;
    }

    private static bool ProgramMatchesColumn(string columnKey, string program)
    {
        var key = (columnKey ?? string.Empty).Trim().ToLowerInvariant();
        var p = program.ToLowerInvariant().Replace('ё', 'е');
        p = Regex.Replace(p, @"\s+", " ").Trim();

        return key switch
        {
            "a" => MatchesProgramA(p),
            "b" => MatchesProgramB(p),
            "ab" => MatchesProgramA(p) || MatchesProgramB(p),
            "v" => MatchesProgramVOneYear(p) || MatchesProgramVThreeYear(p) || MatchesV(p),
            "siz" => MatchesProgramSiz(p),
            "pp" => MatchesProgramPp(p),
            "hire" => IsHireBriefingProgram(program),
            "fire" => MatchesAnyFire(p),
            "fire:responsible" => MatchesFireResponsible(p),
            "fire:briefings" => MatchesFireBriefings(p) || MatchesFireConducting(p),
            "fire:conducting" => MatchesFireConducting(p) || MatchesFireBriefings(p),
            "fire:intro" => MatchesFireIntro(p),
            "fire:primary" => MatchesFirePrimary(p),
            "fire:repeat" => MatchesFireRepeat(p),
            "fire:specialist" => MatchesFireSpecialist(p),
            "fire:training" => MatchesFireTraining(p),
            "height" => MatchesHeight(p),
            "height:1" => MatchesHeight(p) && HeightGroupRank(program) == 1,
            "height:2" => MatchesHeight(p) && HeightGroupRank(program) == 2,
            "height:3" => MatchesHeight(p) && HeightGroupRank(program) == 3,
            "slinger" => p.Contains("стропаль", StringComparison.Ordinal),
            "electrical" => p.Contains("электробезопас", StringComparison.Ordinal)
                            || p.Contains("эл. безопас", StringComparison.Ordinal)
                            || p.Contains("эл безопас", StringComparison.Ordinal)
                            || p.Contains("электро", StringComparison.Ordinal),
            "sout" => p.Contains("соут", StringComparison.Ordinal),
            "prof" => p.Contains("проф", StringComparison.Ordinal) && p.Contains("обуч", StringComparison.Ordinal),
            "industrial" => p.Contains("пром", StringComparison.Ordinal) && p.Contains("безопас", StringComparison.Ordinal),
            _ when key.StartsWith("pb:", StringComparison.Ordinal) => MatchesPbCode(key[3..], p),
            _ when key.StartsWith("h:", StringComparison.Ordinal) => FuzzyHeaderMatch(key[2..], p),
            _ => false
        };
    }

    private static bool MatchesProgramA(string p) =>
        Regex.IsMatch(p, @"^программы?\s+а\b", RegexOptions.IgnoreCase)
        || (Regex.IsMatch(p, @"программы?\s+а\b", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(p, @"программы?\s+б\b", RegexOptions.IgnoreCase)
            && !MatchesCompositeOt(p));

    private static bool MatchesProgramB(string p) =>
        Regex.IsMatch(p, @"^программы?\s+б\b", RegexOptions.IgnoreCase)
        || (Regex.IsMatch(p, @"программы?\s+б\b", RegexOptions.IgnoreCase)
            && !MatchesCompositeOt(p) && !MatchesAb(p));

    private static bool MatchesProgramSiz(string p) =>
        Regex.IsMatch(p, @"^программы?\s+сиз\b", RegexOptions.IgnoreCase)
        || ((p.Contains("сиз", StringComparison.Ordinal) || Regex.IsMatch(p, @"программы?\s+сиз\b", RegexOptions.IgnoreCase))
            && !MatchesCompositeOt(p)
            && !p.Contains("а+б", StringComparison.Ordinal)
            && !p.Contains("высот", StringComparison.Ordinal));

    private static bool MatchesProgramPp(string p) =>
        Regex.IsMatch(p, @"^программы?\s+пп\b", RegexOptions.IgnoreCase)
        || p.Contains("первая помощ", StringComparison.Ordinal)
             || p.Contains("перв. помощ", StringComparison.Ordinal)
        || (p.Contains("программ", StringComparison.Ordinal)
            && Regex.IsMatch(p, @"\bпп\b", RegexOptions.IgnoreCase)
            && !p.Contains("пб", StringComparison.Ordinal)
            && !MatchesCompositeOt(p));

    private static bool MatchesProgramVOneYear(string p) =>
        Regex.IsMatch(p, @"программы?\s+в\b", RegexOptions.IgnoreCase)
        && (p.Contains("1 год", StringComparison.Ordinal)
            || p.Contains("на 1", StringComparison.Ordinal)
            || p.Contains("1 раз в год", StringComparison.Ordinal))
        && !p.Contains("3 год", StringComparison.Ordinal);

    private static bool MatchesProgramVThreeYear(string p) =>
        Regex.IsMatch(p, @"программы?\s+в\b", RegexOptions.IgnoreCase)
        && (p.Contains("3 год", StringComparison.Ordinal) || p.Contains("на 3", StringComparison.Ordinal));

    private static bool MatchesCompositeOt(string p) =>
        p.Contains("а+б", StringComparison.Ordinal)
        || p.Contains("а + б", StringComparison.Ordinal)
        || (p.Contains("программ", StringComparison.Ordinal)
            && p.Contains("сиз", StringComparison.Ordinal)
            && p.Contains("пп", StringComparison.Ordinal));

    private static bool MatchesAb(string p) =>
        Regex.IsMatch(p, @"\bа\s*[-–+/]\s*б\b", RegexOptions.IgnoreCase)
        || p.Contains("а,б", StringComparison.Ordinal)
        || p.Contains("а+б", StringComparison.Ordinal)
        || p.Contains("программы а и б", StringComparison.Ordinal)
        || (p.Contains("охрана труда", StringComparison.Ordinal)
            && (p.Contains("а/б", StringComparison.Ordinal) || p.Contains("а-б", StringComparison.Ordinal)));

    private static bool MatchesB(string p) =>
        Regex.IsMatch(p, @"\bб\s*[-–.]?\s*1\b", RegexOptions.IgnoreCase)
        || (p.Contains("программа б", StringComparison.Ordinal) && !MatchesAb(p) && !MatchesCompositeOt(p));

    private static bool MatchesV(string p) =>
        !p.Contains("вводн", StringComparison.Ordinal)
        && (Regex.IsMatch(p, @"^программа\s+в\b", RegexOptions.IgnoreCase)
            || p.Contains("программа в", StringComparison.Ordinal)
            || Regex.IsMatch(p, @"\bв\s*[-–.]?\s*1\s*раз", RegexOptions.IgnoreCase)
            || (p.Contains("охрана труда", StringComparison.Ordinal)
                && Regex.IsMatch(p, @"программа\s+в\b", RegexOptions.IgnoreCase)));

    private static bool MatchesAnyFire(string p) =>
        p.Contains("пожар", StringComparison.Ordinal)
        || p.Contains("пб", StringComparison.Ordinal)
        || p.Contains("противопожар", StringComparison.Ordinal)
        || p.Contains("птм", StringComparison.Ordinal);

    private static bool MatchesFireResponsible(string p) =>
        p.Contains("ответственн", StringComparison.Ordinal)
        && (p.Contains("обеспечен", StringComparison.Ordinal)
            || p.Contains("пожар", StringComparison.Ordinal)
            || p.Contains("пб", StringComparison.Ordinal))
        && !p.Contains("проведен", StringComparison.Ordinal);

    private static bool MatchesFireConducting(string p) =>
        p.Contains("проведен", StringComparison.Ordinal)
        && p.Contains("инструктаж", StringComparison.Ordinal)
        && MatchesAnyFire(p);

    private static bool MatchesFireBriefings(string p)
    {
        if (!MatchesAnyFire(p))
        {
            return false;
        }

        if (MatchesFireResponsible(p) || MatchesFireConducting(p) || MatchesFireIntro(p)
            || MatchesFirePrimary(p) || MatchesFireRepeat(p) || MatchesFireSpecialist(p))
        {
            return false;
        }

        return p.Contains("инструктаж", StringComparison.Ordinal);
    }

    private static bool MatchesFireIntro(string p) =>
        p.Contains("вводн", StringComparison.Ordinal) && MatchesAnyFire(p);

    private static bool MatchesFirePrimary(string p) =>
        p.Contains("первичн", StringComparison.Ordinal) && MatchesAnyFire(p);

    private static bool MatchesFireRepeat(string p) =>
        p.Contains("повторн", StringComparison.Ordinal) && MatchesAnyFire(p);

    private static bool MatchesFireSpecialist(string p) =>
        p.Contains("специалист", StringComparison.Ordinal)
        && (p.Contains("профилактик", StringComparison.Ordinal)
            || p.Contains("специалист по пожар", StringComparison.Ordinal));

    private static bool MatchesFireTraining(string p) =>
        MatchesAnyFire(p)
        && (p.Contains("обучен", StringComparison.Ordinal) || p.Contains("птм", StringComparison.Ordinal))
        && !p.Contains("инструктаж", StringComparison.Ordinal)
        && !MatchesFireResponsible(p);

    private static bool MatchesHeight(string program)
    {
        var p = program.ToLowerInvariant().Replace('ё', 'е');
        return p.Contains("высот", StringComparison.Ordinal)
               || p.Contains("работе на высоте", StringComparison.Ordinal)
               || p.Contains("работы на высоте", StringComparison.Ordinal);
    }

    private static bool MatchesPbCode(string code, string program)
    {
        var expected = NormalizePbCodeToken(code);
        if (expected.Length == 0)
        {
            return false;
        }

        // Только точный код в начале названия («Б.8.2. …»), без ложного Б.8.2 ⊂ Б.8.22.
        var fromProgram = TrainingScheduleExcelParser.TryExtractIndustrialSafetyCode(program);
        if (fromProgram is not null
            && string.Equals(fromProgram, expected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Запасной вариант: код сразу в начале строки программы.
        var compact = Regex.Replace(
            program.ToLowerInvariant().Replace('ё', 'е'),
            @"\s+",
            "");
        var expectedCompact = expected.ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal);
        if (!compact.StartsWith(expectedCompact, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // После кода — конец, точка или не цифра (чтобы Б.8.2 не съел Б.8.22).
        if (compact.Length == expectedCompact.Length)
        {
            return true;
        }

        var next = compact[expectedCompact.Length];
        return next is '.' or ' ' or '–' or '-' || !char.IsDigit(next);
    }

    private static string NormalizePbCodeToken(string? code)
    {
        var extracted = TrainingScheduleExcelParser.TryExtractIndustrialSafetyCode(code);
        if (extracted is not null)
        {
            return extracted;
        }

        var raw = (code ?? string.Empty).Trim().TrimEnd('.');
        return raw.ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal);
    }

    private static bool FuzzyHeaderMatch(string header, string program)
    {
        if (header.Length < 3 || program.Length < 3)
        {
            return false;
        }

        // «в в - 1 раз в год» → программа В
        if (header.StartsWith("в ", StringComparison.Ordinal) || header == "в")
        {
            return MatchesV(program);
        }

        if (program.Contains(header, StringComparison.OrdinalIgnoreCase)
            || header.Contains(program, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var hTokens = header.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 4)
            .ToArray();
        if (hTokens.Length == 0)
        {
            return false;
        }

        var hits = hTokens.Count(t => program.Contains(t, StringComparison.OrdinalIgnoreCase));
        return hits >= Math.Max(1, (hTokens.Length + 1) / 2);
    }

    private static string DescribeColumn(string key)
    {
        if (key.StartsWith("h:", StringComparison.OrdinalIgnoreCase))
        {
            return key[2..];
        }

        if (key.StartsWith("pb:", StringComparison.OrdinalIgnoreCase))
        {
            return "промбез " + key[3..];
        }

        return key switch
        {
            "a" or "ab" => "А / А-Б",
            "b" => "Б",
            "v" => "В / Программа В",
            "siz" => "СИЗ",
            "pp" => "ПП",
            "hire" => "дата приёма на работу",
            "fire" => "ПБ",
            "fire:responsible" => "ПБ: ответственный → Обучение по программе «Ответственный…»",
            "fire:briefings" => "ПБ: инструктажи → Проведение инструктажей по ПБ",
            "fire:conducting" => "ПБ: проведение инструктажей",
            "fire:intro" => "ПБ: вводный",
            "fire:primary" => "ПБ: первичный",
            "fire:repeat" => "ПБ: повторный",
            "fire:specialist" => "ПБ: специалист по пожарной профилактике",
            "fire:training" => "ПБ: обучение",
            "height" => "работы на высоте",
            "height:1" => "высота: 1 группа",
            "height:2" => "высота: 2 группа",
            "height:3" => "высота: 3 группа",
            "slinger" => "стропальщик",
            "electrical" => "эл.безопасность",
            "sout" => "СОУТ",
            "prof" => "профобучение",
            "industrial" => "промбезопасность",
            _ => key
        };
    }
}
