namespace Zashita.Core.Med;

public static class MedSchedulePrintModes
{
    public const string NamedByDepartment = "named-by-department";
    public const string Named = "named";
    public const string General = "general";
    public const string Acknowledgement = "acknowledgement";
    public const string PsychiatricDirection = "psychiatric-direction";

    public static string Title(string mode) => mode switch
    {
        NamedByDepartment => "График периодического медосмотра (по подразделениям)",
        Named => "График периодического медосмотра (поименный)",
        Acknowledgement => "Лист ознакомления с графиком медосмотра",
        PsychiatricDirection => "Направление на психиатрическое освидетельствование",
        _ => "График периодического медицинского осмотра"
    };

    public static string Normalize(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        NamedByDepartment => NamedByDepartment,
        Named => Named,
        Acknowledgement => Acknowledgement,
        PsychiatricDirection or "psych" or "психиатр" => PsychiatricDirection,
        _ => General
    };
}
