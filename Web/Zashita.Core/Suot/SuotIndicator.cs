namespace Zashita.Core.Suot;

public sealed class SuotIndicator
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string ShortName { get; set; } = string.Empty;

    public string? ParentId { get; set; }

    public bool IsGroup { get; set; }

    public string IndicatorType { get; set; } = "Количественный";

    public string Unit { get; set; } = "шт";

    public string ActivityDirection { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string FormulaDescription { get; set; } = string.Empty;

    public string Formula { get; set; } = string.Empty;
}
