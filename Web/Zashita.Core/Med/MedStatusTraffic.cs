namespace Zashita.Core.Med;

/// <summary>Светофор строки отчёта по медосмотрам (согласован с фильтром «Просрочено»).</summary>
public static class MedStatusTraffic
{
    public static string Resolve(
        string? status,
        string? conclusion,
        string? examType,
        string? periodicity,
        DateTime? examDate,
        DateTime? expiresOn,
        DateTime? employmentDate,
        DateTime asOf)
    {
        if (string.Equals(conclusion, "Не пройден", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Не пройден", StringComparison.OrdinalIgnoreCase))
        {
            return "red";
        }

        var onceOnHire = MedPeriodicity.IsOnceOnHire(periodicity) ||
                         MedStandardAssignmentBuilder.IsPsychiatricKind(examType);
        if (onceOnHire)
        {
            if (examDate is { Year: >= 1900 } &&
                (string.Equals(status, "Пройден", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(conclusion, "Годен", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(conclusion, "Ограниченно годен", StringComparison.OrdinalIgnoreCase)))
            {
                return "green";
            }

            var due = employmentDate?.Date ?? asOf.Date;
            return due <= asOf.Date ? "red" : "blue";
        }

        var hasLastExam = examDate is { Year: >= 1900 };
        if (expiresOn is null && hasLastExam)
        {
            expiresOn = MedPeriodicity.NextExamDate(examDate!.Value, periodicity);
        }

        if (string.Equals(status, "Запланирован", StringComparison.OrdinalIgnoreCase) && !hasLastExam)
        {
            var due = employmentDate?.Date ?? asOf.Date;
            return due <= asOf.Date ? "red" : "blue";
        }

        if (expiresOn is null)
        {
            return string.Equals(status, "Пройден", StringComparison.OrdinalIgnoreCase) ? "green" : "blue";
        }

        if (expiresOn.Value.Date < asOf.Date)
        {
            return "red";
        }

        if (expiresOn.Value.Date <= asOf.Date.AddDays(30))
        {
            return "yellow";
        }

        return "green";
    }

    public static string Resolve(MedExamRecord exam, DateTime? expiresOn, DateTime asOf, DateTime? employmentDate = null) =>
        Resolve(
            exam.Status,
            exam.Conclusion,
            exam.ExamType,
            exam.Periodicity,
            exam.ExamDate.Year >= 1900 ? exam.ExamDate.Date : null,
            expiresOn,
            employmentDate,
            asOf.Date);

    public static string Resolve(MedStatusRow row, DateTime asOf) =>
        Resolve(
            row.Status,
            row.Conclusion,
            row.ExamType,
            row.Periodicity,
            row.ExamDate,
            row.ExpiresOn,
            row.EmploymentDate,
            asOf.Date);

    public static bool IsOverdue(MedStatusRow row, DateTime asOf) =>
        string.Equals(Resolve(row, asOf), "red", StringComparison.OrdinalIgnoreCase);
}
