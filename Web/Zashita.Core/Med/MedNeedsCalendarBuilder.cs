using System.Globalization;

namespace Zashita.Core.Med;

/// <summary>
/// Фильтры потребностей и календарь «вид медосмотра × месяц» (как в обучении).
/// </summary>
public static class MedNeedsCalendarBuilder
{
    public const string FilterOverdue = "overdue";
    public const string FilterNotConducted = "notconducted";
    public const string FilterExpiring = "expiring";
    public const string FilterPassed = "passed";
    public const string FilterPlanned = "planned";
    public const string FilterAll = "all";

    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static readonly string[] MonthLabels =
    [
        "Янв", "Фев", "Мар", "Апр", "Май", "Июн",
        "Июл", "Авг", "Сен", "Окт", "Ноя", "Дек"
    ];

    public static string ClassifyNeed(MedStatusRow row, DateTime asOf)
    {
        if (string.IsNullOrWhiteSpace(row.EmployeeName) || IsSummaryPlaceholder(row))
        {
            return string.Empty;
        }

        var display = ResolveDisplayStatus(row, asOf);
        if (string.Equals(display, MedExamStatuses.Overdue, StringComparison.OrdinalIgnoreCase))
        {
            return FilterOverdue;
        }

        if (string.Equals(display, MedExamStatuses.NotPassed, StringComparison.OrdinalIgnoreCase))
        {
            return FilterNotConducted;
        }

        if (string.Equals(display, MedExamStatuses.Planned, StringComparison.OrdinalIgnoreCase))
        {
            return FilterPlanned;
        }

        if (string.Equals(display, MedExamStatuses.Passed, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(row.Traffic, "yellow", StringComparison.OrdinalIgnoreCase))
            {
                return FilterExpiring;
            }

            return string.Empty;
        }

        if (string.Equals(row.Traffic, "red", StringComparison.OrdinalIgnoreCase))
        {
            return FilterOverdue;
        }

        if (string.Equals(row.Traffic, "yellow", StringComparison.OrdinalIgnoreCase))
        {
            return FilterExpiring;
        }

        return FilterNotConducted;
    }

    private static string ResolveDisplayStatus(MedStatusRow row, DateTime asOf) =>
        string.IsNullOrWhiteSpace(row.DisplayStatus)
            ? MedEffectiveStatus.Resolve(row, false, asOf)
            : row.DisplayStatus;

    /// <summary>Категория человека для сводки и фильтров планирования (взаимоисключающие).</summary>
    public static string ClassifyPersonBucket(IEnumerable<MedStatusRow> personRows, DateTime asOf)
    {
        var factors = personRows.Where(r => !IsSummaryPlaceholder(r)).ToList();
        if (factors.Count == 0)
        {
            return FilterNotConducted;
        }

        var displays = factors.Select(r => ResolveDisplayStatus(r, asOf)).ToList();
        var buckets = factors.Select(r => ClassifyNeed(r, asOf)).ToList();
        var hasOverdue = displays.Any(d => string.Equals(d, MedExamStatuses.Overdue, StringComparison.OrdinalIgnoreCase))
                         || buckets.Any(b => b == FilterOverdue);
        var hasPlanned = displays.Any(d => string.Equals(d, MedExamStatuses.Planned, StringComparison.OrdinalIgnoreCase));
        var hasExpiring = buckets.Any(b => b == FilterExpiring);
        var hasNotConducted = buckets.Any(b => b == FilterNotConducted);
        var allOk = factors.Count > 0
                    && factors.All(r => string.Equals(ResolveDisplayStatus(r, asOf), MedExamStatuses.Passed, StringComparison.OrdinalIgnoreCase))
                    && !hasOverdue
                    && !hasExpiring
                    && !hasPlanned
                    && !hasNotConducted;

        if (allOk)
        {
            return FilterPassed;
        }

        if (hasOverdue)
        {
            return FilterOverdue;
        }

        if (hasPlanned)
        {
            return FilterPlanned;
        }

        if (hasExpiring)
        {
            return FilterExpiring;
        }

        return FilterNotConducted;
    }

    public static bool MatchesNeedFilter(MedStatusRow row, string? filter, DateTime asOf)
    {
        var bucket = ClassifyNeed(row, asOf);
        if (string.IsNullOrWhiteSpace(filter) ||
            string.Equals(filter, FilterAll, StringComparison.OrdinalIgnoreCase))
        {
            return bucket is FilterOverdue or FilterNotConducted or FilterExpiring or FilterPlanned;
        }

        return string.Equals(bucket, filter.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<MedStatusRow> FilterNeeds(
        IEnumerable<MedStatusRow> rows,
        string? filter,
        DateTime asOf)
    {
        var list = rows.ToList();
        var filterKey = string.IsNullOrWhiteSpace(filter) ? FilterAll : filter.Trim();

        if (string.Equals(filterKey, FilterAll, StringComparison.OrdinalIgnoreCase))
        {
            return list.Where(r => MatchesNeedFilter(r, filterKey, asOf)).ToList();
        }

        var matchingPeople = list
            .GroupBy(PersonKey, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Key.Length > 0)
            .Where(g => string.Equals(ClassifyPersonBucket(g, asOf), filterKey, StringComparison.OrdinalIgnoreCase))
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return list.Where(r => matchingPeople.Contains(PersonKey(r))).ToList();
    }

    public static MedNeedsCalendarMatrix BuildMatrix(
        IReadOnlyList<MedStatusRow> statusRows,
        int planYear,
        DateTime asOf,
        IReadOnlyCollection<string>? selectedExamTypes)
    {
        var needRows = statusRows
            .Where(r => !string.IsNullOrWhiteSpace(r.ExamType) || !string.IsNullOrWhiteSpace(r.EmployeeName))
            .Where(r => MatchesNeedFilter(r, FilterAll, asOf) || HasDueInYear(r, planYear, asOf))
            .ToList();

        var examTypes = needRows
            .Select(r => string.IsNullOrWhiteSpace(r.ExamType) ? "Медосмотр" : r.ExamType.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var selected = (selectedExamTypes ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matrixRows = new List<MedNeedsCalendarRow>();
        foreach (var examType in examTypes)
        {
            if (selected.Count > 0 && !selected.Contains(examType))
            {
                continue;
            }

            var counts = new int[12];
            for (var m = 0; m < 12; m++)
            {
                var month = m + 1;
                counts[m] = needRows
                    .Where(r =>
                        string.Equals(
                            string.IsNullOrWhiteSpace(r.ExamType) ? "Медосмотр" : r.ExamType.Trim(),
                            examType,
                            StringComparison.OrdinalIgnoreCase) &&
                        ResolveDueMonth(r, planYear, asOf) == month)
                    .Select(r => PersonKey(r))
                    .Where(k => k.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
            }

            matrixRows.Add(new MedNeedsCalendarRow(examType, counts));
        }

        return new MedNeedsCalendarMatrix(planYear, matrixRows, examTypes);
    }

    public static string PersonKey(MedStatusRow row)
    {
        var name = (row.EmployeeName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return string.Empty;
        }

        // Только ФИО (+ организация): иначе разные dept/prof по факторам
        // дают несколько групп и кнопку «Выдать направление» на каждой строке.
        var org = (row.Organization ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(org) ? name : $"{org}\u001e{name}";
    }

    public static int? ResolveDueMonth(MedStatusRow row, int planYear, DateTime asOf)
    {
        DateTime? due = row.ExpiresOn;
        if (due is null || due.Value.Year < 1900)
        {
            if (row.ExamDate is { } exam && exam.Year >= 1900)
            {
                due = MedPeriodicity.NextExamDate(exam, row.Periodicity) ?? exam.AddYears(1);
            }
            else
            {
                due = asOf;
            }
        }

        if (due.Value.Year != planYear)
        {
            // Просроченные без даты в году плана — в текущий месяц.
            if (due.Value.Date < asOf.Date && planYear == asOf.Year)
            {
                return asOf.Month;
            }

            return null;
        }

        return due.Value.Month;
    }

    private static bool HasDueInYear(MedStatusRow row, int planYear, DateTime asOf) =>
        ResolveDueMonth(row, planYear, asOf) is >= 1 and <= 12;

    private static bool IsSummaryPlaceholder(MedStatusRow row) =>
        string.IsNullOrWhiteSpace(row.ItemNumber)
        && row.ExamDate is null
        && string.IsNullOrWhiteSpace(row.Id)
        && MedStandardAssignmentBuilder.IsStrictPeriodicKind(row.ExamType);

    private static bool IsFactorPassed(MedStatusRow row)
    {
        if (string.Equals(row.Status, "Не пройден", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(row.Conclusion, "Не пройден", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(row.Status, "Пройден", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(row.Conclusion, "Годен", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(row.Conclusion, "Ограниченно годен", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(row.Traffic, "green", StringComparison.OrdinalIgnoreCase)
               || string.Equals(row.Traffic, "yellow", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record MedNeedsCalendarRow(string ExamType, int[] MonthCounts);

public sealed record MedNeedsCalendarMatrix(
    int PlanYear,
    IReadOnlyList<MedNeedsCalendarRow> Rows,
    IReadOnlyList<string> ExamTypeOptions);
