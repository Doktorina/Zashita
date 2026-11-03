using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

/// <summary>
/// Разбор Word с картами СОУТ (форма «КАРТА № … специальной оценки условий труда»).
/// </summary>
public static class SoutCardDocxImportParser
{
    /// <summary>Номер карты: 572, 25А, 1-25, 1/25.</summary>
    private const string CardNumberPattern = @"(?<n>\d+(?:[-\/]\d+)*[А-Яа-яA-Za-z]?)";

    /// <summary>Полный заголовок в одной строке (или после склейки ячеек/соседних строк).</summary>
    private static readonly Regex CardTitleRx = new(
        @"КАРТА\s*(?:№|N[oо]?\.?|Ne|#)\s*" + CardNumberPattern +
        @"\s*специальной\s+оценки(?:\s+условий\s+труда)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>«КАРТА специальной оценки … № 172» — номер в конце строки.</summary>
    private static readonly Regex CardTitleNumberAtEndRx = new(
        @"КАРТА\s+специальной\s+оценки(?:\s+условий\s+труда)?\s*(?:№|N[oо]?\.?|Ne|#)\s*" + CardNumberPattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>«КАРТА № 572» / «КАРТА № 1-25» без хвоста — хвост часто в соседней ячейке/абзаце.</summary>
    private static readonly Regex CardTitleShortRx = new(
        @"^КАРТА\s*(?:№|N[oо]?\.?|Ne|#)\s*" + CardNumberPattern + @"\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CardNumberInTitleRx = new(
        @"КАРТА\s*(?:№|N[oо]?\.?|Ne|#)\s*" + CardNumberPattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CardTitleTailRx = new(
        @"специальной\s+оценки(?:\s+условий\s+труда)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DateRx = new(
        @"(?<d>\d{1,2})[.\-/](?<m>\d{1,2})[.\-/](?<y>\d{4})",
        RegexOptions.Compiled);

    private static readonly Regex OkCodeRx = new(
        @"Код\s+профессии\s*\(должности\)\s*:?\s*(?<c>[\d\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SnilsRx = new(
        @"\d{3}[-\s]?\d{3}[-\s]?\d{3}[-\s]?\d{2}",
        RegexOptions.Compiled);

    public static IReadOnlyList<ParsedSoutCard> Parse(Stream docxStream)
    {
        using var repaired = DocxPackageRepair.ToRepairedMemoryStream(docxStream);
        using var doc = WordprocessingDocument.Open(repaired, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return [];
        }

        var lines = ExtractLines(body);
        return ParseLines(lines);
    }

    public static IReadOnlyList<ParsedSoutCard> ParseFile(string path)
    {
        using var fs = File.OpenRead(path);
        return Parse(fs);
    }

    internal static IReadOnlyList<ParsedSoutCard> ParseLines(IReadOnlyList<string> lines)
    {
        var cards = new List<ParsedSoutCard>();
        ParsedSoutCard? current = null;
        var pendingOrg = string.Empty;
        var i = 0;
        while (i < lines.Count)
        {
            var line = lines[i];
            if (IsIncludeNoise(line))
            {
                i++;
                continue;
            }

            if (LooksLikeOrganizationName(line) &&
                (current is null || string.IsNullOrWhiteSpace(current.Organization)))
            {
                pendingOrg = CleanOrgName(line);
            }

            if (TryMatchCardTitle(lines, i, out var cardNumber, out var consumed))
            {
                current = new ParsedSoutCard
                {
                    CardNumber = cardNumber,
                    Organization = pendingOrg
                };
                cards.Add(current);
                i = FillCard(current, lines, i + consumed);
                continue;
            }

            i++;
        }

        return cards
            .Where(c => !string.IsNullOrWhiteSpace(c.Profession) || !string.IsNullOrWhiteSpace(c.CardNumber))
            .ToList();
    }

    private static bool TryMatchCardTitle(
        IReadOnlyList<string> lines,
        int index,
        out string cardNumber,
        out int consumed)
    {
        cardNumber = string.Empty;
        consumed = 1;
        if (index < 0 || index >= lines.Count)
        {
            return false;
        }

        var normalized = NormalizeForTitle(lines[index]);
        if (normalized.Length == 0)
        {
            return false;
        }

        var full = CardTitleRx.Match(normalized);
        if (full.Success)
        {
            cardNumber = full.Groups["n"].Value.Trim();
            return true;
        }

        var numberAtEnd = CardTitleNumberAtEndRx.Match(normalized);
        if (numberAtEnd.Success)
        {
            cardNumber = numberAtEnd.Groups["n"].Value.Trim();
            return true;
        }

        var numberMatch = CardNumberInTitleRx.Match(normalized);
        if (!numberMatch.Success)
        {
            return false;
        }

        // Номер и хвост уже в одной строке (с подписью организации и т.п.).
        if (CardTitleTailRx.IsMatch(normalized))
        {
            cardNumber = numberMatch.Groups["n"].Value.Trim();
            return true;
        }

        // Хвост «специальной оценки…» в соседних строках/ячейках.
        var combined = normalized;
        for (var j = 1; j <= 4 && index + j < lines.Count; j++)
        {
            var next = NormalizeForTitle(lines[index + j]);
            if (next.Length == 0 || IsIncludeNoise(next))
            {
                continue;
            }

            if (StartsWithRow(next) ||
                Regex.IsMatch(next, @"^КАРТА\s*(?:№|N[oо]?\.?|Ne|#)\s*\d", RegexOptions.IgnoreCase))
            {
                break;
            }

            combined = CollapseSpaces(combined + " " + next);
            if (CardTitleTailRx.IsMatch(next) || CardTitleRx.IsMatch(combined))
            {
                cardNumber = numberMatch.Groups["n"].Value.Trim();
                consumed = j + 1;
                return true;
            }
        }

        // Короткая строка только «КАРТА № N» — принимаем, если рядом есть хвост формы СОУТ.
        if (CardTitleShortRx.IsMatch(normalized))
        {
            for (var j = 1; j <= 6 && index + j < lines.Count; j++)
            {
                var next = NormalizeForTitle(lines[index + j]);
                if (CardTitleTailRx.IsMatch(next) ||
                    next.Contains("Наименование профессии", StringComparison.OrdinalIgnoreCase) ||
                    next.Contains("Строка 010", StringComparison.OrdinalIgnoreCase) ||
                    next.Contains("Строка 020", StringComparison.OrdinalIgnoreCase))
                {
                    cardNumber = numberMatch.Groups["n"].Value.Trim();
                    consumed = 1;
                    return true;
                }

                if (StartsWithRow(next) && !next.Contains("010", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Граница следующей карты внутри FillCard: полный заголовок или «КАРТА № N» + хвост рядом.
    /// Без эвристики «рядом профессия» — иначе обрыв на дубликатах ячеек той же карты.
    /// </summary>
    private static bool IsCardTitleBoundary(IReadOnlyList<string> lines, int index)
    {
        if (index < 0 || index >= lines.Count)
        {
            return false;
        }

        var normalized = NormalizeForTitle(lines[index]);
        if (CardTitleRx.IsMatch(normalized))
        {
            return true;
        }

        if (!CardTitleShortRx.IsMatch(normalized) &&
            !Regex.IsMatch(normalized, @"^КАРТА\s*(?:№|N[oо]?\.?|Ne|#)\s*" + CardNumberPattern + @"\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return false;
        }

        for (var j = 1; j <= 2 && index + j < lines.Count; j++)
        {
            var next = NormalizeForTitle(lines[index + j]);
            if (next.Length == 0 || IsIncludeNoise(next))
            {
                continue;
            }

            return CardTitleTailRx.IsMatch(next);
        }

        return false;
    }

    private static string NormalizeForTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            // Мягкие переносы / zero-width — часто в заголовке из Word.
            if (ch is '\u00ad' or '\u200b' or '\u200c' or '\u200d' or '\ufeff')
            {
                continue;
            }

            if (ch == '\u2116')
            {
                sb.Append('№');
                continue;
            }

            // Латиница, визуально похожая на «КАРТА» (K/A/P/T vs К/А/Р/Т).
            sb.Append(ch switch
            {
                'K' or 'k' => 'К',
                'A' => 'А',
                'a' => 'а',
                'P' or 'p' => 'Р',
                'T' or 't' => 'Т',
                'H' or 'h' => 'Н',
                'O' => 'О',
                'o' => 'о',
                'C' => 'С',
                'c' => 'с',
                'E' => 'Е',
                'e' => 'е',
                'B' => 'В',
                _ => ch
            });
        }

        var text = CollapseSpaces(sb.ToString());
        text = text
            .Replace("No.", "№", StringComparison.OrdinalIgnoreCase)
            .Replace("Nо.", "№", StringComparison.OrdinalIgnoreCase)
            .Replace("Nо", "№", StringComparison.OrdinalIgnoreCase)
            .Replace("Nº", "№", StringComparison.OrdinalIgnoreCase)
            .Replace("N°", "№", StringComparison.OrdinalIgnoreCase);
        return CollapseSpaces(text);
    }

    /// <summary>Образец строк из Word, если карты не распознаны — для сообщения об ошибке.</summary>
    public static string DescribeUnrecognized(Stream docxStream)
    {
        try
        {
            using var repaired = DocxPackageRepair.ToRepairedMemoryStream(docxStream);
            using var doc = WordprocessingDocument.Open(repaired, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is null)
            {
                return "в файле пустое тело документа";
            }

            var lines = ExtractLines(body)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => CollapseSpaces(l))
                .Where(l => l.Length > 0)
                .ToList();
            if (lines.Count == 0)
            {
                return "текст в .docx не извлечён (возможно скан без текстового слоя)";
            }

            var interesting = lines
                .Where(l =>
                    l.Contains("карт", StringComparison.OrdinalIgnoreCase)
                    || l.Contains("соут", StringComparison.OrdinalIgnoreCase)
                    || l.Contains("оценк", StringComparison.OrdinalIgnoreCase)
                    || l.Contains("професс", StringComparison.OrdinalIgnoreCase))
                .Take(4)
                .Select(ShortSample)
                .ToList();
            if (interesting.Count == 0)
            {
                interesting = lines.Take(5).Select(ShortSample).ToList();
            }

            return string.Join(" · ", interesting);
        }
        catch (Exception ex)
        {
            return "не удалось прочитать файл: " + ex.Message;
        }
    }

    private static string ShortSample(string line) =>
        line.Length <= 90 ? line : line[..90] + "…";

    private static int FillCard(ParsedSoutCard card, IReadOnlyList<string> lines, int start)
    {
        var i = start;
        while (i < lines.Count)
        {
            var line = lines[i];
            if (IsCardTitleBoundary(lines, i) || IsIncludeNoise(line) && i > start + 2)
            {
                break;
            }

            if (IsIncludeNoise(line))
            {
                i++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(card.Profession) && IsProfessionFormLabel(line))
            {
                var fromLabel = ExtractProfessionValue(line);
                if (!string.IsNullOrWhiteSpace(fromLabel))
                {
                    card.Profession = fromLabel;
                    i++;
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(card.Profession) &&
                !IsHintLabel(line) &&
                !StartsWithRow(line) &&
                !LooksLikeOrganizationName(line) &&
                !IsProfessionFormLabel(line) &&
                !CardTitleShortRx.IsMatch(NormalizeForTitle(line)) &&
                !CardTitleTailRx.IsMatch(NormalizeForTitle(line)) &&
                !line.Contains("Наименование структурного", StringComparison.OrdinalIgnoreCase) &&
                !line.Contains("Количество и номера", StringComparison.OrdinalIgnoreCase) &&
                line.Length is >= 2 and <= 180)
            {
                card.Profession = CleanValue(line);
                i++;
                continue;
            }

            if (line.Contains("Наименование структурного подразделения", StringComparison.OrdinalIgnoreCase))
            {
                var dept = NormalizeAbsent(ExtractAfterColon(line));
                if (string.IsNullOrWhiteSpace(dept))
                {
                    dept = NormalizeAbsent(StripLabeledPrefix(line,
                        "Наименование структурного подразделения"));
                }

                card.Department = dept;
                i++;
                continue;
            }

            if (line.Contains("Количество и номера аналогичных", StringComparison.OrdinalIgnoreCase))
            {
                var analog = NormalizeAbsent(ExtractAfterColon(line));
                if (string.IsNullOrWhiteSpace(analog))
                {
                    analog = NormalizeAbsent(StripLabeledPrefix(line,
                        "Количество и номера аналогичных рабочих мест"));
                }

                card.AnalogousWorkplaces = analog;
                i++;
                continue;
            }

            if (StartsWithRow(line, "011") || OkCodeRx.IsMatch(line))
            {
                var m = OkCodeRx.Match(line);
                if (m.Success)
                {
                    card.OkCode = CollapseSpaces(m.Groups["c"].Value);
                }

                i++;
                continue;
            }

            if (StartsWithRow(line, "020"))
            {
                i = ReadCounts(card, lines, i + 1);
                continue;
            }

            if (StartsWithRow(line, "021") || line.Contains("СНИЛС работников", StringComparison.OrdinalIgnoreCase))
            {
                i = ReadSnils(card, lines, i + (StartsWithRow(line, "021") ? 1 : 0));
                continue;
            }

            if (StartsWithRow(line, "022") ||
                line.Contains("Используемое производственное оборудование", StringComparison.OrdinalIgnoreCase))
            {
                var (equipment, next) = ReadLabeledBlock(lines, i, "оборудование");
                if (!string.IsNullOrWhiteSpace(equipment))
                {
                    card.Equipment = equipment;
                }

                i = next;
                continue;
            }

            if (line.Contains("Используемые материалы и сырье", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Используемые материалы и сырьё", StringComparison.OrdinalIgnoreCase))
            {
                var (materials, next) = ReadLabeledBlock(lines, i, "материал");
                if (!string.IsNullOrWhiteSpace(materials))
                {
                    card.Materials = materials;
                }

                i = next;
                continue;
            }

            if (line.Contains("Итоговый класс", StringComparison.OrdinalIgnoreCase) ||
                StartsWithRow(line, "030"))
            {
                var parsedClass = ReadClassNearby(lines, i);
                if (!string.IsNullOrWhiteSpace(parsedClass))
                {
                    card.ConditionsClass = parsedClass;
                }

                i++;
                continue;
            }

            if (StartsWithRow(line, "050") ||
                line.Contains("Рекомендации по улучшению", StringComparison.OrdinalIgnoreCase))
            {
                card.Recommendations = CleanValue(ExtractAfterColon(line));
                if (string.IsNullOrWhiteSpace(card.Recommendations))
                {
                    card.Recommendations = CleanValue(line);
                }

                i++;
                continue;
            }

            if (line.Contains("Дата составления", StringComparison.OrdinalIgnoreCase))
            {
                // Дата может быть в той же ячейке, в соседней или на следующей строке.
                card.PreparedDate = ParsePreparedDate(lines, i) ?? card.PreparedDate;
                i++;
                continue;
            }

            if (LooksLikeOrganizationName(line) && string.IsNullOrWhiteSpace(card.Organization))
            {
                card.Organization = CleanOrgName(line);
            }

            i++;
        }

        // Запасной проход: дата/класс могли стоять в таблице отдельно от подписи.
        if (card.PreparedDate is null)
        {
            card.PreparedDate = FindPreparedDateInRange(lines, start, i);
        }

        if (string.IsNullOrWhiteSpace(card.ConditionsClass))
        {
            for (var j = start; j < i; j++)
            {
                var recovered = ReadClassNearby(lines, j);
                if (!string.IsNullOrWhiteSpace(recovered))
                {
                    card.ConditionsClass = recovered;
                    break;
                }
            }
        }

        return i;
    }

    private static int ReadCounts(ParsedSoutCard card, IReadOnlyList<string> lines, int start)
    {
        var i = start;
        string? pendingKey = null;
        while (i < lines.Count)
        {
            var line = lines[i];
            if (StartsWithRow(line) || IsCardTitleBoundary(lines, i))
            {
                break;
            }

            var lower = line.ToLowerInvariant().Replace('ё', 'е');
            if (lower.Contains("на рабочем месте") && !lower.Contains("аналогич") && !lower.Contains("инвалид"))
            {
                pendingKey = "place";
                i++;
                continue;
            }

            if (lower.Contains("на всех аналогичных") ||
                (lower.Contains("аналогичных рабочих") && lower.Contains("местах")))
            {
                pendingKey = "analog";
                i++;
                continue;
            }

            if (lower.StartsWith("женщин") || lower == "женщин")
            {
                pendingKey = "women";
                i++;
                continue;
            }

            if (lower.Contains("до 18"))
            {
                pendingKey = "u18";
                i++;
                continue;
            }

            if (lower.Contains("инвалид"))
            {
                pendingKey = "disabled";
                i++;
                continue;
            }

            if (TryParseCount(line, out var n) && pendingKey is not null)
            {
                switch (pendingKey)
                {
                    case "place": card.WorkerCount = n; break;
                    case "analog": card.AnalogousWorkerCount = n; break;
                    case "women": card.WorkersWomen = n; break;
                    case "u18": card.WorkersUnder18 = n; break;
                    case "disabled": card.WorkersDisabled = n; break;
                }

                pendingKey = null;
                i++;
                continue;
            }

            if (lower is "из них:" or "из них")
            {
                i++;
                continue;
            }

            // «на рабочем месте 1» в одной строке
            if (lower.Contains("на рабочем месте") && TryParseTrailingCount(line, out var placeN))
            {
                card.WorkerCount = placeN;
            }

            i++;
        }

        return i;
    }

    private static int ReadSnils(ParsedSoutCard card, IReadOnlyList<string> lines, int start)
    {
        var i = start;
        while (i < lines.Count)
        {
            var line = lines[i];
            if (StartsWithRow(line) || IsCardTitleBoundary(lines, i) ||
                line.Contains("Используемое", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            foreach (Match m in SnilsRx.Matches(line))
            {
                var digits = OneCEmployeeListParser.NormalizeSnils(m.Value);
                if (digits.Length == 11 && !card.SnilsList.Contains(digits, StringComparer.Ordinal))
                {
                    card.SnilsList.Add(digits);
                }
            }

            i++;
        }

        return i;
    }

    private static string ReadClassNearby(IReadOnlyList<string> lines, int index)
    {
        for (var j = index; j < Math.Min(lines.Count, index + 8); j++)
        {
            var t = lines[j].Trim();
            if (t.Contains("Итоговый класс", StringComparison.OrdinalIgnoreCase)
                || StartsWithRow(t, "030")
                || t.Contains("класс (подкласс)", StringComparison.OrdinalIgnoreCase))
            {
                var after = ExtractAfterColon(t);
                var fromAfter = SoutConditionsClassStore.ExtractSubclassCode(after)
                                ?? SoutConditionsClassStore.ExtractSubclassCode(t);
                if (!string.IsNullOrWhiteSpace(fromAfter))
                {
                    return NormalizeClass(fromAfter);
                }

                continue;
            }

            if (LooksLikeClass(t))
            {
                return NormalizeClass(t);
            }

            // «2 (допустимые)», «3.1 (вредные)» и т.п.
            var code = SoutConditionsClassStore.ExtractSubclassCode(t);
            if (!string.IsNullOrWhiteSpace(code)
                && (t.Contains('(') || Regex.IsMatch(t, @"^[1-4](?:[.,]\d)?$")))
            {
                return NormalizeClass(code);
            }
        }

        return string.Empty;
    }

    private static bool LooksLikeClass(string? value)
    {
        var t = (value ?? string.Empty).Trim();
        if (t.Length == 0)
        {
            return false;
        }

        if (t is "-" or "—" or "–" || t.Contains("не заполн", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return SoutConditionsClassStore.ExtractSubclassCode(t) is not null
               && (Regex.IsMatch(t, @"^[1-4](?:[.,]\d)?$")
                   || t.Contains('(')
                   || t.Contains("класс", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeClass(string raw) =>
        (SoutConditionsClassStore.ExtractSubclassCode(raw) ?? raw.Trim()).Replace(',', '.');

    private static bool TryParseCount(string line, out int value)
    {
        value = 0;
        var t = line.Trim();
        if (t is "-" or "—" or "–" or "" or "нет")
        {
            return true;
        }

        return int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0;
    }

    private static bool TryParseTrailingCount(string line, out int value)
    {
        value = 0;
        var m = Regex.Match(line, @"(\d+)\s*$");
        return m.Success && int.TryParse(m.Groups[1].Value, out value);
    }

    /// <summary>
    /// Дата после подписи «Дата составления» — не дата из блока подписей комиссии/эксперта.
    /// </summary>
    private static DateTime? ParsePreparedDate(IReadOnlyList<string> lines, int index)
    {
        if (index < 0 || index >= lines.Count)
        {
            return null;
        }

        var line = lines[index];
        var labelIdx = line.IndexOf("Дата составления", StringComparison.OrdinalIgnoreCase);
        if (labelIdx >= 0)
        {
            var afterLabel = line[(labelIdx + "Дата составления".Length)..];
            // В одной строке таблицы часто: «Дата составления: 27.10.2025 Председатель … 12.07.2026»
            // — берём только фрагмент ПОСЛЕ подписи «Дата составления», до ролей/подписей.
            // Не парсим всю строку целиком: до подписи могут быть чужие даты.
            var cut = CutBeforeSignatureRoles(afterLabel);
            var fromAfter = ParseDate(cut);
            if (fromAfter is not null)
            {
                return fromAfter;
            }
        }

        // Дата иногда стоит в предыдущей ячейке строки таблицы.
        for (var j = 1; j <= 2 && index - j >= 0; j++)
        {
            var prev = lines[index - j];
            if (string.IsNullOrWhiteSpace(prev) || IsCardTitleBoundary(lines, index - j))
            {
                break;
            }

            if (prev.Contains("Дата составления", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (DateStartsLine(prev) || IsMostlyDateLine(prev))
            {
                var d = ParseDate(prev);
                if (d is not null)
                {
                    return d;
                }
            }
        }

        // На строке только подпись — дата в следующих строках/ячейках таблицы.
        for (var j = 1; j <= 10 && index + j < lines.Count; j++)
        {
            var next = lines[index + j];
            if (string.IsNullOrWhiteSpace(next))
            {
                continue;
            }

            if (IsCardTitleBoundary(lines, index + j) || StartsWithRow(next))
            {
                break;
            }

            if (next.Contains("Дата составления", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var lower = next.ToLowerInvariant().Replace('ё', 'е');
            // Строка-роль без даты в начале — пропускаем, ищем дату дальше.
            if (IsSignatureRoleLine(lower) && !DateStartsLine(next))
            {
                continue;
            }

            // «27.10.2025 ФИО … 12.07.2026» — дата составления обычно первая, в начале строки.
            if (DateStartsLine(next))
            {
                var first = ParseDate(CutBeforeSignatureRoles(next));
                if (first is not null)
                {
                    return first;
                }
            }

            var onlyDate = ParseDate(next);
            if (onlyDate is not null && IsMostlyDateLine(next))
            {
                return onlyDate;
            }
        }

        return null;
    }

    private static DateTime? FindPreparedDateInRange(IReadOnlyList<string> lines, int start, int end)
    {
        for (var i = start; i < end && i < lines.Count; i++)
        {
            var line = lines[i];
            if (!line.Contains("Дата составления", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parsed = ParsePreparedDate(lines, i);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        // Иногда подпись разбита: «Дата» / «составления» / «07.11.2025»
        for (var i = start; i < end - 1 && i < lines.Count; i++)
        {
            var lower = lines[i].ToLowerInvariant().Replace('ё', 'е');
            if (!lower.Contains("составлен", StringComparison.Ordinal))
            {
                continue;
            }

            for (var j = 0; j <= 3 && i + j < end && i + j < lines.Count; j++)
            {
                var candidate = lines[i + j];
                if (DateStartsLine(candidate) || IsMostlyDateLine(candidate))
                {
                    var d = ParseDate(CutBeforeSignatureRoles(candidate));
                    if (d is not null)
                    {
                        return d;
                    }
                }
            }
        }

        return null;
    }

    private static string CutBeforeSignatureRoles(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var lower = text.ToLowerInvariant().Replace('ё', 'е');
        var cut = text.Length;
        foreach (var marker in new[]
                 {
                     "председатель",
                     "член комиссии",
                     "члены комиссии",
                     "эксперт",
                     "эксперт(",
                     "подпись",
                     "м.п"
                 })
        {
            var idx = lower.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0 && idx < cut)
            {
                cut = idx;
            }
        }

        return text[..cut];
    }

    private static bool IsSignatureRoleLine(string lower) =>
        lower.Contains("председатель", StringComparison.Ordinal)
        || lower.Contains("член комиссии", StringComparison.Ordinal)
        || lower.Contains("члены комиссии", StringComparison.Ordinal)
        || lower.Contains("эксперт", StringComparison.Ordinal)
        || lower.Contains("подпись", StringComparison.Ordinal)
        || lower.Contains("м.п", StringComparison.Ordinal)
        || (lower.Contains("дата", StringComparison.Ordinal)
            && !lower.Contains("составления", StringComparison.Ordinal));

    private static bool DateStartsLine(string text)
    {
        var t = (text ?? string.Empty).TrimStart();
        return DateRx.IsMatch(t) && DateRx.Match(t).Index == 0;
    }

    private static bool IsMostlyDateLine(string text)
    {
        var stripped = DateRx.Replace(text ?? string.Empty, string.Empty);
        stripped = Regex.Replace(stripped, @"[\s._\-—–:]+", string.Empty);
        return stripped.Length <= 2;
    }

    private static DateTime? ParseDate(string text)
    {
        var m = DateRx.Match(text ?? string.Empty);
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

        if (y is < 1990 or > 2100 || mo is < 1 or > 12 || d is < 1 or > 31)
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

    private static List<string> ExtractLines(Body body)
    {
        var lines = new List<string>();
        foreach (var child in body.ChildElements)
        {
            CollectLines(child, lines);
        }

        return lines;
    }

    private static void CollectLines(DocumentFormat.OpenXml.OpenXmlElement element, List<string> lines)
    {
        switch (element)
        {
            case Paragraph para:
            {
                var text = CollapseSpaces(para.InnerText ?? string.Empty);
                if (text.Length > 0)
                {
                    lines.Add(text);
                }

                return;
            }
            case Table table:
            {
                foreach (var row in table.Elements<TableRow>())
                {
                    var cells = row.Elements<TableCell>()
                        .Select(c => CollapseSpaces(c.InnerText ?? string.Empty))
                        .Where(t => t.Length > 0)
                        .ToList();
                    if (cells.Count == 0)
                    {
                        continue;
                    }

                    // Склейка ячеек строки: «КАРТА № 572» и «специальной оценки…» часто в разных ячейках.
                    // Отдельные ячейки не добавляем — иначе дубликат «КАРТА № N» обрывает FillCard.
                    var joined = CollapseSpaces(string.Join(' ', cells));
                    if (joined.Length > 0)
                    {
                        lines.Add(joined);
                    }

                    // Для «Дата составления» / «Итоговый класс» дополнительно кладём ячейки по отдельности.
                    if (cells.Count > 1
                        && cells.Any(c =>
                            c.Contains("Дата составления", StringComparison.OrdinalIgnoreCase)
                            || c.Contains("Итоговый класс", StringComparison.OrdinalIgnoreCase)
                            || StartsWithRow(c, "030")))
                    {
                        foreach (var cell in cells)
                        {
                            if (!string.Equals(cell, joined, StringComparison.Ordinal)
                                && !lines.Contains(cell, StringComparer.Ordinal))
                            {
                                lines.Add(cell);
                            }
                        }
                    }
                }

                return;
            }
            case SdtBlock sdt:
            {
                var content = sdt.SdtContentBlock;
                if (content is null)
                {
                    return;
                }

                foreach (var child in content.ChildElements)
                {
                    CollectLines(child, lines);
                }

                return;
            }
            default:
            {
                // Вложенные контейнеры (AlternateContent и т.п.) — обходим детей.
                if (element.HasChildren &&
                    element is not TableCell &&
                    element is not Run)
                {
                    foreach (var child in element.ChildElements)
                    {
                        if (child is Paragraph or Table or SdtBlock)
                        {
                            CollectLines(child, lines);
                        }
                    }
                }

                return;
            }
        }
    }

    private static bool StartsWithRow(string line, string? code = null)
    {
        var t = line.TrimStart();
        if (!t.StartsWith("Строка", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (code is null)
        {
            return true;
        }

        return Regex.IsMatch(t, $@"Строка\s*{code}\b", RegexOptions.IgnoreCase);
    }

    private static bool IsHintLabel(string line)
    {
        var t = line.Trim();
        return t.StartsWith('(') && t.EndsWith(')');
    }

    /// <summary>Подпись поля формы карты — не должность.</summary>
    private static bool IsProfessionFormLabel(string line)
    {
        var t = line.Trim().ToLowerInvariant().Replace('ё', 'е');
        if (t.Length == 0)
        {
            return true;
        }

        return t.Contains("наименование профессии") ||
               t.Contains("наименование должности") ||
               t.StartsWith("профессия (должность") ||
               t is "профессия" or "должность" ||
               (t.StartsWith("наименование") && t.Contains("работник"));
    }

    private static string ExtractProfessionValue(string line)
    {
        var afterColon = NormalizeAbsent(ExtractAfterColon(line));
        if (afterColon.Length > 0)
        {
            return afterColon;
        }

        var stripped = StripLabeledPrefix(line, "Наименование профессии (должности) работника");
        if (stripped.Length == 0)
        {
            stripped = StripLabeledPrefix(line, "Наименование профессии (должности)");
        }

        if (stripped.Length == 0)
        {
            stripped = StripLabeledPrefix(line, "Наименование профессии");
        }

        return NormalizeAbsent(stripped);
    }

    private static string StripLabeledPrefix(string line, string label)
    {
        var t = CleanValue(line);
        if (t.StartsWith(label, StringComparison.OrdinalIgnoreCase))
        {
            return CleanValue(t[label.Length..]);
        }

        var idx = t.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            return CleanValue(t[(idx + label.Length)..]);
        }

        return string.Empty;
    }

    private static bool IsIncludeNoise(string line) =>
        line.Contains("INCLUDETEXT", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("MERGEFORMAT", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeOrganizationName(string line)
    {
        var t = line.Trim();
        if (t.Length < 8 || t.Length > 250)
        {
            return false;
        }

        if (StartsWithRow(t) || CardTitleRx.IsMatch(NormalizeForTitle(t)) || IsHintLabel(t))
        {
            return false;
        }

        if (t.Contains("ИНН", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("ОКПО", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("ОКВЭД", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return t.Contains("ОБЩЕСТВО", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("ООО", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("АО ", StringComparison.OrdinalIgnoreCase) ||
               t.StartsWith("ИП ", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("ЗАКРЫТОЕ", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("АКЦИОНЕР", StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanOrgName(string line) =>
        CollapseSpaces(line)
            .Replace("«", "\"")
            .Replace("»", "\"")
            .Trim(' ', '.', ';');

    private static string ExtractAfterColon(string line)
    {
        var idx = line.IndexOf(':');
        return idx < 0 ? string.Empty : CleanValue(line[(idx + 1)..]);
    }

    /// <summary>
    /// Значение после «:» на текущей строке + продолжения до следующей «Строки» / другого поля карты.
    /// </summary>
    private static (string Value, int NextIndex) ReadLabeledBlock(
        IReadOnlyList<string> lines,
        int start,
        string expectedKeyword)
    {
        var chunks = new List<string>();
        var line = lines[start];
        if (line.Contains(expectedKeyword, StringComparison.OrdinalIgnoreCase))
        {
            var after = NormalizeAbsent(ExtractAfterColon(line));
            if (after.Length > 0)
            {
                chunks.Add(after);
            }
        }

        var i = start + 1;
        while (i < lines.Count)
        {
            var next = lines[i];
            if (StartsWithRow(next) || CardTitleRx.IsMatch(NormalizeForTitle(next)) ||
                CardTitleShortRx.IsMatch(NormalizeForTitle(next)))
            {
                break;
            }

            if (next.Contains("Используемые материалы и сырье", StringComparison.OrdinalIgnoreCase) ||
                next.Contains("Используемые материалы и сырьё", StringComparison.OrdinalIgnoreCase) ||
                next.Contains("Используемое производственное оборудование", StringComparison.OrdinalIgnoreCase) ||
                next.Contains("Итоговый класс", StringComparison.OrdinalIgnoreCase) ||
                next.Contains("Рекомендации по улучшению", StringComparison.OrdinalIgnoreCase) ||
                next.Contains("Дата составления", StringComparison.OrdinalIgnoreCase) ||
                StartsWithRow(next, "021") ||
                StartsWithRow(next, "030") ||
                StartsWithRow(next, "040") ||
                StartsWithRow(next, "050"))
            {
                break;
            }

            if (IsHintLabel(next) || IsIncludeNoise(next))
            {
                i++;
                continue;
            }

            var value = NormalizeAbsent(CleanValue(next));
            if (value.Length > 0)
            {
                chunks.Add(value);
            }

            i++;
        }

        return (string.Join("; ", chunks), i);
    }

    private static string NormalizeAbsent(string value)
    {
        var t = CleanValue(value);
        if (t.Length == 0)
        {
            return string.Empty;
        }

        var lower = t.ToLowerInvariant().Replace('ё', 'е');
        if (lower is "отсутствует" or "отсутствуют" or "-" or "—" or "–" or "нет" or "не используется")
        {
            return string.Empty;
        }

        return t;
    }

    private static string CleanValue(string value) =>
        CollapseSpaces(value).Trim(' ', '.', ';', ':', '—', '-', '\u00a0');

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
