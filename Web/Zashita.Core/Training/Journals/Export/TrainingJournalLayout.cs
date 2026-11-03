namespace Zashita.Core.Training.Journals.Export;

/// <summary>Координаты ячеек шаблона журнала (как в Excel-форме 1С).</summary>
public sealed class TrainingJournalLayout
{
    public int OrganizationRowIndex { get; init; } = 14;
    public int PeriodStartRowIndex { get; init; } = 26;
    public int PeriodEndRowIndex { get; init; } = 27;
    public int PeriodDateColumnIndex { get; init; } = 13;
    public int TableHeaderFirstRowIndex { get; init; } = 30;
    public int TableHeaderLastRowIndex { get; init; } = 31;
    public int PageBreakAfterTitleRowIndex { get; init; } = 29;
    public int FirstDataRowIndex { get; init; } = 32;
    public int ColDate { get; init; } = 0;
    public int ColEmployee { get; init; } = 1;
    public int ColBirth { get; init; } = 4;
    public int ColProfession { get; init; } = 5;
    public int ColDepartment { get; init; } = 10;
    public int ColInstructor { get; init; } = 12;
    public int ColSignInstructor { get; init; } = 14;
    public int ColSignEmployee { get; init; } = 15;

    public static TrainingJournalLayout Standard { get; } = new();
}

