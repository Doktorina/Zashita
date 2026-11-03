namespace Zashita.Core.Training;

public enum TrainingSimpleTreeKind
{
    Topics,
    Tests,
    Questions,
    WorkTypes
}

public static class TrainingSimpleTreeKindLabels
{
    public static string Title(TrainingSimpleTreeKind kind) => kind switch
    {
        TrainingSimpleTreeKind.Topics => "Темы",
        TrainingSimpleTreeKind.Tests => "Тесты",
        TrainingSimpleTreeKind.Questions => "Вопросы",
        TrainingSimpleTreeKind.WorkTypes => "Виды работ",
        _ => "Справочник"
    };

    public static TrainingSimpleTreeKind? TryParse(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "topics" or "темы" => TrainingSimpleTreeKind.Topics,
            "tests" or "тесты" => TrainingSimpleTreeKind.Tests,
            "questions" or "вопросы" => TrainingSimpleTreeKind.Questions,
            "work-types" or "worktypes" or "виды-работ" => TrainingSimpleTreeKind.WorkTypes,
            _ => null
        };
}

public sealed record TrainingStandard(
    string Id,
    string Name,
    string Organization,
    string Profession,
    List<string> Items,
    DateTime CreatedAt,
    List<string>? Workers = null,
    string? Category = null,
    List<string>? Professions = null,
    string? PersonnelCategory = null,
    string? TrainingMethod = null,
    Dictionary<string, string>? ProgramPeriodicities = null,
    int Version = 1,
    DateTime? EffectiveFrom = null)
{
    public string TrainingCategory => string.IsNullOrWhiteSpace(Category) ? "Обучение" : Category.Trim();

    public IReadOnlyList<string> AllProfessions
    {
        get
        {
            if (Professions is { Count: > 0 })
            {
                return Professions
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return string.IsNullOrWhiteSpace(Profession) ? [] : [Profession.Trim()];
        }
    }

    public string ListDisplayName
    {
        get
        {
            var tag = !string.IsNullOrWhiteSpace(PersonnelCategory)
                ? PersonnelCategory.Trim()
                : TrainingCategory;
            return $"{Name} [{tag}]";
        }
    }

    public string PeriodicityFor(string programName, IReadOnlyDictionary<string, string>? kindLookup = null) =>
        TrainingProgramCatalog.ResolvePeriodicity(programName, kindLookup, ProgramPeriodicities);
}

public sealed class TrainingKindNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? ParentId { get; set; }
    public bool IsGroup { get; set; }
    public bool MarkedForDeletion { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Type { get; set; } = "Обучение";
    public string Periodicity { get; set; } = string.Empty;
    public string Section { get; set; } = "Охрана труда";
    public string PersonnelCategory { get; set; } = string.Empty;
}

public sealed class TrainingProgramCourseNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? ParentId { get; set; }
    public bool IsGroup { get; set; }
    public bool MarkedForDeletion { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Section { get; set; } = "Охрана труда";
    public DateTime? ApprovalDate { get; set; }
    public decimal TotalHours { get; set; }
}

public sealed class TrainingPlanTypeNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? ParentId { get; set; }
    public bool IsGroup { get; set; }
    public bool MarkedForDeletion { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? RouteUrl { get; set; }
}

public sealed class TrainingSimpleTreeNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? ParentId { get; set; }
    public bool IsGroup { get; set; }
    public bool MarkedForDeletion { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public decimal? Hours { get; set; }
    public DateTime? ApprovedDate { get; set; }
    public string? Description { get; set; }
}

public sealed record TrainingReportEntry(string Title, string Description, string Section, int Column = 0);

public sealed record TrainingStatusRow(
    string Organization,
    string Department,
    string Profession,
    string Person,
    string Program,
    string Result,
    string Status,
    string Periodicity,
    string? TrainingDate,
    string? NextTrainingDate,
    string CertificateNumber,
    string Traffic,
    string? ConductId = null,
    string? StandardId = null,
    string? StandardName = null,
    string Comment = "",
    string ProtocolMovement = "",
    string UpdStatus = "",
    string ObjectNote = "",
    Dictionary<string, string>? CellStyles = null);

public sealed record TrainingQualificationRow(
    int Number,
    string Organization,
    string Department,
    string Profession,
    string TrainingType,
    string Program,
    string Periodicity,
    string TemplateName);

public sealed class TrainingConductRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    public string Person { get; set; } = string.Empty;
    public string Program { get; set; } = string.Empty;
    public string TrainingType { get; set; } = "Обучение";
    public string Result { get; set; } = "Сдал";
    public DateTime? TrainingDate { get; set; }
    public DateTime? NextTrainingDate { get; set; }
    public string ProtocolNumber { get; set; } = string.Empty;
    public string CertificateNumber { get; set; } = string.Empty;
    /// <summary>готов | на подписи | —</summary>
    public string ProtocolMovement { get; set; } = string.Empty;
    /// <summary>УПД есть | УПД нет | —</summary>
    public string UpdStatus { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    /// <summary>Произвольная заметка по объекту / месту проведения.</summary>
    public string ObjectNote { get; set; } = string.Empty;

    /// <summary>Стили ячеек отчёта: program | comment | object → CSS (color/font-weight).</summary>
    public Dictionary<string, string> CellStyles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Снимок проведения по прежней должности (перевод); не показывается в активном отчёте.</summary>
    public bool IsArchivedSnapshot { get; set; }

    /// <summary>
    /// Переопределение периодичности в отчёте (для «Проф обучение» и аналогов).
    /// Пусто — берём из вида обучения / стандарта.
    /// </summary>
    public string Periodicity { get; set; } = string.Empty;
}
