namespace Zashita.Core.Risks;

/// <summary>
/// Чтение/запись данных карты оценки рисков в разрезе объекта.
/// Вкладка «по организации» = поля Risk* на WorkplaceRecord;
/// остальные объекты = ObjectAssessments[objectId].
/// </summary>
public static class RiskObjectAssessmentScope
{
    public static bool IsOrganizationWide(string? objectId) =>
        string.IsNullOrWhiteSpace(objectId)
        || string.Equals(objectId, RiskAssessmentObjectIds.OrganizationWide, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Возвращает копию РМ с подставленными данными оценки для выбранного объекта
    /// (для списка/карточки). Исходная запись не меняется.
    /// </summary>
    public static WorkplaceRecord ProjectForObject(WorkplaceRecord source, string? objectId)
    {
        if (source.IsGroup || IsOrganizationWide(objectId))
        {
            return source;
        }

        var data = GetObjectData(source, objectId!);
        return CloneWithAssessment(source, data);
    }

    public static IReadOnlyList<WorkplaceRecord> ProjectListForObject(
        IEnumerable<WorkplaceRecord> nodes,
        string? objectId)
    {
        if (IsOrganizationWide(objectId))
        {
            return nodes.ToList();
        }

        return nodes.Select(n => ProjectForObject(n, objectId)).ToList();
    }

    public static void ApplyAssessmentToWorkplace(WorkplaceRecord workplace, string? objectId)
    {
        if (workplace.IsGroup || IsOrganizationWide(objectId))
        {
            return;
        }

        var data = GetObjectData(workplace, objectId!);
        CopyDataOntoWorkplace(workplace, data);
    }

    public static void CaptureAssessmentFromWorkplace(WorkplaceRecord workplace, string? objectId)
    {
        if (workplace.IsGroup || IsOrganizationWide(objectId))
        {
            return;
        }

        workplace.ObjectAssessments ??= new Dictionary<string, WorkplaceRiskAssessmentData>(
            StringComparer.OrdinalIgnoreCase);
        workplace.ObjectAssessments[objectId!.Trim()] = SnapshotFromWorkplace(workplace);

        // Корневые Risk* оставляем как «по организации» — не затираем чужой объект.
        // После захвата сбрасываем в памяти к пустым, чтобы Upsert не перепутал вкладки
        // если вызывающий сохранит только ObjectAssessments — но Upsert пишет весь record.
        // Поэтому при сохранении карточки: сначала Capture, затем восстановить org-wide из existing.
    }

    /// <summary>
    /// Сохраняет оценку текущего объекта, не портя org-wide поля на диске.
    /// </summary>
    public static void SaveAssessment(
        WorkplaceRecord existing,
        WorkplaceRecord edited,
        string? objectId)
    {
        if (IsOrganizationWide(objectId))
        {
            existing.RiskAssessmentDate = edited.RiskAssessmentDate;
            existing.RiskCardNumber = edited.RiskCardNumber ?? string.Empty;
            existing.OverallRisk = edited.OverallRisk ?? string.Empty;
            existing.RiskPeriodicity = edited.RiskPeriodicity ?? string.Empty;
            existing.RiskAssessmentNotConducted = edited.RiskAssessmentNotConducted;
            existing.RiskLines = edited.RiskLines ?? [];
            existing.RiskTrafficMark = edited.RiskTrafficMark ?? string.Empty;
            existing.RiskFamiliarizations = edited.RiskFamiliarizations ?? [];
            existing.ModifiedAt = DateTime.UtcNow;
            return;
        }

        existing.ObjectAssessments ??= new Dictionary<string, WorkplaceRiskAssessmentData>(
            StringComparer.OrdinalIgnoreCase);
        existing.ObjectAssessments[objectId!.Trim()] = SnapshotFromWorkplace(edited);
        existing.ModifiedAt = DateTime.UtcNow;
    }

    public static WorkplaceRiskAssessmentData GetObjectData(WorkplaceRecord workplace, string objectId)
    {
        if (workplace.ObjectAssessments is not null
            && workplace.ObjectAssessments.TryGetValue(objectId.Trim(), out var data)
            && data is not null)
        {
            return CloneData(data);
        }

        return new WorkplaceRiskAssessmentData();
    }

    public static WorkplaceRiskAssessmentData SnapshotFromWorkplace(WorkplaceRecord w) =>
        new()
        {
            RiskAssessmentDate = w.RiskAssessmentDate,
            RiskCardNumber = w.RiskCardNumber ?? string.Empty,
            OverallRisk = w.OverallRisk ?? string.Empty,
            RiskPeriodicity = w.RiskPeriodicity ?? string.Empty,
            RiskAssessmentNotConducted = w.RiskAssessmentNotConducted,
            RiskLines = (w.RiskLines ?? []).Select(CloneLine).ToList(),
            RiskTrafficMark = w.RiskTrafficMark ?? string.Empty,
            RiskFamiliarizations = (w.RiskFamiliarizations ?? [])
                .Select(f => new RiskCardFamiliarization
                {
                    EmployeeKey = f.EmployeeKey,
                    EmployeeName = f.EmployeeName,
                    IsFamiliarized = f.IsFamiliarized,
                    NotWorkingOnObject = f.NotWorkingOnObject
                })
                .ToList()
        };

    private static void CopyDataOntoWorkplace(WorkplaceRecord w, WorkplaceRiskAssessmentData data)
    {
        w.RiskAssessmentDate = data.RiskAssessmentDate;
        w.RiskCardNumber = data.RiskCardNumber ?? string.Empty;
        w.OverallRisk = data.OverallRisk ?? string.Empty;
        w.RiskPeriodicity = data.RiskPeriodicity ?? string.Empty;
        w.RiskAssessmentNotConducted = data.RiskAssessmentNotConducted;
        w.RiskLines = (data.RiskLines ?? []).Select(CloneLine).ToList();
        w.RiskTrafficMark = data.RiskTrafficMark ?? string.Empty;
        w.RiskFamiliarizations = (data.RiskFamiliarizations ?? [])
            .Select(f => new RiskCardFamiliarization
            {
                EmployeeKey = f.EmployeeKey,
                EmployeeName = f.EmployeeName,
                IsFamiliarized = f.IsFamiliarized,
                NotWorkingOnObject = f.NotWorkingOnObject
            })
            .ToList();
    }

    private static WorkplaceRecord CloneWithAssessment(WorkplaceRecord source, WorkplaceRiskAssessmentData data)
    {
        var clone = new WorkplaceRecord
        {
            Id = source.Id,
            Organization = source.Organization,
            ParentId = source.ParentId,
            IsGroup = source.IsGroup,
            Name = source.Name,
            WorkplaceNumber = source.WorkplaceNumber,
            MainWorkplaceNumber = source.MainWorkplaceNumber,
            ConditionsClass = source.ConditionsClass,
            Profession = source.Profession,
            WorkerCount = source.WorkerCount,
            PlanYear = source.PlanYear,
            OkCode = source.OkCode,
            Department = source.Department,
            IsUsed = source.IsUsed,
            OwnerObjectId = source.OwnerObjectId,
            HiddenOnOrganizationWide = source.HiddenOnOrganizationWide,
            SoutAssessmentDate = source.SoutAssessmentDate,
            SoutReportNumber = source.SoutReportNumber,
            LinkedSoutCardWorkplaceId = source.LinkedSoutCardWorkplaceId,
            SoutCard = source.SoutCard,
            DeclarationOfConformity = source.DeclarationOfConformity,
            SoutAssessmentNotConducted = source.SoutAssessmentNotConducted,
            RequiresUnscheduledSout = source.RequiresUnscheduledSout,
            SoutTrafficMark = source.SoutTrafficMark,
            SoutFamiliarizations = source.SoutFamiliarizations,
            PrintOverrides = source.PrintOverrides,
            ObjectAssessments = source.ObjectAssessments,
            CreatedAt = source.CreatedAt,
            ModifiedAt = source.ModifiedAt
        };
        CopyDataOntoWorkplace(clone, data);
        return clone;
    }

    private static WorkplaceRiskAssessmentData CloneData(WorkplaceRiskAssessmentData data) =>
        new()
        {
            RiskAssessmentDate = data.RiskAssessmentDate,
            RiskCardNumber = data.RiskCardNumber ?? string.Empty,
            OverallRisk = data.OverallRisk ?? string.Empty,
            RiskPeriodicity = data.RiskPeriodicity ?? string.Empty,
            RiskAssessmentNotConducted = data.RiskAssessmentNotConducted,
            RiskLines = (data.RiskLines ?? []).Select(CloneLine).ToList(),
            RiskTrafficMark = data.RiskTrafficMark ?? string.Empty,
            RiskFamiliarizations = (data.RiskFamiliarizations ?? [])
                .Select(f => new RiskCardFamiliarization
                {
                    EmployeeKey = f.EmployeeKey,
                    EmployeeName = f.EmployeeName,
                    IsFamiliarized = f.IsFamiliarized,
                    NotWorkingOnObject = f.NotWorkingOnObject
                })
                .ToList()
        };

    private static WorkplaceRiskLine CloneLine(WorkplaceRiskLine line) =>
        new()
        {
            Id = line.Id,
            HazardCatalogId = line.HazardCatalogId,
            HazardNumber = line.HazardNumber,
            HazardTitle = line.HazardTitle,
            Source = line.Source,
            Consequences = line.Consequences,
            WhoMaySuffer = line.WhoMaySuffer,
            InitialRisk = line.InitialRisk,
            ControlMeasures = line.ControlMeasures,
            ResidualRisk = line.ResidualRisk,
            Responsible = line.Responsible,
            Probability = line.Probability,
            Severity = line.Severity,
            Significance = line.Significance
        };
}
