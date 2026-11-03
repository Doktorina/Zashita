namespace Zashita.Core.Training.Journals;

/// <summary>Виды инструктажа. «На рабочем месте» — не отдельный вид, а группа (первичный/повторный/внеплановый/целевой).</summary>
public static class TrainingBriefingTypes
{
    public const string Introductory = "Вводный";
    public const string Primary = "Первичный";
    public const string Repeated = "Повторный";
    public const string Unscheduled = "Внеплановый";
    public const string Targeted = "Целевой";

    /// <summary>Полный перечень допустимых видов (без «На рабочем месте»).</summary>
    public static IReadOnlyList<string> Options { get; } =
    [
        Introductory,
        Primary,
        Repeated,
        Unscheduled,
        Targeted
    ];

    /// <summary>Виды для журнала на рабочем месте (ОТ / ПБ).</summary>
    public static IReadOnlyList<string> WorkplaceOptions { get; } =
    [
        Primary,
        Repeated,
        Unscheduled,
        Targeted
    ];

    /// <summary>Виды для вводных журналов.</summary>
    public static IReadOnlyList<string> IntroductoryOptions { get; } =
    [
        Introductory
    ];

    public static IReadOnlyList<string> OptionsFor(TrainingJournalKind kind) =>
        kind switch
        {
            TrainingJournalKind.WorkplaceOccupationalSafety or
                TrainingJournalKind.WorkplaceFireSafety => WorkplaceOptions,
            TrainingJournalKind.IntroductoryOccupationalSafety or
                TrainingJournalKind.IntroductoryFireSafety or
                TrainingJournalKind.IntroductoryOccupationalSafetyContractors or
                TrainingJournalKind.IntroductoryFireSafetyContractors => IntroductoryOptions,
            _ => Options
        };

    public static bool RequiresReason(string? briefingType)
    {
        if (string.IsNullOrWhiteSpace(briefingType))
        {
            return false;
        }

        return briefingType.Contains("внеплан", StringComparison.OrdinalIgnoreCase) ||
               briefingType.Contains("целев", StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string? briefingType)
    {
        var value = (briefingType ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return string.Empty;
        }

        // Устаревшее значение фильтра — не сохраняем как вид.
        if (value.Equals("На рабочем месте", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        foreach (var option in Options)
        {
            if (string.Equals(option, value, StringComparison.OrdinalIgnoreCase))
            {
                return option;
            }
        }

        return value;
    }

    public static string ReasonPlaceholder(string? briefingType) =>
        RequiresReason(briefingType) ? string.Empty : "—";
}
