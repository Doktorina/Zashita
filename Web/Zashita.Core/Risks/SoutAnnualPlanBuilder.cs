using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

public static class SoutAnnualPlanBuilder
{
    public static SoutAnnualPlanDocument Build(
        string organization,
        int year,
        string? department,
        IEnumerable<WorkplaceRecord> workplaces,
        IEnumerable<WorkplaceAssignmentDocument> assignments,
        IEnumerable<OrganizationEmployee>? employees = null)
    {
        var statusRows = SoutStatusBuilder.BuildTree(
                workplaces.ToList(),
                assignments.ToList(),
                (employees ?? []).ToList(),
                organization,
                new DateTime(year, 1, 1),
                department)
            .Where(line => !line.IsGroup && line.Row is not null)
            .Select(line => line.Row!)
            .ToList();

        return BuildFromStatusReport(organization, year, department, statusRows);
    }

    /// <summary>
    /// Формирует годовой план по данным отчёта по спецоценке (статус СОУТ).
    /// В план попадают РМ с просроченной / внеплановой СОУТ и те, у кого очередная дата в году плана.
    /// </summary>
    public static SoutAnnualPlanDocument BuildFromStatusReport(
        string organization,
        int year,
        string? department,
        IEnumerable<SoutStatusRow> statusRows)
    {
        var yearStart = new DateTime(year, 1, 1);
        var yearEnd = new DateTime(year, 12, 31);
        var rows = statusRows
            .Where(row => string.IsNullOrWhiteSpace(department) ||
                          string.Equals(row.Department, department, StringComparison.OrdinalIgnoreCase))
            .Where(row => NeedsPlanInYear(row, yearStart, yearEnd))
            .OrderBy(row => row.Department, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.WorkplaceNumber, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.WorkplaceName, StringComparer.CurrentCultureIgnoreCase)
            .Select(row => ToDetailRow(row, year, yearEnd))
            .ToList();

        return new SoutAnnualPlanDocument
        {
            Organization = organization,
            Number = string.Empty,
            Date = DateTime.Now,
            PeriodStart = yearStart,
            PeriodEnd = yearEnd,
            PlanYear = year,
            PlanType = "Офис",
            Scenario = $"план на {year} (из отчёта СОУТ)",
            Department = department?.Trim() ?? string.Empty,
            Status = "В подготовке",
            GeneralRows =
            [
                new SoutAnnualPlanGeneralRow
                {
                    AssessmentType = "Специальная оценка условий труда",
                    Contractor = string.Empty
                }
            ],
            DetailRows = rows
        };
    }

    private static bool NeedsPlanInYear(SoutStatusRow row, DateTime yearStart, DateTime yearEnd)
    {
        if (row.IsIndefinite && !row.RequiresUnscheduledSout)
        {
            return false;
        }

        if (row.RequiresUnscheduledSout)
        {
            return true;
        }

        if (string.Equals(row.Status, "Просрочено", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (row.ConductDate is null)
        {
            return true;
        }

        if (row.NextDate is { } next)
        {
            return next.Date <= yearEnd;
        }

        return true;
    }

    private static SoutAnnualPlanDetailRow ToDetailRow(SoutStatusRow row, int year, DateTime yearEnd)
    {
        DateTime? planned = null;
        if (row.RequiresUnscheduledSout || string.Equals(row.Status, "Просрочено", StringComparison.OrdinalIgnoreCase))
        {
            planned = DateTime.Today.Year == year ? DateTime.Today : new DateTime(year, 1, 31);
        }
        else if (row.NextDate is { } next)
        {
            planned = next.Date > yearEnd ? yearEnd : next.Date;
            if (planned.Value.Year < year)
            {
                planned = new DateTime(year, 1, 31);
            }
        }
        else
        {
            planned = yearEnd;
        }

        var employee = row.EmployeeName?.Trim() ?? string.Empty;
        return new SoutAnnualPlanDetailRow
        {
            Employee = employee,
            PersonName = employee,
            Profession = string.IsNullOrWhiteSpace(row.Profession) ? row.WorkplaceName : row.Profession,
            PreviousDate = row.ConductDate?.Date,
            NextDate = row.NextDate?.Date,
            PlannedDate = planned,
            Contractor = string.Empty,
            WorkplaceId = row.WorkplaceId,
            WorkplaceNumber = row.WorkplaceNumber,
            Department = row.Department
        };
    }
}
