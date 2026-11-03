using Zashita.Core.Training.Journals;

namespace Zashita.Core.Training;

public static class TrainingReportCatalog
{
    public const string OccupationalSafetySection = "Охрана труда";
    public const string FireSafetySection = "Пожарная безопасность";
    public const string ElectricalSafetySection = "Электробезопасность";
    public const string HistorySection = "История печати";

    public static IReadOnlyList<string> JournalSectionOrder { get; } =
    [
        OccupationalSafetySection,
        FireSafetySection,
        ElectricalSafetySection,
        HistorySection
    ];

    public static IReadOnlyList<TrainingReportEntry> BuildJournalHubEntries()
    {
        var entries = new List<TrainingReportEntry>();
        entries.AddRange(JournalEntries());
        entries.AddRange(HistoryEntries());
        return entries;
    }

    public static IReadOnlyList<TrainingJournalSection> BuildJournalSections(
        IEnumerable<TrainingReportEntry> entries)
    {
        var list = entries.ToList();
        return JournalSectionOrder
            .Select(section => new TrainingJournalSection(
                section,
                list.Where(entry => string.Equals(entry.Section, section, StringComparison.OrdinalIgnoreCase)).ToList()))
            .Where(section => section.Entries.Count > 0)
            .ToList();
    }

    private static IEnumerable<TrainingReportEntry> JournalEntries() =>
        TrainingJournalRegistry.Journals.Select(j => new TrainingReportEntry(
            j.Title,
            j.Description,
            ResolveJournalSection(j.Kind),
            0));

    private static IEnumerable<TrainingReportEntry> HistoryEntries() =>
    [
        new(
            "История печати журналов",
            "Все ранее сформированные и напечатанные журналы инструктажей",
            HistorySection,
            0)
    ];

    private static string ResolveJournalSection(TrainingJournalKind kind) => kind switch
    {
        TrainingJournalKind.IntroductoryOccupationalSafety => OccupationalSafetySection,
        TrainingJournalKind.WorkplaceOccupationalSafety => OccupationalSafetySection,
        TrainingJournalKind.IntroductoryOccupationalSafetyContractors => OccupationalSafetySection,
        TrainingJournalKind.IntroductoryFireSafety => FireSafetySection,
        TrainingJournalKind.WorkplaceFireSafety => FireSafetySection,
        TrainingJournalKind.IntroductoryFireSafetyContractors => FireSafetySection,
        TrainingJournalKind.ElectroSafetyGroupI => ElectricalSafetySection,
        _ => OccupationalSafetySection
    };
}

public sealed record TrainingJournalSection(string Title, IReadOnlyList<TrainingReportEntry> Entries);
