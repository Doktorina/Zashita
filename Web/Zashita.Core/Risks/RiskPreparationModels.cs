namespace Zashita.Core.Risks;

public sealed class RiskPreparationDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Now;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Department { get; set; } = string.Empty;
    /// <summary>Организация, проводящая оценку (для перечня РМ).</summary>
    public string AssessingOrganization { get; set; } = string.Empty;
    public bool IsPosted { get; set; }
    /// <summary>Откуда собран график: report / workplaces.</summary>
    public string Scenario { get; set; } = string.Empty;
    public Dictionary<string, string> PrintOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

public static class RiskPreparationPrintModes
{
    public const string Schedule = "schedule";
    public const string ScheduleWeekly = "schedule-weekly";
    public const string Order = "order";
    public const string WorkplacesList = "workplaces-list";

    public static string Title(string mode) => mode switch
    {
        ScheduleWeekly => "График идентификации опасностей и оценки рисков в подразделениях (по неделям)",
        Order => "Приказ о создании комиссии по идентификации опасностей и оценке рисков",
        WorkplacesList => "Перечень рабочих мест, подлежащих оценке профессиональных рисков",
        _ => "График идентификации опасностей и оценки рисков в подразделениях"
    };

    public static string Normalize(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        ScheduleWeekly => ScheduleWeekly,
        Order => Order,
        WorkplacesList or "workplaces" or "перечень" => WorkplacesList,
        _ => Schedule
    };
}
