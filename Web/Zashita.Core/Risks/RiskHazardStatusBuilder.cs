using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

/// <summary>
/// Отчёт «Состояние по управлению профессиональными рисками» —
/// иерархия орг → подразделение → профессия → опасность (без ФИО сотрудников).
/// </summary>
public static class RiskHazardStatusBuilder
{
    public static List<RiskHazardStatusRow> Build(
        IEnumerable<(string Organization, OrganizationEmployee Employee)> employees,
        IReadOnlyList<RiskHazardAssessmentRecord> saved,
        DateTime asOf,
        string? organizationFilter = null,
        string? departmentFilter = null,
        string? professionFilter = null,
        string? residualRiskFilter = null,
        string? statusFilter = null,
        IReadOnlyList<RiskHazardCatalogItem>? catalog = null,
        IReadOnlyList<WorkplaceRecord>? workplaces = null)
    {
        var catalogItems = catalog ?? RiskHazardCatalogStore.CreateDefaults();
        var workplaceList = workplaces ?? [];
        var rows = new List<RiskHazardStatusRow>();

        var employeeList = employees
            .Where(x => !string.IsNullOrWhiteSpace(x.Employee.Name))
            .Where(x => string.IsNullOrWhiteSpace(organizationFilter)
                        || string.Equals(x.Organization, organizationFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var keys = new HashSet<(string Org, string Dept, string Prof)>(new OrgDeptProfComparer());
        foreach (var (org, employee) in employeeList)
        {
            var dept = string.IsNullOrWhiteSpace(employee.Department) ? "Без подразделения" : employee.Department.Trim();
            var prof = DisplayProfession(employee.Profession);
            keys.Add((org, dept, prof));
        }

        foreach (var workplace in workplaceList.Where(w => !w.IsGroup))
        {
            if (!string.IsNullOrWhiteSpace(organizationFilter)
                && !string.Equals(workplace.Organization, organizationFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var dept = string.IsNullOrWhiteSpace(workplace.Department) ? "Без подразделения" : workplace.Department.Trim();
            var prof = DisplayProfession(
                string.IsNullOrWhiteSpace(workplace.Profession) ? workplace.Name : workplace.Profession);
            keys.Add((workplace.Organization.Trim(), dept, prof));
        }

        foreach (var (org, dept, prof) in keys
                     .OrderBy(k => k.Org, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(k => k.Dept, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(k => k.Prof, StringComparer.CurrentCultureIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(departmentFilter)
                && !dept.Contains(departmentFilter.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(professionFilter)
                && !prof.Contains(professionFilter.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var workplace = FindWorkplace(workplaceList, org, dept, prof);

            if (IsAssessmentMissing(workplace))
            {
                var notConducted = new RiskHazardStatusRow
                {
                    Id = StableId(org, prof, RiskHazardStatuses.NotConducted),
                    IsGroup = false,
                    Level = 3,
                    Title = RiskHazardStatuses.NotConducted,
                    InitialRisk = "—",
                    ControlMeasures = "—",
                    ResidualRisk = "—",
                    AssessmentDate = null,
                    ReviewDate = null,
                    Status = RiskHazardStatuses.NotConducted,
                    Traffic = "red",
                    Organization = org,
                    Department = dept,
                    Profession = prof,
                    EmployeeName = string.Empty,
                    HazardKey = RiskHazardStatuses.NotConducted
                };

                if (!string.IsNullOrWhiteSpace(statusFilter)
                    && !notConducted.Status.Contains(statusFilter.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                rows.Add(notConducted);
                continue;
            }

            var hazards = ResolveHazards(
                org,
                dept,
                prof,
                string.Empty,
                saved,
                catalogItems,
                workplace?.RiskLines,
                workplace?.RiskAssessmentDate,
                workplace?.RiskPeriodicity);

            foreach (var hazard in hazards)
            {
                if (!string.IsNullOrWhiteSpace(residualRiskFilter)
                    && !hazard.ResidualRisk.Contains(residualRiskFilter.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var noPeriod = WorkplaceRiskScale.IsNoPeriodicity(workplace?.RiskPeriodicity);
                var periodMonths = noPeriod
                    ? 0
                    : hazard.ReviewPeriodMonths
                      ?? WorkplaceRiskScale.ParsePeriodicityMonths(workplace?.RiskPeriodicity)
                      ?? ResidualReviewMonths(hazard.ResidualRisk);
                var review = noPeriod
                    ? null
                    : hazard.ReviewDate ?? ComputeReviewDate(hazard.AssessmentDate, periodMonths);
                var status = ComputeAutoStatus(hazard.AssessmentDate, review, asOf, hazard);
                var traffic = ComputeTraffic(status, review, asOf, hazard.AssessmentDate);

                if (!string.IsNullOrWhiteSpace(statusFilter)
                    && !status.Contains(statusFilter.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                rows.Add(new RiskHazardStatusRow
                {
                    Id = hazard.Id,
                    IsGroup = false,
                    Level = 3,
                    Title = hazard.HazardTitle,
                    WhoMaySuffer = hazard.WhoMaySuffer,
                    InitialRisk = hazard.InitialRisk,
                    ControlMeasures = hazard.ControlMeasures,
                    ResidualRisk = hazard.ResidualRisk,
                    ReviewPeriodMonths = periodMonths,
                    AssessmentDate = hazard.AssessmentDate,
                    ReviewDate = review,
                    Responsible = hazard.Responsible,
                    Status = status,
                    Traffic = traffic,
                    Organization = org,
                    Department = dept,
                    Profession = prof,
                    EmployeeName = string.Empty,
                    HazardKey = hazard.HazardTitle
                });
            }
        }

        return rows
            .OrderBy(r => r.Organization, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Department, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Profession, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static List<RiskHazardTreeLine> BuildTree(
        IReadOnlyList<RiskHazardStatusRow> rows,
        IReadOnlyList<WorkplaceRecord>? workplaces = null)
    {
        var workplaceList = workplaces ?? [];
        var lines = new List<RiskHazardTreeLine>();
        foreach (var orgGroup in rows.GroupBy(r => r.Organization).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var orgId = NodeId("org", orgGroup.Key);
            lines.Add(RiskHazardTreeLine.Group(orgGroup.Key, 0, orgId, null));
            foreach (var deptGroup in orgGroup.GroupBy(r => r.Department).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var deptId = NodeId("dept", orgGroup.Key, deptGroup.Key);
                lines.Add(RiskHazardTreeLine.Group(deptGroup.Key, 1, deptId, orgId));
                foreach (var profGroup in deptGroup.GroupBy(r => r.Profession).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
                {
                    var profId = NodeId("prof", orgGroup.Key, deptGroup.Key, profGroup.Key);
                    var workplace = FindWorkplace(workplaceList, orgGroup.Key, deptGroup.Key, profGroup.Key);
                    var hazardRows = profGroup.ToList();
                    var missing = IsAssessmentMissing(workplace)
                                  || hazardRows.All(r =>
                                      string.Equals(r.Status, RiskHazardStatuses.NotConducted, StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(r.Status, RiskHazardStatuses.NeedsAssessment, StringComparison.OrdinalIgnoreCase));
                    var overdue = !missing && hazardRows.Any(r =>
                        string.Equals(r.Status, RiskHazardStatuses.Overdue, StringComparison.OrdinalIgnoreCase));
                    var professionStatus = missing
                        ? RiskHazardStatuses.NotConducted
                        : overdue
                            ? RiskHazardStatuses.Overdue
                            : "Проведена";
                    lines.Add(new RiskHazardTreeLine
                    {
                        IsGroup = true,
                        Level = 2,
                        Label = profGroup.Key,
                        Id = profId,
                        ParentId = deptId,
                        WorkplaceId = workplace?.Id,
                        RiskCardNumber = workplace?.RiskCardNumber ?? string.Empty,
                        CardAssessmentDate = missing ? null : workplace?.RiskAssessmentDate,
                        OverallRisk = workplace?.OverallRisk ?? string.Empty,
                        RiskPeriodicity = workplace?.RiskPeriodicity ?? string.Empty,
                        RiskAssessmentNotConducted = missing,
                        ProfessionStatus = professionStatus,
                        ProfessionTraffic = missing || overdue ? "red" : "green",
                        ChildCount = hazardRows.Count
                    });
                    foreach (var row in hazardRows.OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase))
                    {
                        lines.Add(RiskHazardTreeLine.Data(row, 3, row.Id, profId));
                    }
                }
            }
        }

        return lines;
    }

    /// <summary>
    /// Отчёт ОПР: орг → подразделение → профессия → сотрудники
    /// (дата трудоустройства + статус ознакомления с картой).
    /// </summary>
    public static List<RiskHazardTreeLine> BuildStaffTree(
        IEnumerable<(string Organization, OrganizationEmployee Employee)> employees,
        IReadOnlyList<WorkplaceRecord> workplaces,
        DateTime? asOf = null,
        string? organizationFilter = null,
        string? departmentFilter = null,
        string? professionFilter = null,
        string? assessmentStatusFilter = null,
        bool onlyProfessionsWithCard = false)
    {
        var today = (asOf ?? DateTime.Today).Date;
        var workplaceList = workplaces ?? [];
        var lines = new List<RiskHazardTreeLine>();

        var employeeList = (employees ?? [])
            .Where(x => x.Employee.IsActive && !string.IsNullOrWhiteSpace(x.Employee.Name))
            .Where(x => string.IsNullOrWhiteSpace(organizationFilter)
                        || string.Equals(x.Organization, organizationFilter, StringComparison.OrdinalIgnoreCase))
            .Select(x =>
            {
                var dept = string.IsNullOrWhiteSpace(x.Employee.Department)
                    ? "Без подразделения"
                    : x.Employee.Department.Trim();
                var prof = DisplayProfession(x.Employee.Profession);
                return (Org: x.Organization.Trim(), Dept: dept, Prof: prof, Emp: x.Employee);
            })
            .Where(x => string.IsNullOrWhiteSpace(departmentFilter)
                        || x.Dept.Contains(departmentFilter.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(professionFilter)
                        || x.Prof.Contains(professionFilter.Trim(), StringComparison.OrdinalIgnoreCase)
                        || ProfessionMatcher.Matches(x.Prof, professionFilter))
            .ToList();

        // Профессии из карт РМ; при onlyProfessionsWithCard — только те, у кого есть карта
        // (удалённая с объекта карта не тянет людей из штата в отчёт по объекту).
        var keys = new HashSet<(string Org, string Dept, string Prof)>(new OrgDeptProfComparer());
        if (!onlyProfessionsWithCard)
        {
            foreach (var e in employeeList)
            {
                keys.Add((e.Org, e.Dept, e.Prof));
            }
        }

        foreach (var workplace in workplaceList.Where(w => !w.IsGroup && w.IsUsed))
        {
            if (!string.IsNullOrWhiteSpace(organizationFilter)
                && !string.Equals(workplace.Organization, organizationFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var dept = string.IsNullOrWhiteSpace(workplace.Department) ? "Без подразделения" : workplace.Department.Trim();
            var prof = DisplayProfession(
                string.IsNullOrWhiteSpace(workplace.Profession) ? workplace.Name : workplace.Profession);
            if (!string.IsNullOrWhiteSpace(departmentFilter)
                && !dept.Contains(departmentFilter.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(professionFilter)
                && !prof.Contains(professionFilter.Trim(), StringComparison.OrdinalIgnoreCase)
                && !ProfessionMatcher.Matches(prof, professionFilter))
            {
                continue;
            }

            keys.Add((workplace.Organization.Trim(), dept, prof));
        }

        foreach (var orgGroup in keys.GroupBy(k => k.Org).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var orgId = NodeId("org", orgGroup.Key);
            lines.Add(RiskHazardTreeLine.Group(orgGroup.Key, 0, orgId, null));

            foreach (var deptGroup in orgGroup.GroupBy(k => k.Dept).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var deptId = NodeId("dept", orgGroup.Key, deptGroup.Key);
                lines.Add(RiskHazardTreeLine.Group(deptGroup.Key, 1, deptId, orgId));

                foreach (var profKey in deptGroup.Select(k => k.Prof).Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase))
                {
                    var workplace = FindWorkplace(workplaceList, orgGroup.Key, deptGroup.Key, profKey);
                    var (professionStatus, traffic) = ResolveProfessionAssessment(workplace, today);
                    if (!string.IsNullOrWhiteSpace(assessmentStatusFilter)
                        && !professionStatus.Contains(assessmentStatusFilter.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var staff = employeeList
                        .Where(e => string.Equals(e.Org, orgGroup.Key, StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(e.Dept, deptGroup.Key, StringComparison.OrdinalIgnoreCase)
                                    && (string.Equals(e.Prof, profKey, StringComparison.OrdinalIgnoreCase)
                                        || ProfessionMatcher.Matches(e.Prof, profKey)))
                        .Select(e => e.Emp)
                        .GroupBy(e => EmployeeFamiliarizationKey(e), StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.OrderByDescending(x => x.EmploymentDate ?? DateTime.MinValue).First())
                        .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();

                    var missing = IsAssessmentMissing(workplace);
                    var profId = NodeId("prof", orgGroup.Key, deptGroup.Key, profKey);
                    lines.Add(new RiskHazardTreeLine
                    {
                        IsGroup = true,
                        Level = 2,
                        Label = profKey,
                        Id = profId,
                        ParentId = deptId,
                        WorkplaceId = workplace?.Id,
                        RiskCardNumber = workplace?.RiskCardNumber ?? string.Empty,
                        CardAssessmentDate = missing ? null : workplace?.RiskAssessmentDate,
                        OverallRisk = workplace?.OverallRisk ?? string.Empty,
                        RiskPeriodicity = workplace?.RiskPeriodicity ?? string.Empty,
                        RiskAssessmentNotConducted = missing,
                        ProfessionStatus = professionStatus,
                        ProfessionTraffic = traffic,
                        ChildCount = staff.Count
                    });

                    foreach (var emp in staff)
                    {
                        var key = EmployeeFamiliarizationKey(emp);
                        var fam = ResolveFamiliarization(workplace, key, emp.Name);
                        var personId = NodeId("emp", orgGroup.Key, deptGroup.Key, profKey, key);
                        lines.Add(RiskHazardTreeLine.Person(
                            emp.Name.Trim(),
                            key,
                            emp.EmploymentDate,
                            fam.IsFamiliarized,
                            fam.NotWorkingOnObject,
                            3,
                            personId,
                            profId,
                            workplace?.Id));
                    }
                }
            }
        }

        return lines;
    }

    public static string EmployeeFamiliarizationKey(OrganizationEmployee employee)
    {
        var snils = OneCEmployeeListParser.NormalizeSnils(employee.Snils);
        if (snils.Length >= 11)
        {
            return snils;
        }

        if (!string.IsNullOrWhiteSpace(employee.ExternalId))
        {
            return "ext:" + employee.ExternalId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(employee.Id))
        {
            return "id:" + employee.Id.Trim();
        }

        return "name:" + (employee.Name ?? string.Empty).Trim().ToLowerInvariant();
    }

    public static bool IsFamiliarized(WorkplaceRecord? workplace, string employeeKey, string? employeeName) =>
        ResolveFamiliarization(workplace, employeeKey, employeeName).IsFamiliarized;

    public static (bool IsFamiliarized, bool NotWorkingOnObject) ResolveFamiliarization(
        WorkplaceRecord? workplace,
        string employeeKey,
        string? employeeName)
    {
        var item = FindFamiliarization(workplace, employeeKey, employeeName);
        if (item is null)
        {
            return (false, false);
        }

        if (item.NotWorkingOnObject)
        {
            return (false, true);
        }

        return (item.IsFamiliarized, false);
    }

    public static RiskCardFamiliarization? FindFamiliarization(
        WorkplaceRecord? workplace,
        string employeeKey,
        string? employeeName)
    {
        var list = workplace?.RiskFamiliarizations;
        if (list is null || list.Count == 0)
        {
            return null;
        }

        foreach (var item in list)
        {
            if (!string.IsNullOrWhiteSpace(employeeKey)
                && string.Equals(item.EmployeeKey, employeeKey, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }

            if (!string.IsNullOrWhiteSpace(employeeName)
                && string.Equals(item.EmployeeName, employeeName, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    private static (string Status, string Traffic) ResolveProfessionAssessment(WorkplaceRecord? workplace, DateTime today)
    {
        if (IsAssessmentMissing(workplace))
        {
            return (RiskHazardStatuses.NotConducted, "red");
        }

        var expires = RiskWorkplaceNeedsBuilder.ResolveExpiryDate(workplace!);
        if (expires is { } due && due.Date < today)
        {
            return (RiskHazardStatuses.Overdue, "red");
        }

        if (expires is { } soon && soon.Date <= today.AddDays(30))
        {
            return ("Проведена", "yellow");
        }

        return (RiskHazardStatuses.Done, "green");
    }

    /// <summary>
    /// Нет оценки: явная отметка или карта без заполненных строк рисков.
    /// </summary>
    public static bool IsAssessmentMissing(WorkplaceRecord? workplace)
    {
        if (workplace is null)
        {
            return true;
        }

        if (workplace.RiskAssessmentNotConducted)
        {
            return true;
        }

        return !RiskAssessmentStatusBuilder.HasFilledRiskLines(workplace);
    }

    private static WorkplaceRecord? FindWorkplace(
        IReadOnlyList<WorkplaceRecord> workplaces,
        string organization,
        string department,
        string profession)
    {
        var leaves = workplaces
            .Where(w => !w.IsGroup
                        && string.Equals(w.Organization, organization, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (leaves.Count == 0)
        {
            return null;
        }

        var byProfAndDept = leaves
            .Where(w =>
                ProfessionMatchesWorkplace(w, profession) &&
                (string.IsNullOrWhiteSpace(w.Department) ||
                 string.Equals(w.Department, department, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(WorkplaceAssessmentScore)
            .ThenBy(w => DisplayProfession(string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession),
                StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault();
        if (byProfAndDept is not null)
        {
            return byProfAndDept;
        }

        return leaves
                   .Where(w => ProfessionMatchesWorkplace(w, profession))
                   .OrderByDescending(WorkplaceAssessmentScore)
                   .FirstOrDefault()
               ?? leaves.FirstOrDefault(w =>
                   string.Equals(w.Department, department, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ProfessionMatchesWorkplace(WorkplaceRecord workplace, string profession) =>
        ProfessionMatcher.Matches(workplace.Profession, profession)
        || ProfessionMatcher.Matches(workplace.Name, profession)
        || string.Equals(
            DisplayProfession(string.IsNullOrWhiteSpace(workplace.Profession) ? workplace.Name : workplace.Profession),
            DisplayProfession(profession),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Предпочитаем заполненную карту, если есть и «Арматурщик», и «Арматурщик*».</summary>
    private static int WorkplaceAssessmentScore(WorkplaceRecord w)
    {
        var score = 0;
        if (w.RiskAssessmentDate.HasValue)
        {
            score += 100;
        }

        if (!string.IsNullOrWhiteSpace(w.RiskCardNumber))
        {
            score += 40;
        }

        if (RiskAssessmentStatusBuilder.HasFilledRiskLines(w))
        {
            score += 20;
        }

        if (!w.RiskAssessmentNotConducted)
        {
            score += 5;
        }

        return score;
    }

    /// <summary>«Арматурщик*» → «Арматурщик» (без звёздочки и лишних пробелов).</summary>
    private static string DisplayProfession(string? profession)
    {
        var normalized = ProfessionMatcher.Normalize(profession);
        return string.IsNullOrWhiteSpace(normalized) ? "Без профессии" : normalized;
    }

    public static string ComputeAutoStatus(
        DateTime? assessmentDate,
        DateTime? reviewDate,
        DateTime asOf,
        RiskHazardAssessmentRecord? hazard = null)
    {
        var incomplete = assessmentDate is null
                         || string.IsNullOrWhiteSpace(hazard?.InitialRisk)
                         || string.IsNullOrWhiteSpace(hazard?.ResidualRisk);

        if (incomplete)
        {
            return RiskHazardStatuses.NotConducted;
        }

        if (reviewDate is { } review && review.Date < asOf.Date)
        {
            return RiskHazardStatuses.Overdue;
        }

        return RiskHazardStatuses.Done;
    }

    public static DateTime? ComputeReviewDate(DateTime? assessmentDate, string residualRisk) =>
        ComputeReviewDate(assessmentDate, ResidualReviewMonths(residualRisk));

    public static DateTime? ComputeReviewDate(DateTime? assessmentDate, int periodMonths)
    {
        if (!assessmentDate.HasValue)
        {
            return null;
        }

        var months = Math.Clamp(periodMonths <= 0 ? 12 : periodMonths, 1, 120);
        return assessmentDate.Value.Date.AddMonths(months);
    }

    public static int ResidualReviewMonths(string residualRisk)
    {
        var r = (residualRisk ?? "").ToLowerInvariant();
        if (r.Contains("неприемл") || r.Contains("недопуст"))
        {
            return 1;
        }

        if (r.Contains("высок"))
        {
            return 3;
        }

        return 12;
    }

    private static string NodeId(params string[] parts)
    {
        var key = string.Join("|", parts.Select(p => (p ?? "").Trim().ToLowerInvariant()));
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(key)));
        return hash[..24].ToLowerInvariant();
    }

    private static List<RiskHazardAssessmentRecord> ResolveHazards(
        string org,
        string dept,
        string prof,
        string name,
        IReadOnlyList<RiskHazardAssessmentRecord> saved,
        IReadOnlyList<RiskHazardCatalogItem> catalog,
        IReadOnlyList<WorkplaceRiskLine>? workplaceLines = null,
        DateTime? workplaceAssessmentDate = null,
        string? workplacePeriodicity = null)
    {
        var existing = saved
            .Where(x => string.Equals(x.Organization, org, StringComparison.OrdinalIgnoreCase)
                        && (string.IsNullOrWhiteSpace(x.Profession)
                            || string.Equals(x.Profession, prof, StringComparison.OrdinalIgnoreCase)
                            || ProfessionMatcher.Matches(x.Profession, prof))
                        && (string.IsNullOrWhiteSpace(name)
                            || string.Equals(x.EmployeeName, name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // При отчёте без ФИО оставляем одну сохранённую оценку на название опасности.
        if (string.IsNullOrWhiteSpace(name))
        {
            existing = existing
                .GroupBy(x => x.HazardTitle.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.AssessmentDate ?? DateTime.MinValue).First())
                .ToList();
        }

        var byTitle = new Dictionary<string, RiskHazardAssessmentRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in existing)
        {
            if (string.IsNullOrWhiteSpace(record.HazardTitle))
            {
                continue;
            }

            byTitle.TryAdd(record.HazardTitle.Trim(), record);
        }

        var result = new List<RiskHazardAssessmentRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) Карта РМ — источник истины: все заполненные опасности попадают в отчёт по ОПР.
        foreach (var line in workplaceLines ?? [])
        {
            var title = (line.HazardTitle ?? string.Empty).Trim();
            if (IsBlankOrDash(title) || !seen.Add(title))
            {
                continue;
            }

            if (!byTitle.TryGetValue(title, out var record))
            {
                record = new RiskHazardAssessmentRecord
                {
                    Id = StableId(org, string.IsNullOrWhiteSpace(name) ? prof : name, title),
                    Organization = org,
                    Department = dept,
                    Profession = prof,
                    EmployeeName = name,
                    HazardTitle = title
                };
            }

            ApplyWorkplaceLine(record, line, workplaceAssessmentDate, workplacePeriodicity);
            EnrichFromCatalogAndWorkplace(record, catalog, workplaceLines, workplaceAssessmentDate, workplacePeriodicity);
            result.Add(record);
        }

        // 2) Ранее сохранённые в отчёте строки, которых нет на карте.
        foreach (var record in existing)
        {
            var title = (record.HazardTitle ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(title) || !seen.Add(title))
            {
                continue;
            }

            EnrichFromCatalogAndWorkplace(record, catalog, workplaceLines, workplaceAssessmentDate, workplacePeriodicity);
            result.Add(record);
        }

        if (result.Count > 0)
        {
            return result;
        }

        // 3) Нет ни карты, ни сохранений — подсказки из реестра по профессии.
        return PickCatalogForProfession(prof, catalog)
            .Select(h =>
            {
                var record = new RiskHazardAssessmentRecord
                {
                    Id = StableId(org, string.IsNullOrWhiteSpace(name) ? prof : name, h.Title),
                    Organization = org,
                    Department = dept,
                    Profession = prof,
                    EmployeeName = name,
                    HazardTitle = h.Title,
                    WhoMaySuffer = h.DefaultWhoMaySuffer,
                    InitialRisk = h.DefaultInitialRisk,
                    ControlMeasures = h.DefaultMeasures,
                    ResidualRisk = h.DefaultResidualRisk,
                    ReviewPeriodMonths = WorkplaceRiskScale.ParsePeriodicityMonths(workplacePeriodicity)
                                         ?? ResidualReviewMonths(h.DefaultResidualRisk),
                    AssessmentDate = workplaceAssessmentDate,
                    ReviewDate = null,
                    Responsible = "",
                    Status = ""
                };
                EnrichFromCatalogAndWorkplace(record, catalog, workplaceLines, workplaceAssessmentDate, workplacePeriodicity);
                return record;
            })
            .ToList();
    }

    /// <summary>Переносит поля с карты идентификации/оценки РМ в строку отчёта ОПР.</summary>
    private static void ApplyWorkplaceLine(
        RiskHazardAssessmentRecord record,
        WorkplaceRiskLine line,
        DateTime? workplaceAssessmentDate,
        string? workplacePeriodicity = null)
    {
        record.HazardTitle = string.IsNullOrWhiteSpace(line.HazardTitle)
            ? record.HazardTitle
            : line.HazardTitle.Trim();

        if (!IsBlankOrDash(line.WhoMaySuffer))
        {
            record.WhoMaySuffer = line.WhoMaySuffer.Trim();
        }

        if (!IsBlankOrDash(line.ControlMeasures))
        {
            record.ControlMeasures = line.ControlMeasures.Trim();
        }

        if (!IsBlankOrDash(line.ResidualRisk))
        {
            record.ResidualRisk = line.ResidualRisk.Trim();
        }

        if (!IsBlankOrDash(line.InitialRisk))
        {
            record.InitialRisk = line.InitialRisk.Trim();
        }
        else if (!IsBlankOrDash(line.Significance))
        {
            record.InitialRisk = line.Significance.Trim();
        }
        else
        {
            var score = WorkplaceRiskScale.ComputeScore(line.Severity, line.Probability);
            if (score is { } s)
            {
                record.InitialRisk = s.ToString();
            }
        }

        if (!IsBlankOrDash(line.Responsible))
        {
            record.Responsible = line.Responsible.Trim();
        }

        if (workplaceAssessmentDate is { } date)
        {
            record.AssessmentDate = date.Date;
        }

        if (record.ReviewPeriodMonths is null or <= 0)
        {
            record.ReviewPeriodMonths = WorkplaceRiskScale.ParsePeriodicityMonths(workplacePeriodicity)
                                        ?? ResidualReviewMonths(record.ResidualRisk);
        }
    }

    private static bool IsBlankOrDash(string? value)
    {
        var t = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(t) || t is "-" or "—" or "–";
    }

    /// <summary>
    /// Подтягивает меры управления, остаточный/исходный риск и дату оценки
    /// из реестра опасностей и карты РМ, если в сохранённой оценке поля пустые.
    /// </summary>
    private static void EnrichFromCatalogAndWorkplace(
        RiskHazardAssessmentRecord record,
        IReadOnlyList<RiskHazardCatalogItem> catalog,
        IReadOnlyList<WorkplaceRiskLine>? workplaceLines,
        DateTime? workplaceAssessmentDate,
        string? workplacePeriodicity = null)
    {
        var catalogItem = catalog.FirstOrDefault(c =>
            string.Equals(c.Title, record.HazardTitle, StringComparison.OrdinalIgnoreCase));

        var line = workplaceLines?.FirstOrDefault(l =>
            string.Equals(l.HazardTitle, record.HazardTitle, StringComparison.OrdinalIgnoreCase));

        if (IsBlankOrDash(record.ControlMeasures))
        {
            record.ControlMeasures = FirstNonEmpty(line?.ControlMeasures, catalogItem?.DefaultMeasures);
        }

        if (IsBlankOrDash(record.ResidualRisk))
        {
            record.ResidualRisk = FirstNonEmpty(line?.ResidualRisk, catalogItem?.DefaultResidualRisk);
        }

        if (IsBlankOrDash(record.InitialRisk))
        {
            var score = line is null ? null : WorkplaceRiskScale.ComputeScore(line.Severity, line.Probability);
            record.InitialRisk = FirstNonEmpty(
                line?.InitialRisk,
                score?.ToString(),
                line?.Significance,
                catalogItem?.DefaultInitialRisk);
        }

        if (IsBlankOrDash(record.WhoMaySuffer))
        {
            record.WhoMaySuffer = FirstNonEmpty(line?.WhoMaySuffer, catalogItem?.DefaultWhoMaySuffer);
        }

        if (IsBlankOrDash(record.Responsible))
        {
            record.Responsible = FirstNonEmpty(line?.Responsible);
        }

        if (record.AssessmentDate is null && workplaceAssessmentDate is { } date)
        {
            record.AssessmentDate = date.Date;
        }

        if (record.ReviewPeriodMonths is null or <= 0)
        {
            record.ReviewPeriodMonths = WorkplaceRiskScale.ParsePeriodicityMonths(workplacePeriodicity)
                                        ?? ResidualReviewMonths(record.ResidualRisk);
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            var t = (value ?? string.Empty).Trim();
            if (t.Length > 0 && t is not ("-" or "—" or "–"))
            {
                return t;
            }
        }

        return string.Empty;
    }

    private static IReadOnlyList<RiskHazardCatalogItem> PickCatalogForProfession(
        string profession,
        IReadOnlyList<RiskHazardCatalogItem> catalog)
    {
        var active = catalog.Where(c => c.IsActive && !string.IsNullOrWhiteSpace(c.Title)).ToList();
        if (active.Count == 0)
        {
            return RiskHazardCatalogStore.CreateDefaults();
        }

        var p = profession.ToLowerInvariant();
        var matched = active
            .Where(c =>
            {
                if (string.IsNullOrWhiteSpace(c.ProfessionHint))
                {
                    return false;
                }

                return c.ProfessionHint
                    .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Any(hint => p.Contains(hint.ToLowerInvariant(), StringComparison.Ordinal));
            })
            .ToList();

        if (matched.Count > 0)
        {
            return matched;
        }

        return active
            .Where(c => string.IsNullOrWhiteSpace(c.ProfessionHint))
            .Take(2)
            .DefaultIfEmpty(active[0])
            .ToList();
    }

    private static string StableId(string org, string employee, string hazard)
    {
        var key = $"{org}|{employee}|{hazard}".ToLowerInvariant();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(key)));
        return hash[..24].ToLowerInvariant();
    }

    private static string ComputeTraffic(
        string status,
        DateTime? review,
        DateTime asOf,
        DateTime? assessmentDate)
    {
        if (string.Equals(status, RiskHazardStatuses.NotConducted, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, RiskHazardStatuses.NeedsAssessment, StringComparison.OrdinalIgnoreCase)
            || assessmentDate is null)
        {
            return "red";
        }

        if (string.Equals(status, RiskHazardStatuses.Overdue, StringComparison.OrdinalIgnoreCase))
        {
            return "red";
        }

        if (review is { } r)
        {
            if (r.Date < asOf.Date)
            {
                return "red";
            }

            if (r.Date <= asOf.Date.AddDays(30))
            {
                return "yellow";
            }
        }

        return "green";
    }

    private sealed class OrgDeptProfComparer : IEqualityComparer<(string Org, string Dept, string Prof)>
    {
        public bool Equals((string Org, string Dept, string Prof) x, (string Org, string Dept, string Prof) y) =>
            string.Equals(x.Org, y.Org, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Dept, y.Dept, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Prof, y.Prof, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Org, string Dept, string Prof) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Org ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Dept ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Prof ?? string.Empty));
    }
}

public sealed class RiskHazardTreeLine
{
    public bool IsGroup { get; init; }
    public int Level { get; init; }
    public string Label { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string? ParentId { get; init; }
    public RiskHazardStatusRow? Row { get; init; }

    /// <summary>Сводка с карты оценки (для строки профессии).</summary>
    public string? WorkplaceId { get; init; }
    public string RiskCardNumber { get; init; } = string.Empty;
    public DateTime? CardAssessmentDate { get; init; }
    public string OverallRisk { get; init; } = string.Empty;
    public string RiskPeriodicity { get; init; } = string.Empty;
    public bool RiskAssessmentNotConducted { get; init; }
    /// <summary>Статус оценки по профессии: Проведена / Не проведена… / Просрочено.</summary>
    public string ProfessionStatus { get; init; } = string.Empty;
    public string ProfessionTraffic { get; init; } = string.Empty;
    public int ChildCount { get; init; }

    /// <summary>Строка сотрудника под профессией.</summary>
    public bool IsPerson { get; init; }
    public string EmployeeKey { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
    public DateTime? EmploymentDate { get; init; }
    public bool IsFamiliarized { get; init; }
    public bool NotWorkingOnObject { get; init; }

    public static RiskHazardTreeLine Group(string label, int level, string id, string? parentId) =>
        new() { IsGroup = true, Level = level, Label = label, Id = id, ParentId = parentId };

    public static RiskHazardTreeLine Data(RiskHazardStatusRow row, int level, string id, string? parentId) =>
        new() { IsGroup = false, Level = level, Row = row, Label = row.Title, Id = id, ParentId = parentId };

    public static RiskHazardTreeLine Person(
        string name,
        string employeeKey,
        DateTime? employmentDate,
        bool familiarized,
        bool notWorkingOnObject,
        int level,
        string id,
        string? parentId,
        string? workplaceId) =>
        new()
        {
            IsGroup = false,
            IsPerson = true,
            Level = level,
            Label = name,
            Id = id,
            ParentId = parentId,
            WorkplaceId = workplaceId,
            EmployeeKey = employeeKey,
            EmployeeName = name,
            EmploymentDate = employmentDate,
            IsFamiliarized = familiarized && !notWorkingOnObject,
            NotWorkingOnObject = notWorkingOnObject
        };
}
