using System.Globalization;
using System.Text;

namespace Zashita.Core.Training;

/// <summary>
/// Имя файла при «Сохранить»: «31 Заявка от 23.10.2026 - А + Б + ПП - Виктория Строй - Плотникова Я.А. - офис».
/// </summary>
public static class TrainingApplicationDownloadFileName
{
    private static readonly char[] Invalid = Path.GetInvalidFileNameChars();

    public static string Build(TrainingPlanningRecord record)
    {
        var parts = new List<string>();

        var number = NormalizeNumber(record.Number);
        var date = record.DocumentDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        parts.Add(string.IsNullOrWhiteSpace(number)
            ? $"Заявка от {date}"
            : $"{number} Заявка от {date}");

        var program = ResolveProgram(record);
        if (!string.IsNullOrWhiteSpace(program))
        {
            parts.Add(program);
        }

        var org = ShortOrganizationName(record.Organization);
        if (!string.IsNullOrWhiteSpace(org))
        {
            parts.Add(org);
        }

        var people = FormatParticipants(record.Participants);
        if (!string.IsNullOrWhiteSpace(people))
        {
            parts.Add(people);
        }

        var objectNote = (record.ObjectNote ?? string.Empty).Trim();
        if (objectNote.Length > 0)
        {
            parts.Add(objectNote);
        }

        var raw = string.Join(" - ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        var safe = Sanitize(raw);
        return string.IsNullOrWhiteSpace(safe) ? "Заявка" : safe;
    }

    public static string WithExtension(TrainingPlanningRecord record) =>
        Build(record) + ".doc";

    /// <summary>
    /// ASCII-имя для Content-Disposition filename= (кириллица в HTTP-заголовке недопустима).
    /// Полное русское имя уходит в filename*=UTF-8''… и в атрибут download.
    /// </summary>
    public static string AsciiFallback(string fileName)
    {
        var sb = new StringBuilder(fileName.Length);
        foreach (var ch in fileName)
        {
            if (ch <= 0x7F && !Invalid.Contains(ch) && ch >= 32)
            {
                sb.Append(ch);
                continue;
            }

            if (char.IsWhiteSpace(ch) || ch is '—' or '–')
            {
                sb.Append(' ');
            }
            // пропускаем кириллицу/прочее — оставляем цифры, латиницу, «-», «+», «.» из исходного
        }

        var cleaned = sb.ToString();
        while (cleaned.Contains("  ", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
        }

        cleaned = cleaned.Trim(' ', '-', '.');
        if (cleaned.Length == 0 || cleaned is ".doc")
        {
            return "Zayavka.doc";
        }

        return cleaned.EndsWith(".doc", StringComparison.OrdinalIgnoreCase)
            ? cleaned
            : cleaned + ".doc";
    }

    /// <summary>«Плотникова Я.А.» — фамилия и инициалы.</summary>
    public static string FormatSurnameInitials(string? fullName)
    {
        var tokens = (fullName ?? string.Empty)
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return string.Empty;
        }

        if (tokens.Length == 1)
        {
            return tokens[0];
        }

        var surname = tokens[0];
        var initials = new StringBuilder();
        for (var i = 1; i < tokens.Length; i++)
        {
            var part = tokens[i];
            if (part.Length == 0)
            {
                continue;
            }

            initials.Append(char.ToUpperInvariant(part[0])).Append('.');
        }

        return initials.Length == 0 ? surname : $"{surname} {initials}";
    }

    private static string NormalizeNumber(string? number)
    {
        var raw = (number ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        if (raw.StartsWith("№", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[1..].Trim();
        }

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0)
        {
            return n.ToString(CultureInfo.InvariantCulture);
        }

        return raw;
    }

    private static string ResolveProgram(TrainingPlanningRecord record)
    {
        var fromKind = (record.TrainingKind ?? string.Empty).Trim();
        if (fromKind.Length > 0)
        {
            return fromKind;
        }

        foreach (var participant in record.Participants ?? [])
        {
            var direction = (participant.TrainingDirection ?? string.Empty).Trim();
            if (direction.Length > 0)
            {
                return direction;
            }

            var program = (participant.ProgramName ?? string.Empty).Trim();
            if (program.Length > 0)
            {
                return program;
            }
        }

        var content = (record.Content ?? string.Empty).Trim();
        if (content.StartsWith("Планирование:", StringComparison.OrdinalIgnoreCase))
        {
            content = content["Планирование:".Length..].Trim();
        }

        return content;
    }

    private static string ShortOrganizationName(string? organization)
    {
        var name = (organization ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return string.Empty;
        }

        if (name.StartsWith("Общество с ограниченной ответственностью", StringComparison.OrdinalIgnoreCase))
        {
            name = name["Общество с ограниченной ответственностью".Length..].Trim();
        }
        else if (name.StartsWith("Индивидуальный предприниматель", StringComparison.OrdinalIgnoreCase))
        {
            name = "ИП " + name["Индивидуальный предприниматель".Length..].Trim();
        }
        else if (name.StartsWith("ООО", StringComparison.OrdinalIgnoreCase))
        {
            name = name[3..].Trim();
        }

        return name.Trim('«', '»', '"', ' ', '\t');
    }

    private static string FormatParticipants(IEnumerable<TrainingPlanningParticipant>? participants)
    {
        var names = (participants ?? [])
            .Select(p => FormatSurnameInitials(p.FullName))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return names.Count == 0 ? string.Empty : string.Join(", ", names);
    }

    private static string Sanitize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (Invalid.Contains(ch) || ch < 32)
            {
                sb.Append(' ');
                continue;
            }

            sb.Append(ch);
        }

        var cleaned = sb.ToString();
        while (cleaned.Contains("  ", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
        }

        cleaned = cleaned.Trim(' ', '.', '-');
        // Windows MAX_PATH комфортнее с запасом под каталог загрузок.
        const int maxLen = 180;
        if (cleaned.Length > maxLen)
        {
            cleaned = cleaned[..maxLen].Trim(' ', '.', '-');
        }

        return cleaned;
    }
}
