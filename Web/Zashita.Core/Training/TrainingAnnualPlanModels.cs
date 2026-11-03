namespace Zashita.Core.Training;

public sealed class TrainingAnnualPlanRecord
{
    public string Organization { get; set; } = string.Empty;
    public int Year { get; set; }
    public string ApproverPosition { get; set; } = "Руководитель организации";
    public string ApproverName { get; set; } = string.Empty;
    public List<TrainingAnnualPlanRowData> GeneralRows { get; set; } = [];
    public List<TrainingAnnualPlanRowData> ProfessionRows { get; set; } = [];
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

public sealed class TrainingAnnualPlanRowData
{
    public string Direction { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Periodicity { get; set; } = string.Empty;
    public int HeadCount { get; set; } = 1;
    public string Provider { get; set; } = string.Empty;
    public List<int> PlannedMonths { get; set; } = [];
}
