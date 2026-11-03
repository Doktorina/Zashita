namespace Zashita.Core.Ppe;

public sealed class PpeActRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Today;
    public string Department { get; set; } = string.Empty;
    public string Employee { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string ApproverPosition { get; set; } = string.Empty;
    public string ApproverName { get; set; } = string.Empty;
    public string CommissionHead { get; set; } = string.Empty;
    public string CommissionHeadPosition { get; set; } = string.Empty;
    public List<PpeActCommissionMember> CommissionMembers { get; set; } = [];
    public string OrderNumber { get; set; } = string.Empty;
    public string SuitabilityDecision { get; set; } = "не пригодны к дальнейшей эксплуатации (утилизация)";
    public List<PpeActLine> Lines { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

public sealed class PpeActCommissionMember
{
    public string Position { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class PpeActLine
{
    public string Name { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1;
    public DateTime IssueDate { get; set; } = DateTime.Today;
    public int WearPercent { get; set; } = 0;
    public string Conclusion { get; set; } = "пригодны к дальнейшей эксплуатации (химчистка)";
}
