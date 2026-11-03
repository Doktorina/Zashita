using System.Text.RegularExpressions;
using Zashita.Core.Med;

namespace Zashita.Core.Training;

/// <summary>Разобранный протокол обучения из PDF.</summary>
public sealed class ParsedTrainingProtocol
{
    public string SourceFileName { get; set; } = string.Empty;
    public string ProtocolNumber { get; set; } = string.Empty;
    public DateTime? TrainingDate { get; set; }
    public string Program { get; set; } = string.Empty;
    public List<string> People { get; set; } = [];
}

/// <summary>
/// Извлечение из PDF протокола обучения: ФИО, программа, дата (и № протокола).
/// Текст — PdfPig; для сканов — тот же OCR, что у заключений МО.
/// </summary>
public static class TrainingProtocolPdfImportParser
{
    private static readonly Regex DateRx = new(
        @"(?<d>\d{1,2})\s*[.\-/]\s*(?<m>\d{1,2})\s*[.\-/]\s*(?<y>\d{4})",
        RegexOptions.Compiled);

    private static readonly Regex ProtocolNoRx = new(
        @"протокол\s*(?:проверки\s+знаний)?\s*(?:№|N|Nº|#)?\s*(?<n>[\w\-–/]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ProtocolDateRx = new(
        @"протокол[^\n]{0,80}?\s+от\s+(?<d>\d{1,2})\s*[.\-/]\s*(?<m>\d{1,2})\s*[.\-/]\s*(?<y>\d{4})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ProgramQuotedRx = new(
        @"(?:по\s+программе|программ[аеыуе]\s*(?:обучения)?|наименование\s+программы)\s*[:«""]?\s*[«""]?(?<p>[^»""\n]{3,160})[»""]?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex FioLineRx = new(
        @"^(?:(?<n>\d{1,3})[\.\)]\s*)?(?<fio>[А-ЯЁA-Z][а-яёa-z\-]+(?:\s+[А-ЯЁA-Z][а-яёa-z\-]+){1,3})\s*$",
        RegexOptions.Compiled);

    private static readonly Regex FioInlineRx = new(
        @"\b(?<fio>[А-ЯЁ][а-яё\-]+(?:\s+[А-ЯЁ][а-яё\-]+){1,2})\b",
        RegexOptions.Compiled);

    private static readonly string[] ProgramHints =
    [
        "программа а", "программа б", "программа в", "программа сиз", "программа пп",
        "охрана труда", "первая помощь", "пожар", "высот", "электробезопас",
        "проверк", "сиз", "оказани"
    ];

    private static readonly HashSet<string> NonPersonTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "протокол", "проверка", "знаний", "комиссия", "председатель", "секретарь",
        "член", "члены", "работник", "работники", "слушатель", "слушатели",
        "организация", "должность", "профессия", "результат", "оценка", "сдал",
        "не", "годен", "подпись", "дата", "номер", "программы", "обучение",
        "охрана", "труда", "первая", "помощь", "пожарная", "безопасность"
    };

    public static ParsedTrainingProtocol Parse(Stream pdfStream, string? sourceFileName = null)
    {
        var text = MedConclusionPdfImportParser.TryExtractPlainText(pdfStream);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException(
                "В PDF нет текстового слоя (скан-картинка), OCR не смог прочитать текст. Нужен PDF с текстом или русский OCR Windows.");
        }

        var card = ParseText(text);
        card.SourceFileName = Path.GetFileName(sourceFileName ?? string.Empty);
        if (card.People.Count == 0 && string.IsNullOrWhiteSpace(card.Program) && card.TrainingDate is null)
        {
            throw new InvalidOperationException(
                "Текст протокола найден, но не удалось прочитать ФИО / программу / дату. Проверьте бланк.");
        }

        return card;
    }

    public static ParsedTrainingProtocol ParseText(string documentText)
    {
        var text = NormalizeText(documentText);
        var lines = text
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => Regex.Replace(l, @"\s+", " ").Trim())
            .Where(l => l.Length > 0)
            .ToList();

        var card = new ParsedTrainingProtocol
        {
            ProtocolNumber = ExtractProtocolNumber(text),
            TrainingDate = ExtractTrainingDate(text, lines),
            Program = ExtractProgram(text, lines),
            People = ExtractPeople(lines)
        };

        return card;
    }

    private static string NormalizeText(string raw)
    {
        var t = (raw ?? string.Empty).Replace('\u00a0', ' ').Replace('ё', 'е').Replace('Ё', 'Е');
        return Regex.Replace(t, @"[ \t]+", " ").Trim();
    }

    private static string ExtractProtocolNumber(string text)
    {
        var m = ProtocolNoRx.Match(text);
        if (!m.Success)
        {
            return string.Empty;
        }

        var n = m.Groups["n"].Value.Trim().TrimEnd('.', ',', ';');
        // «от» после № без номера — отбрасываем.
        if (n.Equals("от", StringComparison.OrdinalIgnoreCase)
            || DateRx.IsMatch(n)
            || n.Length > 40)
        {
            return string.Empty;
        }

        return n;
    }

    private static DateTime? ExtractTrainingDate(string text, IReadOnlyList<string> lines)
    {
        var m = ProtocolDateRx.Match(text);
        if (m.Success && TryBuildDate(m, out var fromTitle))
        {
            return fromTitle;
        }

        // «Дата проведения» / «Дата проверки» / «Дата протокола»
        for (var i = 0; i < lines.Count; i++)
        {
            var lower = lines[i].ToLowerInvariant();
            if (!(lower.Contains("дата проведения")
                  || lower.Contains("дата проверки")
                  || lower.Contains("дата протокола")
                  || lower.Contains("дата заседания")
                  || (lower.Contains("дата") && lower.Contains("обучен"))))
            {
                continue;
            }

            var d = ParseDate(lines[i]) ?? (i + 1 < lines.Count ? ParseDate(lines[i + 1]) : null);
            if (d is { Year: >= 2000 and <= 2100 })
            {
                return d.Value.Date;
            }
        }

        // Запасной путь: дата рядом со словом «протокол» / «от».
        foreach (Match dm in DateRx.Matches(text))
        {
            if (!TryBuildDate(dm, out var d) || d.Year is < 2000 or > 2100)
            {
                continue;
            }

            var start = Math.Max(0, dm.Index - 40);
            var window = text.Substring(start, Math.Min(80, text.Length - start)).ToLowerInvariant();
            if (window.Contains("протокол") || window.Contains(" от ") || window.Contains("проведен"))
            {
                // Не дата рождения / лицензии.
                if (window.Contains("рожден") || window.Contains("лиценз"))
                {
                    continue;
                }

                return d.Date;
            }
        }

        // Последняя разумная дата в документе (часто дата внизу бланка).
        DateTime? last = null;
        foreach (Match dm in DateRx.Matches(text))
        {
            if (TryBuildDate(dm, out var d) && d.Year is >= 2000 and <= 2100)
            {
                last = d.Date;
            }
        }

        return last;
    }

    private static string ExtractProgram(string text, IReadOnlyList<string> lines)
    {
        var m = ProgramQuotedRx.Match(text);
        if (m.Success)
        {
            var p = CleanProgram(m.Groups["p"].Value);
            if (p.Length >= 3)
            {
                return p;
            }
        }

        // Строка с «Программа А/Б/В…» или известным названием.
        foreach (var line in lines)
        {
            var lower = line.ToLowerInvariant();
            if (!ProgramHints.Any(h => lower.Contains(h, StringComparison.Ordinal)))
            {
                continue;
            }

            if (lower.Contains("протокол") && lower.Length < 40)
            {
                continue;
            }

            // «по программе Программа А» / просто «Программа А»
            var cleaned = CleanProgram(line);
            cleaned = Regex.Replace(
                cleaned,
                @"^(?:по\s+)?программ[аеыуе]\s*(?:обучения)?\s*[:\-]?\s*",
                string.Empty,
                RegexOptions.IgnoreCase);
            cleaned = CleanProgram(cleaned);
            if (cleaned.Length >= 3 && cleaned.Length <= 160)
            {
                return cleaned;
            }
        }

        // Канонические короткие имена из заголовка.
        var title = string.Join(' ', lines.Take(8)).ToLowerInvariant();
        if (title.Contains("первая помощь") || title.Contains("первой помощи"))
        {
            return "Программа обучения по оказанию первой помощи";
        }

        if (title.Contains("сиз") || title.Contains("средств индивидуальной"))
        {
            return "Правильное применение СИЗ";
        }

        if (title.Contains("программ") && Regex.IsMatch(title, @"программ[аеы]?\s*а\b"))
        {
            return "Программа А";
        }

        if (title.Contains("программ") && Regex.IsMatch(title, @"программ[аеы]?\s*б\b"))
        {
            return "Программа Б";
        }

        if (title.Contains("высот"))
        {
            return "Работы на высоте";
        }

        if (title.Contains("пожар"))
        {
            return "Пожарная безопасность";
        }

        if (title.Contains("охране труда") || title.Contains("охрана труда"))
        {
            return "Обучение по охране труда";
        }

        return string.Empty;
    }

    private static List<string> ExtractPeople(IReadOnlyList<string> lines)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inPeopleSection = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var lower = line.ToLowerInvariant();

            if (lower.Contains("ф.и.о") || lower.Contains("фио")
                || lower.Contains("список") && (lower.Contains("работ") || lower.Contains("слушател") || lower.Contains("обучаем"))
                || lower.Contains("проверяемые") || lower.Contains("обучаемые"))
            {
                inPeopleSection = true;
                continue;
            }

            if (lower.Contains("председатель") || lower.Contains("состав комиссии")
                || lower.Contains("секретарь комиссии") || lower.StartsWith("подписи"))
            {
                // После комиссии обычно снова не люди из списка — но ФИО комиссии тоже ловятся;
                // отсекаем явно помеченные роли.
                if (TryTakeRoleName(line, out _))
                {
                    continue;
                }
            }

            if (TryTakeRoleName(line, out _))
            {
                continue;
            }

            // Нумерованная строка «1. Иванов Иван Иванович»
            var m = FioLineRx.Match(line);
            if (m.Success)
            {
                var fio = CleanPersonName(m.Groups["fio"].Value);
                if (IsLikelyPersonName(fio) && seen.Add(fio))
                {
                    result.Add(fio);
                    inPeopleSection = true;
                }

                continue;
            }

            // В секции списка — любые ФИО на строке.
            if (inPeopleSection || LooksLikeTableFioRow(line))
            {
                foreach (Match im in FioInlineRx.Matches(line))
                {
                    var fio = CleanPersonName(im.Groups["fio"].Value);
                    if (IsLikelyPersonName(fio) && seen.Add(fio))
                    {
                        result.Add(fio);
                    }
                }
            }
        }

        // Если секция не найдена — собрать все правдоподобные ФИО (кроме шапки).
        if (result.Count == 0)
        {
            foreach (var line in lines.Skip(Math.Min(3, lines.Count)))
            {
                if (TryTakeRoleName(line, out _))
                {
                    continue;
                }

                var m = FioLineRx.Match(line);
                if (!m.Success)
                {
                    continue;
                }

                var fio = CleanPersonName(m.Groups["fio"].Value);
                if (IsLikelyPersonName(fio) && seen.Add(fio))
                {
                    result.Add(fio);
                }
            }
        }

        return result;
    }

    private static bool TryTakeRoleName(string line, out string name)
    {
        name = string.Empty;
        var lower = line.ToLowerInvariant();
        if (!(lower.Contains("председатель") || lower.Contains("секретарь")
              || lower.StartsWith("член комиссии") || lower.Contains("член комиссии")))
        {
            return false;
        }

        var m = FioInlineRx.Match(line);
        if (m.Success)
        {
            name = CleanPersonName(m.Groups["fio"].Value);
            return true;
        }

        return true; // роль без ФИО — всё равно не человек из списка
    }

    private static bool LooksLikeTableFioRow(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 2 or > 8)
        {
            return false;
        }

        var fioLike = 0;
        foreach (var p in parts.Take(3))
        {
            if (p.Length >= 2 && char.IsUpper(p[0]) && p.Skip(1).Any(char.IsLower))
            {
                fioLike++;
            }
        }

        return fioLike >= 2;
    }

    private static bool IsLikelyPersonName(string fio)
    {
        var parts = fio.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 2 or > 4)
        {
            return false;
        }

        if (parts.Any(p => NonPersonTokens.Contains(p)))
        {
            return false;
        }

        if (parts.Any(p => p.Length < 2))
        {
            return false;
        }

        // Все части с заглавной.
        return parts.All(p => char.IsUpper(p[0]));
    }

    private static string CleanPersonName(string value)
    {
        var t = Regex.Replace((value ?? string.Empty).Trim(), @"\s+", " ");
        t = t.Trim(' ', '.', ',', ';', ':', '-', '—');
        return t;
    }

    private static string CleanProgram(string value)
    {
        var t = Regex.Replace((value ?? string.Empty).Trim(), @"\s+", " ");
        t = t.Trim(' ', '.', ',', ';', ':', '-', '—', '«', '»', '"');
        // Обрезать хвост «провели проверку…»
        var cut = t.IndexOf(" провел", StringComparison.OrdinalIgnoreCase);
        if (cut > 8)
        {
            t = t[..cut].Trim();
        }

        return t;
    }

    private static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var m = DateRx.Match(text);
        return m.Success && TryBuildDate(m, out var d) ? d : null;
    }

    private static bool TryBuildDate(Match m, out DateTime date)
    {
        date = default;
        if (!int.TryParse(m.Groups["d"].Value, out var d) ||
            !int.TryParse(m.Groups["m"].Value, out var mo) ||
            !int.TryParse(m.Groups["y"].Value, out var y))
        {
            return false;
        }

        try
        {
            date = new DateTime(y, mo, d);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
