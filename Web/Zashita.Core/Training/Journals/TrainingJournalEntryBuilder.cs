namespace Zashita.Core.Training.Journals;

using System.Text.RegularExpressions;
using Zashita.Core.Employees;

public sealed class TrainingJournalEntryBuilder
{
    private readonly Func<string, IReadOnlyDictionary<string, List<string>>> _employeesByProfession;
    private readonly Func<string, IReadOnlyDictionary<string, TrainingJournalPersonInfo>> _employeesByName;

    public TrainingJournalEntryBuilder(
        Func<string, IReadOnlyDictionary<string, List<string>>> employeesByProfession,
        Func<string, IReadOnlyDictionary<string, TrainingJournalPersonInfo>>? employeesByName = null)
    {
        _employeesByProfession = employeesByProfession;
        _employeesByName = employeesByName ?? (_ => new Dictionary<string, TrainingJournalPersonInfo>(StringComparer.OrdinalIgnoreCase));
    }

    public List<TrainingJournalEntry> Build(
        IReadOnlyList<TrainingStandard> standards,
        TrainingJournalParams parameters,
        TrainingJournalDefinition definition,
        string defaultOrganization,
        IReadOnlyList<TrainingConductRecord>? conductRecords = null,
        IReadOnlyList<TrainingJournalEntry>? existingEntries = null)
    {
        var startRow = Math.Max(1, parameters.StartRowNumber);
        const int pageRowCapacity = 18;

        // Пустые строки дописываются в конец уже сформированных записей.
        if (parameters.EmptyRowCount > 0)
        {
            var kept = (existingEntries ?? []).ToList();
            var next = kept.Count == 0
                ? startRow
                : Math.Max(startRow, kept.Max(e => e.RowNumber) + 1);
            kept.AddRange(BuildEmptyRows(definition, parameters, defaultOrganization, next, conductRecords));
            return PadToPageEnd(kept, definition, parameters, defaultOrganization, conductRecords, pageRowCapacity);
        }

        var organization = defaultOrganization.Trim();
        var byProfession = _employeesByProfession(organization);
        var byName = _employeesByName(organization);
        _ = standards; // штат берём из папки организации, не из пунктов стандартов

        // Все журналы — строго тот же список людей, что вкладка «Организации».
        var entries = BuildFromEmployees(
            definition, parameters, organization, byProfession, byName, startRow, conductRecords);

        // Демо только если организация не выбрана — иначе пустой журнал честнее фейковых ФИО.
        if (entries.Count == 0 && string.IsNullOrWhiteSpace(organization))
        {
            entries.AddRange(BuildDemoEntries(definition, parameters, organization, startRow, conductRecords));
        }

        // Одна строка на человека — убираем повторы из стандартов / штата.
        entries = DeduplicateByPerson(entries);
        RenumberEntries(entries, Math.Max(1, parameters.StartRowNumber));

        return PadToPageEnd(entries, definition, parameters, defaultOrganization, conductRecords, pageRowCapacity);
    }

    private static List<TrainingJournalEntry> DeduplicateByPerson(List<TrainingJournalEntry> entries)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<TrainingJournalEntry>(entries.Count);
        foreach (var entry in entries)
        {
            var key = NormalizePersonKey(entry.EmployeeName);
            if (string.IsNullOrWhiteSpace(key))
            {
                result.Add(entry);
                continue;
            }

            if (!seen.Add(key))
            {
                continue;
            }

            result.Add(entry);
        }

        return result;
    }

    private static void RenumberEntries(List<TrainingJournalEntry> entries, int startRow)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.RowNumber == startRow + i)
            {
                continue;
            }

            entries[i] = e with { RowNumber = startRow + i };
        }
    }

    private static List<TrainingJournalEntry> PadToPageEnd(
        List<TrainingJournalEntry> entries,
        TrainingJournalDefinition definition,
        TrainingJournalParams parameters,
        string defaultOrganization,
        IReadOnlyList<TrainingConductRecord>? conductRecords,
        int pageRowCapacity)
    {
        if (!parameters.PadEmptyRowsToPageEnd || pageRowCapacity <= 0)
        {
            return entries;
        }

        var remainder = entries.Count % pageRowCapacity;
        var pad = remainder == 0
            ? (entries.Count == 0 ? pageRowCapacity : 0)
            : pageRowCapacity - remainder;
        if (pad <= 0)
        {
            return entries;
        }

        var next = entries.Count == 0
            ? Math.Max(1, parameters.StartRowNumber)
            : Math.Max(parameters.StartRowNumber, entries.Max(e => e.RowNumber) + 1);
        var padParams = parameters with { EmptyRowCount = pad };
        entries.AddRange(BuildEmptyRows(definition, padParams, defaultOrganization, next, conductRecords));
        return entries;
    }

    private static List<TrainingJournalEntry> BuildFromEmployees(
        TrainingJournalDefinition definition,
        TrainingJournalParams parameters,
        string organization,
        IReadOnlyDictionary<string, List<string>> byProfession,
        IReadOnlyDictionary<string, TrainingJournalPersonInfo> byName,
        int startRow,
        IReadOnlyList<TrainingConductRecord>? conductRecords)
    {
        var list = new List<TrainingJournalEntry>();
        if (byProfession.Count == 0 && byName.Count == 0)
        {
            return list;
        }

        var from = parameters.DateFrom.Date;
        var to = parameters.DateTo.Date;
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var defaultInstructor = parameters.DefaultInstructor?.Trim() ?? string.Empty;
        var n = 0;
        var seed = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Предпочитаем полный список по карточкам сотрудников (с подразделением),
        // а не только группировку по профессии.
        IEnumerable<(string Profession, string Worker)> source;
        if (byName.Count > 0)
        {
            source = byName.Values
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(p => (
                    string.IsNullOrWhiteSpace(p.Profession) ? "—" : p.Profession.Trim(),
                    p.Name.Trim()));
        }
        else if (byProfession.Count > 0)
        {
            source = byProfession
                .OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase)
                .SelectMany(p => p.Value
                    .Where(w => !string.IsNullOrWhiteSpace(w))
                    .Select(w => (p.Key.Trim(), w.Trim())));
        }
        else
        {
            return list;
        }

        foreach (var (prof, worker) in source)
        {
            var workerKey = NormalizePersonKey(worker);
            if (string.IsNullOrWhiteSpace(workerKey) || !seen.Add(workerKey))
            {
                continue;
            }

            seed++;
            var person = ResolvePerson(worker, byName);
            var dept = ResolveDepartment(person, parameters, organization);
            if (!DepartmentMatches(dept, parameters))
            {
                continue;
            }

            var workplace = !string.IsNullOrWhiteSpace(parameters.Workplace)
                ? parameters.Workplace!.Trim()
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(parameters.Workplace))
            {
                var wp = parameters.Workplace.Trim();
                var matchesWorkplace =
                    workplace.Contains(wp, StringComparison.OrdinalIgnoreCase) ||
                    prof.Contains(wp, StringComparison.OrdinalIgnoreCase) ||
                    dept.Contains(wp, StringComparison.OrdinalIgnoreCase);
                if (!matchesWorkplace)
                {
                    continue;
                }
            }

            var briefingDate = from.AddDays(seed % Math.Max(1, (to - from).Days + 1));
            var personProfession = !string.IsNullOrWhiteSpace(person?.Profession)
                ? person!.Profession.Trim()
                : (prof.Length > 0 ? prof : "—");
            var employeeName = definition.ContractorsOnly ? $"{worker} (подрядчик)" : worker;
            list.Add(CreateEntry(
                definition,
                parameters,
                startRow + n++,
                briefingDate,
                employeeName,
                person?.BirthDate,
                personProfession,
                workplace,
                dept,
                defaultInstructor,
                organization.Length > 0 ? organization : "—",
                conductRecords));
        }

        return list;
    }

    private static List<TrainingJournalEntry> BuildEmptyRows(
        TrainingJournalDefinition definition,
        TrainingJournalParams parameters,
        string defaultOrganization,
        int startRow,
        IReadOnlyList<TrainingConductRecord>? conductRecords)
    {
        var count = Math.Clamp(parameters.EmptyRowCount, 1, 500);
        var org = parameters.Organization ?? defaultOrganization;
        if (string.IsNullOrWhiteSpace(org))
        {
            org = "Организация";
        }

        var instructor = parameters.DefaultInstructor?.Trim() ?? string.Empty;
        var dept = NormalizeDepartment(parameters.Department, org);
        var workplace = parameters.Workplace?.Trim() ?? string.Empty;
        var list = new List<TrainingJournalEntry>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(CreateEntry(
                definition,
                parameters,
                startRow + i,
                parameters.DateFrom.Date,
                string.Empty,
                null,
                string.Empty,
                workplace,
                dept,
                instructor,
                org,
                conductRecords,
                allowEmptyPerson: true));
        }

        return list;
    }

    private static List<TrainingJournalEntry> BuildDemoEntries(
        TrainingJournalDefinition definition,
        TrainingJournalParams parameters,
        string organization,
        int startRow,
        IReadOnlyList<TrainingConductRecord>? conductRecords)
    {
        var org = parameters.Organization ?? organization;
        if (string.IsNullOrWhiteSpace(org))
        {
            org = "Организация";
        }

        var from = parameters.DateFrom.Date;
        var to = parameters.DateTo.Date;
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var demoPrograms = definition.Kind switch
        {
            TrainingJournalKind.WorkplaceOccupationalSafety => new[] { "Инструктаж на рабочем месте" },
            TrainingJournalKind.IntroductoryFireSafety => new[] { "Вводный противопожарный инструктаж" },
            TrainingJournalKind.WorkplaceFireSafety => new[] { "Противопожарный инструктаж на рабочем месте" },
            TrainingJournalKind.IntroductoryOccupationalSafetyContractors => new[] { "Вводный инструктаж (подряд)" },
            TrainingJournalKind.IntroductoryFireSafetyContractors => new[] { "Вводный противопожарный (подряд)" },
            TrainingJournalKind.ElectroSafetyGroupI => new[] { "Присвоение группы I (электробезопасность)" },
            _ => new[] { "Вводный инструктаж по охране труда" }
        };

        var list = new List<TrainingJournalEntry>();
        var i = 0;
        var demoInstructor = !string.IsNullOrWhiteSpace(parameters.DefaultInstructor)
            ? parameters.DefaultInstructor!.Trim()
            : "Петров П.П.";
        foreach (var program in demoPrograms)
        {
            _ = program;
            i++;
            list.Add(CreateEntry(
                definition,
                parameters,
                startRow + i - 1,
                from.AddDays(i * 3),
                definition.ContractorsOnly ? "Иванов И.И. (подрядчик)" : "Иванов И.И.",
                new DateTime(1985, 11, 22),
                "Слесарь",
                "Участок №1",
                "Производство",
                demoInstructor,
                org,
                conductRecords));
        }

        return list;
    }

    private static TrainingJournalEntry CreateEntry(
        TrainingJournalDefinition definition,
        TrainingJournalParams parameters,
        int rowNumber,
        DateTime briefingDate,
        string employeeName,
        DateTime? birthDate,
        string profession,
        string workplace,
        string department,
        string instructorName,
        string organization,
        IReadOnlyList<TrainingConductRecord>? conductRecords,
        bool allowEmptyPerson = false)
    {
        var briefingType = ResolveBriefingType(definition, parameters);
        var briefingReason = TrainingBriefingTypes.RequiresReason(briefingType)
            ? string.Empty
            : TrainingBriefingTypes.ReasonPlaceholder(briefingType);

        var localAct = !string.IsNullOrWhiteSpace(parameters.LocalActName)
            ? parameters.LocalActName!.Trim()
            : string.Empty;
        if (localAct is "-" or "—" or "–")
        {
            localAct = string.Empty;
        }

        var legalEntity = definition.ContractorsOnly ? organization : string.Empty;
        var instructorQualification = !string.IsNullOrWhiteSpace(instructorName) && conductRecords is { Count: > 0 }
            ? TrainingConductLookup.ResolveInstructorQualification(conductRecords, instructorName, briefingDate)
            : string.Empty;

        DateTime? previousAssignment = null;
        if (definition.TableKind == TrainingJournalTableKind.ElectroGroupI7)
        {
            previousAssignment = briefingDate.AddYears(-1);
        }

        return new TrainingJournalEntry(
            RowNumber: rowNumber,
            BriefingDate: briefingDate,
            EmployeeName: allowEmptyPerson && string.IsNullOrWhiteSpace(employeeName) ? string.Empty : employeeName,
            BirthDate: birthDate,
            Profession: profession,
            Workplace: workplace,
            Department: department,
            InstructorName: instructorName,
            Organization: organization,
            BriefingType: briefingType,
            BriefingReason: briefingReason,
            LocalActName: localAct,
            LegalEntity: legalEntity,
            InstructorQualification: instructorQualification,
            TheoryDate: briefingDate,
            PracticeDate: briefingDate,
            PreviousAssignmentDate: previousAssignment);
    }

    private static string ResolveBriefingType(
        TrainingJournalDefinition definition,
        TrainingJournalParams parameters)
    {
        var fromForm = TrainingBriefingTypes.Normalize(parameters.BriefingType);
        if (!string.IsNullOrWhiteSpace(fromForm))
        {
            var allowed = TrainingBriefingTypes.OptionsFor(definition.Kind);
            if (allowed.Any(o => string.Equals(o, fromForm, StringComparison.OrdinalIgnoreCase)))
            {
                return fromForm;
            }
        }

        return definition.Kind switch
        {
            TrainingJournalKind.WorkplaceOccupationalSafety or
                TrainingJournalKind.WorkplaceFireSafety => TrainingBriefingTypes.Primary,
            TrainingJournalKind.IntroductoryOccupationalSafety or
                TrainingJournalKind.IntroductoryFireSafety or
                TrainingJournalKind.IntroductoryOccupationalSafetyContractors or
                TrainingJournalKind.IntroductoryFireSafetyContractors => TrainingBriefingTypes.Introductory,
            _ => string.Empty
        };
    }

    private static TrainingJournalPersonInfo? ResolvePerson(
        string workerName,
        IReadOnlyDictionary<string, TrainingJournalPersonInfo> byName)
    {
        var key = NormalizePersonKey(workerName);
        if (key.Length == 0)
        {
            return null;
        }

        if (byName.TryGetValue(key, out var exact))
        {
            return exact;
        }

        foreach (var pair in byName)
        {
            if (NormalizePersonKey(pair.Key).Equals(key, StringComparison.OrdinalIgnoreCase) ||
                NormalizePersonKey(pair.Value.Name).Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static string ResolveDepartment(
        TrainingJournalPersonInfo? person,
        TrainingJournalParams parameters,
        string organization)
    {
        // В строке журнала — подразделение сотрудника. Фильтр из параметров применяется отдельно.
        return NormalizeDepartment(person?.Department, organization);
    }

    private static string NormalizeDepartment(string? department, string organization)
    {
        var dept = (department ?? string.Empty).Trim();
        if (dept.Length == 0 ||
            dept is "—" or "-" ||
            dept.Contains("не указано", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        // Не подставлять организацию в колонку «подразделение» (точное или явное совпадение с орг.).
        if (!string.IsNullOrWhiteSpace(organization))
        {
            var org = organization.Trim();
            if (string.Equals(dept, org, StringComparison.OrdinalIgnoreCase) ||
                (dept.Length >= 8 && org.StartsWith(dept, StringComparison.OrdinalIgnoreCase)) ||
                (org.Length >= 8 && dept.StartsWith(org, StringComparison.OrdinalIgnoreCase)))
            {
                return string.Empty;
            }
        }

        return dept;
    }

    private static bool DepartmentMatches(string personDept, TrainingJournalParams parameters)
    {
        var filter = NormalizeDepartment(parameters.Department, parameters.Organization);
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(personDept))
        {
            // Фильтр задан — сотрудников без подразделения не показываем.
            return false;
        }

        return parameters.StrictDepartmentFilter
            ? string.Equals(personDept, filter, StringComparison.OrdinalIgnoreCase)
            : personDept.Contains(filter, StringComparison.OrdinalIgnoreCase)
              || filter.Contains(personDept, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePersonKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var normalized = name
            .Replace('\u00A0', ' ')
            .Replace("(подрядчик)", "", StringComparison.OrdinalIgnoreCase)
            .Replace('ё', 'е')
            .Replace('Ё', 'Е');
        normalized = Regex.Replace(normalized, @"[.,;:]+", " ");
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return string.Concat(parts[0], " ", parts[1]).ToLowerInvariant();
        }

        return normalized.ToLowerInvariant();
    }

}
