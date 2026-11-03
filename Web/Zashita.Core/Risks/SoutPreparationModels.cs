namespace Zashita.Core.Risks;

public sealed class SoutPreparationDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Now;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Department { get; set; } = string.Empty;
    public string ContractorOrganization { get; set; } = string.Empty;
    public bool IsPosted { get; set; }
    public Dictionary<string, string> PrintOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Рабочие места для печати перечня (пусто — все подходящие по фильтру).</summary>
    public List<string> SelectedWorkplaceIds { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Реквизиты организации для шапки «Информация для СОУТ» (из карточки организации).</summary>
public sealed class SoutPreparationOrgHeader
{
    public string Address { get; set; } = string.Empty;
    public string Contact { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
}

public sealed class SoutPreparationPrintRow
{
    public int Index { get; set; }
    public string WorkplaceNumber { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Snils { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    /// <summary>Код по ОК (с рабочего места).</summary>
    public string OkCode { get; set; } = string.Empty;
    /// <summary>Продолжительность рабочего дня/недели (часы).</summary>
    public string WorkWeekHours { get; set; } = "40";
    public string ShiftHours { get; set; } = "8";
    public string TotalWorkers { get; set; } = "1";
    public string WomenCount { get; set; } = "0";
    public string MinorsCount { get; set; } = "0";
    public string DisabledCount { get; set; } = "0";
    public string Equipment { get; set; } = string.Empty;
    public string Materials { get; set; } = string.Empty;
    /// <summary>Время работы на оборудовании (часов в смену).</summary>
    public string EquipmentHours { get; set; } = "6";
    public string HarmfulFactors { get; set; } = string.Empty;
    public string ExtraPayPercent { get; set; } = string.Empty;
    public string Milk { get; set; } = "Нет";
    public string ExtraLeave { get; set; } = string.Empty;
    public string ShortDay { get; set; } = "Нет";
    public string PensionBenefits { get; set; } = string.Empty;
    public string MedicalExam { get; set; } = "да";
    public string PreviousConditionsClass { get; set; } = string.Empty;
    public string AccidentOrDisease { get; set; } = "нет";
    public string WorkDescription { get; set; } = string.Empty;
    public string Sex { get; set; } = string.Empty;
    public string ScheduleFrom { get; set; } = "08:00";
    public string ScheduleTo { get; set; } = "17:00";
    public string LunchBreak { get; set; } = "1";
    public string AnalogousWorkplaces { get; set; } = string.Empty;

    public string HeadcountCell =>
        $"{DashIfEmpty(TotalWorkers, "1")}/{DashIfEmpty(WomenCount, "0")}/{DashIfEmpty(MinorsCount, "0")}/{DashIfEmpty(DisabledCount, "0")}";

    private static string DashIfEmpty(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

public static class SoutPreparationPrintModes
{
    public const string Equipment = "equipment";
    public const string WorkplacesList = "workplaces-list";
    public const string InfoNeeded = "info-needed";

    public static string Title(string? mode = null) => Normalize(mode) switch
    {
        WorkplacesList => "Перечень рабочих мест, подлежащих специальной оценке условий труда",
        InfoNeeded => "Информация, необходимая для проведения СОУТ",
        _ => "Перечень оборудования, инструментов и приспособлений"
    };

    public static string Normalize(string? mode) =>
        string.IsNullOrWhiteSpace(mode)
            ? Equipment
            : mode.Trim().ToLowerInvariant() switch
            {
                "workplaces-list" or "workplaces" or "перечень" or "rm" => WorkplacesList,
                "info-needed" or "info" or "sout-info" or "информация" => InfoNeeded,
                "equipment" or "оборудование" => Equipment,
                _ => Equipment
            };

    public static bool IsLandscape(string? mode) =>
        Normalize(mode) is WorkplacesList or InfoNeeded;
}
