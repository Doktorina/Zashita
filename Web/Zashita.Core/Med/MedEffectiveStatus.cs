namespace Zashita.Core.Med;

/// <summary>Отображаемый статус фактора: согласован между сводкой, планированием и отчётами.</summary>
public static class MedEffectiveStatus
{
    public static string Resolve(MedStatusRow row, bool hasDirection, DateTime asOf) =>
        Resolve(
            row.Status,
            row.Conclusion,
            row.ExamType,
            row.Periodicity,
            row.ExamDate,
            row.ExpiresOn,
            row.EmploymentDate,
            row.Traffic,
            hasDirection,
            asOf);

    public static string Resolve(
        string? storedStatus,
        string? conclusion,
        string? examType,
        string? periodicity,
        DateTime? examDate,
        DateTime? expiresOn,
        DateTime? employmentDate,
        string? traffic,
        bool hasDirection,
        DateTime asOf)
    {
        if (string.Equals(storedStatus, MedExamStatuses.NotPassed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(conclusion, MedExamStatuses.NotPassed, StringComparison.OrdinalIgnoreCase))
        {
            return MedExamStatuses.NotPassed;
        }

        var hasExam = examDate is { Year: >= 1900 };
        var wasPassed = hasExam &&
                        (string.Equals(storedStatus, MedExamStatuses.Passed, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(conclusion, "Годен", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(conclusion, "Ограниченно годен", StringComparison.OrdinalIgnoreCase));

        if (wasPassed)
        {
            var trafficRow = new MedStatusRow
            {
                Status = storedStatus ?? string.Empty,
                Conclusion = conclusion ?? string.Empty,
                ExamType = examType ?? string.Empty,
                Periodicity = periodicity ?? string.Empty,
                ExamDate = examDate,
                ExpiresOn = expiresOn,
                EmploymentDate = employmentDate,
                Traffic = traffic ?? MedStatusTraffic.Resolve(
                    storedStatus,
                    conclusion,
                    examType,
                    periodicity,
                    examDate,
                    expiresOn,
                    employmentDate,
                    asOf)
            };

            if (MedStatusTraffic.IsOverdue(trafficRow, asOf))
            {
                return MedExamStatuses.Overdue;
            }

            return MedExamStatuses.Passed;
        }

        if (hasDirection)
        {
            return MedExamStatuses.Planned;
        }

        return string.Empty;
    }

    public static bool WasEverPassed(MedStatusRow row)
    {
        if (row.ExamDate is not { Year: >= 1900 })
        {
            return false;
        }

        return string.Equals(row.Status, MedExamStatuses.Passed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(row.Conclusion, "Годен", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(row.Conclusion, "Ограниченно годен", StringComparison.OrdinalIgnoreCase);
    }
}
