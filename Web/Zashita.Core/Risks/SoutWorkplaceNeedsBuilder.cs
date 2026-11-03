using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

/// <summary>Категории потребности в СОУТ для сводки и фильтров отчёта.</summary>
public static class SoutWorkplaceNeedsBuilder
{
    public const string NeedAll = "all";
    public const string NeedNotConducted = "notconducted";
    public const string NeedOverdue = "overdue";
    public const string NeedExpiring = "expiring";
    public const string NeedAssessed = "assessed";
    public const string NeedPlanned = "planned";

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

    /// <summary>РМ, явно включённые в годовой план СОУТ на указанный год.</summary>
    public static HashSet<string> ResolvePlannedWorkplaceIds(
        IEnumerable<SoutAnnualPlanDocument> plans,
        string organization,
        int year)
    {
        var org = organization?.Trim() ?? string.Empty;
        return plans
            .Where(plan => plan.PlanYear == year)
            .Where(plan => string.IsNullOrWhiteSpace(org)
                           || string.Equals(plan.Organization, org, StringComparison.OrdinalIgnoreCase)
                           || OrganizationMatcher.Matches(org, plan.Organization)
                           || OrganizationMatcher.Matches(plan.Organization, org))
            .SelectMany(plan => plan.DetailRows)
            .Select(row => row.WorkplaceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static int CountByBucket(
        IReadOnlyList<WorkplaceRecord> workplaces,
        string organization,
        string bucket,
        DateTime? asOf = null,
        IReadOnlySet<string>? plannedWorkplaceIds = null)
    {
        var today = (asOf ?? DateTime.Today).Date;
        var leaves = workplaces
            .Where(w => !w.IsGroup && w.IsUsed)
            .Where(w => !RiskPrintChrome.IsIndividualEntrepreneurWorkplace(w))
            .Where(w => MatchesOrganization(w.Organization, organization))
            .ToList();
        var byId = leaves
            .Where(w => !string.IsNullOrWhiteSpace(w.Id))
            .GroupBy(w => w.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Карта-источник, на которую ссылается другое РМ, не считаем второй раз.
        var linkedCardOnlyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in leaves)
        {
            var linked = (w.LinkedSoutCardWorkplaceId ?? string.Empty).Trim();
            if (linked.Length > 0
                && !string.Equals(linked, w.Id, StringComparison.OrdinalIgnoreCase)
                && byId.ContainsKey(linked))
            {
                linkedCardOnlyIds.Add(linked);
            }
        }

        return leaves
            .Where(w => string.IsNullOrWhiteSpace(w.Id) || !linkedCardOnlyIds.Contains(w.Id))
            .Count(w => string.Equals(
                ClassifySummaryBucket(EffectiveForSummary(w, byId), today, plannedWorkplaceIds),
                bucket,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Категория РМ для сводки (взаимоисключающие корзины).</summary>
    public static string ClassifySummaryBucket(
        WorkplaceRecord workplace,
        DateTime asOf,
        IReadOnlySet<string>? plannedWorkplaceIds = null)
    {
        if (workplace.IsGroup || !workplace.IsUsed)
        {
            return string.Empty;
        }

        if (RiskPrintChrome.IsIndividualEntrepreneurWorkplace(workplace))
        {
            return string.Empty;
        }

        plannedWorkplaceIds ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Ручная пометка на «Карты СОУТ» — та же приоритетность, что у ResolveCardsTraffic.
        var mark = SoutStatusBuilder.NormalizeTrafficMark(workplace.SoutTrafficMark);
        if (mark == "green")
        {
            return NeedAssessed;
        }

        if (mark == "blue")
        {
            return NeedExpiring;
        }

        // Дата из отчёта или дата составления карты (как в ToRow / ResolveStatus).
        var assessmentDate = workplace.SoutAssessmentDate?.Date
                             ?? workplace.SoutCard?.PreparedDate?.Date;
        var hasReportNumber = !string.IsNullOrWhiteSpace(workplace.SoutReportNumber);
        var hasAssessmentEvidence = assessmentDate is not null || hasReportNumber;

        // Нет факта СОУТ — «Нет спецоценки», даже при флаге внеплановой.
        // № карты без даты — оценка есть (ResolveStatus → «Актуально»), не «не проведена».
        if (workplace.SoutAssessmentNotConducted || !hasAssessmentEvidence)
        {
            if (!string.IsNullOrWhiteSpace(workplace.Id)
                && plannedWorkplaceIds.Contains(workplace.Id))
            {
                return NeedPlanned;
            }

            return NeedNotConducted;
        }

        // «Просрочена»: истек срок, нужна внеплановая, или красная пометка при уже проведённой СОУТ.
        if (mark == "red" || workplace.RequiresUnscheduledSout)
        {
            return NeedOverdue;
        }

        // Декларация = бессрочно / оценено (как в отчёте и на картах).
        if (workplace.DeclarationOfConformity)
        {
            return NeedAssessed;
        }

        if (assessmentDate is null)
        {
            return NeedAssessed;
        }

        var next = assessmentDate.Value.Date.AddYears(SoutStatusBuilder.ValidityYears);
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

    public static bool MatchesNeedFilter(
        WorkplaceRecord? workplace,
        string filterKey,
        DateTime asOf,
        IReadOnlySet<string>? plannedWorkplaceIds = null,
        IReadOnlyDictionary<string, WorkplaceRecord>? workplacesById = null)
    {
        if (filterKey == NeedAll)
        {
            return true;
        }

        var bucket = workplace is null
            ? NeedNotConducted
            : ClassifySummaryBucket(
                EffectiveForSummary(workplace, workplacesById),
                asOf,
                plannedWorkplaceIds);
        return string.Equals(bucket, filterKey, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Для сводки: если РМ ссылается на другую карту СОУТ — берём даты/№/класс с карты.
    /// </summary>
    public static WorkplaceRecord EffectiveForSummary(
        WorkplaceRecord workplace,
        IReadOnlyDictionary<string, WorkplaceRecord>? workplacesById)
    {
        if (workplacesById is null || workplacesById.Count == 0)
        {
            return workplace;
        }

        var linkedId = (workplace.LinkedSoutCardWorkplaceId ?? string.Empty).Trim();
        if (linkedId.Length == 0
            || string.Equals(linkedId, workplace.Id, StringComparison.OrdinalIgnoreCase)
            || !workplacesById.TryGetValue(linkedId, out var card)
            || card.IsGroup)
        {
            return workplace;
        }

        return new WorkplaceRecord
        {
            Id = workplace.Id,
            Organization = workplace.Organization,
            IsGroup = false,
            IsUsed = workplace.IsUsed,
            Name = workplace.Name,
            Profession = workplace.Profession,
            Department = workplace.Department,
            ConditionsClass = FirstNonEmpty(card.ConditionsClass, workplace.ConditionsClass),
            SoutAssessmentDate = card.SoutAssessmentDate ?? workplace.SoutAssessmentDate,
            SoutReportNumber = FirstNonEmpty(card.SoutReportNumber, workplace.SoutReportNumber),
            SoutCard = card.SoutCard ?? workplace.SoutCard,
            DeclarationOfConformity = card.DeclarationOfConformity || workplace.DeclarationOfConformity,
            SoutAssessmentNotConducted =
                workplace.SoutAssessmentNotConducted && card.SoutAssessmentNotConducted,
            RequiresUnscheduledSout =
                workplace.RequiresUnscheduledSout || card.RequiresUnscheduledSout,
            SoutTrafficMark = FirstNonEmpty(workplace.SoutTrafficMark, card.SoutTrafficMark),
            LinkedSoutCardWorkplaceId = workplace.LinkedSoutCardWorkplaceId,
            PlanYear = workplace.PlanYear
        };
    }

    private static string FirstNonEmpty(string? primary, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary.Trim();
        }

        return (fallback ?? string.Empty).Trim();
    }

    private static bool MatchesOrganization(string? workplaceOrganization, string? filterOrganization)
    {
        var org = (filterOrganization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return true;
        }

        var row = (workplaceOrganization ?? string.Empty).Trim();
        if (row.Length == 0)
        {
            return false;
        }

        return string.Equals(row, org, StringComparison.OrdinalIgnoreCase)
               || OrganizationMatcher.Matches(org, row)
               || OrganizationMatcher.Matches(row, org);
    }

    private static int? ParseClassCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var ch in text)
        {
            if (ch is >= '1' and <= '4')
            {
                return ch - '0';
            }
        }

        return null;
    }
}
