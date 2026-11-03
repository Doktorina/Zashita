using Zashita.Core.Employees;

namespace Zashita.Core.Med;

/// <summary>
/// Убирает из отчёта/графика лишние строки: пустые заглушки, «при приёме», если уже есть периодический.
/// </summary>
public static class MedStatusReportCleanup
{
    public static List<MedStatusRow> Clean(
        IReadOnlyList<MedStatusRow> rows,
        EmployeeRosterIndex? roster = null)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var canonical = CanonicalizeRows(rows, roster);
        var unified = UnifyPersonContext(canonical, roster);
        var afterPreliminary = DropPreliminaryWhenPeriodicExists(unified);
        var personKey = BuildPersonKeySelector(roster);
        var withRealExams = PeopleWithRealExams(afterPreliminary, personKey);
        return afterPreliminary
            .Where(row => !IsRedundant(row, withRealExams, personKey))
            .Where(row => EmployeeNameMatcher.IsPlausiblePersonName(row.EmployeeName))
            .Where(row => roster is null
                          || roster.TryResolve(row.Organization, row.EmployeeName, out _))
            .ToList();
    }

    private static Func<MedStatusRow, string> BuildPersonKeySelector(EmployeeRosterIndex? roster) =>
        roster is null
            ? PersonOrgKey
            : row => roster.BuildPersonOrgKey(row.Organization, row.EmployeeName);

    private static List<MedStatusRow> CanonicalizeRows(
        IReadOnlyList<MedStatusRow> rows,
        EmployeeRosterIndex? roster)
    {
        if (roster is null)
        {
            return rows
                .Where(r => EmployeeNameMatcher.IsPlausiblePersonName(r.EmployeeName))
                .ToList();
        }

        return rows
            .Where(r => EmployeeNameMatcher.IsPlausiblePersonName(r.EmployeeName)
                        || roster.TryResolve(r.Organization, r.EmployeeName, out _))
            .Select(row =>
            {
                if (!roster.TryResolve(row.Organization, row.EmployeeName, out var employee))
                {
                    return row;
                }

                return WithContext(
                    row,
                    EmployeeContextValidator.NormalizeDepartment(
                        string.IsNullOrWhiteSpace(employee.Department) ? row.Department : employee.Department),
                    EmployeeContextValidator.NormalizeProfession(
                        string.IsNullOrWhiteSpace(employee.Profession)
                            ? row.Profession
                            : employee.Profession),
                    employee.Name);
            })
            .ToList();
    }

    /// <summary>
    /// Одна запись штата + старая медкарта без подразделения/должности → одна ветка в дереве.
    /// </summary>
    private static List<MedStatusRow> UnifyPersonContext(
        IReadOnlyList<MedStatusRow> rows,
        EmployeeRosterIndex? roster)
    {
        return rows
            .GroupBy(
                r => roster?.BuildPersonOrgKey(r.Organization, r.EmployeeName) ?? PersonOrgKey(r),
                StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var department = group
                    .Select(r => r.Department)
                    .Select(EmployeeContextValidator.NormalizeDepartment)
                    .FirstOrDefault(d => !string.IsNullOrWhiteSpace(d))
                    ?? string.Empty;
                var profession = group
                    .Select(r => r.Profession)
                    .Select(EmployeeContextValidator.NormalizeProfession)
                    .OrderByDescending(p => p.Length)
                    .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p))
                    ?? string.Empty;
                var employeeName = group
                    .Select(r => r.EmployeeName)
                    .OrderByDescending(n => (n ?? string.Empty).Length)
                    .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                    ?? string.Empty;
                if (roster is not null &&
                    roster.TryResolve(group.First().Organization, employeeName, out var employee))
                {
                    employeeName = employee.Name;
                    if (!string.IsNullOrWhiteSpace(employee.Department))
                    {
                        department = EmployeeContextValidator.NormalizeDepartment(employee.Department);
                    }

                    if (!string.IsNullOrWhiteSpace(employee.Profession))
                    {
                        profession = EmployeeContextValidator.NormalizeProfession(employee.Profession);
                    }
                }

                return group.Select(row => WithContext(row, department, profession, employeeName));
            })
            .ToList();
    }

    private static MedStatusRow WithContext(
        MedStatusRow row,
        string department,
        string profession,
        string? employeeName = null) =>
        new()
        {
            Id = row.Id,
            Organization = row.Organization,
            Department = department,
            EmployeeName = employeeName ?? row.EmployeeName,
            Profession = profession,
            ExamType = row.ExamType,
            ItemNumber = row.ItemNumber,
            Periodicity = row.Periodicity,
            DisplayStatus = row.DisplayStatus,
            Status = row.Status,
            Conclusion = row.Conclusion,
            ConclusionScanFileName = row.ConclusionScanFileName,
            EmploymentDate = row.EmploymentDate,
            ExamDate = row.ExamDate,
            ExpiresOn = row.ExpiresOn,
            Traffic = row.Traffic
        };

    private static List<MedStatusRow> DropPreliminaryWhenPeriodicExists(IReadOnlyList<MedStatusRow> rows)
    {
        var periodicKeys = rows
            .Where(r => MedStandardAssignmentBuilder.IsStrictPeriodicKind(r.ExamType))
            .Select(PersonItemKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return rows
            .Where(r =>
            {
                if (!MedStandardAssignmentBuilder.IsPreliminaryKind(r.ExamType))
                {
                    return true;
                }

                return !periodicKeys.Contains(PersonItemKey(r));
            })
            .ToList();
    }

    private static HashSet<string> PeopleWithRealExams(
        IReadOnlyList<MedStatusRow> rows,
        Func<MedStatusRow, string> personKey) =>
        rows
            .Where(r => !IsEmptyPlaceholder(r) && !IsSpentOnceOnHire(r))
            .Select(personKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsRedundant(
        MedStatusRow row,
        HashSet<string> peopleWithRealExams,
        Func<MedStatusRow, string> personKey)
    {
        if (IsEmptyPlaceholder(row) && peopleWithRealExams.Contains(personKey(row)))
        {
            return true;
        }

        // Психиатрию и внеочередной всегда оставляем в отчёте (в т.ч. после прохождения).
        if (MedStandardAssignmentBuilder.IsPsychiatricKind(row.ExamType)
            || MedStandardAssignmentBuilder.IsExtraordinaryKind(row.ExamType)
            || IsProfessionLevelOnceOnHire(row))
        {
            return false;
        }

        // «При приёме на работу» не нужен в текущем отчёте, если осмотр уже был.
        if (IsSpentOnceOnHire(row))
        {
            return true;
        }

        if (MedPeriodicity.IsOnceOnHire(row.Periodicity)
            && peopleWithRealExams.Contains(personKey(row))
            && row.EmploymentDate is { } hired
            && hired.Date <= DateTime.Today.AddDays(-14))
        {
            return true;
        }

        return false;
    }

    private static bool IsEmptyPlaceholder(MedStatusRow row) =>
        string.IsNullOrWhiteSpace(row.Id)
        && string.IsNullOrWhiteSpace(row.ItemNumber)
        && row.ExamDate is null
        && MedStandardAssignmentBuilder.IsStrictPeriodicKind(row.ExamType);

    private static bool IsSpentOnceOnHire(MedStatusRow row)
    {
        if (!MedPeriodicity.IsOnceOnHire(row.Periodicity))
        {
            return false;
        }

        if (row.ExamDate is { Year: > 1900 })
        {
            return true;
        }

        return string.Equals(row.Status, "Пройден", StringComparison.OrdinalIgnoreCase)
               || string.Equals(row.Conclusion, "Годен", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Профессия-уровневая психиатрия: без пункта приказа, «при приёме».
    /// На случай если ExamType ещё не нормализован — не выкидываем из отчёта.
    /// </summary>
    private static bool IsProfessionLevelOnceOnHire(MedStatusRow row) =>
        MedPeriodicity.IsOnceOnHire(row.Periodicity)
        && string.IsNullOrWhiteSpace(MedItemNumber.Normalize(row.ItemNumber))
        && !MedStandardAssignmentBuilder.IsStrictPeriodicKind(row.ExamType);

    private static string PersonOrgKey(MedStatusRow r) =>
        $"{(r.Organization ?? string.Empty).Trim()}|{(r.EmployeeName ?? string.Empty).Trim()}";

    private static string PersonItemKey(MedStatusRow r) =>
        $"{(r.EmployeeName ?? string.Empty).Trim()}|{ProfessionMatcher.Normalize(r.Profession)}|{MedItemNumber.Normalize(r.ItemNumber)}";
}
