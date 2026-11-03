namespace Zashita.Core.Risks;

public sealed class RiskUnscheduledOrderDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Now;
    public string Department { get; set; } = string.Empty;
    public string CommissionId { get; set; } = string.Empty;
    public bool IsPosted { get; set; }
    public Dictionary<string, string> PrintOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

public static class RiskUnscheduledOrderPrintModes
{
    public const string Order = "order";

    public static string Title(string mode) => mode switch
    {
        _ => "Приказ на оценку рисков"
    };

    public static string Normalize(string? mode) => Order;
}
