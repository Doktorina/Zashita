namespace Zashita.Core.Med;

public sealed class MedExamRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string ExamType { get; set; } = "Периодический";
    public DateTime ExamDate { get; set; } = DateTime.Today;
    public string Status { get; set; } = "Запланирован";
    /// <summary>Номер пункта из справочника факторов.</summary>
    public string ItemNumber { get; set; } = string.Empty;
    /// <summary>«1 раз в год» или «1 раз в 2 года».</summary>
    public string Periodicity { get; set; } = MedPeriodicity.OnceAYear;
    public DateTime? BirthDate { get; set; }
    /// <summary>Дата следующего медосмотра (считается от ExamDate + Periodicity при сохранении).</summary>
    public DateTime? NextExamDate { get; set; }
    /// <summary>Медзаключение: Годен / Не годен / …</summary>
    public string Conclusion { get; set; } = string.Empty;
    /// <summary>Имя файла скана заключения в App_Data/med/scans/.</summary>
    public string ConclusionScanFileName { get; set; } = string.Empty;

    public void RecalculateNextExamDate()
    {
        if (ExamDate.Year < 1900)
        {
            NextExamDate = null;
            return;
        }

        NextExamDate = MedPeriodicity.NextExamDate(ExamDate, Periodicity);
    }
}

public sealed class MedPersonListLine
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime? BirthDate { get; set; }
    public string Profession { get; set; } = string.Empty;
    public string Workplace { get; set; } = string.Empty;
    public string ItemNumber { get; set; } = string.Empty;
    public string Periodicity { get; set; } = MedPeriodicity.OnceAYear;
}

public sealed class MedPersonListRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Date { get; set; } = DateTime.Now;
    public string Number { get; set; } = string.Empty;
    public string ExamType { get; set; } = "Периодический";
    public string Department { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string Factors { get; set; } = "Приказ 29н";
    public string Comment { get; set; } = string.Empty;
    public List<MedPersonListLine> Lines { get; set; } = [];
    public bool IsPosted { get; set; }
    public Dictionary<string, string> PrintOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string ContentSummary
    {
        get
        {
            var names = Lines
                .Select(line => line.EmployeeName.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();
            return names.Count > 0 ? string.Join(", ", names) : Comment;
        }
    }
}

public sealed class MedRestrictionLine
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FactorOrWorkType { get; set; } = string.Empty;
    public string ItemNumber { get; set; } = string.Empty;
    public bool IsPresent { get; set; }
    public DateTime? ReviewDate { get; set; }
    public string Comment { get; set; } = string.Empty;
}

public sealed class MedRestrictionDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Date { get; set; } = DateTime.Now;
    public string Number { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string MedicalInstitution { get; set; } = string.Empty;
    public string Basis { get; set; } = string.Empty;
    public string Diseases { get; set; } = string.Empty;
    public string AdditionalInfo { get; set; } = string.Empty;
    public DateTime? SuspensionFrom { get; set; }
    public List<MedRestrictionLine> Lines { get; set; } = [];
    public bool IsPosted { get; set; }
    public Dictionary<string, string> PrintOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record MedRegistryRow(
    string Id,
    string Kind,
    DateTime Date,
    string Organization,
    string Department,
    string EmployeeName,
    string Status,
    string Comment);

public sealed record MedReferenceEntry(string Id, string Category, string Name, string? Details);

public sealed class MedFactorNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? ParentId { get; set; }
    public bool IsGroup { get; set; }
    public string ItemNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Periodicity { get; set; } = "Никогда";
    public string Description { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string OrderType { get; set; } = string.Empty;
    public DateTime? EffectiveFrom { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string PointNumber { get; set; } = string.Empty;
    public bool ConsiderConditionsClass { get; set; }
    public string ExternalLink { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public List<string> Doctors { get; set; } = [];
    public List<string> Examinations { get; set; } = [];
    public List<string> Contraindications { get; set; } = [];
}

public sealed class MedWorkTypeNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? ParentId { get; set; }
    public bool IsGroup { get; set; }
    public string Code { get; set; } = string.Empty;
    public string ItemNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string FactorSetName { get; set; } = string.Empty;
}

public sealed class MedMoSoutMappingRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string MoElementName { get; set; } = string.Empty;
    public string MoElementDescription { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string PointNumber { get; set; } = string.Empty;
    public string Periodicity { get; set; } = string.Empty;
    public string SoutElementType { get; set; } = string.Empty;
    public string SoutElementName { get; set; } = string.Empty;
    public string SoutElementDescription { get; set; } = string.Empty;
    public string AggregateState { get; set; } = string.Empty;
    public string BodyEffect { get; set; } = string.Empty;
}

public sealed class MedScheduleLine
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string EmployeeName { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public DateTime? BirthDate { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string ActivityKinds { get; set; } = string.Empty;
    public string PriorConclusions { get; set; } = string.Empty;
    public DateTime ExamDate { get; set; } = DateTime.Today;
    public string ItemNumber { get; set; } = string.Empty;
    public string Periodicity { get; set; } = MedPeriodicity.OnceAYear;
    public string Comment { get; set; } = string.Empty;
}

public sealed class MedScheduleDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Date { get; set; } = DateTime.Now;
    public string Number { get; set; } = string.Empty;
    public string ExamType { get; set; } = "Периодический";
    public string Department { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string MedicalInstitution { get; set; } = string.Empty;
    public string EmployerOwnership { get; set; } = "РОССИЙСКАЯ СОБСТВЕННОСТЬ";
    public string EmployerAddress { get; set; } = string.Empty;
    public string EmployerInn { get; set; } = string.Empty;
    public string EmployerOkved { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public List<MedScheduleLine> Lines { get; set; } = [];
    public bool IsPosted { get; set; }
    public Dictionary<string, string> PrintOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record MedExpiryItem(string EmployeeName, string ExamType, DateTime ExpiresOn);

/// <summary>Направление на медицинский осмотр работника.</summary>
public sealed class MedDirectionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Date { get; set; } = DateTime.Today;
    public DateTime? ExamDate { get; set; }
    public string ExamType { get; set; } = "Предварительный";
    public string MedicalOrgName { get; set; } = string.Empty;
    public string MedicalOrgDetails { get; set; } = string.Empty;
    public string MedicalOrgAddress { get; set; } = string.Empty;
    public string Ogrn { get; set; } = string.Empty;
    public string Inn { get; set; } = string.Empty;
    public string Kpp { get; set; } = string.Empty;
    public string LicenseInfo { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string MiddleName { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public int? Age { get; set; }
    public DateTime? BirthDate { get; set; }
    public string Snils { get; set; } = string.Empty;
    public string PolicyNumber { get; set; } = string.Empty;
    public string HomeAddress { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string EmployerContacts { get; set; } = string.Empty;
    public string OwnershipOkved { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string ProfessionOrWorkType { get; set; } = string.Empty;
    public string HireProfession { get; set; } = string.Empty;
    public string ExperienceTotal { get; set; } = string.Empty;
    public string ExperienceProfession { get; set; } = string.Empty;
    public string ExperienceHazardous { get; set; } = string.Empty;
    public string AuthorizedRepresentative { get; set; } = string.Empty;
    /// <summary>Должность уполномоченного (для блока подписи на печати).</summary>
    public string AuthorizedRepresentativePosition { get; set; } = string.Empty;
    /// <summary>Относительный URL картинки подписи (например /uploads/med-signatures/xxx.png).</summary>
    public string SignatureImageUrl { get; set; } = string.Empty;
    public List<MedDirectionFactorLine> Factors { get; set; } = [];
    public Dictionary<string, string> PrintOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsPosted { get; set; }
}

public sealed class MedDirectionFactorLine
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FactorName { get; set; } = string.Empty;
    public string Code988 { get; set; } = string.Empty;
    public string Code29n { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string ConditionsClass { get; set; } = string.Empty;
}

public sealed class MedicalOrganizationRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Ogrn { get; set; } = string.Empty;
    public string Inn { get; set; } = string.Empty;
    public string Kpp { get; set; } = string.Empty;
    public string LicenseInfo { get; set; } = string.Empty;
    public string HeadPosition { get; set; } = string.Empty;
    public string HeadName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }

    public string DetailsSummary =>
        string.Join(", ", new[] { Address, Email, Phone }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

public sealed class MedStandardRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    public bool IsPeriodic { get; set; } = true;
    /// <summary>Предсменный / предрейсовый медосмотр.</summary>
    public bool IsPreShift { get; set; }
    /// <summary>Внеочередной медосмотр.</summary>
    public bool IsExtraordinary { get; set; } = true;
    /// <summary>Психиатрическое освидетельствование.</summary>
    public bool HasPsychiatric { get; set; }

    // Устаревшие флаги — оставлены для совместимости со старыми JSON.
    public bool IsPreliminaryPrimary { get; set; }
    public bool IsPreliminaryRepeat { get; set; }
    public bool AllowsExtraordinaryManual { get; set; } = true;
    public string DefaultMedicalOrganizationId { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public List<MedStandardRule> Rules { get; set; } = [];
}

public sealed class MedStandardRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ItemNumber { get; set; } = string.Empty;
    public string FactorName { get; set; } = string.Empty;
    public string ExamKind { get; set; } = "Периодический";
    public string Periodicity { get; set; } = MedPeriodicity.OnceAYear;
    public bool IsPsychiatricActivity { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class MedDashboardStats
{
    public int Expired { get; init; }
    public int ExpiringSoon { get; init; }
    public int OverduePlanned { get; init; }
    public int Passed { get; init; }
    public int NotPassed { get; init; }
    public int Planned { get; init; }
    public int PassedPercent { get; init; }
    public IReadOnlyList<MedExpiryItem> ExpiringItems { get; init; } = [];
    public IReadOnlyList<MedExamRecord> OverdueItems { get; init; } = [];
}

public sealed class MedStatusRow
{
    public string Id { get; init; } = string.Empty;
    public string Organization { get; init; } = string.Empty;
    public string Department { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
    public string Profession { get; init; } = string.Empty;
    public string ExamType { get; init; } = string.Empty;
    public string ItemNumber { get; init; } = string.Empty;
    public string Periodicity { get; init; } = string.Empty;
    public string DisplayStatus { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Conclusion { get; init; } = string.Empty;
    public string ConclusionScanFileName { get; init; } = string.Empty;
    public DateTime? EmploymentDate { get; init; }
    public DateTime? ExamDate { get; init; }
    public DateTime? ExpiresOn { get; init; }
    public string Traffic { get; init; } = string.Empty;
}

public sealed class MedStatusTreeLine
{
    public bool IsGroup { get; init; }
    public bool IsPerson { get; init; }
    public int Level { get; init; }
    public string Label { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string? ParentId { get; init; }
    public MedStatusRow? Row { get; init; }

    public static MedStatusTreeLine Group(string label, int level, string id, string? parentId) =>
        new() { IsGroup = true, Level = level, Label = label, Id = id, ParentId = parentId };

    public static MedStatusTreeLine Person(string label, int level, string id, string? parentId) =>
        new() { IsGroup = true, IsPerson = true, Level = level, Label = label, Id = id, ParentId = parentId };

    public static MedStatusTreeLine Data(MedStatusRow row, int level, string id, string? parentId) =>
        new()
        {
            IsGroup = false,
            Level = level,
            Label = FormatDataLabel(row),
            Id = id,
            ParentId = parentId,
            Row = row
        };

    private static string FormatDataLabel(MedStatusRow row)
    {
        var examType = string.IsNullOrWhiteSpace(row.ExamType) ? "Медосмотр" : row.ExamType.Trim();
        if (MedStandardAssignmentBuilder.IsPsychiatricKind(examType))
        {
            return "Психиатрия (при приёме)";
        }

        var item = MedItemNumber.Normalize(row.ItemNumber);
        if (!string.IsNullOrWhiteSpace(item))
        {
            return $"{examType} · п.{item}";
        }

        return examType;
    }
}
