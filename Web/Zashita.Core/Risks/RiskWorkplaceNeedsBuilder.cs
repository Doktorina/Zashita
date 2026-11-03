namespace Zashita.Core.Risks;

/// <summary>РМ, подлежащие оценке: не проведена / просрочено / истекает за 30 дней.</summary>
public static class RiskWorkplaceNeedsBuilder
{
    public const string NeedAll = "all";
    public const string NeedOverdue = "overdue";
    public const string NeedNotConducted = "notconducted";
    public const string NeedExpiring = "expiring";
    public const string NeedAssessed = "assessed";
    public const string NeedPlanned = "planned";

    public static List<RiskWorkplaceNeedRow> Build(
        IReadOnlyList<WorkplaceRecord> workplaces,
        string organization,
        DateTime? asOf = null) =>
        BuildFiltered(workplaces, organization, NeedAll, asOf);

    public static List<RiskWorkplaceNeedRow> BuildFiltered(
        IReadOnlyList<WorkplaceRecord> workplaces,
        string organization,
        string? filter,
        DateTime? asOf = null)
    {
        var today = (asOf ?? DateTime.Today).Date;
        var filterKey = NormalizeNeedFilter(filter);

        return workplaces
            .Where(w => !w.IsGroup && w.IsUsed)
            .Where(w => string.IsNullOrWhiteSpace(organization)
                        || string.Equals(w.Organization, organization, StringComparison.OrdinalIgnoreCase))
            .Select(w => (Workplace: w, Bucket: ClassifySummaryBucket(w, today)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Bucket))
            .Where(x => MatchesFilter(x.Bucket, filterKey))
            .Select(x => ToRowForBucket(x.Workplace, x.Bucket, today))
            .OrderBy(r => r.Department, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Profession, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static int CountByBucket(
        IReadOnlyList<WorkplaceRecord> workplaces,
        string organization,
        string bucket,
        DateTime? asOf = null)
    {
        var today = (asOf ?? DateTime.Today).Date;
        return workplaces
            .Where(w => !w.IsGroup && w.IsUsed)
            .Where(w => string.IsNullOrWhiteSpace(organization)
                        || string.Equals(w.Organization, organization, StringComparison.OrdinalIgnoreCase))
            .Count(w => string.Equals(ClassifySummaryBucket(w, today), bucket, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Категория РМ для сводки и фильтров (взаимоисключающие).</summary>
    public static string ClassifySummaryBucket(WorkplaceRecord workplace, DateTime asOf)
    {
        if (workplace.IsGroup || !workplace.IsUsed)
        {
            return string.Empty;
        }

        var year = asOf.Year.ToString();
        var plannedThisYear = !string.IsNullOrWhiteSpace(workplace.PlanYear) &&
                              string.Equals(workplace.PlanYear.Trim(), year, StringComparison.Ordinal) &&
                              workplace.RiskAssessmentDate is null &&
                              !workplace.RiskAssessmentNotConducted;

        if (workplace.RiskAssessmentNotConducted ||
            !RiskAssessmentStatusBuilder.HasFilledRiskLines(workplace))
        {
            return plannedThisYear ? NeedPlanned : NeedNotConducted;
        }

        if (workplace.RiskAssessmentDate is not { } date)
        {
            return NeedAssessed;
        }

        var next = date.Date.AddYears(RiskAssessmentStatusBuilder.ValidityYears);
        var days = (next - asOf.Date).Days;
        if (days < 0)
        {
            return NeedOverdue;
        }

        if (days <= 30)
        {
            return NeedExpiring;
        }

        return NeedAssessed;
    }

    public static string NormalizeNeedFilter(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            NeedOverdue => NeedOverdue,
            NeedNotConducted => NeedNotConducted,
            NeedExpiring => NeedExpiring,
            NeedAssessed => NeedAssessed,
            NeedPlanned => NeedPlanned,
            _ => NeedAll
        };

    private static bool MatchesFilter(string bucket, string filterKey) =>
        filterKey == NeedAll
            ? bucket is NeedOverdue or NeedNotConducted or NeedExpiring
            : string.Equals(bucket, filterKey, StringComparison.OrdinalIgnoreCase);

    private static RiskWorkplaceNeedRow ToRowForBucket(WorkplaceRecord w, string bucket, DateTime today)
    {
        var row = ToRow(w, today);
        return bucket switch
        {
            NeedAssessed => CopyRow(row, NeedAssessed, RiskHazardStatuses.Done, "green"),
            NeedPlanned => CopyRow(row, NeedPlanned, "Запланировано", string.Empty),
            NeedNotConducted => CopyRow(row, NeedNotConducted, RiskHazardStatuses.NotConducted, "red"),
            NeedOverdue => CopyRow(row, NeedOverdue, RiskHazardStatuses.Overdue, "red"),
            NeedExpiring => CopyRow(row, NeedExpiring, "Истекает в течение 30 дней", "yellow"),
            _ => row
        };
    }

    private static RiskWorkplaceNeedRow CopyRow(
        RiskWorkplaceNeedRow row,
        string needKind,
        string status,
        string traffic) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            Profession = row.Profession,
            Department = row.Department,
            WorkplaceNumber = row.WorkplaceNumber,
            AssessmentDate = row.AssessmentDate,
            ExpiresOn = row.ExpiresOn,
            Status = status,
            NeedKind = needKind,
            Traffic = traffic
        };

    private static RiskWorkplaceNeedRow ToRow(WorkplaceRecord w, DateTime today)
    {
        var title = !string.IsNullOrWhiteSpace(w.Profession) ? w.Profession : w.Name;
        var expiresOn = ResolveExpiryDate(w);
        string needKind;
        string status;
        string traffic;

        if (RiskHazardStatusBuilder.IsAssessmentMissing(w) ||
            RiskAssessmentStatusBuilder.IsWithoutRiskAssessment(w, today))
        {
            // Есть дата, но срок уже прошёл — просрочено, а не «не проведена».
            if (expiresOn is { } expired && expired.Date < today)
            {
                needKind = NeedOverdue;
                status = RiskHazardStatuses.Overdue;
                traffic = "red";
            }
            else if (w.RiskAssessmentDate is not null && expiresOn is null)
            {
                needKind = NeedOverdue;
                status = RiskHazardStatuses.Overdue;
                traffic = "red";
            }
            else
            {
                needKind = NeedNotConducted;
                status = RiskHazardStatuses.NotConducted;
                traffic = "red";
            }
        }
        else if (expiresOn is { } due)
        {
            if (due.Date < today)
            {
                needKind = NeedOverdue;
                status = RiskHazardStatuses.Overdue;
                traffic = "red";
            }
            else if (due.Date <= today.AddDays(30))
            {
                needKind = NeedExpiring;
                status = "Истекает в течение 30 дней";
                traffic = "yellow";
            }
            else
            {
                needKind = "ok";
                status = RiskHazardStatuses.Done;
                traffic = "green";
            }
        }
        else
        {
            needKind = "ok";
            status = RiskHazardStatuses.Done;
            traffic = "green";
        }

        return new RiskWorkplaceNeedRow
        {
            Id = w.Id,
            Name = w.Name,
            Profession = title,
            Department = w.Department ?? string.Empty,
            WorkplaceNumber = w.WorkplaceNumber ?? string.Empty,
            AssessmentDate = w.RiskAssessmentDate?.Date,
            ExpiresOn = expiresOn?.Date,
            Status = status,
            NeedKind = needKind,
            Traffic = traffic
        };
    }

    public static DateTime? ResolveExpiryDate(WorkplaceRecord w)
    {
        if (w.RiskAssessmentDate is not { } assessed)
        {
            return null;
        }

        if (WorkplaceRiskScale.IsNoPeriodicity(w.RiskPeriodicity))
        {
            return null;
        }

        var months = WorkplaceRiskScale.ParsePeriodicityMonths(w.RiskPeriodicity);
        if (months is > 0)
        {
            return assessed.Date.AddMonths(months.Value);
        }

        if (!string.IsNullOrWhiteSpace(w.OverallRisk))
        {
            return RiskHazardStatusBuilder.ComputeReviewDate(assessed, w.OverallRisk);
        }

        return assessed.Date.AddYears(RiskAssessmentStatusBuilder.ValidityYears);
    }
}

public sealed class RiskWorkplaceNeedRow
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Profession { get; init; } = string.Empty;
    public string Department { get; init; } = string.Empty;
    public string WorkplaceNumber { get; init; } = string.Empty;
    public DateTime? AssessmentDate { get; init; }
    public DateTime? ExpiresOn { get; init; }
    public string Status { get; init; } = string.Empty;
    /// <summary>overdue | notconducted | expiring | ok</summary>
    public string NeedKind { get; init; } = string.Empty;
    public string Traffic { get; init; } = string.Empty;
}
