namespace Zashita.Core.Risks;

/// <summary>
/// Объект (площадка / филиал) оценки рисков внутри юрлица.
/// Состав карт на объекте — свой (WorkplaceIds); данные оценки — в ObjectAssessments.
/// </summary>
public sealed class RiskAssessmentObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Полное официальное название объекта, как в договоре.</summary>
    public string OfficialName { get; set; } = string.Empty;
    /// <summary>
    /// Системная вкладка «Оценка рисков по организации» — хранит уже заполненные
    /// карты без привязки к объекту (поля Risk* на WorkplaceRecord).
    /// </summary>
    public bool IsOrganizationWide { get; set; }
    /// <summary>
    /// Id карт (листьев), видимых на этом объекте.
    /// При создании объекта — снимок состава «по организации».
    /// Удаление карты на объекте убирает id отсюда, не трогая саму карту.
    /// </summary>
    public List<string> WorkplaceIds { get; set; } = [];
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Данные карты оценки рисков для конкретного объекта (не org-wide).</summary>
public sealed class WorkplaceRiskAssessmentData
{
    public DateTime? RiskAssessmentDate { get; set; }
    public string RiskCardNumber { get; set; } = string.Empty;
    public string OverallRisk { get; set; } = string.Empty;
    public string RiskPeriodicity { get; set; } = string.Empty;
    public bool RiskAssessmentNotConducted { get; set; }
    public List<WorkplaceRiskLine> RiskLines { get; set; } = [];
    public string RiskTrafficMark { get; set; } = string.Empty;
    public List<RiskCardFamiliarization> RiskFamiliarizations { get; set; } = [];
}

public static class RiskAssessmentObjectIds
{
    /// <summary>Фиксированный id вкладки «Оценка рисков по организации».</summary>
    public const string OrganizationWide = "org-wide";

    public const string OrganizationWideName = "Оценка рисков по организации";
}
