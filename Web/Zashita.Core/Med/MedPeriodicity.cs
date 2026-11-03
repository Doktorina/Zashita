namespace Zashita.Core.Med;

/// <summary>Периодичность периодических медосмотров (приказ 29н и др.).</summary>
public static class MedPeriodicity
{
    public const string OnceAYear = "1 раз в год";
    public const string OnceEveryTwoYears = "1 раз в 2 года";
    /// <summary>Однократно при приёме на работу (психиатрия и т.п.).</summary>
    public const string OnceOnHire = "При приёме на работу";

    public static readonly IReadOnlyList<string> Options = [OnceAYear, OnceEveryTwoYears];

    public static IReadOnlyList<string> CatalogOptions { get; } =
        [OnceOnHire, "Никогда", OnceAYear, OnceEveryTwoYears];

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return OnceAYear;
        }

        if (value.Contains("приём", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("прием", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("принят", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("однократ", StringComparison.OrdinalIgnoreCase))
        {
            return OnceOnHire;
        }

        if (value.Contains('2', StringComparison.Ordinal) ||
            value.Contains("два", StringComparison.OrdinalIgnoreCase))
        {
            return OnceEveryTwoYears;
        }

        if (value.Contains("Никогда", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("нет", StringComparison.OrdinalIgnoreCase))
        {
            return "Никогда";
        }

        return OnceAYear;
    }

    public static int Years(string? value)
    {
        var normalized = Normalize(value);
        if (string.Equals(normalized, OnceEveryTwoYears, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (string.Equals(normalized, OnceOnHire, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Никогда", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return 1;
    }

    /// <summary>Возраст на дату (по умолчанию — сегодня). null, если даты рождения нет.</summary>
    public static int? CalculateAge(DateTime? birthDate, DateTime? onDate = null)
    {
        if (birthDate is null)
        {
            return null;
        }

        var today = (onDate ?? DateTime.Today).Date;
        var birth = birthDate.Value.Date;
        var age = today.Year - birth.Year;
        if (birth > today.AddYears(-age))
        {
            age--;
        }

        return age < 0 ? 0 : age;
    }

    /// <summary>Склонение «год / года / лет» для возраста: 21 год, 34 года, 25 лет.</summary>
    public static string YearsWord(int number)
    {
        var n = Math.Abs(number) % 100;
        var n1 = n % 10;
        if (n is > 10 and < 20)
        {
            return "лет";
        }

        return n1 switch
        {
            1 => "год",
            2 or 3 or 4 => "года",
            _ => "лет"
        };
    }

    public static string FormatAgeYears(int age) => $"{age} {YearsWord(age)}";

    /// <summary>Для возраста младше 21 года периодичность всегда «1 раз в год» (приказ 29н).</summary>
    public static string ApplyUnder21Override(DateTime? birthDate, string? periodicity)
    {
        var normalized = Normalize(periodicity);
        if (string.Equals(normalized, OnceOnHire, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Никогда", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        var age = CalculateAge(birthDate);
        if (age is < 21)
        {
            return OnceAYear;
        }

        return normalized;
    }

    public static DateTime? NextExamDate(DateTime? lastExamDate, string? periodicity)
    {
        if (lastExamDate is null)
        {
            return null;
        }

        var normalized = Normalize(periodicity);
        if (string.Equals(normalized, "Никогда", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, OnceOnHire, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var years = Years(normalized);
        return years <= 0 ? null : lastExamDate.Value.Date.AddYears(years);
    }

    public static bool IsOnceOnHire(string? periodicity) =>
        string.Equals(Normalize(periodicity), OnceOnHire, StringComparison.OrdinalIgnoreCase);
}
