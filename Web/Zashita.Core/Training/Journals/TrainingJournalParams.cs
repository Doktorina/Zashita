namespace Zashita.Core.Training.Journals;

public sealed record TrainingJournalParams(
    DateTime DateFrom,
    DateTime DateTo,
    string? Organization,
    string? Department,
    string? Workplace,
    bool StrictDepartmentFilter,
    bool ShowTitlePage,
    string? BriefingType = null,
    string? DefaultInstructor = null,
    string? LocalActName = null,
    int StartRowNumber = 1,
    int EmptyRowCount = 0,
    /// <summary>Дата «Начат» на титульном листе (если null — берётся DateFrom).</summary>
    DateTime? TitleStarted = null,
    /// <summary>Дата «Окончен» на титульном листе (если null — берётся DateTo).</summary>
    DateTime? TitleEnded = null,
    /// <summary>Дописать пустые строки до конца страницы (~18 строк таблицы).</summary>
    bool PadEmptyRowsToPageEnd = false);
