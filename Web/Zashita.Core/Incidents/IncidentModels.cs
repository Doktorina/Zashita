namespace Zashita.Core.Incidents;

public sealed record IncidentHistoryEntry(DateTime Timestamp, string Action, string Summary);

public sealed class WorkplaceAccidentRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Posted { get; set; }
    public DateTime DocumentDate { get; set; } = DateTime.Today;
    public string Number { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Place { get; set; } = string.Empty;
    public string IncidentType { get; set; } = "Несчастный случай";
    public string IncidentNature { get; set; } = string.Empty;
    public string Victims { get; set; } = string.Empty;
    public DateTime? IncidentDateTime { get; set; }
    public string Circumstances { get; set; } = string.Empty;
    public string MeasuresTaken { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public List<IncidentHistoryEntry> History { get; set; } = [];
}

public sealed class AccidentInvestigationRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Posted { get; set; }
    public DateTime DocumentDate { get; set; } = DateTime.Today;
    public string Number { get; set; } = string.Empty;
    public string? SourceAccidentId { get; set; }
    public string SourceAccidentNumber { get; set; } = string.Empty;
    public DateTime? AccidentDateTime { get; set; }
    public string Victims { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string Circumstances { get; set; } = string.Empty;
    public string Conclusion { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public List<IncidentHistoryEntry> History { get; set; } = [];
}

public sealed class MicrotraumaRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Posted { get; set; }
    public DateTime DocumentDate { get; set; } = DateTime.Today;
    public string Number { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Victims { get; set; } = string.Empty;
    public string BodyPart { get; set; } = string.Empty;
    public string InjuryNature { get; set; } = string.Empty;
    public string Place { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? EventDateTime { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public List<IncidentHistoryEntry> History { get; set; } = [];
}

public sealed class MicrotraumaInvestigationRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Posted { get; set; }
    public DateTime DocumentDate { get; set; } = DateTime.Today;
    public string Number { get; set; } = string.Empty;
    public string? SourceMicrotraumaId { get; set; }
    public string SourceMicrotraumaNumber { get; set; } = string.Empty;
    public DateTime? EventDateTime { get; set; }
    public string Victims { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string Circumstances { get; set; } = string.Empty;
    public string Conclusion { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public List<IncidentHistoryEntry> History { get; set; } = [];
}

public enum IncidentCatalogKind
{
    IncidentTypes,
    OccupationGroups,
    InjuryTypes,
    BodyParts,
    AccidentConsequences,
    IncidentCauses,
    IncidentNature
}

public sealed record IncidentCatalogItem(string Id, string Kind, string Code, string Name);

public sealed class IncidentHubStats
{
    public int WorkplaceCount { get; init; }
    public int InvestigationCount { get; init; }
    public int MicrotraumaCount { get; init; }
    public int MicrotraumaInvestigationCount { get; init; }
    public int DaysWithoutAccident { get; init; }
}
