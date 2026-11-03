namespace Zashita.Core.Training.Journals;

public sealed record TrainingJournalEntry(
    int RowNumber,
    DateTime BriefingDate,
    string EmployeeName,
    DateTime? BirthDate,
    string Profession,
    string Workplace,
    string Department,
    string InstructorName,
    string Organization,
    string BriefingType = "",
    string BriefingReason = "",
    string LocalActName = "",
    string LegalEntity = "",
    string InstructorQualification = "",
    DateTime? TheoryDate = null,
    DateTime? PracticeDate = null,
    DateTime? PreviousAssignmentDate = null);
