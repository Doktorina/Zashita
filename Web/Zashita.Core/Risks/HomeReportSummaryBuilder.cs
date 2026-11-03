using Zashita.Core.Med;
using Zashita.Core.Training;

namespace Zashita.Core.Risks;

/// <summary>Сводка счётчиков отчётов для главной страницы.</summary>
public static class HomeReportSummaryBuilder
{
    public sealed record ModuleSummary(
        int Line1,
        int Line2,
        int Line3,
        int Line4,
        int Line5);

    /// <summary>Статус покрытия по организации (для режима «Все организации»).</summary>
    public enum OrgCoverageStatus
    {
        Empty,
        Complete,
        Partial,
        None,
        Overdue
    }

    public sealed record OrgCoverageRow(
        string Organization,
        OrgCoverageStatus Status,
        string StatusLabel,
        int Done,
        int Missing,
        int Overdue,
        int Expiring);

    public static ModuleSummary FromMed(MedDashboardStats med) =>
        new(
            med.Expired,
            med.ExpiringSoon,
            med.NotPassed,
            med.Passed,
            med.Planned);

    public static ModuleSummary FromTraining(
        IReadOnlyList<TrainingStatusRow> rows,
        DateTime asOf,
        int plannedCount)
    {
        var programRows = rows.Where(r => !string.IsNullOrWhiteSpace(r.Program)).ToList();
        var people = programRows
            .GroupBy(r => $"{r.Person}\u001e{r.Department}\u001e{r.Profession}", StringComparer.OrdinalIgnoreCase)
            .ToList();

        var overdue = 0;
        var expiring = 0;
        var notTrained = 0;
        var fullyTrained = 0;

        foreach (var personRows in people)
        {
            var buckets = personRows
                .Select(r => TrainingNeedsCalendarBuilder.ClassifyNeed(r, asOf))
                .ToList();
            var hasOverdue = buckets.Any(b => b == TrainingNeedsCalendarBuilder.FilterOverdue);
            var hasExpiring = buckets.Any(b => b == TrainingNeedsCalendarBuilder.FilterExpiring);
            var hasNotConducted = buckets.Any(b => b == TrainingNeedsCalendarBuilder.FilterNotConducted);
            var okCount = personRows.Count(r =>
                string.Equals(r.Traffic, "green", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.Status, "Пройдено", StringComparison.OrdinalIgnoreCase));
            var allOk = okCount == personRows.Count() && !hasOverdue && !hasExpiring && !hasNotConducted;

            if (allOk)
            {
                fullyTrained++;
            }
            else if (hasOverdue)
            {
                overdue++;
            }
            else if (hasExpiring)
            {
                expiring++;
            }
            else
            {
                notTrained++;
            }
        }

        // Line1 overdue, Line2 expiring, Line3 not trained, Line4 fully trained, Line5 planned applications.
        return new ModuleSummary(overdue, expiring, notTrained, fullyTrained, Math.Max(0, plannedCount));
    }

    public static ModuleSummary FromSout(
        IReadOnlyList<WorkplaceRecord> workplaces,
        DateTime asOf,
        IReadOnlySet<string>? plannedWorkplaceIds = null)
    {
        var none = SoutWorkplaceNeedsBuilder.CountByBucket(
            workplaces, string.Empty, SoutWorkplaceNeedsBuilder.NeedNotConducted, asOf, plannedWorkplaceIds);
        var overdue = SoutWorkplaceNeedsBuilder.CountByBucket(
            workplaces, string.Empty, SoutWorkplaceNeedsBuilder.NeedOverdue, asOf, plannedWorkplaceIds);
        var expiring = SoutWorkplaceNeedsBuilder.CountByBucket(
            workplaces, string.Empty, SoutWorkplaceNeedsBuilder.NeedExpiring, asOf, plannedWorkplaceIds);
        var assessed = SoutWorkplaceNeedsBuilder.CountByBucket(
            workplaces, string.Empty, SoutWorkplaceNeedsBuilder.NeedAssessed, asOf, plannedWorkplaceIds);
        var planned = SoutWorkplaceNeedsBuilder.CountByBucket(
            workplaces, string.Empty, SoutWorkplaceNeedsBuilder.NeedPlanned, asOf, plannedWorkplaceIds);

        return new ModuleSummary(none, overdue, expiring, assessed, planned);
    }

    public static ModuleSummary FromRisks(IReadOnlyList<WorkplaceRecord> workplaces, DateTime asOf)
    {
        var none = RiskWorkplaceNeedsBuilder.CountByBucket(
            workplaces, string.Empty, RiskWorkplaceNeedsBuilder.NeedNotConducted, asOf);
        var overdue = RiskWorkplaceNeedsBuilder.CountByBucket(
            workplaces, string.Empty, RiskWorkplaceNeedsBuilder.NeedOverdue, asOf);
        var expiring = RiskWorkplaceNeedsBuilder.CountByBucket(
            workplaces, string.Empty, RiskWorkplaceNeedsBuilder.NeedExpiring, asOf);
        var assessed = RiskWorkplaceNeedsBuilder.CountByBucket(
            workplaces, string.Empty, RiskWorkplaceNeedsBuilder.NeedAssessed, asOf);
        var planned = RiskWorkplaceNeedsBuilder.CountByBucket(
            workplaces, string.Empty, RiskWorkplaceNeedsBuilder.NeedPlanned, asOf);

        return new ModuleSummary(none, overdue, expiring, assessed, planned);
    }

    /// <summary>
    /// Нормализует счётчики модуля к (просрочено / истекает / не проведено / проведено)
    /// и классифицирует организацию.
    /// </summary>
    public static OrgCoverageRow ClassifyOrganization(string organization, string moduleKey, ModuleSummary summary)
    {
        var (overdue, expiring, missing, done) = NormalizeCounters(moduleKey, summary);
        var status = Classify(overdue, expiring, missing, done);
        return new OrgCoverageRow(
            organization,
            status,
            StatusLabel(status, moduleKey),
            done,
            missing,
            overdue,
            expiring);
    }

    private static (int Overdue, int Expiring, int Missing, int Done) NormalizeCounters(
        string moduleKey,
        ModuleSummary s)
    {
        var key = (moduleKey ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            // Med/Training: L1 overdue, L2 expiring, L3 missing, L4 done
            "med" or "медосмотры" or "training" or "обучение" =>
                (s.Line1, s.Line2, s.Line3, s.Line4),
            // SOUT/Risks: L1 missing, L2 overdue, L3 expiring, L4 done
            "sout" or "спецоценка" or "специальная оценка" or "специальная-оценка"
                or "risks" or "риски" =>
                (s.Line2, s.Line3, s.Line1, s.Line4),
            _ => (s.Line1, s.Line2, s.Line3, s.Line4)
        };
    }

    private static OrgCoverageStatus Classify(int overdue, int expiring, int missing, int done)
    {
        if (overdue <= 0 && expiring <= 0 && missing <= 0 && done <= 0)
        {
            return OrgCoverageStatus.Empty;
        }

        if (overdue > 0)
        {
            return OrgCoverageStatus.Overdue;
        }

        if (done > 0 && missing <= 0)
        {
            return OrgCoverageStatus.Complete;
        }

        if (done <= 0 && missing > 0)
        {
            return OrgCoverageStatus.None;
        }

        if (done > 0 && missing > 0)
        {
            return OrgCoverageStatus.Partial;
        }

        if (expiring > 0)
        {
            return OrgCoverageStatus.Partial;
        }

        return OrgCoverageStatus.Partial;
    }

    public static string StatusLabel(OrgCoverageStatus status, string? moduleKey = null)
    {
        var key = (moduleKey ?? string.Empty).Trim().ToLowerInvariant();
        // Обучение — ср. род («проведено»), медосмотры — мн. ч. («проведены»).
        var form = key switch
        {
            "training" or "обучение" => "neuter",
            "med" or "медосмотры" => "plural",
            _ => "feminine"
        };

        return (status, form) switch
        {
            (OrgCoverageStatus.Complete, "neuter") => "Проведено полностью",
            (OrgCoverageStatus.Partial, "neuter") => "Проведено частично",
            (OrgCoverageStatus.None, "neuter") => "Не проведено",
            (OrgCoverageStatus.Overdue, "neuter") => "Просрочено",

            (OrgCoverageStatus.Complete, "plural") => "Проведены полностью",
            (OrgCoverageStatus.Partial, "plural") => "Проведены частично",
            (OrgCoverageStatus.None, "plural") => "Не проведены",
            (OrgCoverageStatus.Overdue, "plural") => "Просрочены",

            (OrgCoverageStatus.Complete, _) => "Проведена полностью",
            (OrgCoverageStatus.Partial, _) => "Проведена частично",
            (OrgCoverageStatus.None, _) => "Не проведена",
            (OrgCoverageStatus.Overdue, _) => "Просрочена",
            _ => "Нет данных"
        };
    }

    public static (string Complete, string Partial, string None, string Overdue) KpiLabels(string? moduleKey)
    {
        var key = (moduleKey ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "training" or "обучение" =>
                ("проведено полностью", "проведено частично", "не проведено", "просрочено"),
            "med" or "медосмотры" =>
                ("проведены полностью", "проведены частично", "не проведены", "просрочены"),
            _ =>
                ("проведена полностью", "проведена частично", "не проведена", "просрочена")
        };
    }

    /// <summary>
    /// Единая палитра диаграмм сводки (организации + ознакомление по орг.):
    /// зелёный — ок; янтарь — частично; красный — не проведена / не ознакомлены; тёмно-красный — просрочено; серый — нет данных.
    /// </summary>
    public static class StatusChartColors
    {
        public const string Complete = "#2e9b5f";
        public const string Partial = "#f79009";
        public const string None = "#d92d20";
        public const string Overdue = "#912018";
        public const string Empty = "#98a2b3";
        public const string Familiarized = Complete;
        public const string NotFamiliarized = None;
        public const string NotWorking = Empty;
    }
}