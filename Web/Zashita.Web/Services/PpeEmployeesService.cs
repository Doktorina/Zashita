using System.Data;
using Zashita.Core.Employees;
using Zashita.Core.Excel;

namespace Zashita.Web.Services;

public sealed class PpeEmployeesService
{
    private readonly ExcelWorkbookStore _excel;
    private readonly KristinaProfessionsStore _kristina;
    private readonly OrganizationEmployeesStore _orgStore;

    public PpeEmployeesService(
        ExcelWorkbookStore excel,
        KristinaProfessionsStore kristina,
        OrganizationEmployeesStore orgStore)
    {
        _excel = excel;
        _kristina = kristina;
        _orgStore = orgStore;
    }

    public IReadOnlyDictionary<string, List<string>> GetByProfession(string organization)
    {
        var merged = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var orgEmployees = _orgStore.Get(organization);

        if (orgEmployees.Count > 0)
        {
            foreach (var employee in orgEmployees)
            {
                if (!IsValidEmployeeName(employee.Name))
                {
                    continue;
                }

                var profession = string.IsNullOrWhiteSpace(employee.Profession)
                    ? "Сотрудники"
                    : employee.Profession.Trim();
                Add(merged, profession, employee.Name);
            }

            AddKristinaProfessionKeys(merged, organization);
            AddIndividualEntrepreneur(merged, organization);
            return ToSortedDictionary(merged);
        }

        if (_kristina.IsAvailable)
        {
            foreach (var pair in _kristina.GetEmployeesByProfession(organization, _excel))
            {
                foreach (var person in pair.Value)
                {
                    if (IsValidEmployeeName(person))
                    {
                        Add(merged, pair.Key, person);
                    }
                }
            }
        }

        foreach (var pair in LoadFromWorkbook(organization))
        {
            foreach (var person in pair.Value)
            {
                Add(merged, pair.Key, person);
            }
        }

        AddIndividualEntrepreneur(merged, organization);

        return merged.Count == 0
            ? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            : ToSortedDictionary(merged);
    }

    public IReadOnlyList<string> GetEmployeesForProfession(string organization, string profession)
    {
        if (string.IsNullOrWhiteSpace(profession))
        {
            return [];
        }

        var expected = ProfessionMatcher.Normalize(profession);
        var byProfession = GetByProfession(organization);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (byProfession.TryGetValue(profession, out var direct))
        {
            foreach (var person in direct.Where(IsValidEmployeeName))
            {
                names.Add(person);
            }
        }

        foreach (var pair in byProfession)
        {
            if (string.Equals(ProfessionMatcher.Normalize(pair.Key), expected, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var person in pair.Value.Where(IsValidEmployeeName))
                {
                    names.Add(person);
                }
            }
        }

        if (names.Count == 0)
        {
            foreach (var employee in _orgStore.Get(organization))
            {
                if (!IsValidEmployeeName(employee.Name))
                {
                    continue;
                }

                if (string.Equals(ProfessionMatcher.Normalize(employee.Profession), expected, StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(employee.Name);
                }
            }
        }

        return names
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> GetAllEmployeeNames(string organization) =>
        GetByProfession(organization)
            .SelectMany(pair => pair.Value)
            .Where(IsValidEmployeeName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Карта ФИО → данные сотрудника (дата рождения, подразделение) для журналов.</summary>
    public IReadOnlyDictionary<string, Zashita.Core.Training.Journals.TrainingJournalPersonInfo> GetDetailsByName(
        string organization)
    {
        var map = new Dictionary<string, Zashita.Core.Training.Journals.TrainingJournalPersonInfo>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var employee in _orgStore.Get(organization))
        {
            if (!IsValidEmployeeName(employee.Name))
            {
                continue;
            }

            var key = employee.Name.Trim();
            map[key] = new Zashita.Core.Training.Journals.TrainingJournalPersonInfo(
                key,
                employee.Profession?.Trim() ?? string.Empty,
                employee.Department?.Trim() ?? string.Empty,
                employee.BirthDate);
        }

        return map;
    }

    private void AddKristinaProfessionKeys(Dictionary<string, HashSet<string>> merged, string organization)
    {
        if (!_kristina.IsAvailable)
        {
            return;
        }

        foreach (var profession in _kristina.GetProfessionsForOrganization(organization))
        {
            if (!merged.ContainsKey(profession))
            {
                merged[profession] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    private static void AddIndividualEntrepreneur(Dictionary<string, HashSet<string>> merged, string organization)
    {
        var ip = OrganizationMatcher.InferIndividualEntrepreneurEmployee(organization);
        if (ip is not null)
        {
            Add(merged, "Индивидуальный предприниматель", ip);
        }
    }

    private IReadOnlyDictionary<string, List<string>> LoadFromWorkbook(string organization)
    {
        var byProfession = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        if (!_excel.HasWorkbook)
        {
            return ToSortedDictionary(byProfession);
        }

        foreach (var sheet in _excel.SheetNames)
        {
            var table = _excel.GetSheet(sheet);
            if (table is null)
            {
                continue;
            }

            if (OneCEmployeeListParser.TryParse(table, out var reportOrg, out var reportEmployees) &&
                reportEmployees.Count > 0 &&
                OrganizationMatcher.Matches(organization, reportOrg ?? sheet))
            {
                foreach (var employee in reportEmployees)
                {
                    Add(byProfession, employee.Profession, employee.Name);
                }

                continue;
            }

            var employeeCol = FindColumn(table, "сотрудник", "фио", "работник", "физ. лицо", "физ лицо");
            if (employeeCol is null)
            {
                continue;
            }

            var professionCol = FindColumn(table, "должность", "профессия", "проф");
            var orgCol = FindColumn(table, "организация", "юр лицо", "юридическое лицо", "юр. лицо");

            foreach (DataRow row in table.Rows)
            {
                var employee = row[employeeCol]?.ToString()?.Trim();
                if (!IsValidEmployeeName(employee))
                {
                    continue;
                }

                var profession = professionCol is null
                    ? "Сотрудники"
                    : row[professionCol]?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(profession))
                {
                    profession = "Сотрудники";
                }

                if (orgCol is not null)
                {
                    var org = row[orgCol]?.ToString() ?? string.Empty;
                    if (!OrganizationMatcher.Matches(organization, org))
                    {
                        continue;
                    }
                }
                else if (!OrganizationMatcher.Matches(organization, sheet))
                {
                    continue;
                }

                Add(byProfession, profession, employee!);
            }
        }

        return ToSortedDictionary(byProfession);
    }

    private static bool IsValidEmployeeName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        !name.Contains("не указан", StringComparison.OrdinalIgnoreCase);

    private static void Add(Dictionary<string, HashSet<string>> map, string profession, string name)
    {
        if (!map.TryGetValue(profession, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            map[profession] = set;
        }

        set.Add(name);
    }

    private static IReadOnlyDictionary<string, List<string>> ToSortedDictionary(
        Dictionary<string, HashSet<string>> map) =>
        map
            .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                k => k.Key,
                v => v.Value.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);

    private static string? FindColumn(DataTable table, params string[] keys)
    {
        foreach (DataColumn col in table.Columns)
        {
            var name = col.ColumnName.ToLowerInvariant();
            if (keys.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                return col.ColumnName;
            }
        }

        return null;
    }
}
