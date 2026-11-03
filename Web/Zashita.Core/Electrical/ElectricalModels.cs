namespace Zashita.Core.Electrical;

/// <summary>Запись реестра «Проверка знаний ЭБ».</summary>
public sealed class ElectricalKnowledgeCheckRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    /// <summary>СНИЛС — ключ человека для синка ЭБ между компаниями (не ФИО).</summary>
    public string Snils { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    /// <summary>Один из <see cref="ElectricalEducationLevels"/>.</summary>
    public string Education { get; set; } = string.Empty;
    /// <summary>I / II / III / IV / V.</summary>
    public string CurrentGroup { get; set; } = string.Empty;
    public DateTime? CurrentGroupAssignedDate { get; set; }
    /// <summary>«сдано» / «не сдано».</summary>
    public string LastCheckResult { get; set; } = string.Empty;
    /// <summary>Периодичность проверки знаний: 12 или 36 мес. (для любой группы).</summary>
    public int? PeriodicityMonths { get; set; }
    public string ResponsibleNote { get; set; } = string.Empty;
    /// <summary>Добавлен вручную из штата (не удалять при очистке пустых автострок).</summary>
    public bool AddedManually { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

public static class ElectricalEducationLevels
{
    public const string BasicGeneral = "Основное общее образование";
    public const string SecondaryGeneral = "Среднее общее образование";
    public const string SecondaryVocationalOrHigherTechnical =
        "Среднее профессиональное / высшее (техническое) образование";
    public const string HigherElectricPower =
        "Высшее (техническое) образование в области электроэнергетики";

    public static readonly IReadOnlyList<string> All =
    [
        BasicGeneral,
        SecondaryGeneral,
        SecondaryVocationalOrHigherTechnical,
        HigherElectricPower
    ];
}

public static class ElectricalGroups
{
    public const string I = "I";
    public const string II = "II";
    public const string III = "III";
    public const string IV = "IV";
    public const string V = "V";

    public static readonly IReadOnlyList<string> All = [I, II, III, IV, V];

    /// <summary>IV и V — периодическая проверка (12/36 мес.), без «повышения группы».</summary>
    public static bool IsPeriodicCheckGroup(string? group) =>
        string.Equals(group, IV, StringComparison.OrdinalIgnoreCase)
        || string.Equals(group, V, StringComparison.OrdinalIgnoreCase);
}

public static class ElectricalCheckResults
{
    public const string Passed = "сдано";
    public const string Failed = "не сдано";

    public static readonly IReadOnlyList<string> All = [Passed, Failed];
}

public static class ElectricalStatuses
{
    public const string Normal = "В норме";
    public const string Overdue = "Просрочено";
    public const string ExperienceGaining = "Стаж набирается";
    public const string ExperienceReady = "Стаж набран — можно повышать";
}

public static class ElectricalNextActionTypes
{
    public const string PlannedGroupUpgrade = "Плановое повышение группы";
    public const string PeriodicCheck = "Очередная периодическая проверка";
}

/// <summary>Рассчитанные поля строки реестра ЭБ.</summary>
public sealed record ElectricalKnowledgeCheckComputed(
    int? MinMonthsInGroup,
    string MinMonthsDisplay,
    int? PeriodicityMonths,
    string PeriodicityDisplay,
    DateTime? PlannedNextActionDate,
    string NextActionType,
    int? DaysLeft,
    string Status,
    string StatusCssClass);

/// <summary>Комиссия по электробезопасности (отдельное хранилище).</summary>
public sealed class ElectricalCommissionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public bool IsGroup { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Composition { get; set; } = string.Empty;
    public List<ElectricalCommissionMember> Members { get; set; } = [];
    public DateTime? OrderDate { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime? CompositionApprovalDate { get; set; }
    public int? CompositionCheckPeriodMonths { get; set; }
    public int? CertifiedInRostechnadzorCount { get; set; }
    public string CompositionCompliance { get; set; } = string.Empty;
    public bool IsInactive { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ElectricalCommissionMember
{
    public string Status { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool CertifiedInRostechnadzor { get; set; }
}

public static class ElectricalCommissionMemberHelpers
{
    public const string ChairmanStatus = "Председатель комиссии по ЭБ";

    public static string MemberStatus(int number) => $"Член комиссии {number}";

    public static IReadOnlyList<string> StatusOptions(int memberSlots = 12)
    {
        var list = new List<string> { ChairmanStatus };
        for (var i = 1; i <= Math.Max(1, memberSlots); i++)
        {
            list.Add(MemberStatus(i));
        }

        return list;
    }

    public static string BuildCompositionText(IEnumerable<ElectricalCommissionMember>? members) =>
        string.Join(", ",
            (members ?? [])
                .Select(m =>
                {
                    var name = m.Name?.Trim() ?? string.Empty;
                    var pos = m.Position?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        return string.Empty;
                    }

                    return string.IsNullOrWhiteSpace(pos) ? name : $"{name} ({pos})";
                })
                .Where(n => !string.IsNullOrWhiteSpace(n)));

    public static void SyncComposition(ElectricalCommissionRecord record)
    {
        record.Members ??= [];
        if (record.Members.Count > 0)
        {
            record.Composition = BuildCompositionText(record.Members);
        }
    }

    public static string FormatMemberLine(ElectricalCommissionMember m)
    {
        var name = m.Name?.Trim() ?? string.Empty;
        var pos = m.Position?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(pos) ? name : $"{name} ({pos})";
    }

    public static string ChairmanDisplay(ElectricalCommissionRecord record)
    {
        var chair = (record.Members ?? [])
            .FirstOrDefault(m => string.Equals(m.Status, ChairmanStatus, StringComparison.OrdinalIgnoreCase));
        return chair is null ? "—" : (FormatMemberLine(chair) is { Length: > 0 } t ? t : "—");
    }

    public static string MembersDisplay(ElectricalCommissionRecord record)
    {
        var lines = (record.Members ?? [])
            .Where(m => !string.Equals(m.Status, ChairmanStatus, StringComparison.OrdinalIgnoreCase))
            .Select(FormatMemberLine)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
        return lines.Count == 0 ? "—" : string.Join(", ", lines);
    }
}

public static class ElectricalCommissionCompliance
{
    public const string Compliant = "Соответствует";
    public const string NonCompliant = "Не соответствует";

    public static readonly IReadOnlyList<string> All = [Compliant, NonCompliant];
}

/// <summary>Рассчитанные сроки проверки состава комиссии.</summary>
public sealed record ElectricalCommissionComputed(
    DateTime? PlannedCheckDate,
    int? DaysLeft,
    string DeadlineStatus,
    string DeadlineStatusCssClass);
