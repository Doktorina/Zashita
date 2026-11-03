namespace Zashita.Core.Med;

public sealed class MedDataPaths
{
    public MedDataPaths(string appDataFolder)
    {
        var medFolder = Path.Combine(appDataFolder, "med");
        Directory.CreateDirectory(medFolder);
        ExamsFile = Path.Combine(medFolder, "exams.json");
        PersonListsFile = Path.Combine(medFolder, "person-lists.json");
        RegistryFile = Path.Combine(medFolder, "registry.json");
        ReferencesFile = Path.Combine(medFolder, "references.json");
        RestrictionsFile = Path.Combine(medFolder, "restrictions.json");
        SchedulesFile = Path.Combine(medFolder, "schedules.json");
        FactorsFile = Path.Combine(medFolder, "factors.json");
        WorkTypesFile = Path.Combine(medFolder, "work-types.json");
        MoSoutMappingsFile = Path.Combine(medFolder, "mo-sout-mappings.json");
        MedicalOrganizationsFile = Path.Combine(medFolder, "medical-organizations.json");
        StandardsFile = Path.Combine(medFolder, "standards.json");
        DirectionsFile = Path.Combine(medFolder, "directions.json");
        ContingentOverridesFile = Path.Combine(medFolder, "contingent-overrides.json");
        PhysicalRestrictionNotesFile = Path.Combine(medFolder, "physical-restriction-notes.json");
        ScansFolder = Path.Combine(medFolder, "scans");
        Directory.CreateDirectory(ScansFolder);
        SignaturesFolder = Path.Combine(medFolder, "signatures");
        Directory.CreateDirectory(SignaturesFolder);
        ConclusionImportDraftsFolder = Path.Combine(medFolder, "conclusion-import-drafts");
        Directory.CreateDirectory(ConclusionImportDraftsFolder);
    }

    public string ExamsFile { get; }
    public string PersonListsFile { get; }
    public string RegistryFile { get; }
    public string ReferencesFile { get; }
    public string RestrictionsFile { get; }
    public string SchedulesFile { get; }
    public string FactorsFile { get; }
    public string WorkTypesFile { get; }
    public string MoSoutMappingsFile { get; }
    public string MedicalOrganizationsFile { get; }
    public string StandardsFile { get; }
    public string DirectionsFile { get; }
    public string ContingentOverridesFile { get; }
    public string PhysicalRestrictionNotesFile { get; }
    public string ScansFolder { get; }
    /// <summary>Подписи уполномоченных на направлениях МО (не wwwroot — переживает публикацию).</summary>
    public string SignaturesFolder { get; }
    /// <summary>Черновики импорта заключений из Word.</summary>
    public string ConclusionImportDraftsFolder { get; }
}
