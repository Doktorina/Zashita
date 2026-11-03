using Zashita.Core.Employees;
using Zashita.Core.Training;

namespace Zashita.Core.Electrical;

/// <summary>Строки реестра ЭБ → отчёт по обучению.</summary>
public static class ElectricalTrainingReportBridge
{
    public static IReadOnlyList<TrainingStatusRow> ToTrainingRows(
        IEnumerable<ElectricalKnowledgeCheckRecord> records,
        DateTime reportDate,
        IReadOnlyList<(string Organization, string Name, string Profession, string Department)>? employees = null)
    {
        var today = reportDate.Date;
        var staffList = (employees ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .ToList();
        var staff = staffList
            .GroupBy(e => StaffKey(e.Organization, e.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var staffByName = staffList
            .GroupBy(e => e.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var filterByStaff = staffList.Count > 0;

        var rows = new List<TrainingStatusRow>();
        foreach (var record in records
                     .Where(r => !string.IsNullOrWhiteSpace(r.EmployeeName) &&
                                 !string.IsNullOrWhiteSpace(r.CurrentGroup)))
        {
            (string Organization, string Name, string Profession, string Department)? emp = null;
            if (filterByStaff)
            {
                if (!TryResolveStaff(record, staff, staffByName, out var matched))
                {
                    continue;
                }

                emp = matched;
            }
            else if (staff.TryGetValue(StaffKey(record.Organization, record.EmployeeName), out var direct))
            {
                emp = direct;
            }

            var computed = ElectricalKnowledgeCalculator.Compute(record, today);
            var program = ElectricalKnowledgeCalculator.FormatTrainingProgram(record.CurrentGroup);
            var periodicity = ElectricalKnowledgeCalculator.FormatTrainingPeriodicity(
                computed, record.CurrentGroup);
            var passed = string.Equals(
                record.LastCheckResult?.Trim(),
                ElectricalCheckResults.Passed,
                StringComparison.OrdinalIgnoreCase);
            var result = string.IsNullOrWhiteSpace(record.LastCheckResult)
                ? "Не проводилось"
                : record.LastCheckResult.Trim();
            var trainingDate = record.CurrentGroupAssignedDate?.ToString("dd.MM.yyyy");
            var nextDate = computed.PlannedNextActionDate?.ToString("dd.MM.yyyy");

            string traffic;
            string status;
            if (!passed || !record.CurrentGroupAssignedDate.HasValue)
            {
                traffic = "pink";
                status = "Не пройдено";
            }
            else if (computed.DaysLeft is < 0)
            {
                traffic = "red";
                status = "Просрочено";
            }
            else if (computed.DaysLeft is <= 30)
            {
                traffic = "yellow";
                status = "Скоро истекает";
            }
            else
            {
                traffic = "green";
                status = "Пройдено";
            }

            var department = "—";
            var profession = string.IsNullOrWhiteSpace(record.Position) ? "—" : record.Position.Trim();
            if (emp is { } staffRow)
            {
                if (!string.IsNullOrWhiteSpace(staffRow.Department))
                {
                    department = staffRow.Department.Trim();
                }

                if (!string.IsNullOrWhiteSpace(staffRow.Profession))
                {
                    profession = staffRow.Profession.Trim();
                }
            }

            rows.Add(new TrainingStatusRow(
                record.Organization,
                department,
                profession,
                record.EmployeeName.Trim(),
                program,
                result,
                status,
                periodicity,
                trainingDate,
                nextDate,
                string.Empty,
                traffic,
                null,
                null,
                null,
                record.ResponsibleNote ?? string.Empty));
        }

        return rows;
    }

    private static bool TryResolveStaff(
        ElectricalKnowledgeCheckRecord record,
        IReadOnlyDictionary<string, (string Organization, string Name, string Profession, string Department)> staff,
        IReadOnlyDictionary<string, List<(string Organization, string Name, string Profession, string Department)>> staffByName,
        out (string Organization, string Name, string Profession, string Department) emp)
    {
        var key = StaffKey(record.Organization, record.EmployeeName);
        if (staff.TryGetValue(key, out emp))
        {
            return true;
        }

        var name = record.EmployeeName.Trim();
        if (!staffByName.TryGetValue(name, out var candidates) || candidates.Count == 0)
        {
            emp = default;
            return false;
        }

        var org = (record.Organization ?? string.Empty).Trim();
        var match = candidates.FirstOrDefault(c =>
            string.Equals(c.Organization.Trim(), org, StringComparison.OrdinalIgnoreCase)
            || OrganizationMatcher.Matches(c.Organization, org)
            || OrganizationMatcher.Matches(org, c.Organization));
        if (string.IsNullOrWhiteSpace(match.Name))
        {
            emp = default;
            return false;
        }

        emp = match;
        return true;
    }

    private static string StaffKey(string? organization, string? name) =>
        $"{(organization ?? string.Empty).Trim()}|{(name ?? string.Empty).Trim()}";
}
