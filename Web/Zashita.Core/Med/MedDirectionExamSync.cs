namespace Zashita.Core.Med;

/// <summary>При выдаче направления помечает соответствующие факторы как «Запланирован».</summary>
public static class MedDirectionExamSync
{
    public static void ApplyDirectionIssued(MedDirectionRecord direction, IList<MedExamRecord> exams)
    {
        var org = (direction.Organization ?? string.Empty).Trim();
        var name = MedDirectionMatcher.DirectionFullName(direction);
        if (org.Length == 0 || name.Length == 0)
        {
            return;
        }

        var examType = MedStandardAssignmentBuilder.NormalizeExamType(direction.ExamType);
        var factors = direction.Factors ?? [];
        if (factors.Count == 0)
        {
            MarkPlanned(exams, org, name, examType, string.Empty);
            return;
        }

        foreach (var factor in factors)
        {
            var item = MedItemNumber.Normalize(factor.Code29n);
            MarkPlanned(exams, org, name, examType, item);
        }
    }

    private static void MarkPlanned(
        IList<MedExamRecord> exams,
        string organization,
        string employeeName,
        string examType,
        string itemNumber)
    {
        var key = MedStandardAssignmentBuilder.BuildExamKey(organization, employeeName, examType, itemNumber);
        var exam = exams.FirstOrDefault(e =>
            string.Equals(MedStandardAssignmentBuilder.BuildExamKey(e), key, StringComparison.OrdinalIgnoreCase));
        if (exam is null)
        {
            exams.Add(new MedExamRecord
            {
                Id = MedStandardAssignmentBuilder.StableStandardExamId(organization, employeeName, examType, itemNumber),
                Organization = organization,
                EmployeeName = employeeName,
                ExamType = examType,
                ItemNumber = itemNumber,
                ExamDate = DateTime.MinValue,
                Status = MedExamStatuses.Planned,
                Periodicity = MedStandardAssignmentBuilder.IsPsychiatricKind(examType)
                    ? MedPeriodicity.OnceOnHire
                    : MedPeriodicity.OnceAYear
            });
            return;
        }

        if (MedStandardAssignmentBuilder.HasRecordedProgress(exam))
        {
            return;
        }

        exam.Status = MedExamStatuses.Planned;
        exam.ExamType = examType;
        exam.ItemNumber = itemNumber;
    }
}
