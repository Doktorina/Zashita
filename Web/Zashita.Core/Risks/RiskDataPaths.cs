namespace Zashita.Core.Risks;

public sealed class RiskDataPaths
{
    public RiskDataPaths(string appDataFolder)
    {
        var folder = Path.Combine(appDataFolder, "risks");
        Directory.CreateDirectory(folder);
        WorkplacesFile = Path.Combine(folder, "workplaces.json");
        RiskWorkplacesFile = Path.Combine(folder, "risk-workplaces.json");
        AssignmentsFile = Path.Combine(folder, "workplace-assignments.json");
        PreparationFile = Path.Combine(folder, "sout-preparation.json");
        AnnualPlansFile = Path.Combine(folder, "sout-annual-plans.json");
        EquipmentFile = Path.Combine(folder, "sout-equipment.json");
        MaterialsFile = Path.Combine(folder, "sout-materials.json");
        ConditionsClassesFile = Path.Combine(folder, "sout-conditions-classes.json");
        CommissionsFile = Path.Combine(folder, "sout-commissions.json");
        ImportSettingsFile = Path.Combine(folder, "sout-import-settings.json");
        RiskPreparationFile = Path.Combine(folder, "risk-preparation.json");
        UnscheduledOrdersFile = Path.Combine(folder, "risk-unscheduled-orders.json");
        HazardAssessmentsFile = Path.Combine(folder, "risk-hazard-assessments.json");
        HazardCatalogFile = Path.Combine(folder, "risk-hazard-catalog.json");
        HazardPrintSelectionFile = Path.Combine(folder, "risk-hazard-print-selection.json");
        AssessmentObjectsFile = Path.Combine(folder, "risk-assessment-objects.json");
        OrgPrintDetailsFile = Path.Combine(folder, "org-print-details.json");
        CardImportDraftsFolder = Path.Combine(folder, "card-import-drafts");
        Directory.CreateDirectory(CardImportDraftsFolder);
        SoutCardImportDraftsFolder = Path.Combine(folder, "sout-card-import-drafts");
        Directory.CreateDirectory(SoutCardImportDraftsFolder);
        RegulationTemplatesFolder = Path.Combine(folder, "regulation-templates");
        Directory.CreateDirectory(RegulationTemplatesFolder);
    }

    public string WorkplacesFile { get; }
    /// <summary>Перечень РМ модуля «Риски» — независим от СОУТ.</summary>
    public string RiskWorkplacesFile { get; }
    public string AssignmentsFile { get; }
    public string PreparationFile { get; }
    public string AnnualPlansFile { get; }
    public string EquipmentFile { get; }
    public string MaterialsFile { get; }
    public string ConditionsClassesFile { get; }
    public string CommissionsFile { get; }
    public string ImportSettingsFile { get; }
    public string RiskPreparationFile { get; }
    public string UnscheduledOrdersFile { get; }
    public string HazardAssessmentsFile { get; }
    public string HazardCatalogFile { get; }
    /// <summary>Выбор опасностей для печати (реестр / перечень мер) по организации.</summary>
    public string HazardPrintSelectionFile { get; }
    /// <summary>Объекты оценки рисков по организациям.</summary>
    public string AssessmentObjectsFile { get; }
    public string OrgPrintDetailsFile { get; }
    /// <summary>Черновики импорта карт идентификации из Word.</summary>
    public string CardImportDraftsFolder { get; }
    /// <summary>Черновики импорта карт СОУТ из Word.</summary>
    public string SoutCardImportDraftsFolder { get; }
    /// <summary>Шаблоны положения (методики) ОПР по организациям.</summary>
    public string RegulationTemplatesFolder { get; }
}
