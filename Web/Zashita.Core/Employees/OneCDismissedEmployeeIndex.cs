namespace Zashita.Core.Employees;

/// <summary>
/// Индекс штата по CSV 1С.
/// Вкладка «Организации» и деактивация: человек виден в юрлице ТОЛЬКО если
/// в выгрузке есть актуальная строка штата для этого юрлица (работа / отпуск по уходу и т.п.).
/// </summary>
public sealed class OneCDismissedEmployeeIndex
{
    private readonly List<StaffRecord> _working;
    private readonly List<StaffRecord> _dismissedOnly;

    private OneCDismissedEmployeeIndex(List<StaffRecord> working, List<StaffRecord> dismissedOnly)
    {
        _working = working;
        _dismissedOnly = dismissedOnly;
    }

    public static OneCDismissedEmployeeIndex BuildFromCsvFiles(IEnumerable<string> filePaths)
    {
        var parsed = OneCEmployeesCsvParser.ParseFiles(filePaths);
        var consolidated = OneCEmployeeCsvMerge.ApplyPerLegalEntityDismissalRule(
            OneCEmployeeCsvMerge.ConsolidateOrganizations(parsed));

        var workingByKey = new Dictionary<string, StaffRecord>(StringComparer.OrdinalIgnoreCase);
        var dismissedByKey = new Dictionary<string, StaffRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var (organization, rows) in consolidated)
        {
            foreach (var (row, isActive) in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Name))
                {
                    continue;
                }

                // Как при синке: КСК → тройка в КСК, остальные в Строй Каркас.
                var org = ResolveStaffOrganization(organization, row);
                var working = OneCEmployeeCsvMerge.IsCsvRowEmployed(row, isActive);
                var record = new StaffRecord(
                    org,
                    OneCEmployeeListParser.NormalizeSnils(row.Snils),
                    row.Name.Trim());
                var key = BuildPersonOrgKey(org, row);

                if (working)
                {
                    workingByKey[key] = record;
                    dismissedByKey.Remove(key);
                }
                else if (!workingByKey.ContainsKey(key))
                {
                    // Уволен только в этом ЮЛ (ключ org|СНИЛС) — на другие ЮЛ не распространяется.
                    dismissedByKey[key] = record;
                }
            }
        }

        foreach (var key in workingByKey.Keys)
        {
            dismissedByKey.Remove(key);
        }

        return new OneCDismissedEmployeeIndex(
            workingByKey.Values.ToList(),
            dismissedByKey.Values.ToList());
    }

    private static string ResolveStaffOrganization(string organization, OneCEmployeeRow row) =>
        OrganizationLegalEntitySplit.ResolveCsvOrganization(organization, row.Name, row.Snils);

    /// <summary>
    /// Есть ли в CSV для этого юрлица строка со статусом «Работа».
    /// Без совпадения по компании + СНИЛС/ФИО человек НЕ должен быть во вкладке организации.
    /// </summary>
    public bool IsWorkingInOrganization(string organization, string employeeName, string? snils = null) =>
        FindMatch(_working, organization, employeeName, snils) is not null;

    /// <summary>В CSV по юрлицу только увольнение / нет строки «Работа».</summary>
    public bool IsDismissed(string organization, string employeeName, string? snils = null)
    {
        if (IsWorkingInOrganization(organization, employeeName, snils))
        {
            return false;
        }

        return FindMatch(_dismissedOnly, organization, employeeName, snils) is not null;
    }

    public int Count => _dismissedOnly.Count;

    public int WorkingCount => _working.Count;

    private static StaffRecord? FindMatch(
        List<StaffRecord> records,
        string organization,
        string employeeName,
        string? snils)
    {
        var org = (organization ?? string.Empty).Trim();
        var name = (employeeName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(org) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var snilsNorm = OneCEmployeeListParser.NormalizeSnils(snils ?? string.Empty);
        foreach (var record in records)
        {
            if (!OrganizationMatcher.Matches(record.Organization, org)
                || OrganizationLegalEntitySplit.AreKnownDistinct(record.Organization, org))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(snilsNorm) && !string.IsNullOrWhiteSpace(record.Snils))
            {
                if (string.Equals(record.Snils, snilsNorm, StringComparison.OrdinalIgnoreCase))
                {
                    return record;
                }

                // СНИЛС есть, но не совпал — всё равно пробуем ФИО
                // (в одном ЮЛ опечатка СНИЛС, в другом тот же человек чистый).
            }

            if (EmployeeNameMatcher.Matches(record.Name, name))
            {
                return record;
            }
        }

        return null;
    }

    private static string BuildPersonOrgKey(string organization, OneCEmployeeRow row)
    {
        var snils = OneCEmployeeListParser.NormalizeSnils(row.Snils);
        if (!string.IsNullOrWhiteSpace(snils))
        {
            return "snils:" + snils + "|" + OrganizationMatcher.Normalize(organization);
        }

        return OneCEmployeeListParser.BuildPersonKey(organization, row.Snils, row.ExternalId, row.Name)
               + "|" + OrganizationMatcher.Normalize(organization);
    }

    private sealed record StaffRecord(string Organization, string Snils, string Name);
}

