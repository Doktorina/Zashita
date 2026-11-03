namespace Zashita.Core.Training;

public static class TrainingApplicationPrintModes
{
    public const string ApplicationV2 = "application-v2";
    public const string ApplicationV3 = "application-v3";
    public const string ApplicationV4 = "application-v4";
    public const string IndustrialSafety = "industrial-safety";

    public static string Title(string? mode = null) => Normalize(mode) switch
    {
        ApplicationV3 => "Заявка на обучение (вариант 3)",
        ApplicationV4 => "Заявка на обучение (вариант 4)",
        IndustrialSafety => "Заявление об аттестации по вопросам промышленной безопасности",
        _ => "Заявка на обучение"
    };

    public static string Normalize(string? mode) =>
        string.IsNullOrWhiteSpace(mode)
            ? ApplicationV2
            : mode.Trim().ToLowerInvariant() switch
            {
                "application-v3" or "v3" or "вариант3" => ApplicationV3,
                "application-v4" or "v4" or "вариант4" => ApplicationV4,
                "industrial-safety" or "prombez" or "промбез" or "аттестация" => IndustrialSafety,
                "application-v2" or "v2" or "вариант2" or "application" => ApplicationV2,
                _ => ApplicationV2
            };
}
