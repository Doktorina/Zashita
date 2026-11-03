namespace Zashita.Core.Compliance;

public sealed class InspectionActRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool IsPosted { get; set; }
    public string Organization { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Today;
    public string ConstructionObject { get; set; } = string.Empty;
    public string Contractor { get; set; } = string.Empty;
    public string RepresentativeTitle { get; set; } = string.Empty;
    public string RepresentativeName { get; set; } = string.Empty;
    public string InspectorTitle { get; set; } = "Специалист службы охраны труда";
    public string InspectorName { get; set; } = string.Empty;
    public int PhotoCount { get; set; }
    public List<InspectionActViolationLine> Violations { get; set; } = [];
    public Dictionary<string, string> PrintOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

public sealed class InspectionActViolationLine
{
    public string Description { get; set; } = string.Empty;
    public string Regulations { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime? Deadline { get; set; }
}
