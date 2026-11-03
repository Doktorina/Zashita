namespace Zashita.Core.Suot;

public sealed class SuotIndicatorFact
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string IndicatorId { get; set; } = string.Empty;

    public string Organization { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    public decimal Value { get; set; }

    public string Comment { get; set; } = string.Empty;
}
