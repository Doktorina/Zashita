namespace Zashita.Core.Risks;

/// <summary>Карта СОУТ из Word («КАРТА № … специальной оценки условий труда»).</summary>
public sealed class ParsedSoutCard
{
    public string CardNumber { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string AnalogousWorkplaces { get; set; } = string.Empty;
    public string OkCode { get; set; } = string.Empty;
    public int WorkerCount { get; set; }
    public int AnalogousWorkerCount { get; set; }
    public int WorkersWomen { get; set; }
    public int WorkersUnder18 { get; set; }
    public int WorkersDisabled { get; set; }
    public List<string> SnilsList { get; set; } = [];
    public string Equipment { get; set; } = string.Empty;
    public string Materials { get; set; } = string.Empty;
    public string ConditionsClass { get; set; } = string.Empty;
    public DateTime? PreparedDate { get; set; }
    public string Recommendations { get; set; } = string.Empty;
}

public sealed class SoutCardImportDraft
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<SoutCardImportDraftItem> Items { get; set; } = [];
    /// <summary>Подсказки, если в каком-то .docx не распознаны заголовки карт (не сохраняется в JSON).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<string> UnrecognizedHints { get; set; } = [];
}

public sealed class SoutCardImportDraftItem
{
    public string ItemKey { get; set; } = Guid.NewGuid().ToString("N");
    public ParsedSoutCard Card { get; set; } = new();
    public string? MatchedWorkplaceId { get; set; }
    public string MatchedWorkplaceName { get; set; } = string.Empty;
    public string MatchNote { get; set; } = string.Empty;
    public bool Selected { get; set; } = true;
    public bool OverwriteExisting { get; set; }
    public bool HasExistingData { get; set; }
}

public sealed class SoutCardImportApplyResult
{
    public int Applied { get; set; }
    public int Skipped { get; set; }
    public int Created { get; set; }
    public List<string> Messages { get; set; } = [];
}
