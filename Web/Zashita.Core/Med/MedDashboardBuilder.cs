using Zashita.Core.Employees;

namespace Zashita.Core.Med;

public static class MedDashboardBuilder
{
    private static readonly HashSet<string> ActiveStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Пройден",
        "Запланирован",
        "Не пройден"
    };

    public static MedDashboardStats Build(IEnumerable<MedExamRecord> source, DateTime today) =>
        BuildFromStatusRows(BuildStatusRows(source, today), today, source);

    /// <summary>
    /// Сводка по людям: «пройдено» — только если пройдены все факторы из стандарта профессии.
    /// </summary>
    public static MedDashboardStats BuildFromStatusRows(
        IEnumerable<MedStatusRow> statusRows,
        DateTime today,
        IEnumerable<MedExamRecord>? examSource = null)
    {
        var rows = statusRows
            .Where(r => !string.IsNullOrWhiteSpace(r.EmployeeName))
            .ToList();

        var expired = 0;
        var expiringSoon = 0;
        var notPassed = 0;
        var passed = 0;
        var planned = 0;

        foreach (var personRows in rows.GroupBy(
                     MedNeedsCalendarBuilder.PersonKey,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(personRows.Key))
            {
                continue;
            }

            switch (MedNeedsCalendarBuilder.ClassifyPersonBucket(personRows, today))
            {
                case MedNeedsCalendarBuilder.FilterPassed:
                    passed++;
                    break;
                case MedNeedsCalendarBuilder.FilterOverdue:
                    expired++;
                    break;
                case MedNeedsCalendarBuilder.FilterPlanned:
                    planned++;
                    break;
                case MedNeedsCalendarBuilder.FilterExpiring:
                    expiringSoon++;
                    break;
                default:
                    notPassed++;
                    break;
            }
        }

        var total = passed + notPassed + planned + expired + expiringSoon;
        var passedPercent = total == 0 ? 0 : (int)Math.Round(passed * 100.0 / total);

        var records = (examSource ?? []).ToList();
        var overduePlanned = records
            .Where(r => ActiveStatuses.Contains(r.Status))
            .Where(r => !string.Equals(r.Status, "Пройден", StringComparison.OrdinalIgnoreCase))
            .Where(r => ResolveDueDate(r).Date < today)
            .OrderBy(r => ResolveDueDate(r))
            .ToList();

        var lastPassedByEmployee = records
            .Where(r => string.Equals(r.Status, "Пройден", StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => r.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var last = group.OrderByDescending(r => r.ExamDate).First();
                var expires = ResolveDueDate(last);
                return new MedExpiryItem(
                    group.First().EmployeeName,
                    last.ExamType,
                    expires);
            })
            .OrderBy(item => item.ExpiresOn)
            .ToList();

        var expiringItems = lastPassedByEmployee
            .Where(item => item.ExpiresOn >= today && item.ExpiresOn <= today.AddDays(30))
            .ToList();

        var overduePlannedPeople = overduePlanned
            .Select(r => (r.EmployeeName ?? string.Empty).Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new MedDashboardStats
        {
            Expired = expired,
            ExpiringSoon = expiringSoon,
            OverduePlanned = overduePlannedPeople,
            Passed = passed,
            NotPassed = notPassed,
            Planned = planned,
            PassedPercent = passedPercent,
            ExpiringItems = expiringItems,
            OverdueItems = overduePlanned
        };
    }

    public static IReadOnlyList<MedStatusRow> BuildStatusRows(
        IEnumerable<MedExamRecord> source,
        DateTime today)
    {
        return source
            .OrderBy(r => r.Organization)
            .ThenBy(r => ResolveDueDate(r))
            .ThenBy(r => r.EmployeeName)
            .Select(r =>
            {
                var due = ResolveDueDate(r);
                DateTime? expires = string.Equals(r.Status, "Пройден", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.Status, "Запланирован", StringComparison.OrdinalIgnoreCase)
                    ? due
                    : null;
                var traffic = MedStatusTraffic.Resolve(r, expires, today);
                return new MedStatusRow
                {
                    Id = r.Id,
                    Organization = r.Organization,
                    Department = r.Department,
                    EmployeeName = r.EmployeeName,
                    Profession = r.Profession,
                    ExamType = r.ExamType,
                    ItemNumber = r.ItemNumber,
                    Periodicity = MedPeriodicity.Normalize(r.Periodicity),
                    Status = r.Status,
                    Conclusion = r.Conclusion,
                    ExamDate = r.ExamDate,
                    ExpiresOn = expires,
                    Traffic = traffic
                };
            })
            .ToList();
    }

    /// <summary>
    /// Строки для графика: ближайшая дата медосмотра по каждому сотруднику
    /// (просроченные и с наступающим сроком).
    /// </summary>
    public static IReadOnlyList<MedScheduleLine> BuildScheduleLinesFromExams(
        IEnumerable<MedExamRecord> source,
        DateTime today,
        string? department = null,
        IReadOnlyList<OrganizationEmployee>? employees = null)
    {
        var employeeLookup = (employees ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .GroupBy(e => e.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        return source
            .Where(r => !string.IsNullOrWhiteSpace(r.EmployeeName))
            .Where(r => string.IsNullOrWhiteSpace(department)
                        || string.Equals(r.Department?.Trim(), department.Trim(), StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => r.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var lastPassed = group
                    .Where(r => string.Equals(r.Status, "Пройден", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(r => r.ExamDate)
                    .FirstOrDefault();
                var planned = group
                    .Where(r => string.Equals(r.Status, "Запланирован", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(r => r.ExamDate)
                    .FirstOrDefault();
                var sourceRow = planned ?? lastPassed ?? group.OrderByDescending(r => r.ExamDate).First();
                var examDate = planned is not null
                    ? planned.ExamDate.Date
                    : ResolveDueDate(sourceRow);

                employeeLookup.TryGetValue(sourceRow.EmployeeName.Trim(), out var employee);
                var lineDepartment = !string.IsNullOrWhiteSpace(department)
                    ? department.Trim()
                    : !string.IsNullOrWhiteSpace(employee?.Department)
                        ? employee!.Department.Trim()
                        : sourceRow.Department?.Trim() ?? string.Empty;
                var profession = !string.IsNullOrWhiteSpace(employee?.Profession)
                    ? employee!.Profession.Trim()
                    : sourceRow.Profession?.Trim() ?? string.Empty;

                return new MedScheduleLine
                {
                    EmployeeName = sourceRow.EmployeeName,
                    Profession = profession,
                    Department = lineDepartment,
                    BirthDate = employee?.BirthDate ?? sourceRow.BirthDate,
                    ExamDate = examDate,
                    ItemNumber = MedItemNumber.Normalize(sourceRow.ItemNumber),
                    Periodicity = MedPeriodicity.Normalize(sourceRow.Periodicity),
                    Comment = examDate < today ? "Просрочено" : string.Empty
                };
            })
            .OrderBy(line => line.Department, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(line => line.ExamDate)
            .ThenBy(line => line.EmployeeName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Подтягивает подразделение/должность/дату рождения из штата перед печатью.</summary>
    public static void EnrichScheduleLinesFromEmployees(
        MedScheduleDocument document,
        IReadOnlyList<OrganizationEmployee>? employees)
    {
        if (document.Lines.Count == 0 || employees is null || employees.Count == 0)
        {
            return;
        }

        var lookup = employees
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .GroupBy(e => e.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var line in document.Lines)
        {
            var name = line.EmployeeName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || !lookup.TryGetValue(name, out var employee))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(line.Department) && !string.IsNullOrWhiteSpace(employee.Department))
            {
                line.Department = employee.Department.Trim();
            }

            if (string.IsNullOrWhiteSpace(line.Profession) && !string.IsNullOrWhiteSpace(employee.Profession))
            {
                line.Profession = employee.Profession.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(employee.Profession))
            {
                line.Profession = employee.Profession.Trim();
            }

            if (line.BirthDate is null || line.BirthDate.Value.Year < 1900)
            {
                line.BirthDate = employee.BirthDate;
            }
        }
    }

    public static DateTime GetExamExpiresOn(DateTime lastExamDate, string examType, string? periodicity = null)
    {
        _ = examType;
        return MedPeriodicity.NextExamDate(lastExamDate, periodicity) ?? lastExamDate.Date.AddYears(1);
    }

    public static DateTime ResolveDueDate(MedExamRecord record)
    {
        if (record.NextExamDate is { } next)
        {
            return next.Date;
        }

        if (record.ExamDate.Year < 1900)
        {
            return DateTime.Today;
        }

        if (string.Equals(record.Status, "Пройден", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(record.Status, "Запланирован", StringComparison.OrdinalIgnoreCase))
        {
            return GetExamExpiresOn(record.ExamDate, record.ExamType, record.Periodicity);
        }

        return record.ExamDate.Date;
    }
}
