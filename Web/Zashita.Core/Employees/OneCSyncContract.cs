using System.Text.RegularExpressions;

namespace Zashita.Core.Employees;

/// <summary>
/// Зафиксированный контракт суточной выгрузки 1С → Защита (v1).
/// Источник — только серверный SharedPath (/srv/share): оба CSV ЗУП сразу.
/// Правила ETL: deploy/1C-ETL-ШТАТ.md и <see cref="OneCStaffEligibility"/>.
/// </summary>
public static partial class OneCSyncContract
{
    /// <summary>Серверный путь по умолчанию (OneC:SharedPath).</summary>
    public const string DefaultSharedPath = "/srv/share";

    /// <summary>Имя локальной копии после обработки (кэш после синка с сервера).</summary>
    public const string WorkersFileName = "workers.xlsx";

    /// <summary>
    /// Актуальные выгрузки двух ЗУП (без даты в имени). Оба файла обязательны при синке,
    /// даже если один обновили позже другого. Полная перезапись штата только из этой пары.
    /// </summary>
    public static readonly string[] CurrentLiveCsvNames =
    [
        "Employees_zup.csv",
        "Employees_zup_anzhela.csv"
    ];

    /// <summary>Штат всегда из двух живых CSV; один файл → синк отменяется.</summary>
    public const string SourceFilesPolicy = "Both Employees_zup.csv + Employees_zup_anzhela.csv required; hard replace";

    /// <summary>Поддерживаемые расширения выгрузки 1С.</summary>
    public static readonly string[] SupportedExtensions = [".xlsx", ".xls", ".csv"];

    /// <summary>Текущий «живой» CSV (Employees_zup.csv / Employees_zup_anzhela.csv).</summary>
    public static bool IsCurrentLiveCsv(string? fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty);
        return CurrentLiveCsvNames.Any(n =>
            string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Архивная копия с датой или суффиксом _prev — не подмешивать в штат.</summary>
    public static bool IsDatedArchiveCsv(string? fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
        if (stem.Length == 0)
        {
            return false;
        }

        if (stem.Contains("_prev", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return DatedArchiveSuffixRegex().IsMatch(stem);
    }

    [GeneratedRegex(@"_\d{4}-\d{2}-\d{2}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DatedArchiveSuffixRegex();

    /// <summary>
    /// Ключ трудового отношения (приоритет идентификации человека):
    /// 1) СНИЛС; 2) ExternalId; 3) ФИО в рамках организации.
    /// Уникальность записи: человек + должность + вид занятости + дата приёма
    /// (один человек может иметь несколько должностей/совместительств в одном юрлице).
    /// </summary>
    public const string EmployeeKeyPolicy = "Snils|Profession|EmploymentType|HireDate per organization";

    /// <summary>1С — источник правды по кадровым полям.</summary>
    public static readonly string[] HrFieldsOverwrittenByOneC =
    [
        "Name",
        "LastName",
        "FirstName",
        "Patronymic",
        "Profession",
        "Department",
        "Snils",
        "BirthDate",
        "EmploymentDate",
        "TransferDate",
        "DismissalDate",
        "EmploymentType",
        "ExternalId",
        "HomeAddress",
        "HrStatus",
        "IsActive"
    ];

    /// <summary>Локальные модули (обучение, медосмотры, риски, СИЗ-выдачи) синкаом не затираются.</summary>
    public const string LocalDataPolicy = "DoNotTouch";

    /// <summary>Сотрудник, исчезнувший из выгрузки, помечается неактивным (не удаляется).</summary>
    public const string MissingEmployeePolicy = "MarkInactive";
}
