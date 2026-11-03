using System.Globalization;
using System.Text;

namespace Zashita.Core.Employees;

/// <summary>
/// Нормализация полей выгрузки 1С (шаг NORMALIZE ETL), см. deploy/1C-ETL-ШТАТ.md.
/// </summary>
public static class OneCStaffNormalize
{
    /// <summary>ФИО: лишние пробелы → «Иванов Иван Иванович».</summary>
    public static string PersonName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var parts = raw
            .Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(raw.Length);
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(ToTitleToken(parts[i]));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Канонический статус: Работа / Увольнение / Отпуск по уходу за ребенком /
    /// Работа в отпуске по уходу за ребенком (иначе исходная очищенная строка).
    /// </summary>
    public static string HrStatus(string? raw)
    {
        var s = Compact(raw);
        if (s.Length == 0)
        {
            return string.Empty;
        }

        var lower = s.ToLowerInvariant().Replace('ё', 'е');
        // Любой мусор со словом «увол» (Уволен, уволен по собств., Увольнение…).
        if (lower.Contains("увол", StringComparison.Ordinal))
        {
            return "Увольнение";
        }

        if (lower.Contains("работа в отпуске", StringComparison.Ordinal)
            && (lower.Contains("уход", StringComparison.Ordinal) || lower.Contains("ребен", StringComparison.Ordinal)))
        {
            return "Работа в отпуске по уходу за ребенком";
        }

        if (lower.Contains("уход за ребен", StringComparison.Ordinal)
            || lower.Contains("отпуск по уходу", StringComparison.Ordinal)
            || lower.Contains("декрет", StringComparison.Ordinal))
        {
            return "Отпуск по уходу за ребенком";
        }

        if (lower is "работа" or "работает")
        {
            return "Работа";
        }

        return s;
    }

    /// <summary>
    /// Канонический вид занятости: Основное место работы /
    /// Внутреннее совместительство / Внешнее совместительство.
    /// </summary>
    public static string EmploymentType(string? raw)
    {
        var s = Compact(raw);
        if (s.Length == 0)
        {
            return "Основное место работы";
        }

        var lower = s.ToLowerInvariant().Replace('ё', 'е');
        if (lower.Contains("внешн", StringComparison.Ordinal))
        {
            return "Внешнее совместительство";
        }

        if (lower.Contains("внутр", StringComparison.Ordinal))
        {
            return "Внутреннее совместительство";
        }

        // «Совместительство» без уточнения — по умолчанию внутреннее.
        if (lower.Contains("совмест", StringComparison.Ordinal))
        {
            return "Внутреннее совместительство";
        }

        if (lower.Contains("основн", StringComparison.Ordinal))
        {
            return "Основное место работы";
        }

        return s;
    }

    /// <summary>Даты-заглушки Excel (01.01.1900 и раньше) → null.</summary>
    public static DateTime? DateOrNull(DateTime? value, int minYear = 1901)
    {
        if (value is null)
        {
            return null;
        }

        var d = value.Value.Date;
        if (d.Year < minYear)
        {
            return null;
        }

        return d;
    }

    /// <summary>
    /// Дата увольнения: мусор 0 / 01.01.1900 / прочерк / год &lt; 2000 → null
    /// (иначе ломается сравнение с «сегодня»).
    /// </summary>
    public static DateTime? ParseDismissalDate(string? text) =>
        DateOrNull(ParseDateRaw(text), minYear: 2000);

    public static DateTime? ParseDate(string? text) =>
        DateOrNull(ParseDateRaw(text), minYear: 1901);

    private static DateTime? ParseDateRaw(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim().Trim('"');
        if (value is "00.00.0000" or "0" or "-" or "—" or "." or "нет")
        {
            return null;
        }

        if (DateTime.TryParseExact(
                value,
                ["dd.MM.yyyy", "yyyy-MM-dd", "dd/MM/yyyy"],
                CultureInfo.GetCultureInfo("ru-RU"),
                DateTimeStyles.None,
                out var dt))
        {
            return dt.Date;
        }

        return DateTime.TryParse(value, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out var any)
            ? any.Date
            : null;
    }

    private static string Compact(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return string.Join(
            ' ',
            raw.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string ToTitleToken(string token)
    {
        if (token.Length == 0)
        {
            return token;
        }

        // Дефисные фамилии: Петров-Водкин
        if (token.Contains('-', StringComparison.Ordinal))
        {
            return string.Join('-', token.Split('-').Select(ToTitleToken));
        }

        var lower = token.ToLowerInvariant();
        return char.ToUpper(lower[0], CultureInfo.GetCultureInfo("ru-RU")) + lower[1..];
    }
}
