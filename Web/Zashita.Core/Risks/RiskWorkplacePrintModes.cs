namespace Zashita.Core.Risks;

public static class RiskWorkplacePrintModes
{
    public const string Identification = "identification";
    public const string IdentificationMeasures = "identification-measures";
    public const string Assessment = "assessment";
    public const string PpeDuty = "ppe-duty";
    public const string Chronometry = "chronometry";

    public static string Title(string mode) => mode switch
    {
        IdentificationMeasures => "Карта идентификации опасностей и оценки рисков (с мерами по управлению рисками)",
        Assessment => "Карта оценки рисков",
        PpeDuty => "Карточка учета выдачи дежурных СИЗ",
        Chronometry => "Хронометраж рабочего времени",
        _ => "Карта идентификации опасностей и оценки рисков"
    };

    public static string Normalize(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        IdentificationMeasures => IdentificationMeasures,
        Assessment => Assessment,
        PpeDuty => PpeDuty,
        Chronometry => Chronometry,
        _ => Identification
    };
}
