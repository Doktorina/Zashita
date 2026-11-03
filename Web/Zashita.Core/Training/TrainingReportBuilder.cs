using Zashita.Core.Employees;

namespace Zashita.Core.Training;

public static class TrainingReportBuilder
{
    public sealed record TrainingPlannedRow(
        int Number,
        string Organization,
        string Department,
        string Profession,
        string Person,
        string TrainingType,
        string Program,
        DateTime PlannedDate,
        string Periodicity,
        string Comment);

    private sealed record StatusDemoScenario(bool Passed, int MonthsSinceTraining, bool ForceOverdue, int OverdueDays);

    private static readonly StatusDemoScenario[] DemoScenarios =
    [
        new(true, 1, false, 0),
        new(true, 2, false, 0),
        new(true, 4, false, 0),
        new(true, 8, true, 15),
        new(true, 12, true, 45),
        new(true, 18, true, 120),
        new(true, 24, true, 365),
        new(false, 0, false, 0),
        new(false, 0, false, 0),
        new(true, 6, true, 7),
        new(true, 3, true, 30),
        new(true, 14, true, 90)
    ];

    private static readonly string[] DeptNames =
    [
        "Бригада ремонта",
        "Производство",
        "Администрация",
        "Участок №1",
        "Склад"
    ];

    public static IReadOnlyList<TrainingQualificationRow> BuildQualificationReport(
        IEnumerable<TrainingStandard> standards,
        IReadOnlyList<TrainingKindNode>? trainingKinds = null)
    {
        var kinds = trainingKinds ?? [];
        var activeKindNames = kinds.Count == 0
            ? null
            : kinds
                .Where(k => !k.IsGroup && !k.MarkedForDeletion && !string.IsNullOrWhiteSpace(k.Name))
                .Select(k => k.Name.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = new List<TrainingQualificationRow>();
        var n = 0;

        foreach (var s in standards
                     .Where(s => s.Items.Any(x => !string.IsNullOrWhiteSpace(x)))
                     .OrderBy(x => x.Organization, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(x => x.Profession, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var org = (s.Organization ?? "").Trim();
            var prof = (s.Profession ?? "").Trim();
            var type = s.TrainingCategory;

            foreach (var raw in s.Items)
            {
                var program = (raw ?? "").Trim();
                if (string.IsNullOrWhiteSpace(program))
                {
                    continue;
                }

                if (activeKindNames is { Count: > 0 } && !activeKindNames.Contains(program))
                {
                    continue;
                }

                rows.Add(new TrainingQualificationRow(
                    ++n,
                    org.Length > 0 ? org : "—",
                    "—",
                    prof.Length > 0 ? prof : "—",
                    type,
                    program,
                    TrainingProgramCatalog.ResolvePeriodicityForKind(
                        program,
                        kinds,
                        s.PersonnelCategory,
                        s.ProgramPeriodicities),
                    s.Name));
            }
        }

        return rows;
    }

    public static IReadOnlyList<TrainingStatusRow> BuildStatusReport(
        IEnumerable<TrainingStandard> standards,
        string organization,
        IReadOnlyDictionary<string, List<string>> employeesByProfession,
        DateTime reportDate,
        IEnumerable<TrainingConductRecord>? conductRecords = null)
    {
        var conduct = conductRecords?.ToList() ?? [];
        var employeeRows = employeesByProfession
            .SelectMany(pair => pair.Value.Select(name => (
                Organization: organization ?? "",
                Name: name,
                Profession: pair.Key,
                Department: "—")))
            .ToList();
        return BuildManualStatusReport(standards, organization, employeeRows, reportDate, conduct);
    }

    /// <summary>
    /// Состояние по обучению: программы только из «Стандарта обучения»
    /// (бывшие квалификационные требования) по юрлицу + должности.
    /// Без стандарта — строк программ нет (не подставляем демо-список).
    /// </summary>
    public static IReadOnlyList<TrainingStatusRow> BuildManualStatusReport(
        IEnumerable<TrainingStandard> standards,
        string organization,
        IReadOnlyList<(string Organization, string Name, string Profession, string Department)> employees,
        DateTime reportDate,
        IEnumerable<TrainingConductRecord>? conductRecords = null,
        IReadOnlyDictionary<string, string>? standardAssignments = null,
        IReadOnlyList<TrainingKindNode>? trainingKinds = null,
        bool forArchive = false)
    {
        var selectedOrg = (organization ?? string.Empty).Trim();
        var kinds = trainingKinds ?? [];
        var activeKindNames = kinds.Count == 0
            ? null
            : kinds
                .Where(k => !k.IsGroup && !k.MarkedForDeletion && !string.IsNullOrWhiteSpace(k.Name))
                .Select(k => k.Name.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var today = reportDate.Date;
        var conduct = (conductRecords ?? [])
            .Where(r => forArchive || !r.IsArchivedSnapshot)
            .Where(r => string.IsNullOrWhiteSpace(selectedOrg)
                || string.IsNullOrWhiteSpace(r.Organization)
                || string.Equals(r.Organization, selectedOrg, StringComparison.OrdinalIgnoreCase)
                || OrganizationMatcher.Matches(selectedOrg, r.Organization))
            .ToList();

        // Только стандарты с указанной организацией; при фильтре — строго своя компания.
        var standardsList = standards
            .Where(s => s.Items.Any(x => !string.IsNullOrWhiteSpace(x)))
            .Where(s => !string.IsNullOrWhiteSpace(s.Organization))
            .Where(s => string.IsNullOrWhiteSpace(selectedOrg)
                || string.Equals(s.Organization.Trim(), selectedOrg, StringComparison.OrdinalIgnoreCase)
                || OrganizationMatcher.Matches(selectedOrg, s.Organization))
            .ToList();

        var rows = new List<TrainingStatusRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conductIndex = BuildConductIndex(conduct);
        var standardsByOrg = new Dictionary<string, IReadOnlyList<TrainingStandard>>(StringComparer.OrdinalIgnoreCase);

        foreach (var employee in EmployeeReportCollapse.Collapse(employees)
                     .OrderBy(e => e.Organization, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var org = string.IsNullOrWhiteSpace(employee.Organization)
                ? selectedOrg
                : employee.Organization.Trim();
            if (string.IsNullOrWhiteSpace(org) && !string.IsNullOrWhiteSpace(selectedOrg))
            {
                org = selectedOrg;
            }

            var person = employee.Name.Trim();
            var profession = string.IsNullOrWhiteSpace(employee.Profession) ? "—" : employee.Profession.Trim();
            var department = EmployeeReportCollapse.SanitizeDepartment(org, employee.Department);
            if (string.IsNullOrWhiteSpace(department))
            {
                department = "—";
            }

            // Каноническое имя выбранной организации — без второй ветки из-за «ИП … Б.Э.» vs полное ФИО.
            if (!string.IsNullOrWhiteSpace(selectedOrg)
                && OrganizationMatcher.Matches(selectedOrg, org))
            {
                org = selectedOrg;
            }
            if (IsIndividualEntrepreneurReportPerson(org, person, profession, department)
                && (profession == "—"
                    || string.IsNullOrWhiteSpace(profession)
                    || !IsIndividualEntrepreneurProfession(profession)))
            {
                if (IsIndividualEntrepreneurProfession(department)
                    || profession == "—"
                    || string.IsNullOrWhiteSpace(profession))
                {
                    profession = TrainingIndividualEntrepreneurStandards.ProfessionName;
                }
            }

            if (!standardsByOrg.TryGetValue(org, out var forOrg))
            {
                forOrg = StandardsForOrganization(standardsList, org);
                standardsByOrg[org] = forOrg;
            }

            var standard = ResolveStandardFromOrgList(
                forOrg, standardAssignments, org, person, profession, department);
            if (standard is null)
            {
                // Нет стандарта — одна служебная строка без программы (для выбора стандарта в UI).
                var placeholderKey = $"{org}|{person}|{ProfessionMatcher.Normalize(profession)}|__no_standard__";
                if (!seen.Add(placeholderKey))
                {
                    continue;
                }

                rows.Add(new TrainingStatusRow(
                    org.Length > 0 ? org : "Организация",
                    department,
                    profession,
                    person,
                    "",
                    "—",
                    "Нет стандарта обучения",
                    "—",
                    null,
                    null,
                    string.Empty,
                    "yellow",
                    null,
                    null,
                    null));
                continue;
            }

            var programs = standard.Items
                .Select(x => (x ?? string.Empty).Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(p => activeKindNames is null || activeKindNames.Contains(p))
                .OrderBy(p => TrainingProgramDisplayOrder.GetRank(p))
                .ThenBy(p => p, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (programs.Count == 0)
            {
                var emptyKey = $"{org}|{person}|{ProfessionKey(profession)}|__empty_after_kinds__";
                if (!seen.Add(emptyKey))
                {
                    continue;
                }

                rows.Add(new TrainingStatusRow(
                    org.Length > 0 ? org : "Организация",
                    department,
                    profession,
                    person,
                    "",
                    "—",
                    "Нет программ в стандарте",
                    "—",
                    null,
                    null,
                    string.Empty,
                    "yellow",
                    null,
                    standard.Id,
                    standard.Name));
                continue;
            }

            foreach (var program in programs)
            {
                var key = $"{org}|{person}|{ProfessionKey(profession)}|{program}";
                if (!seen.Add(key))
                {
                    continue;
                }

                var match = FindConduct(conductIndex, org, person, program, profession);
                var catalogPeriodicity = TrainingProgramCatalog.ResolvePeriodicityForKind(
                    program,
                    kinds,
                    standard.PersonnelCategory,
                    standard.ProgramPeriodicities);
                var periodicity = !TrainingProgramCatalog.IsBlankPeriodicityMarker(match?.Periodicity)
                    ? match!.Periodicity.Trim()
                    : catalogPeriodicity;
                if (TrainingProgramCatalog.IsBlankPeriodicityMarker(periodicity))
                {
                    periodicity = TrainingProgramCatalog.AllowsManualPeriodicityInReport(program)
                        ? string.Empty
                        : catalogPeriodicity;
                }

                if (match is not null)
                {
                    var (rowOrg, rowDept) = ResolveConductPlacement(org, department, match);
                    if (TrainingConductValues.IsNotTraining(match.Result))
                    {
                        rows.Add(new TrainingStatusRow(
                            rowOrg,
                            rowDept,
                            profession,
                            person,
                            program,
                            TrainingConductValues.NotTrainingResult,
                            TrainingConductValues.NotTrainingResult,
                            periodicity,
                            match.TrainingDate?.ToString("dd.MM.yyyy"),
                            ResolveNextTrainingDate(match.TrainingDate, periodicity, match.NextTrainingDate)
                                ?.ToString("dd.MM.yyyy"),
                            FormatProtocolNumber(match),
                            "grey",
                            match.Id,
                            standard.Id,
                            standard.Name,
                            match.Comment ?? string.Empty,
                            match.ProtocolMovement ?? string.Empty,
                            match.UpdStatus ?? string.Empty,
                            match.ObjectNote ?? string.Empty,
                            CloneCellStyles(match.CellStyles)));
                        continue;
                    }

                    // Заявка без даты прохождения — отдельный статус; обычная заметка — «Не пройдено».
                    if (!match.TrainingDate.HasValue && !string.IsNullOrWhiteSpace(match.Comment))
                    {
                        var pendingResult = IsPassedResult(match.Result) || string.IsNullOrWhiteSpace(match.Result)
                            ? "Не проводилось"
                            : match.Result;
                        var isApplication = TrainingConductValues.LooksLikeApplicationComment(match.Comment);
                        rows.Add(new TrainingStatusRow(
                            rowOrg,
                            rowDept,
                            profession,
                            person,
                            program,
                            pendingResult,
                            isApplication ? "По заявке" : "Не пройдено",
                            periodicity,
                            null,
                            null,
                            FormatProtocolNumber(match),
                            isApplication ? "orange" : "pink",
                            match.Id,
                            standard.Id,
                            standard.Name,
                            match.Comment ?? string.Empty,
                            match.ProtocolMovement ?? string.Empty,
                            match.UpdStatus ?? string.Empty,
                            match.ObjectNote ?? string.Empty,
                            CloneCellStyles(match.CellStyles)));
                        continue;
                    }

                    var passed = match.TrainingDate.HasValue &&
                                 (IsPassedResult(match.Result) || string.IsNullOrWhiteSpace(match.Result));
                    var result = match.Result;
                    if (string.IsNullOrWhiteSpace(result))
                    {
                        result = match.TrainingDate.HasValue ? FormatResult(true, program) : "Не проводилось";
                    }
                    else if (!match.TrainingDate.HasValue && IsPassedResult(result))
                    {
                        // «Сдал/Пройден» без даты — не завершённое обучение.
                        result = "Не проводилось";
                        passed = false;
                    }

                    // Следующую дату считаем по актуальной периодичности из справочника,
                    // а не по устаревшему NextTrainingDate (мог быть сохранён с «1 раз в год»).
                    var nextDate = ResolveNextTrainingDate(match.TrainingDate, periodicity, match.NextTrainingDate);
                    var traffic = passed
                        ? EvaluateTraffic(result, nextDate, today, periodicity)
                        : "pink";
                    var status = ToDisplayStatus(passed, traffic, nextDate, today);
                    rows.Add(new TrainingStatusRow(
                        rowOrg,
                        rowDept,
                        profession,
                        person,
                        program,
                        result,
                        status,
                        periodicity,
                        match.TrainingDate?.ToString("dd.MM.yyyy"),
                        nextDate?.ToString("dd.MM.yyyy"),
                        FormatProtocolNumber(match),
                        traffic,
                        match.Id,
                        standard.Id,
                        standard.Name,
                        match.Comment ?? string.Empty,
                        match.ProtocolMovement ?? string.Empty,
                        match.UpdStatus ?? string.Empty,
                        match.ObjectNote ?? string.Empty,
                        CloneCellStyles(match.CellStyles)));
                }
                else
                {
                    rows.Add(new TrainingStatusRow(
                        org.Length > 0 ? org : "Организация",
                        department,
                        profession,
                        person,
                        program,
                        "Не проводилось",
                        "Не пройдено",
                        periodicity,
                        null,
                        null,
                        string.Empty,
                        "pink",
                        null,
                        standard.Id,
                        standard.Name));
                }
            }
        }

        return rows
            .OrderBy(r => r.Organization, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Department, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Profession, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Person, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => TrainingProgramDisplayOrder.GetRank(r.Program))
            .ThenBy(r => r.Program, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static TrainingStandard? ResolveStandard(
        IReadOnlyList<TrainingStandard> standards,
        IReadOnlyDictionary<string, string>? assignments,
        string organization,
        string person,
        string profession,
        string? department = null)
    {
        _ = department;
        var forOrg = StandardsForOrganization(standards, organization);
        return ResolveStandardFromOrgList(forOrg, assignments, organization, person, profession, department);
    }

    private static TrainingStandard? ResolveStandardFromOrgList(
        IReadOnlyList<TrainingStandard> forOrg,
        IReadOnlyDictionary<string, string>? assignments,
        string organization,
        string person,
        string profession,
        string? department = null)
    {
        if (forOrg.Count == 0)
        {
            return null;
        }

        if (assignments is not null)
        {
            foreach (var keyProfession in AssignmentProfessionAliases(profession, department))
            {
                var key = AssignmentKey(organization, person, keyProfession);
                if (!assignments.TryGetValue(key, out var assignedId))
                {
                    continue;
                }

                // Пустой id — явный выбор «— без стандарта —»: не автоподбирать другой стандарт.
                if (string.IsNullOrWhiteSpace(assignedId))
                {
                    return null;
                }

                var byId = forOrg.FirstOrDefault(s => string.Equals(s.Id, assignedId, StringComparison.Ordinal));
                if (byId is not null)
                {
                    return byId;
                }
            }
        }

        var best = PickBestStandard(forOrg, profession);
        if (best is not null)
        {
            return best;
        }

        // ИП: стандарт «Индивидуальный предприниматель» только самому предпринимателю по ФИО.
        if (IsIndividualEntrepreneurReportPerson(organization, person, profession, department))
        {
            return PickIndividualEntrepreneurStandard(forOrg);
        }

        return null;
    }

    private static bool IsIndividualEntrepreneurReportPerson(
        string organization,
        string person,
        string profession,
        string? department = null)
    {
        _ = profession;
        _ = department;
        // Только сам предприниматель по ФИО из «ИП …» — не все сотрудники организации.
        return OrganizationMatcher.IsIndividualEntrepreneurPerson(organization, person);
    }

    private static IEnumerable<string> AssignmentProfessionAliases(string profession, string? department = null)
    {
        var raw = (profession ?? string.Empty).Trim();
        yield return string.IsNullOrWhiteSpace(raw) ? "—" : raw;
        // Алиас «Индивидуальный предприниматель» — только если должность/отдел уже про ИП.
        // Пустая профессия у наёмных не должна подхватывать назначение стандарта ИП.
        if (IsIndividualEntrepreneurProfession(raw))
        {
            yield return TrainingIndividualEntrepreneurStandards.ProfessionName;
        }

        var dept = (department ?? string.Empty).Trim();
        if (IsIndividualEntrepreneurProfession(dept))
        {
            yield return TrainingIndividualEntrepreneurStandards.ProfessionName;
            yield return dept;
        }
    }

    private static TrainingStandard? PickIndividualEntrepreneurStandard(IReadOnlyList<TrainingStandard> forOrg) =>
        forOrg
            .Where(s =>
                IsIndividualEntrepreneurProfession(s.Name)
                || IsIndividualEntrepreneurProfession(s.Profession)
                || s.AllProfessions.Any(IsIndividualEntrepreneurProfession))
            .OrderByDescending(s => s.Items.Count(i => !string.IsNullOrWhiteSpace(i)))
            .ThenByDescending(s => s.CreatedAt)
            .FirstOrDefault();

    /// <summary>Стандарты строго своей организации (пустое юрлицо у стандарта — не подходит никому).</summary>
    public static IReadOnlyList<TrainingStandard> StandardsForOrganization(
        IEnumerable<TrainingStandard> standards,
        string organization)
    {
        var org = (organization ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(org))
        {
            return [];
        }

        return standards
            .Where(s => !string.IsNullOrWhiteSpace(s.Organization))
            .Where(s =>
                string.Equals(s.Organization.Trim(), org, StringComparison.OrdinalIgnoreCase)
                || OrganizationMatcher.Matches(org, s.Organization))
            .ToList();
    }

    public static string AssignmentKey(string organization, string person, string profession) =>
        $"{organization.Trim()}|{person.Trim()}|{profession.Trim()}".ToLowerInvariant();

    private static Dictionary<string, List<TrainingConductRecord>> BuildConductIndex(
        IReadOnlyList<TrainingConductRecord> conduct)
    {
        var index = new Dictionary<string, List<TrainingConductRecord>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in conduct)
        {
            if (string.IsNullOrWhiteSpace(r.Person)
                || string.IsNullOrWhiteSpace(r.Program))
            {
                continue;
            }

            var key = $"{r.Person.Trim()}|{r.Program.Trim()}";
            if (!index.TryGetValue(key, out var list))
            {
                list = [];
                index[key] = list;
            }

            list.Add(r);
        }

        return index;
    }

    /// <summary>
    /// Юрлицо и подразделение в строке отчёта — из штата (карточки).
    /// Из проведения подразделение берём только если в карточке пусто, иначе старые
    /// записи проведения (импорт/ручной ввод) сдвигали людей в чужие ветки дерева.
    /// </summary>
    private static (string Organization, string Department) ResolveConductPlacement(
        string employeeOrganization,
        string employeeDepartment,
        TrainingConductRecord match)
    {
        var org = (employeeOrganization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            org = "Организация";
        }

        var cardDept = (employeeDepartment ?? string.Empty).Trim();
        var hasCardDept = !string.IsNullOrWhiteSpace(cardDept) && cardDept != "—";
        var dept = hasCardDept ? cardDept : "—";

        if (!hasCardDept)
        {
            var matchDept = (match.Department ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(matchDept) && matchDept != "—")
            {
                dept = matchDept;
            }

            var matchOrg = (match.Organization ?? string.Empty).Trim();
            if (matchOrg.Length > 0
                && !string.Equals(matchOrg, org, StringComparison.OrdinalIgnoreCase)
                && !OrganizationMatcher.Matches(org, matchOrg)
                && !OrganizationMatcher.LooksLikeOrganization(matchOrg)
                && (string.IsNullOrWhiteSpace(dept) || dept == "—"))
            {
                // «Строительство» в колонке организации проведения — подразделение, не вторая ветка дерева.
                dept = matchOrg;
            }
        }

        dept = EmployeeReportCollapse.SanitizeDepartment(org, dept);
        if (string.IsNullOrWhiteSpace(dept))
        {
            dept = "—";
        }

        return (org, dept);
    }

    private static TrainingConductRecord? FindConduct(
        Dictionary<string, List<TrainingConductRecord>> index,
        string organization,
        string person,
        string program,
        string profession)
    {
        var candidates = new List<TrainingConductRecord>();
        if (index.TryGetValue($"{person}|{program}", out var exact) && exact.Count > 0)
        {
            candidates.AddRange(exact);
        }

        // Всегда ищем мягкие совпадения («Инструктажи по ПБ» ↔ «Проведение…»):
        // точная пустая запись не должна перекрывать протокол под соседним именем.
        foreach (var kv in index)
        {
            var pipe = kv.Key.IndexOf('|');
            if (pipe <= 0)
            {
                continue;
            }

            var keyPerson = kv.Key[..pipe];
            var keyProgram = kv.Key[(pipe + 1)..];
            if (!string.Equals(keyPerson, person, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(keyProgram, program, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!ProgramsLooselyMatch(keyProgram, program))
            {
                continue;
            }

            candidates.AddRange(kv.Value);
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        TrainingConductRecord? best = null;
        var bestScore = int.MinValue;
        var bestDate = DateTime.MinValue;

        foreach (var r in candidates)
        {
            if (!string.IsNullOrWhiteSpace(organization)
                && !string.IsNullOrWhiteSpace(r.Organization)
                && !string.Equals(r.Organization, organization, StringComparison.OrdinalIgnoreCase)
                && !OrganizationMatcher.Matches(organization, r.Organization))
            {
                // «Строительство» в Organization — не чужое юрлицо, а ошибка импорта; не отбрасываем.
                if (OrganizationMatcher.LooksLikeOrganization(r.Organization))
                {
                    continue;
                }
            }

            var score = 0;
            if (string.Equals(r.Program, program, StringComparison.OrdinalIgnoreCase))
            {
                // Точное имя программы всегда важнее мягкого совпадения с чужим протоколом.
                score += 50;
            }

            if (!string.IsNullOrWhiteSpace(r.ProtocolNumber)
                || !string.IsNullOrWhiteSpace(r.CertificateNumber))
            {
                score += 8;
            }

            if (r.TrainingDate.HasValue)
            {
                score += 4;
            }

            if (string.Equals(r.Profession, profession, StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
            }
            else if (!string.IsNullOrWhiteSpace(r.Profession))
            {
                score += 1;
            }

            if (string.Equals(r.Organization, organization, StringComparison.OrdinalIgnoreCase))
            {
                score += 1;
            }

            var date = r.TrainingDate ?? DateTime.MinValue;
            if (score > bestScore || (score == bestScore && date > bestDate))
            {
                bestScore = score;
                bestDate = date;
                best = r;
            }
        }

        return best;
    }

    /// <summary>
    /// Сопоставление имён программ для подстановки протокола/даты в отчёт
    /// (график Excel и справочник часто называют один курс по-разному).
    /// </summary>
    private static bool ProgramsLooselyMatch(string left, string right)
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

        static string Norm(string s) =>
            System.Text.RegularExpressions.Regex.Replace(
                    s.ToLowerInvariant().Replace('ё', 'е'),
                    @"\s+",
                    " ")
                .Trim();

        var na = Norm(a);
        var nb = Norm(b);
        if (na == nb)
        {
            return true;
        }

        // Высота: разные группы (1/2/3) никогда не склеиваем — иначе «2 гр» светится и у 1 группы.
        static bool IsHeight(string n) =>
            n.Contains("высот", StringComparison.Ordinal);

        static int? HeightGroup(string n)
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                n,
                @"\b([123])\s*групп",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success
                && int.TryParse(m.Groups[1].Value, out var g)
                && g is >= 1 and <= 3)
            {
                return g;
            }

            return null;
        }

        if (IsHeight(na) && IsHeight(nb))
        {
            var ga = HeightGroup(na);
            var gb = HeightGroup(nb);
            if (ga is not null && gb is not null)
            {
                return ga == gb;
            }

            // Без номера группы — только точное/нормализованное имя, не Contains.
            return false;
        }

        // Профобучение: «Арматурщик» ≠ «Арматурщик 4 разряд» (Contains склеивал обе строки в отчёте).
        if (AreDistinctProfessionTrainingTitles(na, nb))
        {
            return false;
        }

        if (a.Contains(b, StringComparison.OrdinalIgnoreCase)
            || b.Contains(a, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // ПБ: «Инструктажи…» и «Проведение инструктажей…» — один курс 24 ч.
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

        // Ответственный за ПБ — короткий Excel и полное «Обучение по программе…».
        static bool IsFireResponsibleFamily(string n) =>
            n.Contains("ответственн", StringComparison.Ordinal)
            && (n.Contains("пб", StringComparison.Ordinal)
                || n.Contains("пожар", StringComparison.Ordinal)
                || n.Contains("обеспечен", StringComparison.Ordinal));

        if (IsFireResponsibleFamily(na) && IsFireResponsibleFamily(nb))
        {
            return true;
        }

        // Специалист по пожарной профилактике
        static bool IsFireSpecialistFamily(string n) =>
            n.Contains("специалист", StringComparison.Ordinal)
            && (n.Contains("профилактик", StringComparison.Ordinal)
                || n.Contains("специалист по пожар", StringComparison.Ordinal));

        if (IsFireSpecialistFamily(na) && IsFireSpecialistFamily(nb))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Разные курсы профобучения/квалификации не склеиваем по подстроке
    /// («Арматурщик» и «Арматурщик 4 разряд» — две строки отчёта).
    /// </summary>
    private static bool AreDistinctProfessionTrainingTitles(string na, string nb)
    {
        if (string.Equals(na, nb, StringComparison.Ordinal))
        {
            return false;
        }

        static int? Rank(string n)
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                n,
                @"(\d+)\s*-?\s*разр",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var r))
            {
                return r;
            }

            return null;
        }

        var ra = Rank(na);
        var rb = Rank(nb);
        if (ra is not null || rb is not null)
        {
            return ra != rb;
        }

        static bool HasOtCourseHint(string n) =>
            n.Contains("программ", StringComparison.Ordinal)
            || n.Contains("инструктаж", StringComparison.Ordinal)
            || n.Contains("обучен", StringComparison.Ordinal)
            || n.Contains("проверк", StringComparison.Ordinal)
            || n.Contains("сиз", StringComparison.Ordinal)
            || n.Contains("пожар", StringComparison.Ordinal)
            || n.Contains("высот", StringComparison.Ordinal)
            || n.Contains("электро", StringComparison.Ordinal)
            || n.Contains("промбез", StringComparison.Ordinal)
            || n.Contains("пб", StringComparison.Ordinal);

        // Короткие названия профессий без маркеров курса ОТ — не матчим через Contains.
        if (!HasOtCourseHint(na) && !HasOtCourseHint(nb))
        {
            return true;
        }

        string shorter;
        string longer;
        if (na.Length <= nb.Length)
        {
            shorter = na;
            longer = nb;
        }
        else
        {
            shorter = nb;
            longer = na;
        }

        if (!longer.StartsWith(shorter, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = longer[shorter.Length..].TrimStart(' ', '-', '—', '–', '.', ',');
        if (rest.Length == 0)
        {
            return false;
        }

        if (char.IsDigit(rest[0]))
        {
            return true;
        }

        return rest.Contains("разр", StringComparison.Ordinal)
               || rest.Contains("категор", StringComparison.Ordinal)
               || rest.Contains("профобуч", StringComparison.Ordinal)
               || rest.Contains("проф обуч", StringComparison.Ordinal)
               || rest.Contains("квалиф", StringComparison.Ordinal);
    }

    private static TrainingStandard? PickBestStandard(IReadOnlyList<TrainingStandard> forOrg, string profession)
    {
        var scored = forOrg
            .Select(s => (Standard: s, Score: ProfessionMatchScore(s, profession)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Standard.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return scored.FirstOrDefault().Standard;
    }

    private static int ProfessionMatchScore(TrainingStandard standard, string profession)
    {
        if (string.IsNullOrWhiteSpace(profession) || profession == "—")
        {
            return 0;
        }

        var all = standard.AllProfessions;
        if (all.Count == 0)
        {
            // Стандарт без профессий не автоподставляем — только явное назначение.
            return 0;
        }

        var best = 0;
        var professionIsIe = IsIndividualEntrepreneurProfession(profession);
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

            // ИП ↔ стандарт «Индивидуальный предприниматель»
            if (professionIsIe && IsIndividualEntrepreneurProfession(p))
            {
                best = Math.Max(best, 90);
            }
        }

        return best;
    }

    private static bool IsIndividualEntrepreneurProfession(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Contains("индивидуальн", StringComparison.OrdinalIgnoreCase)
               && text.Contains("предпринимател", StringComparison.OrdinalIgnoreCase);
    }

    private static bool StandardMatches(TrainingStandard standard, string organization, string profession) =>
        StandardsForOrganization([standard], organization).Count > 0
        && ProfessionMatchScore(standard, profession) > 0;

    private static IReadOnlyList<string> ProgramsForProfession(
        IReadOnlyList<TrainingStandard> standards,
        string profession,
        IReadOnlyList<string> fallback)
    {
        var programs = standards
            .Where(s =>
            {
                var all = s.AllProfessions.Count > 0
                    ? s.AllProfessions
                    : [string.IsNullOrWhiteSpace(s.Profession) ? "—" : s.Profession.Trim()];
                return all.Any(p =>
                    string.IsNullOrWhiteSpace(p)
                    || string.Equals(p, profession, StringComparison.OrdinalIgnoreCase)
                    || profession == "—");
            })
            .SelectMany(s => s.Items)
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Больше не подставляем демо-программы — только стандарт обучения.
        return programs;
    }

    public static IReadOnlyList<TrainingPlannedRow> BuildPlannedReport(
        IEnumerable<TrainingStandard> standards,
        string organization,
        IReadOnlyDictionary<string, List<string>> employeesByProfession,
        DateTime reportDate)
    {
        var today = reportDate.Date;
        var rows = new List<TrainingPlannedRow>();
        var scenarioIndex = 0;
        var n = 0;
        var deptNames = new[] { "Администрация", "Производство", "Участок №1", "Склад" };

        foreach (var s in standards
                     .Where(s => s.Items.Any(x => !string.IsNullOrWhiteSpace(x)))
                     .OrderBy(x => x.Organization, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(x => x.Profession, StringComparer.CurrentCultureIgnoreCase))
        {
            var org = (s.Organization ?? "").Trim();
            if (org.Length == 0)
            {
                org = organization.Trim();
            }

            var prof = (s.Profession ?? "").Trim();
            var workers = ExpandWorkers(s, prof, employeesByProfession);

            foreach (var worker in workers)
            {
                foreach (var raw in s.Items)
                {
                    var program = (raw ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(program))
                    {
                        continue;
                    }

                    var scenario = DemoScenarios[scenarioIndex % DemoScenarios.Length];
                    scenarioIndex++;
                    if (scenario.Passed)
                    {
                        continue;
                    }

                    var plannedDate = today.AddDays(7 + scenarioIndex % 90);
                    var dept = deptNames[scenarioIndex % deptNames.Length];
                    rows.Add(new TrainingPlannedRow(
                        ++n,
                        org.Length > 0 ? org : "—",
                        dept,
                        prof.Length > 0 ? prof : "—",
                        worker,
                        s.TrainingCategory,
                        program,
                        plannedDate,
                        TrainingProgramCatalog.DefaultPeriodicity(program),
                        "Запланировано, не проведено"));
                }
            }
        }

        return rows;
    }

    private static IReadOnlyList<TrainingStatusRow> BuildFromConduct(
        IReadOnlyList<TrainingConductRecord> conduct,
        string organization,
        DateTime reportDate)
    {
        var rows = new List<TrainingStatusRow>();
        foreach (var record in conduct
                     .Where(r => string.IsNullOrWhiteSpace(organization)
                         || string.Equals(r.Organization, organization, StringComparison.OrdinalIgnoreCase)
                         || string.IsNullOrWhiteSpace(r.Organization))
                     .OrderBy(r => r.Department, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(r => r.Profession, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(r => r.Person, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(r => r.Program, StringComparer.CurrentCultureIgnoreCase))
        {
            var org = string.IsNullOrWhiteSpace(record.Organization) ? organization : record.Organization;
            var periodicity = TrainingProgramCatalog.DefaultPeriodicity(record.Program);
            if (TrainingConductValues.IsNotTraining(record.Result))
            {
                rows.Add(new TrainingStatusRow(
                    org,
                    string.IsNullOrWhiteSpace(record.Department) ? "—" : record.Department,
                    string.IsNullOrWhiteSpace(record.Profession) ? "—" : record.Profession,
                    record.Person,
                    record.Program,
                    TrainingConductValues.NotTrainingResult,
                    TrainingConductValues.NotTrainingResult,
                    periodicity,
                    record.TrainingDate?.ToString("dd.MM.yyyy"),
                    record.NextTrainingDate?.ToString("dd.MM.yyyy"),
                    FormatProtocolNumber(record),
                    "grey",
                    record.Id,
                    Comment: record.Comment ?? string.Empty,
                    ProtocolMovement: record.ProtocolMovement ?? string.Empty,
                    UpdStatus: record.UpdStatus ?? string.Empty,
                    ObjectNote: record.ObjectNote ?? string.Empty,
                    CellStyles: CloneCellStyles(record.CellStyles)));
                continue;
            }

            var passed = IsPassedResult(record.Result);
            var traffic = EvaluateTraffic(record.Result, record.NextTrainingDate, reportDate, periodicity);
            var status = ToDisplayStatus(passed, traffic, record.NextTrainingDate, reportDate);
            rows.Add(new TrainingStatusRow(
                org,
                string.IsNullOrWhiteSpace(record.Department) ? "—" : record.Department,
                string.IsNullOrWhiteSpace(record.Profession) ? "—" : record.Profession,
                record.Person,
                record.Program,
                record.Result,
                status,
                periodicity,
                record.TrainingDate?.ToString("dd.MM.yyyy"),
                record.NextTrainingDate?.ToString("dd.MM.yyyy"),
                FormatProtocolNumber(record),
                traffic,
                record.Id,
                Comment: record.Comment ?? string.Empty,
                ProtocolMovement: record.ProtocolMovement ?? string.Empty,
                UpdStatus: record.UpdStatus ?? string.Empty,
                ObjectNote: record.ObjectNote ?? string.Empty,
                CellStyles: CloneCellStyles(record.CellStyles)));
        }

        return rows;
    }

    private static Dictionary<string, string>? CloneCellStyles(Dictionary<string, string>? source)
    {
        if (source is null || source.Count == 0)
        {
            return null;
        }

        return new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase);
    }

    private static TrainingStatusRow BuildStatusRow(
        string organization,
        string department,
        string profession,
        string person,
        string program,
        StatusDemoScenario scenario,
        DateTime today)
    {
        var periodMonths = TrainingProgramCatalog.PeriodMonths(program);
        var passed = scenario.Passed;
        DateTime? lastDate = passed ? today.AddMonths(-scenario.MonthsSinceTraining) : null;
        DateTime? nextDate = lastDate?.AddMonths(periodMonths);

        if (passed && scenario.ForceOverdue && nextDate.HasValue)
        {
            nextDate = today.AddDays(-scenario.OverdueDays);
        }

        var result = FormatResult(passed, program);
        var traffic = EvaluateTraffic(result, nextDate, today);
        var status = ToDisplayStatus(passed, traffic, nextDate, today);

        if (passed && scenario.ForceOverdue && scenario.OverdueDays > 0)
        {
            status = "Просрочено";
            traffic = "red";
        }

        return new TrainingStatusRow(
            organization,
            department,
            profession,
            person,
            program,
            result,
            status,
            TrainingProgramCatalog.DefaultPeriodicity(program),
            lastDate?.ToString("dd.MM.yyyy"),
            nextDate?.ToString("dd.MM.yyyy"),
            passed ? DemoCertificateNumber(person, program) : string.Empty,
            traffic);
    }

    private static List<string> ExpandWorkers(
        TrainingStandard standard,
        string profession,
        IReadOnlyDictionary<string, List<string>> byProfession)
    {
        var workers = standard.Workers?.Where(w => !string.IsNullOrWhiteSpace(w)).ToList() ?? [];
        if (workers.Count == 0 && byProfession.TryGetValue(profession, out var fromProf))
        {
            workers = fromProf.Where(w => !string.IsNullOrWhiteSpace(w)).ToList();
        }

        if (workers.Count == 0)
        {
            workers = byProfession.Values
                .SelectMany(list => list)
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(50)
                .ToList();
        }

        if (workers.Count == 0)
        {
            workers.Add("—");
        }

        return workers;
    }

    private static string FormatResult(bool passed, string program)
    {
        if (!passed)
        {
            return "Не проводилось";
        }

        return (program ?? string.Empty).Contains("проверк", StringComparison.OrdinalIgnoreCase)
            ? "Сдал"
            : "Пройден";
    }

    private static string EvaluateTraffic(
        string? result,
        DateTime? nextTrainingDate,
        DateTime reportDate,
        string? periodicity = null)
    {
        if (TrainingConductValues.IsNotTraining(result))
        {
            return "grey";
        }

        var passed = IsPassedResult(result);
        if (!passed)
        {
            return "pink";
        }

        // Разовые обучения (при приеме/поступлении, единоразово) не считаем просрочкой после прохождения.
        if (TrainingConductValues.IsOneTimePeriodicity(periodicity))
        {
            return "green";
        }

        if (nextTrainingDate is null)
        {
            return "green";
        }

        var days = (nextTrainingDate.Value.Date - reportDate.Date).Days;
        if (days < 0)
        {
            return "red";
        }

        // Ближайший срок (включая «сегодня») — предупреждение, не просрочка.
        if (days <= 30)
        {
            return "yellow";
        }

        if (days <= 60)
        {
            return "yellow";
        }

        return "green";
    }

    private static string ToDisplayStatus(bool passed, string traffic, DateTime? nextDate, DateTime reportDate)
    {
        if (string.Equals(traffic, "grey", StringComparison.OrdinalIgnoreCase)
            || string.Equals(traffic, "gray", StringComparison.OrdinalIgnoreCase))
        {
            return TrainingConductValues.NotTrainingResult;
        }

        if (string.Equals(traffic, "green", StringComparison.OrdinalIgnoreCase))
        {
            return "Пройдено";
        }

        if (string.Equals(traffic, "yellow", StringComparison.OrdinalIgnoreCase))
        {
            return "Скоро истекает";
        }

        if (string.Equals(traffic, "red", StringComparison.OrdinalIgnoreCase))
        {
            return "Просрочено";
        }

        if (string.Equals(traffic, "pink", StringComparison.OrdinalIgnoreCase))
        {
            return "Не пройдено";
        }

        if (!passed)
        {
            return "Не пройдено";
        }

        if (nextDate is { } nd && nd.Date < reportDate.Date)
        {
            return "Просрочено";
        }

        return "Не пройдено";
    }

    /// <summary>
    /// Счётчики по строкам отчёта «Состояние по обучению» (без служебных «нет стандарта»).
    /// Overdue = просрочено + не проведено; ExpiringSoon = «по плану» (≤ 30/60 дн.).
    /// </summary>
    public static (int Overdue, int ExpiringSoon, int Ok, int Total) SummarizeStatusRows(
        IEnumerable<TrainingStatusRow> rows)
    {
        var programRows = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Program))
            .ToList();

        var overdue = programRows.Count(r =>
            string.Equals(r.Traffic, "red", StringComparison.OrdinalIgnoreCase)
            || string.Equals(r.Status, "Просрочено", StringComparison.OrdinalIgnoreCase));
        var expiring = programRows.Count(r =>
            string.Equals(r.Traffic, "yellow", StringComparison.OrdinalIgnoreCase));
        var ok = programRows.Count(r =>
            string.Equals(r.Traffic, "green", StringComparison.OrdinalIgnoreCase));

        return (overdue, expiring, ok, programRows.Count);
    }

    private static bool IsPassedResult(string? result)
    {
        var r = (result ?? string.Empty).Trim();
        if (r.Length == 0)
        {
            return false;
        }

        if (TrainingConductValues.IsNotTraining(r)
            || r.Contains("не проводилось", StringComparison.OrdinalIgnoreCase)
            || r.Contains("не сдал", StringComparison.OrdinalIgnoreCase)
            || r.Contains("не пройден", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return r.Contains("сдал", StringComparison.OrdinalIgnoreCase) ||
               r.Contains("пройден", StringComparison.OrdinalIgnoreCase) ||
               r.Equals("пройдено", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime? ResolveNextTrainingDate(
        DateTime? trainingDate,
        string? periodicity,
        DateTime? storedNextDate)
    {
        if (!trainingDate.HasValue)
        {
            return storedNextDate;
        }

        if (TrainingConductValues.IsOneTimePeriodicity(periodicity))
        {
            return null;
        }

        var months = TrainingProgramCatalog.PeriodMonthsFromPeriodicity(periodicity);
        if (months <= 0)
        {
            return storedNextDate;
        }

        return trainingDate.Value.Date.AddMonths(months);
    }

    private static string FormatProtocolNumber(TrainingConductRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.ProtocolNumber))
        {
            return record.ProtocolNumber.Trim();
        }

        return (record.CertificateNumber ?? string.Empty).Trim();
    }

    private static string DemoCertificateNumber(string person, string program)
    {
        var key = $"{person}|{program}";
        var n = Math.Abs(key.GetHashCode(StringComparison.Ordinal)) % 90000 + 10000;
        return n.ToString();
    }

    /// <summary>
    /// Для одного человека в организации — одно подразделение и одна должность в дереве отчёта
    /// (не дублировать «Администрация» и «—», «Главный энергетик» и «Главный энергетик *»).
    /// Если у человека несколько разных должностей — строки не схлопываются.
    /// </summary>
    public static List<TrainingStatusRow> UnifyPersonDepartments(IEnumerable<TrainingStatusRow> rows)
    {
        var source = rows.ToList();
        if (source.Count == 0)
        {
            return source;
        }

        // Одинаковые юрлица с разным написанием + подразделение ≠ название организации.
        var canonicalOrgs = OrganizationMatcher.BuildCanonicalNameMap(source.Select(r => r.Organization));
        source = source
            .Select(row =>
            {
                var org = canonicalOrgs.TryGetValue((row.Organization ?? string.Empty).Trim(), out var c)
                    ? c
                    : (row.Organization ?? string.Empty).Trim();
                var dept = EmployeeReportCollapse.SanitizeDepartment(org, row.Department);
                if (string.Equals(org, row.Organization, StringComparison.Ordinal)
                    && string.Equals(dept, row.Department, StringComparison.Ordinal))
                {
                    return row;
                }

                return row with { Organization = org, Department = dept };
            })
            .ToList();

        // «Строительство» как отдельная «организация» рядом с «ИП …» — вернуть людей под настоящее юрлицо.
        source = FoldFakeOrganizationBranches(source);

        var multiProfessionKeys = source
            .GroupBy(r => PersonKey(r.Organization, r.Person), StringComparer.OrdinalIgnoreCase)
            .Where(g => HasMultipleProfessions(g.Select(r => r.Profession)))
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var preferred = source
            .Where(r => !multiProfessionKeys.Contains(PersonKey(r.Organization, r.Person)))
            .GroupBy(r => PersonKey(r.Organization, r.Person), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    // Чаще всего встречающееся непустое подразделение (карточка),
                    // а не «первое попавшееся» из старого проведения.
                    Department = g.Select(r => r.Department)
                        .Where(d => !string.IsNullOrWhiteSpace(d) && !string.Equals(d, "—", StringComparison.Ordinal))
                        .GroupBy(d => d.Trim(), StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(x => x.Count())
                        .ThenByDescending(x => x.Key.Length)
                        .Select(x => x.First())
                        .FirstOrDefault()
                        ?? g.First().Department,
                    Profession = g.Select(r => r.Profession)
                        .OrderByDescending(p => (p ?? string.Empty).Length)
                        .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && !string.Equals(p, "—", StringComparison.Ordinal))
                        ?? g.First().Profession
                },
                StringComparer.OrdinalIgnoreCase);

        var unified = source
            .Select(row =>
            {
                var key = PersonKey(row.Organization, row.Person);
                if (multiProfessionKeys.Contains(key))
                {
                    return row;
                }

                if (!preferred.TryGetValue(key, out var profile))
                {
                    return row;
                }

                return row with
                {
                    Department = profile.Department,
                    Profession = profile.Profession
                };
            })
            .ToList();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<TrainingStatusRow>(unified.Count);
        foreach (var row in unified)
        {
            var professionKey = ProfessionKey(row.Profession);
            var key = string.IsNullOrWhiteSpace(row.Program)
                ? $"{row.Organization}|{row.Person}|{professionKey}|__empty__"
                : $"{row.Organization}|{row.Person}|{professionKey}|{row.Program}";
            if (!seen.Add(key))
            {
                continue;
            }

            result.Add(row);
        }

        return result;
    }

    /// <summary>
    /// Жёстко ставит подразделение из карточки штата (после Unify / ЭБ-строк),
    /// чтобы дерево «орг → подразделение → должность» совпадало с 1С.
    /// </summary>
    public static List<TrainingStatusRow> ApplyStaffDepartments(
        IEnumerable<TrainingStatusRow> rows,
        IReadOnlyList<(string Organization, string Name, string Profession, string Department)> employees)
    {
        var source = rows.ToList();
        var staff = (employees ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .ToList();
        if (source.Count == 0 || staff.Count == 0)
        {
            return source;
        }

        var byPersonProfession = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var byPerson = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in staff.GroupBy(
                     e => PersonKey(e.Organization, e.Name),
                     StringComparer.OrdinalIgnoreCase))
        {
            var bestDept = group
                .Select(e => EmployeeReportCollapse.SanitizeDepartment(e.Organization, e.Department))
                .Where(d => !string.IsNullOrWhiteSpace(d) && d != "—")
                .GroupBy(d => d, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Key.Length)
                .Select(g => g.Key)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(bestDept))
            {
                byPerson[group.Key] = bestDept;
            }

            foreach (var profGroup in group.GroupBy(e => ProfessionKey(e.Profession), StringComparer.OrdinalIgnoreCase))
            {
                var dept = profGroup
                    .Select(e => EmployeeReportCollapse.SanitizeDepartment(e.Organization, e.Department))
                    .FirstOrDefault(d => !string.IsNullOrWhiteSpace(d) && d != "—");
                if (!string.IsNullOrWhiteSpace(dept))
                {
                    byPersonProfession[$"{group.Key}|{profGroup.Key}"] = dept;
                }
            }
        }

        if (byPerson.Count == 0 && byPersonProfession.Count == 0)
        {
            return source;
        }

        return source
            .Select(row =>
            {
                var personKey = PersonKey(row.Organization, row.Person);
                var profKey = $"{personKey}|{ProfessionKey(row.Profession)}";
                string? dept = null;
                if (byPersonProfession.TryGetValue(profKey, out var byProf))
                {
                    dept = byProf;
                }
                else if (byPerson.TryGetValue(personKey, out var byName))
                {
                    dept = byName;
                }

                if (string.IsNullOrWhiteSpace(dept)
                    || string.Equals(dept, row.Department, StringComparison.OrdinalIgnoreCase))
                {
                    return row;
                }

                return row with { Department = dept };
            })
            .ToList();
    }

    /// <summary>
    /// Ветка «Строительство» на уровне юрлица — это подразделение, ошибочно попавшее в Organization.
    /// Переносим людей под настоящее юрлицо (ИП/ООО), где они уже есть.
    /// </summary>
    private static List<TrainingStatusRow> FoldFakeOrganizationBranches(List<TrainingStatusRow> source)
    {
        var realRows = source
            .Where(r => OrganizationMatcher.LooksLikeOrganization(r.Organization))
            .ToList();
        if (realRows.Count == 0)
        {
            return source;
        }

        // Подразделение «Строительство» → юрлицо, где оно уже встречается.
        var deptToRealOrg = realRows
            .Select(r => (Dept: (r.Department ?? string.Empty).Trim(), Org: r.Organization))
            .Where(x => x.Dept.Length > 0 && x.Dept != "—")
            .GroupBy(x => x.Dept, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.Org).First(),
                StringComparer.OrdinalIgnoreCase);

        // Человек уже под ИП/ООО → любая ложная ветка с тем же ФИО уходит туда.
        var personToRealOrg = realRows
            .Where(r => !string.IsNullOrWhiteSpace(r.Person))
            .GroupBy(r => r.Person.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g
                    .GroupBy(r => r.Organization, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(x => x.Count())
                    .First()
                    .Key,
                StringComparer.OrdinalIgnoreCase);

        return source
            .Select(row =>
            {
                var org = (row.Organization ?? string.Empty).Trim();
                if (org.Length == 0 || OrganizationMatcher.LooksLikeOrganization(org))
                {
                    return row;
                }

                string? targetOrg = null;
                if (deptToRealOrg.TryGetValue(org, out var byDept))
                {
                    targetOrg = byDept;
                }
                else if (!string.IsNullOrWhiteSpace(row.Person)
                         && personToRealOrg.TryGetValue(row.Person.Trim(), out var byPerson))
                {
                    targetOrg = byPerson;
                }

                if (string.IsNullOrWhiteSpace(targetOrg))
                {
                    return row;
                }

                var dept = (row.Department ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(dept) || dept == "—")
                {
                    dept = org;
                }

                dept = EmployeeReportCollapse.SanitizeDepartment(targetOrg, dept);
                if (string.IsNullOrWhiteSpace(dept))
                {
                    dept = "—";
                }

                return row with { Organization = targetOrg, Department = dept };
            })
            .ToList();
    }

    private static bool HasMultipleProfessions(IEnumerable<string?> professions) =>
        professions
            .Select(ProfessionKey)
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() >= 2;

    private static string ProfessionKey(string? profession)
    {
        var core = ProfessionMatcher.CoreKey(profession);
        return core.Length > 0 ? core : ProfessionMatcher.Normalize(profession);
    }

    private static string PersonKey(string organization, string person) =>
        $"{(organization ?? string.Empty).Trim()}|{(person ?? string.Empty).Trim()}";
}
