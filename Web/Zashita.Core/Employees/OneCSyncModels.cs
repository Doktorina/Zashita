namespace Zashita.Core.Employees;

/// <summary>Строка расшифровки синка: наём / увольнение / перевод.</summary>
public sealed class OneCSyncStaffChange
{
    public string Kind { get; set; } = "";
    public DateTime? OccurredAt { get; set; }
    public string Organization { get; set; } = "";
    public string FromOrganization { get; set; } = "";
    public string Person { get; set; } = "";
    public string Profession { get; set; } = "";
    public string Message { get; set; } = "";

    public static string KindLabel(string? kind) =>
        (kind ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "hired" or "0" => "Приём",
            "fired" or "2" => "Увольнение",
            "transferred" or "1" => "Перевод",
            _ => string.IsNullOrWhiteSpace(kind) ? "—" : kind.Trim()
        };

    public static OneCSyncStaffChange FromEvent(StaffingChangeEvent e) => new()
    {
        Kind = e.Kind.ToString(),
        OccurredAt = e.OccurredAt,
        Organization = e.Organization?.Trim() ?? string.Empty,
        FromOrganization = e.FromOrganization?.Trim() ?? string.Empty,
        Person = e.Person?.Trim() ?? string.Empty,
        Profession = !string.IsNullOrWhiteSpace(e.NewProfession)
            ? e.NewProfession.Trim()
            : (e.OldProfession?.Trim() ?? string.Empty),
        Message = e.Message?.Trim() ?? string.Empty
    };
}

/// <summary>Результат одного прогона синхронизации 1С.</summary>
public sealed class OneCSyncRunLog
{
    public const int MaxStaffChangesStored = 4000;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public bool Success { get; set; }
    public string Source { get; set; } = "";
    public string FileName { get; set; } = "";
    public int OrganizationsTouched { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int MarkedInactive { get; set; }
    public int Skipped { get; set; }

    /// <summary>Строки CSV, отсеянные: нет ФИО / нет даты приёма / неактивный статус / увольнение / будущий приём.</summary>
    public int FilteredValidation { get; set; }

    /// <summary>Строки, отсеянные фильтром актуальности (увольнение / статус / даты).</summary>
    public int FilteredInactive { get; set; }

    /// <summary>Схлопнутые полные дубли одного места работы.</summary>
    public int Deduplicated { get; set; }

    public List<string> Errors { get; set; } = [];
    public string Message { get; set; } = "";

    /// <summary>Расшифровка: кого приняли / уволили / перевели в этом прогоне.</summary>
    public List<OneCSyncStaffChange> StaffChanges { get; set; } = [];

    public bool StaffChangesTruncated { get; set; }

    public void AttachStaffChanges(IEnumerable<StaffingChangeEvent> events)
    {
        var list = (events ?? []).ToList();
        StaffChangesTruncated = list.Count > MaxStaffChangesStored;
        StaffChanges = list
            .Take(MaxStaffChangesStored)
            .Select(OneCSyncStaffChange.FromEvent)
            .Where(c => !string.IsNullOrWhiteSpace(c.Person))
            .OrderBy(c => c.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Organization, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(c => c.Person, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}

public sealed class OneCSyncLogDocument
{
    public List<OneCSyncRunLog> Runs { get; set; } = [];
}

/// <summary>Нормализованная строка сотрудника из выгрузки 1С.</summary>
public sealed class OneCNormalizedEmployee
{
    public string Organization { get; set; } = "";
    public string Name { get; set; } = "";
    public string Profession { get; set; } = "";
    public string Department { get; set; } = "";
    public string Snils { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public DateTime? BirthDate { get; set; }
    public DateTime? EmploymentDate { get; set; }
}
