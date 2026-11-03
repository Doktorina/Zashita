namespace Zashita.Core.Risks;

/// <summary>Карта из Word (один блок «Карта … № …»).</summary>
public sealed class ParsedRiskCard
{
    public string CardNumber { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    public string ProfessionRaw { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public DateTime? AssessmentDate { get; set; }
    public string OverallRisk { get; set; } = string.Empty;
    public string CompiledBy { get; set; } = string.Empty;
    public List<string> WorkingGroupMembers { get; set; } = [];
    public List<string> AcquaintedNames { get; set; } = [];
    public List<ParsedRiskCardLine> Lines { get; set; } = [];
}

public sealed class ParsedRiskCardLine
{
    public string HazardNumber { get; set; } = string.Empty;
    public string HazardTitle { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Probability { get; set; } = string.Empty;
    public string InitialRisk { get; set; } = string.Empty;
    public string ControlMeasures { get; set; } = string.Empty;
    public string ResidualRisk { get; set; } = string.Empty;
}

/// <summary>Черновик импорта (превью → применение).</summary>
public sealed class RiskCardImportDraft
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<RiskCardImportDraftItem> Items { get; set; } = [];
}

public sealed class RiskCardImportDraftItem
{
    public string ItemKey { get; set; } = Guid.NewGuid().ToString("N");
    public ParsedRiskCard Card { get; set; } = new();
    public string? MatchedWorkplaceId { get; set; }
    public string MatchedWorkplaceName { get; set; } = string.Empty;
    public string MatchNote { get; set; } = string.Empty;
    public bool Selected { get; set; } = true;
    public bool OverwriteExisting { get; set; }
    public int ExistingLineCount { get; set; }
}

public sealed class RiskCardImportApplyResult
{
    public int Applied { get; set; }
    public int Skipped { get; set; }
    public int Created { get; set; }
    public List<string> Messages { get; set; } = [];
}
