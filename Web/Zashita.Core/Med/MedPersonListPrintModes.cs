namespace Zashita.Core.Med;

public static class MedPersonListPrintModes
{
    public const string Standard = "standard";
    public const string ByDepartment = "by-department";
    public const string Moscow = "moscow";
    public const string FullFactors = "full-factors";
    public const string WorkplacesByDepartment = "workplaces-by-department";
    public const string Dispensary = "dispensary";
    public const string Psychiatric = "psychiatric";
    public const string DirectionSverdlovsk = "direction-sverdlovsk";

    public static string Title(string mode) => mode switch
    {
        ByDepartment => "Поименный список лиц для медосмотра (по подразделениям)",
        Moscow => "Поименный список лиц для медосмотра (Москва)",
        FullFactors => "Поименный список лиц для медосмотра (полное наименование факторов)",
        WorkplacesByDepartment => "Поименный список лиц для медосмотра с учетом рабочих мест (по подразделениям)",
        Dispensary => "Поименный список лиц для диспансеризации",
        Psychiatric => "Поименный список лиц, подлежащих психиатрическому осмотру",
        DirectionSverdlovsk => "Направление на медосмотр по работнику (Свердловская область)",
        _ => "Поименный список лиц для медосмотра"
    };

    public static string Normalize(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        ByDepartment => ByDepartment,
        Moscow => Moscow,
        FullFactors => FullFactors,
        WorkplacesByDepartment => WorkplacesByDepartment,
        Dispensary => Dispensary,
        Psychiatric => Psychiatric,
        DirectionSverdlovsk => DirectionSverdlovsk,
        _ => Standard
    };
}
