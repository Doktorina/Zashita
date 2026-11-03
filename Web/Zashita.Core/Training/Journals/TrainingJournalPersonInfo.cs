namespace Zashita.Core.Training.Journals;

/// <summary>Сотрудник для заполнения строк журнала (ФИО, подразделение, дата рождения).</summary>
public sealed record TrainingJournalPersonInfo(
    string Name,
    string Profession,
    string Department,
    DateTime? BirthDate);
