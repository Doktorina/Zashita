using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Zashita.Core.Med;

/// <summary>
/// Разбор Word «ЗАКЛЮЧЕНИЕ … МЕДИЦИНСКОГО ОСМОТРА».
/// </summary>
public static class MedConclusionDocxImportParser
{
    private static readonly Regex TitleRx = new(
        @"ЗАКЛЮЧЕНИЕ\s+(?:ПРЕДВАРИТЕЛЬНОГО|ПЕРИОДИЧЕСКОГО|ВНЕОЧЕРЕДНОГО|[^\n]{0,80}?)\s*МЕДИЦИНСКОГО\s+ОСМОТРА",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ShortTitleRx = new(
        @"ЗАКЛЮЧЕНИЕ\s+ПРЕДВАРИТЕЛЬНОГО",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DateRx = new(
        @"(?<d>\d{1,2})\s*[.\-/]\s*(?<m>\d{1,2})\s*[.\-/]\s*(?<y>\d{4})",
        RegexOptions.Compiled);

    private static readonly Regex DateSpacedRx = new(
        @"(?<d>\d{1,2})\s+(?<m>\d{1,2})\s+(?<y>\d{4})",
        RegexOptions.Compiled);

    private static readonly Regex ItemRx = new(
        @"п\.?\s*(?<n>\d+(?:\.\d+)*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<ParsedMedConclusion> Parse(Stream docxStream, string? sourceFileName = null)
    {
        using var repaired = Zashita.Core.Risks.DocxPackageRepair.ToRepairedMemoryStream(docxStream);
        using var doc = WordprocessingDocument.Open(repaired, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return [];
        }

        var lines = ExtractLines(body);
        var cards = ParseLines(lines);
        var file = Path.GetFileName(sourceFileName ?? string.Empty);
        foreach (var card in cards)
        {
            card.SourceFileName = file;
        }

        return cards;
    }

    public static IReadOnlyList<ParsedMedConclusion> ParseFile(string path)
    {
        using var fs = File.OpenRead(path);
        return Parse(fs, path);
    }

    internal static IReadOnlyList<ParsedMedConclusion> ParseLines(IReadOnlyList<string> lines)
    {
        var blocks = SplitBlocks(lines);
        var result = new List<ParsedMedConclusion>();
        foreach (var block in blocks)
        {
            var card = ParseBlock(block);
            if (!string.IsNullOrWhiteSpace(card.EmployeeName) || card.ConclusionDate is not null)
            {
                result.Add(card);
            }
        }

        return result;
    }

    private static List<List<string>> SplitBlocks(IReadOnlyList<string> lines)
    {
        var blocks = new List<List<string>>();
        List<string>? current = null;
        foreach (var line in lines)
        {
            if (IsTitle(line))
            {
                current = [line];
                blocks.Add(current);
                continue;
            }

            if (current is null)
            {
                if (LooksLikeFieldStart(line))
                {
                    current = [line];
                    blocks.Add(current);
                }

                continue;
            }

            current.Add(line);
        }

        if (blocks.Count == 0 && lines.Count > 0)
        {
            blocks.Add(lines.ToList());
        }

        return blocks;
    }

    private static ParsedMedConclusion ParseBlock(IReadOnlyList<string> lines)
    {
        var card = new ParsedMedConclusion();
        var joined = string.Join('\n', lines);
        card.ExamType = DetectExamType(joined);
        string? lastName = null;
        string? firstName = null;
        string? patronymic = null;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var lower = line.ToLowerInvariant().Replace('ё', 'е');

            if (lastName is null &&
                (ContainsWord(lower, "фамилия") ||
                 Regex.IsMatch(line, @"^\s*1[\.\)]")))
            {
                // Строка только с подписями «1. Фамилия 2. Имя 3. Отчество» — значения на следующих строках.
                if (ContainsWord(lower, "имя") || ContainsWord(lower, "отчество"))
                {
                    continue;
                }

                lastName = CleanPersonNamePart(ExtractLabeledValue(lines, i, "Имя", "Отчество", "Дата рождения", "2.", "3."));
                continue;
            }

            if (firstName is null &&
                ((ContainsWord(lower, "имя") && !lower.Contains("отчество") && !lower.Contains("фамилия") && !lower.Contains("наименован")) ||
                 Regex.IsMatch(line, @"^\s*2[\.\)]\s*(?:имя)?", RegexOptions.IgnoreCase)))
            {
                if (ContainsWord(lower, "фамилия") || ContainsWord(lower, "отчество"))
                {
                    continue;
                }

                firstName = CleanPersonNamePart(ExtractLabeledValue(lines, i, "Отчество", "Дата рождения", "3.", "4."));
                continue;
            }

            if (patronymic is null &&
                (ContainsWord(lower, "отчество") ||
                 Regex.IsMatch(line, @"^\s*3[\.\)]\s*(?:отчеств)?", RegexOptions.IgnoreCase)))
            {
                // Не путать с «3. Пол работника»
                if (!lower.Contains("пол") || lower.Contains("отчеств"))
                {
                    patronymic = CleanPersonNamePart(ExtractLabeledValue(lines, i, "Дата рождения", "Пол", "4."));
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(card.EmployeeName) &&
                (lower.Contains("ф.и.о") || lower.Contains("фио") || lower.Contains("ф и о")))
            {
                card.EmployeeName = CleanPersonName(ExtractLabeledValue(lines, i, "Дата рождения", "2.", "3.", "Пол"));
                continue;
            }

            if (card.BirthDate is null && lower.Contains("дата рождения"))
            {
                card.BirthDate = ParseDate(ExtractLabeledValue(lines, i, "Пол", "3.", "4."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(card.Gender) && (lower.Contains("пол работника") || Regex.IsMatch(line, @"^\s*3[\.\)]\s*пол", RegexOptions.IgnoreCase) || (lower.StartsWith("3.") && lower.Contains("пол"))))
            {
                var g = ExtractLabeledValue(lines, i, "Место работы", "4.");
                card.Gender = NormalizeGender(g);
                continue;
            }

            if (string.IsNullOrWhiteSpace(card.Organization) &&
                (lower.Contains("наименование работодателя") || lower.Contains("4.1")))
            {
                card.Organization = CleanValue(ExtractLabeledValue(lines, i, "Структурн", "4.2", "5."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(card.Department) &&
                (lower.Contains("структурное подразделение") || lower.Contains("4.2")))
            {
                card.Department = CleanValue(ExtractLabeledValue(lines, i, "Наименование должности", "5."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(card.Profession) &&
                (lower.Contains("наименование должности") || lower.Contains("вида работ") ||
                 Regex.IsMatch(line, @"^\s*5[\.\)]")))
            {
                card.Profession = CleanValue(ExtractLabeledValue(lines, i, "вредных", "6.", "факторов"));
                continue;
            }

            if (card.ItemNumbers.Count == 0 &&
                (lower.Contains("вредных") || lower.Contains("опасных производственных") ||
                 Regex.IsMatch(line, @"^\s*6[\.\)]")))
            {
                var factors = ExtractLabeledValue(lines, i, "Результат", "7.", "Группа здоровья");
                card.ItemNumbers = ExtractItemNumbers(factors);
                if (card.ItemNumbers.Count == 0)
                {
                    card.ItemNumbers = ExtractItemNumbers(line);
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(card.ResultText) &&
                (lower.Contains("результаты проведенного") || lower.Contains("результаты осмотра") ||
                 Regex.IsMatch(line, @"^\s*7[\.\)]")))
            {
                card.ResultText = CleanValue(ExtractLabeledValue(lines, i, "Группа здоровья", "8.", "Решение"));
                card.Conclusion = MapConclusion(card.ResultText);
                continue;
            }

            if (string.IsNullOrWhiteSpace(card.HealthGroup) && lower.Contains("группа здоровья"))
            {
                card.HealthGroup = CleanValue(ExtractLabeledValue(lines, i, "Решение", "9.", "Председатель"));
                continue;
            }

            if (card.ConclusionDate is null &&
                (lower.Contains("дата выдачи") || lower.Contains("дата заключения") ||
                 (lower.Contains("дата") && lower.Contains("заключен"))))
            {
                card.ConclusionDate = ParseDate(line) ?? ParseDate(ExtractLabeledValue(lines, i));
            }
        }

        if (string.IsNullOrWhiteSpace(card.EmployeeName))
        {
            card.EmployeeName = JoinPersonName(lastName, firstName, patronymic);
        }
        else
        {
            card.EmployeeName = CleanPersonName(card.EmployeeName);
        }

        if (string.IsNullOrWhiteSpace(card.EmployeeName))
        {
            card.EmployeeName = TryExtractFioFallback(lines);
        }

        if (string.IsNullOrWhiteSpace(card.Conclusion) && !string.IsNullOrWhiteSpace(card.ResultText))
        {
            card.Conclusion = MapConclusion(card.ResultText);
        }

        if (card.ConclusionDate is null)
        {
            // Дата часто внизу блока — берём последнюю подходящую.
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                var lower = lines[i].ToLowerInvariant();
                if (lower.Contains("лиценз") || lower.Contains("огрн") || lower.Contains("действует"))
                {
                    continue;
                }

                var d = ParseDate(lines[i]);
                if (d is not null && d.Value.Year is >= 2000 and <= 2100)
                {
                    card.ConclusionDate = d;
                    break;
                }
            }
        }

        return card;
    }

    private static bool ContainsWord(string lowerHaystack, string word) =>
        Regex.IsMatch(lowerHaystack, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase);

    private static string JoinPersonName(string? last, string? first, string? patronymic)
    {
        var parts = new[] { last, first, patronymic }
            .Select(p => CleanPersonNamePart(p ?? string.Empty))
            .Where(p => p.Length > 0)
            .ToList();
        if (parts.Count == 0)
        {
            return string.Empty;
        }

        // Полное ФИО — через общую очистку; одно слово (только фамилия) тоже допустимо на этом этапе.
        var joined = string.Join(' ', parts);
        if (parts.Count < 2)
        {
            return joined;
        }

        var cleaned = CleanPersonName(joined);
        return cleaned.Length > 0 ? cleaned : joined;
    }

    private static string TryExtractFioFallback(IReadOnlyList<string> lines)
    {
        // Таблица Word: строка подписей «Фамилия | Имя | Отчество», ниже значения.
        var fromTable = TryExtractFioFromLabelValueTable(lines);
        if (fromTable.Length > 0)
        {
            return fromTable;
        }

        // Подписи в одной строке, ФИО — в следующей (частый PDF-бланк).
        for (var i = 0; i < Math.Min(lines.Count, 40); i++)
        {
            var lower = lines[i].ToLowerInvariant().Replace('ё', 'е');
            if (!(lower.Contains("фамилия") && lower.Contains("имя")))
            {
                continue;
            }

            for (var j = i + 1; j < Math.Min(lines.Count, i + 6); j++)
            {
                var candidate = lines[j];
                if (candidate.Contains("дата рождения", StringComparison.OrdinalIgnoreCase) ||
                    candidate.Contains("заключен", StringComparison.OrdinalIgnoreCase) ||
                    LooksLikeNumberedField(candidate))
                {
                    break;
                }

                var name = CleanPersonName(candidate);
                if (name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2)
                {
                    return name;
                }
            }
        }

        // «1. Фамилия … 2. Имя … 3. Отчество …» в одной/соседних строках (часто так в PDF).
        var window = string.Join(' ', lines.Take(Math.Min(lines.Count, 50)));
        var labeled = Regex.Match(
            window,
            @"фамилия\s*[:\.]?\s*(?<ln>[А-ЯЁа-яёA-Za-z\-]+)\s+(?:имя\s*[:\.]?\s*)?(?<fn>[А-ЯЁа-яёA-Za-z\-]+)\s+(?:отчество\s*[:\.]?\s*)?(?<pt>[А-ЯЁа-яёA-Za-z\-]+)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (labeled.Success)
        {
            var name = JoinPersonName(
                labeled.Groups["ln"].Value,
                labeled.Groups["fn"].Value,
                labeled.Groups["pt"].Success ? labeled.Groups["pt"].Value : null);
            if (name.Length > 0)
            {
                return name;
            }
        }

        // Три слова подряд с заглавных букв сразу после заголовка / пункта 1.
        foreach (var line in lines.Take(40))
        {
            var m = Regex.Match(
                line,
                @"(?:^\s*1[\.\)]\s*(?:фамилия\s*[:\.]?\s*)?|(?:ф\.?\s*и\.?\s*о\.?|фио)\s*[:\.]?\s*)(?<fio>[А-ЯЁ][а-яё\-]+(?:\s+[А-ЯЁ][а-яё\-]+){1,3})",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success)
            {
                var name = CleanPersonName(m.Groups["fio"].Value);
                if (name.Length > 0)
                {
                    return name;
                }
            }

            // Строка целиком — только ФИО из 2–4 слов.
            var onlyName = CleanPersonName(line);
            if (onlyName.Length > 0
                && !line.Contains("заключен", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("медицинск", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("фамилия", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("работодател", StringComparison.OrdinalIgnoreCase))
            {
                return onlyName;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Ячейки таблицы: сначала подписи «1. Фамилия», «2. Имя», «3. Отчество», затем значения в следующих ячейках.
    /// </summary>
    private static string TryExtractFioFromLabelValueTable(IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (!IsNameFieldLabel(lines[i], "фамилия") && !Regex.IsMatch(lines[i], @"^\s*1[\.\)]\s*фамилия", RegexOptions.IgnoreCase))
            {
                continue;
            }

            var labelIdx = new List<int> { i };
            for (var j = i + 1; j < Math.Min(lines.Count, i + 8) && labelIdx.Count < 3; j++)
            {
                if (IsNameFieldLabel(lines[j], "имя") || IsNameFieldLabel(lines[j], "отчество") ||
                    Regex.IsMatch(lines[j], @"^\s*[23][\.\)]\s*(?:имя|отчеств)?", RegexOptions.IgnoreCase))
                {
                    labelIdx.Add(j);
                    continue;
                }

                // Между подписями иногда пустые/служебные ячейки — не сбиваем поиск.
                if (IsOnlyLabel(lines[j]) || LooksLikeNumberedField(lines[j]))
                {
                    break;
                }
            }

            if (labelIdx.Count < 2)
            {
                continue;
            }

            var values = new List<string>();
            var start = labelIdx[^1] + 1;
            for (var j = start; j < Math.Min(lines.Count, start + 12) && values.Count < 3; j++)
            {
                var part = CleanPersonNamePart(lines[j]);
                if (part.Length == 0 || IsNameFieldLabel(lines[j], "фамилия") || IsNameFieldLabel(lines[j], "имя") ||
                    IsNameFieldLabel(lines[j], "отчество") || LooksLikeNumberedField(lines[j]) ||
                    lines[j].Contains("дата рождения", StringComparison.OrdinalIgnoreCase))
                {
                    if (values.Count > 0)
                    {
                        break;
                    }

                    continue;
                }

                // Одно слово-имя; если в ячейке сразу полное ФИО — берём его.
                if (part.Contains(' '))
                {
                    var full = CleanPersonName(part);
                    if (full.Length > 0)
                    {
                        return full;
                    }
                }

                values.Add(part.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]);
            }

            var joined = JoinPersonName(
                values.ElementAtOrDefault(0),
                values.ElementAtOrDefault(1),
                values.ElementAtOrDefault(2));
            if (joined.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2)
            {
                return joined;
            }
        }

        return string.Empty;
    }

    private static bool IsNameFieldLabel(string line, string kind)
    {
        var t = CleanValue(line).ToLowerInvariant().Replace('ё', 'е');
        t = Regex.Replace(t, @"^\d+[\.\)]\s*", string.Empty).Trim();
        if (kind == "имя")
        {
            return t is "имя" || (t.StartsWith("имя") && !t.Contains("отчеств") && !t.Contains("наименован") && t.Length <= 8);
        }

        return t == kind || t.StartsWith(kind + " ") || t.StartsWith(kind + ":");
    }

    private static string DetectExamType(string text)
    {
        var t = text.ToLowerInvariant().Replace('ё', 'е');
        if (t.Contains("внеочередн"))
        {
            return "Внеочередной";
        }

        if (t.Contains("предварительн") || t.Contains("при поступлении"))
        {
            return "Предварительный";
        }

        return "Периодический";
    }

    public static string MapConclusion(string? resultText)
    {
        var mapped = TryMapConclusion(resultText);
        if (!string.IsNullOrWhiteSpace(mapped))
        {
            return mapped;
        }

        return string.IsNullOrWhiteSpace(resultText) ? string.Empty : "Годен";
    }

    /// <summary>
    /// Результат заключения из текста бланка/PDF.
    /// «медицинских противопоказаний не выявлено» → Годен;
    /// при наличии противопоказаний → Ограниченно годен / Не годен.
    /// </summary>
    public static string? TryMapConclusion(string? resultText)
    {
        var t = NormalizeConclusionText(resultText);
        if (t.Length < 4)
        {
            return null;
        }

        // Явно: противопоказаний нет.
        if (HasNoMedicalContraindications(t))
        {
            return "Годен";
        }

        if (t.Contains("не пройден", StringComparison.Ordinal))
        {
            return "Не пройден";
        }

        if (t.Contains("ограниченно годен", StringComparison.Ordinal)
            || (t.Contains("ограничен", StringComparison.Ordinal)
                && t.Contains("годен", StringComparison.Ordinal)
                && !t.Contains("не годен", StringComparison.Ordinal)))
        {
            return "Ограниченно годен";
        }

        if (t.Contains("не годен", StringComparison.Ordinal)
            || t.Contains("негоден", StringComparison.Ordinal)
            || HasMedicalContraindications(t))
        {
            return "Не годен";
        }

        if (t.Contains("годен", StringComparison.Ordinal)
            && !t.Contains("не годен", StringComparison.Ordinal)
            && !t.Contains("ограничен", StringComparison.Ordinal))
        {
            return "Годен";
        }

        return null;
    }

    private static string NormalizeConclusionText(string? raw)
    {
        var t = (raw ?? string.Empty).ToLowerInvariant().Replace('ё', 'е');
        t = t.Replace('\u00a0', ' ');
        // OCR часто вставляет мусор между буквами («выяв„лены», «выямены»).
        t = t.Replace('„', ' ').Replace('«', ' ').Replace('»', ' ').Replace('"', ' ');
        t = Regex.Replace(t, @"\s+", " ").Trim();
        // OCR: выямены / выяюены / выяюлены / ВЫЯШЕНЫ / выяалены → не выявлены
        t = Regex.Replace(t, @"не\s*выя\w*", "не выявлены");
        // OCR: пуивопоказания / поотивопоказания / противопоказ→ния
        t = Regex.Replace(t, @"п\w{0,3}тивопоказ\w*", "противопоказания");
        return t;
    }

    private static bool HasNoMedicalContraindications(string t) =>
        Regex.IsMatch(t, @"противопоказани\w*\s+не\s+выявлен")
        || Regex.IsMatch(t, @"не\s+выявлен\w*\s+[^.…]{0,40}противопоказан")
        || t.Contains("медицинских противопоказаний не выявлено", StringComparison.Ordinal)
        || t.Contains("медицинские противопоказания не выявлены", StringComparison.Ordinal)
        || t.Contains("противопоказаний не выявлено", StringComparison.Ordinal)
        || t.Contains("противопоказания не выявлены", StringComparison.Ordinal)
        || (t.Contains("противопоказан", StringComparison.Ordinal)
            && Regex.IsMatch(t, @"не\s+выявлен"));

    private static bool HasMedicalContraindications(string t)
    {
        if (!t.Contains("противопоказан", StringComparison.Ordinal))
        {
            return false;
        }

        // Уже отфильтровали «не выявлено» выше; здесь — положительные формулировки.
        return Regex.IsMatch(t, @"противопоказани\w*\s+выявлен")
               || t.Contains("имеются медицинские противопоказания", StringComparison.Ordinal)
               || t.Contains("выявлены медицинские противопоказания", StringComparison.Ordinal)
               || t.Contains("выявлено медицинское противопоказание", StringComparison.Ordinal)
               || t.Contains("наличие медицинских противопоказаний", StringComparison.Ordinal)
               || t.Contains("есть медицинские противопоказания", StringComparison.Ordinal);
    }

    /// <summary>
    /// Дата медосмотра в отчёте = дата выдачи заключения.
    /// Приоритет: «дата выдачи заключения» → запасные метки → дата осмотра → карточка.
    /// </summary>
    public static DateTime? TryMapExamDate(string? documentText)
    {
        var text = (documentText ?? string.Empty).Trim();
        if (text.Length < 8)
        {
            return null;
        }

        var lines = text
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => Regex.Replace(l.Replace('\u00a0', ' '), @"\s+", " ").Trim())
            .Where(l => l.Length > 0)
            .ToList();

        DateTime? FromLines(Func<string, bool> predicate)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var lower = line.ToLowerInvariant().Replace('ё', 'е');
                if (!predicate(lower))
                {
                    continue;
                }

                // Дата должна быть ПОСЛЕ метки — иначе в длинной OCR-строке
                // берётся дата лицензии (18.10.2019) вместо даты выдачи.
                var d = ParseDateAfterLabel(line, lower)
                        ?? ParseDate(ExtractLabeledValue(lines, i));
                if (d is { Year: >= 2000 and <= 2100 } && !IsExcludedDateNearMatch(line, d.Value))
                {
                    return d.Value.Date;
                }

                for (var j = i + 1; j < Math.Min(lines.Count, i + 4); j++)
                {
                    d = ParseDate(lines[j]);
                    if (d is { Year: >= 2000 and <= 2100 }
                        && !IsExcludedDateNearMatch(lines[j], d.Value))
                    {
                        return d.Value.Date;
                    }
                }
            }

            return null;
        }

        // 1) «Дата выдачи Заключения, печать медицинской организации» — основной источник
        // в колонку «дата медосмотра». Не путать с «Дата подписи» в штампе ЭЦП
        // и сроком «Действителен с … по …».
        var issueDate = FromLines(IsIssueConclusionDateLabel);
        if (issueDate is not null)
        {
            return issueDate;
        }

        var tailDate = TryMapIssueDateFallback(lines);
        if (tailDate is not null)
        {
            return tailDate;
        }

        // 2) Запасной вариант — дата осмотра / проведения.
        var examDate = FromLines(lower =>
            (lower.Contains("дата осмотра") || lower.Contains("дата проведения")
             || lower.Contains("осмотр проведен") || lower.Contains("осмотр проведён")
             || (lower.Contains("дата") && lower.Contains("медицинского осмотра")))
            && !lower.Contains("очередн")
            && !lower.Contains("следующ")
            && !lower.Contains("выдач"));
        if (examDate is not null)
        {
            return examDate;
        }

        var cards = ParseLines(lines);
        var fromCard = cards
            .Select(c => c.ConclusionDate)
            .FirstOrDefault(d => d is { Year: >= 2000 and <= 2100 });
        return fromCard?.Date;
    }

    /// <summary>
    /// Метка бланка: «Дата выдачи Заключения…» (OCR: выдач/водач, заключен/заточен…).
    /// Не «Дата подписи» ЭЦП и не срок действия сертификата.
    /// </summary>
    private static bool IsIssueConclusionDateLabel(string lower)
    {
        if (string.IsNullOrWhiteSpace(lower))
        {
            return false;
        }

        if (lower.Contains("дата подписи", StringComparison.Ordinal)
            || lower.Contains("действителен", StringComparison.Ordinal)
            || lower.Contains("лиценз", StringComparison.Ordinal))
        {
            return false;
        }

        // Полная/усечённая формулировка бланка.
        if (lower.Contains("дата выдачи заключения", StringComparison.Ordinal)
            || lower.Contains("дата выдачи заключен", StringComparison.Ordinal)
            || lower.Contains("дата выдачи заточен", StringComparison.Ordinal) // OCR
            || (lower.Contains("дата выдач", StringComparison.Ordinal)
                && (lower.Contains("заключен", StringComparison.Ordinal)
                    || lower.Contains("заточен", StringComparison.Ordinal)
                    || lower.Contains("печать", StringComparison.Ordinal)
                    || lower.Contains("медицин", StringComparison.Ordinal))))
        {
            return true;
        }

        // Короткая «дата выдачи» без чужого контекста.
        if (lower.Contains("дата выдачи", StringComparison.Ordinal)
            || lower.Contains("дата выдач", StringComparison.Ordinal)
            || lower.Contains("дата водачи", StringComparison.Ordinal)) // OCR в/в
        {
            return !lower.Contains("подпис", StringComparison.Ordinal);
        }

        return lower.Contains("дата", StringComparison.Ordinal)
               && lower.Contains("заключен", StringComparison.Ordinal)
               && !lower.Contains("подпис", StringComparison.Ordinal);
    }

    /// <summary>Дата после метки на той же строке (не первая дата во всём тексте).</summary>
    private static DateTime? ParseDateAfterLabel(string line, string lower)
    {
        var labelIdx = -1;
        foreach (var label in new[]
                 {
                     "дата выдачи заключения", "дата выдачи заключен", "дата выдачи заточен",
                     "дата выдачи", "дата выдач", "дата водачи",
                     "дата осмотра", "дата проведения", "осмотр проведен", "осмотр проведён"
                 })
        {
            var idx = lower.IndexOf(label, StringComparison.Ordinal);
            if (idx >= 0 && (labelIdx < 0 || idx < labelIdx))
            {
                labelIdx = idx;
            }
        }

        if (labelIdx < 0 && lower.Contains("дата", StringComparison.Ordinal)
            && (lower.Contains("заключен", StringComparison.Ordinal)
                || lower.Contains("заточен", StringComparison.Ordinal)))
        {
            labelIdx = lower.IndexOf("дата", StringComparison.Ordinal);
        }

        if (labelIdx < 0)
        {
            return ParseDate(line);
        }

        // Ищем дату в хвосте после метки; если нет — первую дату справа от метки в исходной строке.
        var after = line[Math.Min(labelIdx, line.Length)..];
        foreach (Match m in DateRx.Matches(after).Cast<Match>()
                     .Concat(DateSpacedRx.Matches(after).Cast<Match>()))
        {
            if (TryBuildDate(m, out var d) && d.Year is >= 2000 and <= 2100)
            {
                return d;
            }
        }

        return null;
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

    /// <summary>
    /// Отсекает дату лицензии / рождения по локальному контексту вокруг совпадения,
    /// а не по всей OCR-строке (там почти всегда есть и «лицензия», и «дата выдачи»).
    /// </summary>
    private static bool IsExcludedDateNearMatch(string line, DateTime date)
    {
        var lower = line.ToLowerInvariant().Replace('ё', 'е');
        var dateToken = $"{date.Day:00}.{date.Month:00}.{date.Year}";
        var dateTokenAlt = $"{date.Day}.{date.Month}.{date.Year}";
        var idx = lower.IndexOf(dateToken, StringComparison.Ordinal);
        if (idx < 0)
        {
            idx = lower.IndexOf(dateTokenAlt, StringComparison.Ordinal);
        }

        if (idx < 0)
        {
            // Дата могла быть с пробелами «18 06 2026».
            idx = lower.IndexOf($"{date.Day:00} {date.Month:00} {date.Year}", StringComparison.Ordinal);
        }

        string window;
        if (idx >= 0)
        {
            var start = Math.Max(0, idx - 48);
            var len = Math.Min(lower.Length - start, 48 + dateToken.Length + 24);
            window = lower.Substring(start, len);
        }
        else
        {
            // Короткая строка — смотрим целиком; длинный OCR-blob без токена не режем глобально.
            if (lower.Length > 160)
            {
                return false;
            }

            window = lower;
        }

        if (window.Contains("рожден", StringComparison.Ordinal)
            || window.Contains("лиценз", StringComparison.Ordinal)
            || window.Contains("огрн", StringComparison.Ordinal)
            || window.Contains("действителен", StringComparison.Ordinal)
            || (window.Contains("дата подписи", StringComparison.Ordinal)
                && !window.Contains("выдач", StringComparison.Ordinal)))
        {
            return true;
        }

        // «от 18.10.2019г.» у лицензии.
        if (Regex.IsMatch(window, @"от\s+\d{1,2}\s*[.\-/]\s*\d{1,2}\s*[.\-/]\s*" + date.Year))
        {
            return true;
        }

        return false;
    }

    private static DateTime? TryMapIssueDateFallback(IReadOnlyList<string> lines)
    {
        // Снизу вверх: «Дата выдачи Заключения…» и дата на следующей строке (как на бланке).
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var lower = lines[i].ToLowerInvariant().Replace('ё', 'е');
            if (!IsIssueConclusionDateLabel(lower)
                && !lower.Contains("водач", StringComparison.Ordinal))
            {
                continue;
            }

            var d = ParseDateAfterLabel(lines[i], lower) ?? ParseDate(lines[i]);
            if (d is { Year: >= 2000 and <= 2100 } && !IsExcludedDateNearMatch(lines[i], d.Value))
            {
                return d.Value.Date;
            }

            if (i + 1 < lines.Count)
            {
                d = ParseDate(lines[i + 1]);
                if (d is { Year: >= 2000 and <= 2100 }
                    && !IsExcludedDateNearMatch(lines[i + 1], d.Value))
                {
                    return d.Value.Date;
                }
            }
        }

        return null;
    }

    /// <summary>Пункты факторов (п. 6.1, п. 6.2 …) из текста заключения.</summary>
    public static IReadOnlyList<string> TryMapItemNumbers(string? documentText)
    {
        var text = (documentText ?? string.Empty).Trim();
        if (text.Length < 3)
        {
            return [];
        }

        var lines = text
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => Regex.Replace(l.Replace('\u00a0', ' '), @"\s+", " ").Trim())
            .Where(l => l.Length > 0)
            .ToList();

        var fromCards = ParseLines(lines)
            .SelectMany(c => c.ItemNumbers)
            .Select(MedItemNumber.Normalize)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (fromCards.Count > 0)
        {
            return DropParentOnlyItemNumbers(fromCards);
        }

        return ExtractItemNumbers(text);
    }

    private static List<string> ExtractItemNumbers(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        foreach (Match m in ItemRx.Matches(text))
        {
            var n = MedItemNumber.Normalize(m.Groups["n"].Value);
            if (n.Length > 0 && !result.Contains(n, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(n);
            }
        }

        // «п. 6» из заголовка «6. Наименование вредных…» — ложный пункт, если есть 6.1/6.2.
        return DropParentOnlyItemNumbers(result);
    }

    /// <summary>Убирает «6», если в списке уже есть «6.1» / «6.2».</summary>
    private static List<string> DropParentOnlyItemNumbers(List<string> items)
    {
        if (items.Count <= 1)
        {
            return items;
        }

        return items
            .Where(n =>
            {
                if (n.Contains('.', StringComparison.Ordinal))
                {
                    return true;
                }

                // Целое «6» — только если нет дочерних «6.*».
                return !items.Any(other =>
                    other.Length > n.Length
                    && other.StartsWith(n + ".", StringComparison.OrdinalIgnoreCase));
            })
            .ToList();
    }

    private static string ExtractLabeledValue(IReadOnlyList<string> lines, int index, params string[] stopMarkers)
    {
        var line = lines[index];
        var afterLabel = ExtractValueFromLabeledLine(line);
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(afterLabel) && !IsOnlyLabel(afterLabel))
        {
            sb.Append(afterLabel);
        }

        for (var j = index + 1; j < Math.Min(lines.Count, index + 8); j++)
        {
            var next = lines[j];
            if (stopMarkers.Any(s => next.Contains(s, StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }

            if (IsTitle(next) || LooksLikeNumberedField(next))
            {
                break;
            }

            // Не приклеиваем дату рождения / пол к ФИО.
            var nextLower = next.ToLowerInvariant().Replace('ё', 'е');
            if (DateRx.IsMatch(next) ||
                nextLower is "м" or "ж" or "муж" or "жен" ||
                nextLower.StartsWith("мужск") || nextLower.StartsWith("женск") ||
                nextLower.Contains("дата рождения") ||
                nextLower.Contains("пол работника"))
            {
                break;
            }

            if (IsOnlyLabel(next))
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(next);
        }

        return CleanValue(sb.ToString());
    }

    /// <summary>Значение после «Фамилия:», «1. Фамилия», «1. Фамилия Горбатенко».</summary>
    private static string ExtractValueFromLabeledLine(string line)
    {
        var afterColon = ExtractAfterColon(line);
        if (!string.IsNullOrWhiteSpace(afterColon) && !IsOnlyLabel(afterColon))
        {
            return afterColon.Trim();
        }

        var m = Regex.Match(
            line,
            @"^\s*\d+[\.\)]\s*(?:фамилия|имя|отчество|ф\.?\s*и\.?\s*о\.?|фио)\s*[:\.]?\s*(.*)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (m.Success)
        {
            var rest = m.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(rest) && !IsOnlyLabel(rest))
            {
                return rest;
            }
        }

        m = Regex.Match(
            line,
            @"(?:фамилия|имя|отчество|ф\.?\s*и\.?\s*о\.?|фио)\s*[:\.]?\s*(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (m.Success)
        {
            var rest = m.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(rest) && !IsOnlyLabel(rest))
            {
                return rest;
            }
        }

        return string.Empty;
    }

    private static string ExtractAfterColon(string line)
    {
        var idx = line.IndexOf(':');
        return idx < 0 ? string.Empty : line[(idx + 1)..].Trim();
    }

    private static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var m = DateRx.Match(text);
        if (!m.Success)
        {
            m = DateSpacedRx.Match(text);
        }

        if (!m.Success)
        {
            return null;
        }

        if (!int.TryParse(m.Groups["d"].Value, out var d) ||
            !int.TryParse(m.Groups["m"].Value, out var mo) ||
            !int.TryParse(m.Groups["y"].Value, out var y))
        {
            return null;
        }

        try
        {
            return new DateTime(y, mo, d);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeGender(string? raw)
    {
        var t = CleanValue(raw).ToLowerInvariant();
        if (t.StartsWith('ж') || t.Contains("жен"))
        {
            return "Ж";
        }

        if (t.StartsWith('м') || t.Contains("муж"))
        {
            return "М";
        }

        return CleanValue(raw);
    }

    private static bool LooksLikeNumberedField(string line) =>
        Regex.IsMatch(line.TrimStart(), @"^\d{1,2}(\.\d+)?[\.\)]\s+\S");

    private static bool LooksLikeFieldStart(string line)
    {
        var lower = line.ToLowerInvariant();
        return lower.Contains("фамилия") || lower.Contains("ф.и.о") ||
               Regex.IsMatch(line.TrimStart(), @"^1[\.\)]");
    }

    private static bool IsTitle(string line)
    {
        var t = line.Replace('\u00ad', ' ');
        if (TitleRx.IsMatch(t) || ShortTitleRx.IsMatch(t))
        {
            return true;
        }

        var lower = t.ToLowerInvariant().Replace('ё', 'е');
        return lower.Contains("заключение") &&
               (lower.Contains("медицинского осмотра") ||
                (lower.Contains("предварительного") && lower.Contains("медицинск")) ||
                (lower.Contains("периодического") && lower.Contains("медицинск")));
    }

    private static bool IsOnlyLabel(string value)
    {
        var t = CleanValue(value).ToLowerInvariant().Replace('ё', 'е');
        t = Regex.Replace(t, @"^\d+[\.\)]\s*", string.Empty).Trim();
        return t is "фамилия" or "имя" or "отчество" or "дата рождения" or "пол"
            or "ф.и.о" or "ф.и.о." or "фио" or "пол работника";
    }

    /// <summary>Одно слово ФИО (фамилия / имя / отчество) — без требования «2–4 слова».</summary>
    private static string CleanPersonNamePart(string value)
    {
        var t = CleanValue(value);
        t = Regex.Replace(t, @"\s+\d{1,2}[./]\d{1,2}[./]\d{2,4}.*$", string.Empty);
        t = Regex.Replace(t, @"^\d+[\.\)]\s*", string.Empty);
        t = Regex.Replace(t, @"^(?:фамилия|имя|отчество|ф\.?\s*и\.?\s*о\.?|фио)[:\s]*", string.Empty,
            RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\s*[\(\[\{]\s*\d{1,3}\s*[\)\]\}]", string.Empty);
        t = Regex.Replace(t, @"\b(?:фамилия|имя|отчество|дата|рождения|пол|работника|наименование)\b[:\s]*",
            " ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"[^A-Za-zА-Яа-яЁёІіЇїЄє\-'\s]", " ");
        t = CleanValue(t);
        var parts = t.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Length >= 2 && p.Any(char.IsLetter) && !p.Any(char.IsDigit))
            .Where(p => !FormGarbageWords.Contains(p))
            .ToArray();
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        // В поле «Фамилия» иногда попадает всё ФИО — берём первое слово; иначе всё до 3.
        if (parts.Length == 1)
        {
            return parts[0];
        }

        return string.Join(' ', parts.Take(3));
    }

    private static string CleanPersonName(string value)
    {
        var t = CleanValue(value);
        // Обрезаем хвост после даты / пола / следующего пункта формы.
        t = Regex.Replace(t, @"\s+\d{1,2}[./]\d{1,2}[./]\d{2,4}.*$", string.Empty);
        t = Regex.Replace(t, @"\s+[МЖмж]\s*(?:\(|$|,).*", string.Empty);
        t = Regex.Replace(t, @"^\d+[\.\)]\s*", string.Empty);
        t = Regex.Replace(t, @"^(?:фамилия|имя|отчество|ф\.?\s*и\.?\s*о\.?)[:\s]*", string.Empty,
            RegexOptions.IgnoreCase);
        // Мусор из Word/PDF/полей формы: Горбатенко(1), Иванов[2]
        t = Regex.Replace(t, @"\s*[\(\[\{]\s*\d{1,3}\s*[\)\]\}]", string.Empty);
        t = Regex.Replace(t, @"\b(?:фамилия|имя|отчество|дата рождения|пол работника|наименование)\b[:\s]*",
            " ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\d+[\.\)]\s*", " ");
        // Оставляем буквы ФИО (кириллица/латиница), дефис, апостроф, пробелы.
        t = Regex.Replace(t, @"[^A-Za-zА-Яа-яЁёІіЇїЄє\-'\s]", " ");
        t = CleanValue(t);

        var parts = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Форма часто склеивает ФИО с должностью — берём первые 2–4 слова-имени.
        if (parts.Length > 4)
        {
            parts = parts.Take(3).ToArray();
            t = string.Join(' ', parts);
        }

        // Отбрасываем явный мусор («заключение», «медицинского»…).
        if (parts.Any(p => FormGarbageWords.Contains(p)))
        {
            parts = parts.Where(p => !FormGarbageWords.Contains(p)).ToArray();
            t = string.Join(' ', parts);
        }

        if (!LooksLikePersonName(t))
        {
            return string.Empty;
        }

        return t;
    }

    private static readonly HashSet<string> FormGarbageWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "заключение", "предварительного", "периодического", "медицинского", "осмотра",
        "работника", "работодателя", "структурное", "подразделение", "должность",
        "вредных", "факторов", "результатов", "годен", "противопоказаний"
    };

    private static bool LooksLikePersonName(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 2 or > 4)
        {
            return false;
        }

        return parts.All(p =>
            p.Length >= 2 &&
            p.Any(char.IsLetter) &&
            !p.Any(char.IsDigit));
    }

    /// <summary>Публичная очистка ФИО из заключения перед сопоставлением со штатом.</summary>
    public static string CleanImportedPersonName(string? value)
    {
        var raw = value ?? string.Empty;
        var cleaned = CleanPersonName(raw);
        if (cleaned.Length > 0)
        {
            return cleaned;
        }

        // Уже собранное «Фамилия Имя [Отчество]» не отбрасываем, если строгая очистка не прошла.
        var parts = CleanValue(raw)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(CleanPersonNamePart)
            .Where(p => p.Length > 0)
            .Take(4)
            .ToArray();
        return parts.Length >= 2 ? string.Join(' ', parts) : string.Empty;
    }

    private static string CleanValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var t = CollapseSpaces(value)
            .Trim(' ', '.', ';', ':', '—', '-', '\u00a0');
        var lower = t.ToLowerInvariant();
        if (lower is "отсутствует" or "отсутствуют" or "-" or "—" or "нет")
        {
            return string.Empty;
        }

        return t;
    }

    private static List<string> ExtractLines(Body body)
    {
        var lines = new List<string>();
        foreach (var child in body.ChildElements)
        {
            if (child is Paragraph para)
            {
                var text = CollapseSpaces(para.InnerText ?? string.Empty);
                if (text.Length > 0)
                {
                    lines.Add(text);
                }

                continue;
            }

            if (child is not Table table)
            {
                continue;
            }

            foreach (var row in table.Elements<TableRow>())
            {
                foreach (var cell in row.Elements<TableCell>())
                {
                    var text = CollapseSpaces(cell.InnerText ?? string.Empty);
                    if (text.Length > 0)
                    {
                        lines.Add(text);
                    }
                }
            }
        }

        return lines;
    }

    private static string CollapseSpaces(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        var prevSpace = false;
        foreach (var ch in value.Replace('\u00a0', ' '))
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!prevSpace)
                {
                    sb.Append(' ');
                    prevSpace = true;
                }
            }
            else
            {
                sb.Append(ch);
                prevSpace = false;
            }
        }

        return sb.ToString().Trim();
    }
}
