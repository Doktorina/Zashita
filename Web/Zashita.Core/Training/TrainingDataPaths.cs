using Zashita.Core.Training.Journals;

namespace Zashita.Core.Training;

public sealed class TrainingDataPaths
{
    public TrainingDataPaths(string appDataFolder)
    {
        Directory.CreateDirectory(appDataFolder);
        StandardsFile = Path.Combine(appDataFolder, "training-standards.json");
        StandardsHistoryFile = Path.Combine(appDataFolder, "training-standards-history.json");
        IeStandardSuppressFile = Path.Combine(appDataFolder, "training-ie-standard-suppress.json");
        KindsFile = Path.Combine(appDataFolder, "training-kinds.json");
        ProgramsFile = Path.Combine(appDataFolder, "training-program-courses.json");
        TopicsFile = Path.Combine(appDataFolder, "training-topics.json");
        TestsFile = Path.Combine(appDataFolder, "training-tests.json");
        QuestionsFile = Path.Combine(appDataFolder, "training-questions.json");
        WorkTypesFile = Path.Combine(appDataFolder, "training-work-types.json");
        ConductFile = Path.Combine(appDataFolder, "training-conduct.json");
        AnnualPlansFile = Path.Combine(appDataFolder, "training-annual-plans.json");
        PlanTypesFile = Path.Combine(appDataFolder, "training-plan-types.json");
        WorkTypeMappingFile = Path.Combine(appDataFolder, "training-work-type-mapping.json");
        KindMappingFile = Path.Combine(appDataFolder, "training-kind-mapping.json");
        PlanningFile = Path.Combine(appDataFolder, "training-planning.json");
        JournalsDirectory = Path.Combine(appDataFolder, "training", "journals");
        JournalsHistoryFile = Path.Combine(appDataFolder, "training-journals-history.json");
        Directory.CreateDirectory(JournalsDirectory);
    }

    public string StandardsFile { get; }
    public string StandardsHistoryFile { get; }
    public string IeStandardSuppressFile { get; }
    public string KindsFile { get; }
    public string ProgramsFile { get; }
    public string TopicsFile { get; }
    public string TestsFile { get; }
    public string QuestionsFile { get; }
    public string WorkTypesFile { get; }
    public string ConductFile { get; }
    public string AnnualPlansFile { get; }
    public string PlanTypesFile { get; }
    public string WorkTypeMappingFile { get; }
    public string KindMappingFile { get; }
    public string PlanningFile { get; }
    public string JournalsDirectory { get; }
    public string JournalsHistoryFile { get; }

    public string MappingFile(TrainingMappingKind kind) => kind switch
    {
        TrainingMappingKind.WorkTypes => WorkTypeMappingFile,
        TrainingMappingKind.Kinds => KindMappingFile,
        _ => WorkTypeMappingFile
    };

    public string JournalFile(TrainingJournalKind kind) =>
        Path.Combine(JournalsDirectory, $"{kind}.json");

    public string SimpleTreeFile(TrainingSimpleTreeKind kind) => kind switch
    {
        TrainingSimpleTreeKind.Topics => TopicsFile,
        TrainingSimpleTreeKind.Tests => TestsFile,
        TrainingSimpleTreeKind.Questions => QuestionsFile,
        TrainingSimpleTreeKind.WorkTypes => WorkTypesFile,
        _ => TopicsFile
    };
}
