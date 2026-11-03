namespace Zashita.Core.Training.Journals;

public sealed record TrainingJournalColumn(string Key, string Title, bool IsDate, bool ReadOnly = false);

public static class TrainingJournalColumns
{
    public static IReadOnlyList<TrainingJournalColumn> GetColumns(TrainingJournalKind kind) =>
        kind switch
        {
            TrainingJournalKind.IntroductoryOccupationalSafety => IntroductoryOt(),
            TrainingJournalKind.WorkplaceOccupationalSafety => OccupationalSafety(),
            TrainingJournalKind.IntroductoryOccupationalSafetyContractors => ContractorOt(),
            TrainingJournalKind.IntroductoryFireSafety => FireIntro(),
            TrainingJournalKind.IntroductoryFireSafetyContractors => Fire(contractors: true),
            TrainingJournalKind.WorkplaceFireSafety => Fire(contractors: false),
            TrainingJournalKind.ElectroSafetyGroupI => Electro(),
            _ => OccupationalSafety()
        };

    public static IReadOnlyList<TrainingJournalColumn> GetColumns(TrainingJournalTableKind tableKind) =>
        tableKind switch
        {
            TrainingJournalTableKind.OccupationalSafetyIntro9 => IntroductoryOt(),
            TrainingJournalTableKind.ContractorOccupationalSafety9 => ContractorOt(),
            TrainingJournalTableKind.FireSafety11 => Fire(contractors: false),
            TrainingJournalTableKind.FireSafetyContractors12 => Fire(contractors: true),
            TrainingJournalTableKind.ElectroGroupI7 => Electro(),
            _ => OccupationalSafety()
        };

    public static string GetPrimaryDateColumnKey(TrainingJournalTableKind tableKind) =>
        tableKind switch
        {
            TrainingJournalTableKind.ElectroGroupI7 => "briefingDate",
            _ => "briefingDate"
        };

    public static string GetCellValue(TrainingJournalEntry entry, string key) =>
        key switch
        {
            "rowNumber" => entry.RowNumber.ToString(),
            "briefingDate" => FormatDisplayDate(entry.BriefingDate),
            "employeeName" => entry.EmployeeName,
            "birthDate" => FormatDisplayDate(entry.BirthDate),
            "profession" => entry.Profession,
            "department" => entry.Department,
            "workplace" => entry.Workplace,
            "instructorName" or "practiceInstructor" => FormatInstructor(entry),
            "briefingType" => entry.BriefingType,
            "briefingReason" => entry.BriefingReason,
            "localActName" => string.IsNullOrWhiteSpace(entry.LocalActName) ? entry.Workplace : entry.LocalActName,
            "legalEntity" => entry.LegalEntity,
            "practiceDate" => FormatDisplayDate(entry.PracticeDate ?? entry.TheoryDate ?? entry.BriefingDate),
            "previousAssignmentDate" => FormatDisplayDate(entry.PreviousAssignmentDate),
            "signInstructor" or "signEmployee" or "signTheoryEmployee" or "signTheoryInstructor"
                or "signPracticeEmployee" or "signPracticeInstructor" or "signExaminee" or "signExaminer" => "",
            _ => ""
        };

    public static TrainingJournalEntry ApplyCellValue(TrainingJournalEntry entry, string key, string? value)
    {
        value ??= string.Empty;
        return key switch
        {
            "rowNumber" when int.TryParse(value, out var n) => entry with { RowNumber = n },
            "briefingDate" when TryParseInputDate(value, out var d) => entry with { BriefingDate = d },
            "employeeName" => entry with { EmployeeName = value },
            "birthDate" when TryParseInputDate(value, out var bd) => entry with { BirthDate = bd },
            "profession" => entry with { Profession = value },
            "department" => entry with { Department = value },
            "workplace" => entry with { Workplace = value },
            "instructorName" or "practiceInstructor" => entry with { InstructorName = value },
            "briefingType" => entry with { BriefingType = value },
            "briefingReason" => entry with { BriefingReason = value },
            "localActName" => entry with { LocalActName = value },
            "legalEntity" => entry with { LegalEntity = value },
            "practiceDate" when TryParseInputDate(value, out var pd) => entry with { PracticeDate = pd },
            "previousAssignmentDate" when TryParseInputDate(value, out var prev) => entry with { PreviousAssignmentDate = prev },
            _ => entry
        };
    }

    public static string FormatDisplayDate(DateTime? date) =>
        date.HasValue ? date.Value.ToString("dd.MM.yyyy") : string.Empty;

    public static string FormatDisplayDate(DateTime date) => date.ToString("dd.MM.yyyy");

    private static string FormatInstructor(TrainingJournalEntry entry) =>
        string.IsNullOrWhiteSpace(entry.InstructorQualification)
            ? entry.InstructorName
            : $"{entry.InstructorName}, {entry.InstructorQualification}";

    private static string? FormatInputDate(DateTime? date) =>
        date.HasValue ? date.Value.ToString("yyyy-MM-dd") : null;

    private static bool TryParseInputDate(string? value, out DateTime date)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            date = default;
            return false;
        }

        if (DateTime.TryParse(value, out date))
        {
            date = date.Date;
            return true;
        }

        if (int.TryParse(value, out var year) && value.Trim().Length <= 4)
        {
            date = new DateTime(year, 1, 1);
            return true;
        }

        date = default;
        return false;
    }

    private static IReadOnlyList<TrainingJournalColumn> IntroductoryOt() =>
    [
        Col("rowNumber", "№ п/п"),
        Col("briefingDate", "Дата проведения вводного инструктажа", isDate: true),
        Col("employeeName", "ФИО работника, прошедшего инструктаж"),
        Col("profession", "Профессия (должность)"),
        Col("birthDate", "Дата рождения", isDate: true),
        Col("department", "Подразделение"),
        Col("instructorName", "ФИО и должность инструктирующего"),
        Col("signInstructor", "Подпись инструктирующего", readOnly: true),
        Col("signEmployee", "Подпись инструктируемого", readOnly: true)
    ];

    private static IReadOnlyList<TrainingJournalColumn> OccupationalSafety() =>
    [
        Col("rowNumber", "№ п/п"),
        Col("briefingDate", "Дата проведения инструктажа", isDate: true),
        Col("employeeName", "ФИО работника, прошедшего инструктаж"),
        Col("birthDate", "Год рождения", isDate: true),
        Col("profession", "Профессия (должность)"),
        Col("department", "Подразделение"),
        Col("briefingType", "Вид инструктажа"),
        Col("briefingReason", "Причины (внеплановый/целевой)"),
        Col("instructorName", "ФИО и должность инструктирующего"),
        Col("localActName", "Локальный акт"),
        Col("signInstructor", "Подпись инструктирующего", readOnly: true),
        Col("signEmployee", "Подпись инструктируемого", readOnly: true)
    ];

    private static IReadOnlyList<TrainingJournalColumn> ContractorOt() =>
    [
        Col("rowNumber", "№ п/п"),
        Col("briefingDate", "Дата проведения вводного инструктажа", isDate: true),
        Col("employeeName", "ФИО работника, прошедшего инструктаж"),
        Col("profession", "Профессия (должность)"),
        Col("birthDate", "Дата рождения", isDate: true),
        Col("legalEntity", "Юридическое лицо (подрядчик)"),
        Col("department", "Подразделение"),
        Col("instructorName", "ФИО и должность инструктирующего"),
        Col("signInstructor", "Подпись инструктирующего", readOnly: true),
        Col("signEmployee", "Подпись инструктируемого", readOnly: true)
    ];

    private static IReadOnlyList<TrainingJournalColumn> Electro() =>
    [
        Col("rowNumber", "№ п/п"),
        Col("employeeName", "Ф.И.О."),
        Col("department", "Подразделение"),
        Col("profession", "Должность (профессия)"),
        Col("previousAssignmentDate", "Дата предыдущего присвоения", isDate: true),
        Col("briefingDate", "Дата присвоения", isDate: true),
        Col("signExaminee", "Подпись проверяемого", readOnly: true),
        Col("signExaminer", "Подпись проверяющего", readOnly: true)
    ];

    private static IReadOnlyList<TrainingJournalColumn> FireIntro() =>
    [
        Col("rowNumber", "№ п/п"),
        Col("briefingDate", "Дата", isDate: true),
        Col("briefingType", "Вид инструктажа"),
        Col("employeeName", "ФИО инструктируемого"),
        Col("birthDate", "Дата рождения", isDate: true),
        Col("profession", "Профессия, должность"),
        Col("instructorName", "ФИО инструктирующего"),
        Col("signTheoryEmployee", "Подпись инструктируемого", readOnly: true),
        Col("signTheoryInstructor", "Подпись инструктирующего", readOnly: true)
    ];

    private static IReadOnlyList<TrainingJournalColumn> Fire(bool contractors)
    {
        if (contractors)
        {
            return
            [
                Col("rowNumber", "№ п/п"),
                Col("briefingDate", "Дата", isDate: true),
                Col("briefingType", "Вид инструктажа"),
                Col("legalEntity", "Наименование подрядной организации"),
                Col("employeeName", "ФИО инструктируемого"),
                Col("birthDate", "Дата рождения", isDate: true),
                Col("profession", "Профессия, должность"),
                Col("instructorName", "ФИО инструктирующего"),
                Col("signTheoryEmployee", "Подпись инструктируемого", readOnly: true),
                Col("signTheoryInstructor", "Подпись инструктирующего", readOnly: true)
            ];
        }

        return
        [
            Col("rowNumber", "№ п/п"),
            Col("briefingDate", "Дата", isDate: true),
            Col("briefingType", "Вид инструктажа"),
            Col("employeeName", "ФИО инструктируемого"),
            Col("birthDate", "Дата рождения", isDate: true),
            Col("profession", "Профессия (должность)"),
            Col("instructorName", "Теория: инструктирующий"),
            Col("signTheoryEmployee", "Теория: подпись инструктируемого", readOnly: true),
            Col("signTheoryInstructor", "Теория: подпись инструктирующего", readOnly: true),
            Col("practiceDate", "Дата практики", isDate: true),
            Col("practiceInstructor", "Практика: инструктирующий"),
            Col("signPracticeEmployee", "Практика: подпись инструктируемого", readOnly: true),
            Col("signPracticeInstructor", "Практика: подпись инструктирующего", readOnly: true)
        ];
    }

    private static TrainingJournalColumn Col(string key, string title, bool isDate = false, bool readOnly = false) =>
        new(key, title, isDate, readOnly);
}
