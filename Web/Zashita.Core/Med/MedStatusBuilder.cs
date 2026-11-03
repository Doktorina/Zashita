using Zashita.Core.Employees;

namespace Zashita.Core.Med;

public static class MedStatusBuilder
{
    public static IReadOnlyList<string> ConclusionOptions { get; } =
        ["Годен", "Не годен", "Ограниченно годен", "Не пройден"];

    public static IReadOnlyList<MedStatusRow> Build(
        IEnumerable<(string Organization, OrganizationEmployee Employee)> employees,
        IEnumerable<MedExamRecord> exams,
        DateTime reportDate,
        IReadOnlySet<string>? directionKeys = null)
    {
        directionKeys ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var examList = exams.ToList();
        var employeeList = employees
            .Where(x => !string.IsNullOrWhiteSpace(x.Employee.Name))
            .Select(x => (Organization: x.Organization?.Trim() ?? string.Empty, Employee: x.Employee))
            .ToList();

        var rows = new List<MedStatusRow>();
        var matchedExamIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (org, employee) in employeeList
                     .OrderBy(x => x.Organization, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(x => x.Employee.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var name = employee.Name.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var personExams = examList
                .Where(e => !matchedExamIds.Contains(e.Id))
                .Where(e => SameOrganization(e.Organization, org) && EmployeeNameMatcher.Matches(e.EmployeeName, name))
                .OrderByDescending(e => e.ExamDate)
                .ThenBy(e => e.ExamType, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (personExams.Count == 0)
            {
                rows.Add(CreatePlaceholderRow(org, employee, reportDate, directionKeys));
                continue;
            }

            foreach (var exam in personExams)
            {
                matchedExamIds.Add(exam.Id);
                rows.Add(FromExam(exam, employee, org, reportDate, directionKeys));
            }
        }

        var rosterOrganizations = employeeList
            .Select(x => x.Organization)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var exam in examList.Where(e => !matchedExamIds.Contains(e.Id)))
        {
            var roster = FindEmployee(employeeList, exam.Organization, exam.EmployeeName);
            // Только актуальный штат: осмотры уволенных / чужих ФИО в отчёт не тащим.
            if (roster is null)
            {
                continue;
            }

            var canonicalOrg = rosterOrganizations
                .FirstOrDefault(name => SameOrganization(name, exam.Organization))
                ?? ResolveCanonicalOrganization(exam.Organization, rosterOrganizations);
            rows.Add(FromExam(exam, roster, canonicalOrg, reportDate, directionKeys));
        }

        return rows;
    }

    public static List<MedStatusTreeLine> BuildTree(IReadOnlyList<MedStatusRow> rows)
    {
        var lines = new List<MedStatusTreeLine>();
        foreach (var orgGroup in rows.GroupBy(r => r.Organization).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var orgId = NodeId("o", orgGroup.Key);
            lines.Add(MedStatusTreeLine.Group(
                string.IsNullOrWhiteSpace(orgGroup.Key) ? "Организация" : orgGroup.Key,
                0,
                orgId,
                null));

            foreach (var deptGroup in orgGroup
                         .GroupBy(r => string.IsNullOrWhiteSpace(r.Department) ? "Без подразделения" : r.Department.Trim())
                         .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var deptId = NodeId("d", orgGroup.Key, deptGroup.Key);
                lines.Add(MedStatusTreeLine.Group(deptGroup.Key, 1, deptId, orgId));

                foreach (var profGroup in deptGroup
                             .GroupBy(r =>
                             {
                                 var prof = ProfessionMatcher.Normalize(r.Profession);
                                 return string.IsNullOrWhiteSpace(prof) ? "Без профессии" : prof;
                             })
                             .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
                {
                    var profId = NodeId("p", orgGroup.Key, deptGroup.Key, profGroup.Key);
                    lines.Add(MedStatusTreeLine.Group(profGroup.Key, 2, profId, deptId));

                    foreach (var personGroup in profGroup
                                 .GroupBy(r => (r.EmployeeName ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                                 .Where(g => EmployeeNameMatcher.IsPlausiblePersonName(g.Key))
                                 .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
                    {
                        var personLabel = personGroup.First().EmployeeName?.Trim() ?? personGroup.Key;
                        var personId = NodeId("e", orgGroup.Key, deptGroup.Key, profGroup.Key, personLabel);
                        lines.Add(MedStatusTreeLine.Person(personLabel, 3, personId, profId));

                        foreach (var row in personGroup
                                     .OrderBy(r => r.ExamType, StringComparer.CurrentCultureIgnoreCase)
                                     .ThenBy(r => r.ItemNumber, StringComparer.CurrentCultureIgnoreCase))
                        {
                            var leafId = string.IsNullOrWhiteSpace(row.Id)
                                ? NodeId("empty", row.Organization, row.EmployeeName, row.ExamType, row.ItemNumber)
                                : row.Id;
                            lines.Add(MedStatusTreeLine.Data(row, 4, leafId, personId));
                        }
                    }
                }
            }
        }

        return lines;
    }

    private static OrganizationEmployee? FindEmployee(
        IReadOnlyList<(string Organization, OrganizationEmployee Employee)> employees,
        string? organization,
        string? employeeName)
    {
        var name = (employeeName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return employees
            .Where(x => SameOrganization(x.Organization, organization) && EmployeeNameMatcher.Matches(x.Employee.Name, name))
            .Select(x => x.Employee)
            .FirstOrDefault();
    }

    private static bool SameOrganization(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase)
               || OrganizationMatcher.Matches(left, right)
               || OrganizationMatcher.Matches(right, left);
    }

    private static string ResolveCanonicalOrganization(
        string? organization,
        IReadOnlyList<string> rosterOrganizations)
    {
        var raw = (organization ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return OrganizationMatcher.ResolveOrganizationName(raw, rosterOrganizations) ?? raw;
    }

    private static MedStatusRow FromExam(
        MedExamRecord exam,
        OrganizationEmployee? employee,
        string canonicalOrganization,
        DateTime reportDate,
        IReadOnlySet<string> directionKeys)
    {
        var hasLastExam = exam.ExamDate.Year >= 1900;
        DateTime? lastExam = hasLastExam ? exam.ExamDate.Date : null;
        var onceOnHire = MedPeriodicity.IsOnceOnHire(exam.Periodicity) ||
                         MedStandardAssignmentBuilder.IsPsychiatricKind(exam.ExamType);
        DateTime? expires = onceOnHire
            ? null
            : exam.NextExamDate?.Date
              ?? (hasLastExam ? MedPeriodicity.NextExamDate(exam.ExamDate, exam.Periodicity) : null);

        // Штат — источник истины для подразделения/должности; запись медосмотра может быть устаревшей.
        var department = !string.IsNullOrWhiteSpace(employee?.Department)
            ? EmployeeContextValidator.NormalizeDepartment(employee!.Department)
            : EmployeeContextValidator.NormalizeDepartment(exam.Department);
        var profession = !string.IsNullOrWhiteSpace(employee?.Profession)
            ? EmployeeContextValidator.NormalizeProfession(employee!.Profession)
            : EmployeeContextValidator.NormalizeProfession(exam.Profession);
        var examType = MedStandardAssignmentBuilder.NormalizeExamType(exam.ExamType);
        var itemNumber = MedStandardAssignmentBuilder.IsPsychiatricKind(examType)
            ? string.Empty
            : MedItemNumber.Normalize(exam.ItemNumber);

        var traffic = MedStatusTraffic.Resolve(exam, expires, reportDate.Date, employee?.EmploymentDate?.Date);
        var organization = string.IsNullOrWhiteSpace(canonicalOrganization)
            ? exam.Organization
            : canonicalOrganization;
        var row = new MedStatusRow
        {
            Id = exam.Id,
            Organization = organization,
            Department = department,
            EmployeeName = string.IsNullOrWhiteSpace(employee?.Name) ? exam.EmployeeName : employee!.Name.Trim(),
            Profession = profession,
            ExamType = examType,
            ItemNumber = itemNumber,
            Periodicity = onceOnHire
                ? MedPeriodicity.OnceOnHire
                : MedPeriodicity.Normalize(exam.Periodicity),
            Status = exam.Status,
            Conclusion = exam.Conclusion,
            ConclusionScanFileName = exam.ConclusionScanFileName,
            EmploymentDate = employee?.EmploymentDate?.Date,
            ExamDate = lastExam,
            ExpiresOn = expires,
            Traffic = traffic
        };

        return new MedStatusRow
        {
            Id = row.Id,
            Organization = row.Organization,
            Department = row.Department,
            EmployeeName = row.EmployeeName,
            Profession = row.Profession,
            ExamType = row.ExamType,
            ItemNumber = row.ItemNumber,
            Periodicity = row.Periodicity,
            Status = row.Status,
            Conclusion = row.Conclusion,
            ConclusionScanFileName = row.ConclusionScanFileName,
            EmploymentDate = row.EmploymentDate,
            ExamDate = row.ExamDate,
            ExpiresOn = row.ExpiresOn,
            Traffic = row.Traffic,
            DisplayStatus = MedEffectiveStatus.Resolve(
                row,
                MedDirectionMatcher.HasDirection(directionKeys, row),
                reportDate.Date)
        };
    }

    private static MedStatusRow CreatePlaceholderRow(
        string organization,
        OrganizationEmployee employee,
        DateTime reportDate,
        IReadOnlySet<string> directionKeys)
    {
        var row = new MedStatusRow
        {
            Id = string.Empty,
            Organization = organization,
            Department = EmployeeContextValidator.NormalizeDepartment(employee.Department),
            EmployeeName = employee.Name,
            Profession = EmployeeContextValidator.NormalizeProfession(employee.Profession),
            ExamType = "Периодический",
            ItemNumber = string.Empty,
            Periodicity = MedPeriodicity.OnceAYear,
            Status = string.Empty,
            Conclusion = string.Empty,
            EmploymentDate = employee.EmploymentDate?.Date,
            ExamDate = null,
            ExpiresOn = null,
            Traffic = "blue"
        };

        return new MedStatusRow
        {
            Id = row.Id,
            Organization = row.Organization,
            Department = row.Department,
            EmployeeName = row.EmployeeName,
            Profession = row.Profession,
            ExamType = row.ExamType,
            ItemNumber = row.ItemNumber,
            Periodicity = row.Periodicity,
            Status = row.Status,
            Conclusion = row.Conclusion,
            EmploymentDate = row.EmploymentDate,
            ExamDate = row.ExamDate,
            ExpiresOn = row.ExpiresOn,
            Traffic = row.Traffic,
            DisplayStatus = MedEffectiveStatus.Resolve(
                row,
                MedDirectionMatcher.HasDirection(directionKeys, row),
                reportDate.Date)
        };
    }

    private static string NodeId(params string[] parts)
    {
        // Дешёвый стабильный id вместо SHA256 на каждом узле дерева
        var key = string.Join("|", parts.Select(p => (p ?? "").Trim().ToLowerInvariant()));
        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in key)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return hash.ToString("x8");
        }
    }
}
