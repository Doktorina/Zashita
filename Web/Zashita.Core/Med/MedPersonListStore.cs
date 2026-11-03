using System.Text.Json;
using System.Text.RegularExpressions;
using Zashita.Core.Employees;
using Zashita.Core.Incidents;

namespace Zashita.Core.Med;

public sealed class MedPersonListStore
{
    private readonly MedDataPaths _paths;
    private readonly MedJsonListCache<MedPersonListRecord> _cache = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public MedPersonListStore(MedDataPaths paths) => _paths = paths;

    public List<MedPersonListRecord> LoadAll() =>
        _cache.GetOrLoad(_paths.PersonListsFile, LoadFromDisk);

    private List<MedPersonListRecord> LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_paths.PersonListsFile))
            {
                return [];
            }

            var json = File.ReadAllText(_paths.PersonListsFile);
            var list = JsonSerializer.Deserialize<List<MedPersonListRecord>>(json, JsonOpts) ?? [];
            foreach (var record in list)
            {
                record.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                record.Lines ??= [];
                if (string.IsNullOrWhiteSpace(record.ExamType))
                {
                    record.ExamType = InferExamType(record.Comment);
                }
            }

            return list;
        }
        catch
        {
            return [];
        }
    }

    public MedPersonListRecord? GetById(string id) =>
        LoadAll().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));

    public List<MedPersonListRecord> LoadByOrganization(string organization) =>
        LoadAll()
            .Where(r => MedOrganizationFilter.Matches(organization, r.Organization))
            .OrderByDescending(r => r.Date)
            .ThenByDescending(r => r.Number, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public int AlignOrganizationNames(IEnumerable<string> canonicalOrganizations)
    {
        var list = LoadAll();
        var changed = MedOrganizationFilter.AlignOrganizations(
            list,
            record => record.Organization,
            (record, name) => record.Organization = name,
            canonicalOrganizations);
        if (changed > 0)
        {
            SaveAll(list);
        }

        return changed;
    }

    public void SaveAll(IEnumerable<MedPersonListRecord> records)
    {
        var data = records
            .Where(r => !string.IsNullOrWhiteSpace(r.Id))
            .OrderByDescending(r => r.Date)
            .ThenByDescending(r => r.Number, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        File.WriteAllText(_paths.PersonListsFile, JsonSerializer.Serialize(data, JsonOpts));
        _cache.Replace(data, _paths.PersonListsFile);
    }

    public void Upsert(MedPersonListRecord record)
    {
        var list = LoadAll();
        var idx = list.FindIndex(x => string.Equals(x.Id, record.Id, StringComparison.Ordinal));
        if (idx >= 0)
        {
            list[idx] = record;
        }
        else
        {
            list.Add(record);
        }

        SaveAll(list);
    }

    public void Delete(string id)
    {
        var list = LoadAll();
        list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        SaveAll(list);
    }

    public string SuggestNextNumber(string? excludeId = null) =>
        IncidentNumberHelper.NextNumber(
            "OT-",
            LoadAll()
                .Where(r => excludeId is null || !string.Equals(r.Id, excludeId, StringComparison.Ordinal))
                .Select(r => r.Number),
            pad: 8);

    /// <summary>
    /// Больше не создаём демо-ФИО. Удаляет старые засеянные документы и исправляет битые номера.
    /// </summary>
    public void EnsureSeeded(string organization)
    {
        var all = LoadAll();
        var changed = PurgeLegacyDemoDocuments(all);
        changed |= FixMalformedNumbers(all);
        if (changed)
        {
            SaveAll(all);
        }
    }

    public MedPersonListRecord CreateFromEmployees(
        string organization,
        IReadOnlyList<OrganizationEmployee> employees,
        string? department = null,
        IReadOnlyList<MedExamRecord>? exams = null)
    {
        var deptFilter = department?.Trim() ?? string.Empty;
        var filtered = string.IsNullOrWhiteSpace(deptFilter)
            ? employees
            : employees
                .Where(e => string.IsNullOrWhiteSpace(e.Department) ||
                            e.Department.Contains(deptFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var document = new MedPersonListRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Organization = organization,
            Number = SuggestNextNumber(),
            Date = DateTime.Today,
            ExamType = "Периодический",
            Factors = "Приказ 29н",
            Department = deptFilter,
            Lines = BuildLinesFromEmployees(filtered, exams)
        };

        Upsert(document);
        return document;
    }

    public static List<MedPersonListLine> BuildLinesFromEmployees(
        IReadOnlyList<OrganizationEmployee> employees,
        IReadOnlyList<MedExamRecord>? exams = null)
    {
        var examLookup = (exams ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e.EmployeeName))
            .GroupBy(e => e.EmployeeName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(e => e.ExamDate)
                    .ThenByDescending(e => !string.IsNullOrWhiteSpace(e.ItemNumber) ? 1 : 0)
                    .First(),
                StringComparer.OrdinalIgnoreCase);

        return employees
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(e =>
            {
                examLookup.TryGetValue(e.Name.Trim(), out var exam);
                return (employee: e, exam);
            })
            .Where(pair => IsEligibleForPersonList(pair.exam))
            .Select(pair => new MedPersonListLine
            {
                Id = Guid.NewGuid().ToString("N"),
                EmployeeName = pair.employee.Name,
                BirthDate = pair.employee.BirthDate,
                Profession = !string.IsNullOrWhiteSpace(pair.employee.Profession) ? pair.employee.Profession : pair.exam?.Profession ?? string.Empty,
                Workplace = !string.IsNullOrWhiteSpace(pair.employee.Department) ? pair.employee.Department : pair.exam?.Department ?? string.Empty,
                ItemNumber = MedItemNumber.Normalize(pair.exam?.ItemNumber),
                Periodicity = MedPeriodicity.ApplyUnder21Override(
                    pair.employee.BirthDate,
                    pair.exam?.Periodicity ?? MedPeriodicity.OnceAYear)
            })
            .ToList();
    }

    /// <summary>
    /// Подтягивает пустые пункты/должности из актуальных медосмотров и штата перед печатью.
    /// </summary>
    public static void EnrichLinesFromSources(
        MedPersonListRecord document,
        IReadOnlyList<MedExamRecord>? exams,
        IReadOnlyList<OrganizationEmployee>? employees = null)
    {
        if (document.Lines.Count == 0)
        {
            return;
        }

        var examLookup = (exams ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e.EmployeeName))
            .GroupBy(e => e.EmployeeName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(e => e.ExamDate)
                    .ThenByDescending(e => !string.IsNullOrWhiteSpace(e.ItemNumber) ? 1 : 0)
                    .First(),
                StringComparer.OrdinalIgnoreCase);

        var employeeLookup = (employees ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .GroupBy(e => e.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var line in document.Lines)
        {
            var name = line.EmployeeName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            examLookup.TryGetValue(name, out var exam);
            employeeLookup.TryGetValue(name, out var employee);

            line.ItemNumber = MedItemNumber.Normalize(
                !string.IsNullOrWhiteSpace(line.ItemNumber) ? line.ItemNumber : exam?.ItemNumber);

            if (string.IsNullOrWhiteSpace(line.Profession))
            {
                line.Profession = !string.IsNullOrWhiteSpace(employee?.Profession)
                    ? employee!.Profession
                    : exam?.Profession ?? string.Empty;
            }
            else if (employee is not null && !string.IsNullOrWhiteSpace(employee.Profession))
            {
                // Должность из штата актуальнее снимка в документе.
                line.Profession = employee.Profession;
            }

            if (string.IsNullOrWhiteSpace(line.Workplace))
            {
                line.Workplace = !string.IsNullOrWhiteSpace(employee?.Department)
                    ? employee!.Department
                    : exam?.Department ?? string.Empty;
            }

            if (exam is not null && !string.IsNullOrWhiteSpace(exam.Periodicity))
            {
                // Если в снимке списка осталась периодичность по умолчанию — берём из медосмотра.
                if (string.IsNullOrWhiteSpace(line.Periodicity) ||
                    string.Equals(line.Periodicity, MedPeriodicity.OnceAYear, StringComparison.OrdinalIgnoreCase))
                {
                    line.Periodicity = MedPeriodicity.Normalize(exam.Periodicity);
                }
            }

            line.Periodicity = MedPeriodicity.Normalize(line.Periodicity);
        }
    }

    private static bool PurgeLegacyDemoDocuments(List<MedPersonListRecord> records)
    {
        var before = records.Count;
        records.RemoveAll(IsLegacyDemoDocument);
        return records.Count != before;
    }

    private static bool FixMalformedNumbers(List<MedPersonListRecord> records)
    {
        var changed = false;
        foreach (var record in records)
        {
            if (!IsMalformedNumber(record.Number))
            {
                continue;
            }

            record.Number = IncidentNumberHelper.NextNumber(
                "OT-",
                records
                    .Where(r => !string.Equals(r.Id, record.Id, StringComparison.Ordinal))
                    .Select(r => r.Number),
                pad: 8);
            changed = true;
        }

        return changed;
    }

    private static bool IsMalformedNumber(string? number) =>
        string.IsNullOrWhiteSpace(number) ||
        number.Contains("Dpad", StringComparison.OrdinalIgnoreCase) ||
        !Regex.IsMatch(number, @"^OT-\d{8}$", RegexOptions.CultureInvariant);

    private static bool IsLegacyDemoDocument(MedPersonListRecord record)
    {
        string[] demoNames =
        [
            "Кирпичников В.И.",
            "Красильникова А.В.",
            "Иванов И.И.",
            "Онисова А.Д.",
            "Петров П.П."
        ];

        string[] demoDepartments =
        [
            "Служба главного инженера",
            "Гараж",
            "Администрация",
            "Бригада ремонта",
            "КСР"
        ];

        var names = record.Lines
            .Select(l => l.EmployeeName?.Trim() ?? "")
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();
        if (names.Count == 0)
        {
            return false;
        }

        var hasDemoName = names.Any(n => demoNames.Any(d => string.Equals(d, n, StringComparison.OrdinalIgnoreCase)));
        var allDemoNames = names.All(n => demoNames.Any(d => string.Equals(d, n, StringComparison.OrdinalIgnoreCase)));
        var hasDemoDepartment = demoDepartments.Any(d =>
            string.Equals(d, record.Department, StringComparison.OrdinalIgnoreCase));

        return allDemoNames || (hasDemoName && hasDemoDepartment);
    }

    private static string InferExamType(string comment)
    {
        if (comment.Contains("предвар", StringComparison.OrdinalIgnoreCase))
        {
            return "Предварительный";
        }

        return "Периодический";
    }

    private static bool IsEligibleForPersonList(MedExamRecord? exam) =>
        exam is not null && !string.IsNullOrWhiteSpace(MedItemNumber.Normalize(exam.ItemNumber));
}
