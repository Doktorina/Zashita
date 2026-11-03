namespace Zashita.Core.Training.Journals;

public enum TrainingJournalTableKind
{
    OccupationalSafety9,
    /// <summary>Официальная форма вводного инструктажа ОТ (9 граф).</summary>
    OccupationalSafetyIntro9,
    ContractorOccupationalSafety9,
    FireSafety11,
    FireSafetyContractors12,
    ElectroGroupI7
}

public static class TrainingJournalTableKindExtensions
{
    public static TrainingJournalTableKind GetTableKind(this TrainingJournalKind kind) =>
        kind switch
        {
            TrainingJournalKind.IntroductoryOccupationalSafety => TrainingJournalTableKind.OccupationalSafetyIntro9,
            TrainingJournalKind.WorkplaceOccupationalSafety => TrainingJournalTableKind.OccupationalSafety9,
            TrainingJournalKind.IntroductoryOccupationalSafetyContractors =>
                TrainingJournalTableKind.ContractorOccupationalSafety9,
            TrainingJournalKind.IntroductoryFireSafety => TrainingJournalTableKind.FireSafety11,
            TrainingJournalKind.WorkplaceFireSafety => TrainingJournalTableKind.FireSafety11,
            TrainingJournalKind.IntroductoryFireSafetyContractors =>
                TrainingJournalTableKind.FireSafetyContractors12,
            TrainingJournalKind.ElectroSafetyGroupI => TrainingJournalTableKind.ElectroGroupI7,
            _ => TrainingJournalTableKind.OccupationalSafety9
        };
}
