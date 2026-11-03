using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

/// <summary>
/// График идентификации опасностей и оценки рисков — по перечню РМ или отчёту по рискам.
/// </summary>
public static class RiskIdentificationScheduleBuilder
{
    public const string SourceReport = "report";
    public const string SourceWorkplaces = "workplaces";

    public static RiskPreparationDocument BuildFromWorkplaces(
        string organization,
        int year,
        string? department,
        IReadOnlyList<WorkplaceRecord> workplaces,
        DateTime? asOf = null)
    {
        var today = (asOf ?? DateTime.Today).Date;
        var candidates = workplaces
            .Where(w => !w.IsGroup && w.IsUsed)
            .Where(w => OrganizationMatches(organization, w.Organization))
            .Where(w => string.IsNullOrWhiteSpace(department)
                        || (w.Department ?? string.Empty).Contains(department.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(w => NeedsAssessmentInYear(w, year, today))
            .ToList();

        return BuildDocument(organization, year, department, candidates, today, SourceWorkplaces);
    }

    public static RiskPreparationDocument BuildFromStatusReport(
        string organization,
        int year,
        string? department,
        IReadOnlyList<WorkplaceRecord> workplaces,
        IReadOnlyList<(string Organization, OrganizationEmployee Employee)> employees,
        IReadOnlyList<RiskHazardAssessmentRecord> savedAssessments,
        DateTime? asOf = null)
    {
        var today = (asOf ?? DateTime.Today).Date;
        var reportRows = RiskHazardStatusBuilder.Build(
            employees,
            savedAssessments,
            today,
            organization,
            department,
            catalog: null,
            workplaces: workplaces);

        var departments = reportRows
            .Where(row => !row.IsGroup)
            .Where(row => NeedsReportRow(row))
            .Select(row => row.Department?.Trim() ?? "Без подразделения")
            .Where(d => d.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = workplaces
            .Where(w => !w.IsGroup && w.IsUsed)
            .Where(w => OrganizationMatches(organization, w.Organization))
            .Where(w => departments.Contains((w.Department ?? "Без подразделения").Trim()))
            .Where(w => NeedsAssessmentInYear(w, year, today))
            .ToList();

        return BuildDocument(organization, year, department, candidates, today, SourceReport);
    }

    private static RiskPreparationDocument BuildDocument(
        string organization,
        int year,
        string? department,
        IReadOnlyList<WorkplaceRecord> candidates,
        DateTime today,
        string source)
    {
        var deptMonths = BuildDepartmentMonths(candidates, year, today);
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["schedule-source"] = source,
            ["schedule-year"] = year.ToString(),
            ["schedule-workplaces"] = candidates.Count.ToString()
        };

        var row = 1;
        foreach (var dept in deptMonths.Keys.OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase))
        {
            overrides[$"sch-dept-{row}"] = dept;
            overrides[$"sch-{row}-num"] = row.ToString();
            for (var month = 1; month <= 12; month++)
            {
                overrides[$"sch-{row}-m{month}"] = deptMonths[dept].Contains(month) ? "+" : string.Empty;
            }

            row++;
        }

        var yearStart = new DateTime(year, 1, 1);
        var scenario = source switch
        {
            SourceReport => $"график на {year} (из отчёта по рискам)",
            _ => $"график на {year} (из перечня рабочих мест)"
        };

        return new RiskPreparationDocument
        {
            Organization = organization.Trim(),
            Number = string.Empty,
            Date = DateTime.Now,
            StartDate = yearStart,
            EndDate = new DateTime(year, 12, 31),
            Department = department?.Trim() ?? string.Empty,
            PrintOverrides = overrides,
            Scenario = scenario
        };
    }

    private static Dictionary<string, HashSet<int>> BuildDepartmentMonths(
        IReadOnlyList<WorkplaceRecord> workplaces,
        int year,
        DateTime today)
    {
        var result = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var workplace in workplaces)
        {
            var dept = string.IsNullOrWhiteSpace(workplace.Department)
                ? "Без подразделения"
                : workplace.Department.Trim();
            if (!result.TryGetValue(dept, out var months))
            {
                months = [];
                result[dept] = months;
            }

            foreach (var month in ResolveMonthsForWorkplace(workplace, year, today))
            {
                months.Add(month);
            }
        }

        foreach (var dept in result.Keys.ToList())
        {
            if (result[dept].Count == 0)
            {
                result[dept].Add(today.Year == year ? today.Month : 1);
            }
        }

        return result;
    }

    private static IEnumerable<int> ResolveMonthsForWorkplace(WorkplaceRecord workplace, int year, DateTime today)
    {
        var bucket = RiskWorkplaceNeedsBuilder.ClassifySummaryBucket(workplace, today);
        if (bucket is RiskWorkplaceNeedsBuilder.NeedOverdue or RiskWorkplaceNeedsBuilder.NeedNotConducted)
        {
            yield return today.Year == year ? today.Month : 1;
            yield break;
        }

        if (bucket is RiskWorkplaceNeedsBuilder.NeedPlanned)
        {
            yield return SpreadMonth(workplace.Id, year);
            yield break;
        }

        if (bucket is RiskWorkplaceNeedsBuilder.NeedExpiring)
        {
            var expiry = RiskWorkplaceNeedsBuilder.ResolveExpiryDate(workplace);
            if (expiry is { } due)
            {
                if (due.Year == year)
                {
                    yield return due.Month;
                }
                else if (due.Year < year)
                {
                    yield return 1;
                }
            }
            else
            {
                yield return today.Year == year ? today.Month : 1;
            }

            yield break;
        }

        var nextReview = RiskWorkplaceNeedsBuilder.ResolveExpiryDate(workplace);
        if (nextReview is { } review && review.Year == year)
        {
            yield return review.Month;
        }
    }

    private static int SpreadMonth(string workplaceId, int year)
    {
        var hash = Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode($"{workplaceId}:{year}"));
        return (hash % 12) + 1;
    }

    private static bool NeedsAssessmentInYear(WorkplaceRecord workplace, int year, DateTime today)
    {
        var bucket = RiskWorkplaceNeedsBuilder.ClassifySummaryBucket(workplace, today);
        if (bucket is RiskWorkplaceNeedsBuilder.NeedOverdue
            or RiskWorkplaceNeedsBuilder.NeedNotConducted
            or RiskWorkplaceNeedsBuilder.NeedExpiring
            or RiskWorkplaceNeedsBuilder.NeedPlanned)
        {
            return true;
        }

        var expiry = RiskWorkplaceNeedsBuilder.ResolveExpiryDate(workplace);
        return expiry is { } due && due.Year <= year;
    }

    private static bool NeedsReportRow(RiskHazardStatusRow row)
    {
        var status = (row.Status ?? string.Empty).Trim();
        if (status.Contains(RiskHazardStatuses.NotConducted, StringComparison.OrdinalIgnoreCase)
            || status.Contains(RiskHazardStatuses.Overdue, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (row.ReviewDate is { } review)
        {
            return review.Year <= DateTime.Today.Year;
        }

        return false;
    }

    private static bool OrganizationMatches(string organization, string? workplaceOrg)
    {
        if (string.IsNullOrWhiteSpace(organization))
        {
            return true;
        }

        return string.Equals(organization.Trim(), (workplaceOrg ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)
               || OrganizationMatcher.Matches(organization, workplaceOrg ?? string.Empty)
               || OrganizationMatcher.Matches(workplaceOrg ?? string.Empty, organization);
    }
}
