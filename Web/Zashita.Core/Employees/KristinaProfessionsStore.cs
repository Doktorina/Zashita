using System.Data;
using Zashita.Core.Excel;

namespace Zashita.Core.Employees;

/// <summary>
/// Профессии по юр. лицам из файла Кристины + сопоставление сотрудников из Excel 1С.
/// </summary>
public sealed class KristinaProfessionsStore
{
    private readonly string _appDataFile;
    private readonly string? _configuredPath;
    private readonly object _sync = new();
    private string? _cachedPath;
    private DateTime _cachedWriteTime;
    private Dictionary<string, List<string>> _professionsByOrg = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _organizations = new(StringComparer.OrdinalIgnoreCase);

    public KristinaProfessionsStore(string appDataDirectory, string? configuredPath = null)
    {
        Directory.CreateDirectory(appDataDirectory);
        _appDataFile = Path.Combine(appDataDirectory, "kristina-professions.xlsx");
        _configuredPath = configuredPath;
    }

    public bool IsAvailable => File.Exists(ResolvePath());

    public IReadOnlyList<string> GetOrganizations()
    {
        EnsureLoaded();
        return _organizations.OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public IReadOnlyList<string> GetProfessionsForOrganization(string organization)
    {
        EnsureLoaded();
        return GetProfessionsForOrganizationInternal(organization);
    }

    public IReadOnlyDictionary<string, List<string>> GetEmployeesByProfession(
        string organization,
        ExcelWorkbookStore? workersWorkbook = null)
    {
        EnsureLoaded();
        var org = (organization ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(org))
        {
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }

        var professions = GetProfessionsForOrganizationInternal(org);
        if (professions.Count == 0)
        {
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var profession in professions)
        {
            var employees = ResolveEmployeesForProfession(org, profession, workersWorkbook);
            if (employees.Count == 0)
            {
                employees = ["Сотрудник не указан"];
            }

            if (!result.TryGetValue(profession, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                result[profession] = set;
            }

            foreach (var employee in employees)
            {
                set.Add(employee);
            }
        }

        return result
            .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                k => k.Key,
                v => v.Value.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    private List<string> GetProfessionsForOrganizationInternal(string organization)
    {
        var direct = _professionsByOrg
            .Where(kv => OrganizationMatcher.Matches(organization, kv.Key))
            .SelectMany(kv => kv.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return direct;
    }

    private List<string> ResolveEmployeesForProfession(
        string organization,
        string profession,
        ExcelWorkbookStore? workersWorkbook)
    {
        var list = new List<string>();
        if (workersWorkbook is { HasWorkbook: true })
        {
            list.AddRange(FindWorkersInWorkbook(workersWorkbook, organization, profession));
        }

        // Нет совпадения по профессии (типично для выгрузки 1С без должностей) —
        // подтягиваем всех сотрудников организации из загруженного Excel.
        if (list.Count == 0 && workersWorkbook is { HasWorkbook: true })
        {
            list.AddRange(FindAllWorkersForOrganization(workersWorkbook, organization));
        }

        if (list.Count == 0)
        {
            var ipEmployee = OrganizationMatcher.InferIndividualEntrepreneurEmployee(organization);
            if (!string.IsNullOrWhiteSpace(ipEmployee))
            {
                list.Add(ipEmployee);
            }
        }

        return list
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> FindWorkersInWorkbook(
        ExcelWorkbookStore workbook,
        string organization,
        string profession)
    {
        var list = new List<string>();
        foreach (var sheetName in workbook.SheetNames)
        {
            var table = workbook.GetSheet(sheetName);
            if (table is null)
            {
                continue;
            }

            if (OneCEmployeeListParser.TryParse(table, out var reportOrg, out var reportEmployees) &&
                reportEmployees.Count > 0 &&
                OrganizationMatcher.Matches(organization, reportOrg ?? sheetName))
            {
                foreach (var employee in reportEmployees)
                {
                    if (ProfessionMatcher.Matches(profession, employee.Profession) ||
                        string.Equals(employee.Profession, "Сотрудники", StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(employee.Name);
                    }
                }

                continue;
            }

            var employeeCol = FindColumn(table, "сотрудник", "фио", "работник", "физ. лицо", "физ лицо");
            var professionCol = FindColumn(table, "должность", "профессия", "проф");
            if (employeeCol is null || professionCol is null)
            {
                continue;
            }

            var orgCol = FindColumn(table, "организация", "юр лицо", "юридическое лицо", "юр. лицо");

            foreach (DataRow row in table.Rows)
            {
                var employee = row[employeeCol]?.ToString()?.Trim();
                var prof = row[professionCol]?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(employee) || string.IsNullOrWhiteSpace(prof))
                {
                    continue;
                }

                if (!ProfessionMatcher.Matches(profession, prof))
                {
                    continue;
                }

                if (orgCol is not null)
                {
                    var org = row[orgCol]?.ToString() ?? string.Empty;
                    if (!OrganizationMatcher.Matches(organization, org))
                    {
                        continue;
                    }
                }
                else if (!OrganizationMatcher.Matches(organization, sheetName))
                {
                    continue;
                }

                list.Add(employee);
            }
        }

        return list;
    }

    private static List<string> FindAllWorkersForOrganization(
        ExcelWorkbookStore workbook,
        string organization)
    {
        var list = new List<string>();
        foreach (var sheetName in workbook.SheetNames)
        {
            var table = workbook.GetSheet(sheetName);
            if (table is null)
            {
                continue;
            }

            if (OneCEmployeeListParser.TryParse(table, out var reportOrg, out var reportEmployees) &&
                reportEmployees.Count > 0 &&
                OrganizationMatcher.Matches(organization, reportOrg ?? sheetName))
            {
                list.AddRange(reportEmployees.Select(e => e.Name));
                continue;
            }

            var employeeCol = FindColumn(table, "сотрудник", "фио", "работник", "физ. лицо", "физ лицо");
            if (employeeCol is null)
            {
                continue;
            }

            var orgCol = FindColumn(table, "организация", "юр лицо", "юридическое лицо", "юр. лицо");
            foreach (DataRow row in table.Rows)
            {
                var employee = row[employeeCol]?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(employee))
                {
                    continue;
                }

                if (orgCol is not null)
                {
                    var org = row[orgCol]?.ToString() ?? string.Empty;
                    if (!OrganizationMatcher.Matches(organization, org))
                    {
                        continue;
                    }
                }
                else if (!OrganizationMatcher.Matches(organization, sheetName))
                {
                    continue;
                }

                list.Add(employee);
            }
        }

        return list;
    }

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

    private void EnsureLoaded()
    {
        var path = ResolvePath();
        if (!File.Exists(path))
        {
            lock (_sync)
            {
                _professionsByOrg = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                _organizations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _cachedPath = path;
                _cachedWriteTime = DateTime.MinValue;
            }

            return;
        }

        var writeTime = File.GetLastWriteTimeUtc(path);
        lock (_sync)
        {
            if (_cachedPath == path && _cachedWriteTime == writeTime && _professionsByOrg.Count > 0)
            {
                return;
            }

            try
            {
                _professionsByOrg = ParseSimpleLayout(path);
                _organizations = new HashSet<string>(_professionsByOrg.Keys, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                _professionsByOrg = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                _organizations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            _cachedPath = path;
            _cachedWriteTime = writeTime;
        }
    }

    /// <summary>
    /// Принудительно сбрасывает кеш после замены файла на диске.
    /// </summary>
    public void Invalidate()
    {
        var path = ResolvePath();
        lock (_sync)
        {
            _professionsByOrg = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            _organizations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _cachedPath = path;
            _cachedWriteTime = DateTime.MinValue;
        }
    }

    private string ResolvePath()
    {
        if (!string.IsNullOrWhiteSpace(_configuredPath) && File.Exists(_configuredPath))
        {
            return _configuredPath;
        }

        if (File.Exists(_appDataFile))
        {
            return _appDataFile;
        }

        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "Telegram Desktop",
            "От_Кристины_Списки_профессий_по_юр_лицам_1.xlsx");
        if (File.Exists(downloads))
        {
            return downloads;
        }

        return _appDataFile;
    }

    private static Dictionary<string, List<string>> ParseSimpleLayout(string path)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var store = new ExcelWorkbookStore();
        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        store.LoadFromStream(fs, Path.GetFileName(path));

        foreach (var sheetName in store.SheetNames)
        {
            var table = store.GetSheet(sheetName);
            if (table is null || table.Columns.Count < 2)
            {
                continue;
            }

            var orgCol = table.Columns[0].ColumnName;
            var profCol = table.Columns[1].ColumnName;
            var activeOrg = string.Empty;

            if (OrganizationMatcher.LooksLikeOrganization(sheetName))
            {
                AddOrganization(result, sheetName);
            }

            foreach (DataRow row in table.Rows)
            {
                var colA = row[orgCol]?.ToString()?.Trim() ?? string.Empty;
                var colB = row[profCol]?.ToString()?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(colA))
                {
                    activeOrg = colA;
                    if (OrganizationMatcher.LooksLikeOrganization(activeOrg))
                    {
                        AddOrganization(result, activeOrg);
                    }
                }

                if (string.IsNullOrWhiteSpace(colB))
                {
                    continue;
                }

                if (colB.Contains("должност", StringComparison.OrdinalIgnoreCase) ||
                    colB.Contains("професс", StringComparison.OrdinalIgnoreCase) ||
                    colB.Contains("сотруд", StringComparison.OrdinalIgnoreCase) ||
                    colB.Equals("Компания", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var org = string.IsNullOrWhiteSpace(activeOrg) ? sheetName : activeOrg;
                if (!OrganizationMatcher.LooksLikeOrganization(org))
                {
                    continue;
                }

                AddProfession(result, org, colB);
            }
        }

        return result.ToDictionary(
            k => k.Key,
            v => v.Value.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static void AddOrganization(Dictionary<string, HashSet<string>> map, string org)
    {
        if (!map.ContainsKey(org))
        {
            map[org] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void AddProfession(Dictionary<string, HashSet<string>> map, string org, string profession)
    {
        if (!map.TryGetValue(org, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            map[org] = set;
        }

        set.Add(profession);
    }
}
