using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

/// <summary>Данные для круговой диаграммы ознакомления на главной модуля.</summary>
public sealed record FamiliarizationChartSlice(string Label, int Count, string Color);

public sealed record FamiliarizationChartData(
    string Title,
    IReadOnlyList<FamiliarizationChartSlice> Slices,
    string? Subtitle = null,
    string? ObjectId = null)
{
    public int Total => Slices.Sum(s => s.Count);
    public bool HasData => Total > 0;
}

/// <summary>
/// Подсчёт ознакомления сотрудников с картами рисков / СОУТ.
/// </summary>
public static class WorkplaceFamiliarizationChartBuilder
{
    /// <summary>
    /// По одной диаграмме на каждый объект оценки рисков
    /// (ознакомление своё на каждом объекте).
    /// </summary>
    public static IReadOnlyList<FamiliarizationChartData> ForRisksByObjects(
        string organization,
        IReadOnlyList<WorkplaceRecord> workplaces,
        IReadOnlyList<RiskAssessmentObject> assessmentObjects,
        IEnumerable<(string Organization, OrganizationEmployee Employee)> employees,
        IEnumerable<OrganizationEmployee>? includingInactive = null)
    {
        var org = (organization ?? string.Empty).Trim();
        var workplaceList = (workplaces ?? [])
            .Where(w => string.IsNullOrWhiteSpace(org)
                        || string.Equals(w.Organization, org, StringComparison.OrdinalIgnoreCase)
                        || OrganizationMatcher.Matches(org, w.Organization)
                        || OrganizationMatcher.Matches(w.Organization, org))
            .ToList();

        var employeeList = (employees ?? []).ToList();
        var inactive = includingInactive?.ToList();

        var objects = (assessmentObjects ?? [])
            .Where(o => !string.IsNullOrWhiteSpace(o.Name))
            .OrderBy(o => o.IsOrganizationWide ? 0 : 1)
            .ThenBy(o => o.SortOrder)
            .ThenBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (objects.Count == 0)
        {
            // Нет объектов — одна диаграмма по данным «по организации».
            return
            [
                BuildRiskChartForObject(
                    RiskAssessmentObjectIds.OrganizationWide,
                    "Ознакомление с картами рисков",
                    null,
                    workplaceList,
                    employeeList,
                    inactive,
                    org)
            ];
        }

        return objects
            .Select(obj =>
            {
                var scoped = RiskObjectMembership.FilterForObject(workplaceList, obj.Id, obj);
                return BuildRiskChartForObject(
                    obj.Id,
                    obj.IsOrganizationWide
                        ? "Ознакомление с картами рисков"
                        : $"Ознакомление — {obj.Name.Trim()}",
                    obj.IsOrganizationWide ? "по организации" : obj.Name.Trim(),
                    scoped,
                    employeeList,
                    inactive,
                    org);
            })
            .ToList();
    }

    /// <summary>Одна общая диаграмма (без разбивки по объектам) — для совместимости.</summary>
    public static FamiliarizationChartData ForRisks(
        string organization,
        IReadOnlyList<WorkplaceRecord> workplaces,
        IEnumerable<(string Organization, OrganizationEmployee Employee)> employees,
        IEnumerable<OrganizationEmployee>? includingInactive = null) =>
        ForRisksByObjects(
            organization,
            workplaces,
            [],
            employees,
            includingInactive)[0];

    public static FamiliarizationChartData ForSout(
        string organization,
        IReadOnlyList<WorkplaceRecord> workplaces,
        IReadOnlyList<WorkplaceAssignmentDocument> assignments,
        IReadOnlyList<OrganizationEmployee> employees,
        DateTime? asOf = null)
    {
        var org = (organization ?? string.Empty).Trim();
        var today = (asOf ?? DateTime.Today).Date;
        var lines = SoutStatusBuilder.BuildTree(
            workplaces,
            assignments,
            employees,
            org,
            today);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var familiarized = 0;
        var notFamiliarized = 0;
        foreach (var person in lines.Where(l => l.IsPerson))
        {
            var key = string.IsNullOrWhiteSpace(person.EmployeeKey)
                ? "name:" + (person.Label ?? string.Empty).Trim().ToLowerInvariant()
                : person.EmployeeKey.Trim();
            if (!seen.Add(key))
            {
                continue;
            }

            if (person.IsFamiliarized)
            {
                familiarized++;
            }
            else
            {
                notFamiliarized++;
            }
        }

        return new FamiliarizationChartData(
            "Ознакомление с картами СОУТ",
            [
                new FamiliarizationChartSlice("Ознакомлены", familiarized, HomeReportSummaryBuilder.StatusChartColors.Familiarized),
                new FamiliarizationChartSlice("Не ознакомлены", notFamiliarized, HomeReportSummaryBuilder.StatusChartColors.NotFamiliarized)
            ]);
    }

    private static FamiliarizationChartData BuildRiskChartForObject(
        string objectId,
        string title,
        string? subtitle,
        IReadOnlyList<WorkplaceRecord> workplaces,
        IReadOnlyList<(string Organization, OrganizationEmployee Employee)> employees,
        IReadOnlyList<OrganizationEmployee>? includingInactive,
        string organization)
    {
        var projected = RiskObjectAssessmentScope.ProjectListForObject(workplaces, objectId)
            .Where(w => !w.IsGroup && w.IsUsed)
            .ToList();

        var lines = RiskHazardStatusBuilder.BuildStaffTree(
            employees,
            projected,
            organizationFilter: string.IsNullOrWhiteSpace(organization) ? null : organization,
            onlyProfessionsWithCard: !RiskObjectAssessmentScope.IsOrganizationWide(objectId));

        var activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var familiarized = 0;
        var notFamiliarized = 0;
        var notWorking = 0;
        foreach (var person in lines.Where(l => l.IsPerson))
        {
            var key = string.IsNullOrWhiteSpace(person.EmployeeKey)
                ? "name:" + (person.EmployeeName ?? person.Label).Trim().ToLowerInvariant()
                : person.EmployeeKey.Trim();
            if (!activeKeys.Add(key))
            {
                continue;
            }

            if (person.NotWorkingOnObject)
            {
                notWorking++;
            }
            else if (person.IsFamiliarized)
            {
                familiarized++;
            }
            else
            {
                notFamiliarized++;
            }
        }

        notWorking += CountNotWorkingOnSite(
            projected,
            w => w.RiskFamiliarizations,
            activeKeys,
            includingInactive,
            organization);

        return new FamiliarizationChartData(
            title,
            BuildSlices(familiarized, notFamiliarized, notWorking),
            subtitle,
            objectId);
    }

    private static IReadOnlyList<FamiliarizationChartSlice> BuildSlices(
        int familiarized,
        int notFamiliarized,
        int notWorking) =>
    [
        new FamiliarizationChartSlice("Ознакомлены", familiarized, HomeReportSummaryBuilder.StatusChartColors.Familiarized),
        new FamiliarizationChartSlice("Не ознакомлены", notFamiliarized, HomeReportSummaryBuilder.StatusChartColors.NotFamiliarized),
        new FamiliarizationChartSlice("Не работают на объекте", notWorking, HomeReportSummaryBuilder.StatusChartColors.NotWorking)
    ];

    /// <summary>
    /// Люди из списка ознакомления / уволенные, которых уже нет среди активных на РМ.
    /// </summary>
    private static int CountNotWorkingOnSite(
        IReadOnlyList<WorkplaceRecord> workplaces,
        Func<WorkplaceRecord, List<RiskCardFamiliarization>?> pickList,
        IReadOnlySet<string> activeEmployeeKeys,
        IEnumerable<OrganizationEmployee>? includingInactive,
        string organization)
    {
        var notWorkingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var workplace in workplaces)
        {
            foreach (var item in pickList(workplace) ?? [])
            {
                var key = ResolveFamiliarizationKey(item);
                if (key.Length == 0 || activeEmployeeKeys.Contains(key))
                {
                    continue;
                }

                notWorkingKeys.Add(key);
            }
        }

        foreach (var employee in includingInactive ?? [])
        {
            if (EmployeeHrStatus.IsEmployed(employee))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(employee.Name))
            {
                continue;
            }

            var key = RiskHazardStatusBuilder.EmployeeFamiliarizationKey(employee);
            if (activeEmployeeKeys.Contains(key))
            {
                continue;
            }

            var mentioned = workplaces.Any(w =>
                (pickList(w) ?? []).Any(f =>
                    string.Equals(f.EmployeeKey, key, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(f.EmployeeName, employee.Name, StringComparison.OrdinalIgnoreCase)));
            if (mentioned)
            {
                notWorkingKeys.Add(key);
            }
        }

        _ = organization;
        return notWorkingKeys.Count;
    }

    private static string ResolveFamiliarizationKey(RiskCardFamiliarization item)
    {
        if (!string.IsNullOrWhiteSpace(item.EmployeeKey))
        {
            return item.EmployeeKey.Trim();
        }

        var name = (item.EmployeeName ?? string.Empty).Trim();
        return name.Length == 0 ? string.Empty : "name:" + name.ToLowerInvariant();
    }
}
