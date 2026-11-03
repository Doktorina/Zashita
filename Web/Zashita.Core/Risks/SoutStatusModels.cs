namespace Zashita.Core.Risks;

public sealed class SoutStatusRow
{
    public string WorkplaceId { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string WorkplaceName { get; set; } = string.Empty;
    public string WorkplaceNumber { get; set; } = string.Empty;
    public string MainWorkplaceNumber { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    public DateTime? EmploymentDate { get; set; }
    /// <summary>ФИО закреплённого сотрудника.</summary>
    public string EmployeeName { get; set; } = string.Empty;
    /// <summary>СНИЛС закреплённого сотрудника.</summary>
    public string Snils { get; set; } = string.Empty;
    /// <summary>Код профессии по ОК (с карты / РМ).</summary>
    public string OkCode { get; set; } = string.Empty;
    public string ConditionsClass { get; set; } = string.Empty;
    public string Periodicity { get; set; } = string.Empty;
    public DateTime? ConductDate { get; set; }
    public DateTime? NextDate { get; set; }
    public bool IsIndefinite { get; set; }
    public string NextDateDisplay { get; set; } = "—";
    public string ReportNumber { get; set; } = string.Empty;
    /// <summary>Выбранная карта СОУТ (Id РМ-источника).</summary>
    public string LinkedSoutCardWorkplaceId { get; set; } = string.Empty;
    public bool DeclarationOfConformity { get; set; }
    public bool RequiresUnscheduledSout { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Traffic { get; set; } = string.Empty;
    public int WorkerCount { get; set; }
    public bool IsAnalogous { get; set; }
}

public sealed class SoutStatusTreeLine
{
    public string Id { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public int Level { get; set; }
    public bool IsGroup { get; set; }
    /// <summary>Строка сотрудника — выбор карты СОУТ (как стандарт в обучении).</summary>
    public bool IsPerson { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    /// <summary>Привязанная карта (если у всех РМ человека одна и та же).</summary>
    public string LinkedSoutCardWorkplaceId { get; set; } = string.Empty;
    /// <summary>РМ под этим сотрудником — цели для применения карты.</summary>
    public IReadOnlyList<string> WorkplaceIds { get; set; } = [];
    /// <summary>Ключ сотрудника для ознакомления с картой СОУТ.</summary>
    public string EmployeeKey { get; set; } = string.Empty;
    public bool IsFamiliarized { get; set; }
    public SoutStatusRow? Row { get; set; }
}

/// <summary>Вариант карты СОУТ для выбора в отчёте.</summary>
public sealed class SoutCardPickOption
{
    public string WorkplaceId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    public string ReportNumber { get; set; } = string.Empty;
    public string OkCode { get; set; } = string.Empty;
    public int WorkerCount { get; set; }
    public DateTime? ConductDate { get; set; }
    public string ConditionsClass { get; set; } = string.Empty;
}
