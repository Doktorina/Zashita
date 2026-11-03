namespace Zashita.Core.Med;

/// <summary>Заключение медосмотра из Word.</summary>
public sealed class ParsedMedConclusion
{
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime? BirthDate { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    public List<string> ItemNumbers { get; set; } = [];
    public string ResultText { get; set; } = string.Empty;
    public string HealthGroup { get; set; } = string.Empty;
    public string Conclusion { get; set; } = string.Empty;
    public string ExamType { get; set; } = "Периодический";
    public DateTime? ConclusionDate { get; set; }
    public string SourceFileName { get; set; } = string.Empty;
}

public sealed class MedConclusionImportDraft
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<MedConclusionImportDraftItem> Items { get; set; } = [];
}

public sealed class MedConclusionImportDraftItem
{
    public string ItemKey { get; set; } = Guid.NewGuid().ToString("N");
    public ParsedMedConclusion Conclusion { get; set; } = new();
    public string? MatchedEmployeeName { get; set; }
    public string MatchNote { get; set; } = string.Empty;
    public bool Selected { get; set; } = true;
    public bool OverwriteExisting { get; set; }
    public bool HasExistingData { get; set; }
    public int MatchedExamCount { get; set; }
}

public sealed class MedConclusionImportApplyResult
{
    public int Applied { get; set; }
    public int Created { get; set; }
    public int Skipped { get; set; }
    public List<string> Messages { get; set; } = [];
}
