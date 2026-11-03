using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Zashita.Core.Risks;

/// <summary>
/// Разбор Word «Карта идентификации опасностей…».
/// Поддерживает шаблон с абзацами (как печать модуля) и табличный шаблон
/// (заголовки/строки внутри таблиц, часто с пустым первым столбцом).
/// </summary>
public static class RiskCardDocxImportParser
{
    private static readonly Regex CardTitleRx = new(
        @"Карта\s+идентификации\s+опасностей\s+и\s+оценки\s+рисков\s*№\s*(?<n>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CardNoShortRx = new(
        @"Карта\s*№\s*(?<n>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DateRx = new(
        @"(?<d>\d{1,2})\.(?<m>\d{1,2})\.(?<y>\d{4})",
        RegexOptions.Compiled);

    private static readonly Regex FioRx = new(
        @"(?<fio>[А-ЯЁA-Z][а-яёa-z]+(?:-[А-ЯЁA-Z][а-яёa-z]+)?\s+[А-ЯЁA-Z]\.\s*[А-ЯЁA-Z]\.?)",
        RegexOptions.Compiled);

    private static readonly Regex HazardNumberRx = new(
        @"^\d+(\.\d+)*\.?$",
        RegexOptions.Compiled);

    public static IReadOnlyList<ParsedRiskCard> Parse(Stream docxStream)
    {
        using var repaired = DocxPackageRepair.ToRepairedMemoryStream(docxStream);
        using var doc = WordprocessingDocument.Open(repaired, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return [];
        }

        var cards = new List<ParsedRiskCard>();
        ParsedRiskCard? current = null;
        var headerBuffer = new StringBuilder();

        foreach (var child in body.ChildElements)
        {
            if (child is Paragraph para)
            {
                HandleParagraphText(para.InnerText ?? string.Empty, ref current, cards, headerBuffer);
                continue;
            }

            if (child is not Table table)
            {
                continue;
            }

            foreach (var row in table.Elements<TableRow>())
            {
                var cells = GetMeaningfulCells(row);
                if (cells.Count == 0)
                {
                    continue;
                }

                var rowText = CollapseSpaces(string.Join(' ', cells));
                if (rowText.Length == 0)
                {
                    continue;
                }

                // Заголовок карты внутри ячейки таблицы.
                var titleMatch = CardTitleRx.Match(rowText);
                if (titleMatch.Success)
                {
                    FlushHeader(current, headerBuffer);
                    current = new ParsedRiskCard
                    {
                        CardNumber = titleMatch.Groups["n"].Value.Trim().PadLeft(2, '0')
                    };
                    cards.Add(current);
                    headerBuffer.Clear();
                    // В той же строке могут быть и другие поля — добираем.
                    AppendHeaderPieces(headerBuffer, cells);
                    continue;
                }

                if (current is null)
                {
                    // Короткие колонтитулы «Карта №02 стр. 2» без полной шапки — пропускаем.
                    continue;
                }

                if (LooksLikeHazardHeader(cells) || LooksLikeHazardHeader(rowText))
                {
                    FlushHeader(current, headerBuffer);
                    continue;
                }

                if (rowText.Contains("Карту составил", StringComparison.OrdinalIgnoreCase) ||
                    rowText.Contains("Члены рабочей группы", StringComparison.OrdinalIgnoreCase))
                {
                    FlushHeader(current, headerBuffer);
                    AppendSignaturesFromCells(current, cells);
                    continue;
                }

                if (rowText.Contains("Ознакомлен", StringComparison.OrdinalIgnoreCase))
                {
                    FlushHeader(current, headerBuffer);
                    AppendAcquaintedFromCells(current, cells);
                    continue;
                }

                if (LooksLikeHazardDataCells(cells))
                {
                    FlushHeader(current, headerBuffer);
                    AppendHazardFromCells(current, cells);
                    continue;
                }

                // Строка метаданных: Организация | ООО | … | должность | …
                if (LooksLikeMetaRow(cells, rowText))
                {
                    AppendHeaderPieces(headerBuffer, cells);
                    // Сразу разберём — в табличных шаблонах буфер короткий.
                    FlushHeader(current, headerBuffer);
                    continue;
                }

                // Одиночные подписи полей / значения.
                if (IsMetaLabelOrValue(rowText) || cells.Count <= 6)
                {
                    AppendHeaderPieces(headerBuffer, cells);
                }
            }
        }

        FlushHeader(current, headerBuffer);
        return cards
            .Where(c => c.Lines.Count > 0 || !string.IsNullOrWhiteSpace(c.Profession))
            .ToList();
    }

    public static IReadOnlyList<ParsedRiskCard> ParseFile(string docxPath)
    {
        using var fs = File.OpenRead(docxPath);
        return Parse(fs);
    }

    private static void HandleParagraphText(
        string raw,
        ref ParsedRiskCard? current,
        List<ParsedRiskCard> cards,
        StringBuilder headerBuffer)
    {
        var text = CollapseSpaces(StripWordJunk(raw));
        if (text.Length == 0)
        {
            return;
        }

        // Колонтитулы вида «Карта №02 стр. 2 из 3» — не шапка карты.
        if (CardNoShortRx.IsMatch(text) && !CardTitleRx.IsMatch(text) &&
            text.Contains("стр", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var titleMatch = CardTitleRx.Match(text);
        if (titleMatch.Success)
        {
            FlushHeader(current, headerBuffer);
            current = new ParsedRiskCard
            {
                CardNumber = titleMatch.Groups["n"].Value.Trim().PadLeft(2, '0')
            };
            cards.Add(current);
            headerBuffer.Clear();
            return;
        }

        if (current is null)
        {
            return;
        }

        headerBuffer.Append(' ').Append(text);
    }

    private static void AppendHeaderPieces(StringBuilder buffer, List<string> cells)
    {
        foreach (var cell in cells)
        {
            var t = CollapseSpaces(StripWordJunk(cell));
            if (t.Length == 0)
            {
                continue;
            }

            buffer.Append(' ').Append(t);
        }
    }

    private static bool LooksLikeMetaRow(List<string> cells, string rowText) =>
        rowText.Contains("Организация", StringComparison.OrdinalIgnoreCase) ||
        rowText.Contains("Наименование должности", StringComparison.OrdinalIgnoreCase) ||
        rowText.Contains("Подразделение", StringComparison.OrdinalIgnoreCase) ||
        rowText.Contains("Общий риск", StringComparison.OrdinalIgnoreCase) ||
        rowText.Contains("Дата проведения", StringComparison.OrdinalIgnoreCase) ||
        (cells.Count >= 2 && cells.Any(c =>
            string.Equals(c, "Организация", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c, "Подразделение", StringComparison.OrdinalIgnoreCase)));

    private static bool IsMetaLabelOrValue(string text)
    {
        if (text.Contains("Номер опасности", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Наименование опасности", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Меры управления", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return text.Contains("Организация", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("должности", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Подразделение", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Общий риск", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Дата", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("профессии", StringComparison.OrdinalIgnoreCase) ||
               DateRx.IsMatch(text) ||
               text.Contains("ООО", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("ИП ", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Средн", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Высок", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Низк", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeHazardDataCells(List<string> cells)
    {
        if (cells.Count < 2)
        {
            return false;
        }

        // Вариант A: [номер][название]…
        if (HazardNumberRx.IsMatch(NormalizeHazardNumber(cells[0])) &&
            !string.IsNullOrWhiteSpace(cells[1]) &&
            !IsMetaLabelOrValue(cells[0]))
        {
            var joined = string.Join(' ', cells);
            return !joined.Contains("Карту составил", StringComparison.OrdinalIgnoreCase) &&
                   !joined.Contains("Ознакомлен", StringComparison.OrdinalIgnoreCase);
        }

        // Вариант B: пустая первая «служебная» колонка уже снята GetMeaningfulCells —
        // если после снятия всё ещё не номер — нет.
        return false;
    }

    private static void AppendHazardFromCells(ParsedRiskCard card, List<string> cells)
    {
        // Ожидаем: номер, название, источник, тяжесть, вероятность, R, меры, остаток
        while (cells.Count < 8)
        {
            cells.Add(string.Empty);
        }

        var number = NormalizeHazardNumber(cells[0]);
        var title = CollapseSpaces(cells[1]);
        if (!HazardNumberRx.IsMatch(number) || string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        card.Lines.Add(new ParsedRiskCardLine
        {
            HazardNumber = number,
            HazardTitle = title,
            Source = CollapseSpaces(cells[2]),
            Severity = CollapseSpaces(cells[3]),
            Probability = CollapseSpaces(cells[4]),
            InitialRisk = CollapseSpaces(cells[5]),
            ControlMeasures = CollapseSpaces(cells[6]),
            ResidualRisk = CollapseSpaces(cells[7])
        });
    }

    private static void AppendSignaturesFromCells(ParsedRiskCard card, List<string> cells) =>
        AppendSignatures(card, [cells]);

    private static void AppendAcquaintedFromCells(ParsedRiskCard card, List<string> cells) =>
        AppendAcquainted(card, [cells]);

    private static void FlushHeader(ParsedRiskCard? card, StringBuilder buffer)
    {
        if (card is null || buffer.Length == 0)
        {
            return;
        }

        var text = CollapseSpaces(buffer.ToString());
        buffer.Clear();
        if (text.Length == 0)
        {
            return;
        }

        ApplyHeaderText(card, text);
    }

    private static void ApplyHeaderText(ParsedRiskCard card, string text)
    {
        if (string.IsNullOrWhiteSpace(card.Organization))
        {
            var org = ExtractBetween(text, "Организация", "Наименование должности");
            if (string.IsNullOrWhiteSpace(org))
            {
                org = ExtractBetween(text, "Организация", "Подразделение");
            }

            if (!string.IsNullOrWhiteSpace(org))
            {
                card.Organization = CleanLabelArtifacts(org);
            }
        }

        if (string.IsNullOrWhiteSpace(card.ProfessionRaw))
        {
            var prof = ExtractAfter(text, "Наименование должности");
            if (string.IsNullOrWhiteSpace(prof))
            {
                prof = ExtractAfter(text, "должности");
            }

            if (!string.IsNullOrWhiteSpace(prof))
            {
                prof = CutAtLabels(prof, "Подразделение", "Дата", "Общий риск", "(профессии)", "профессии)");
                card.ProfessionRaw = CleanLabelArtifacts(prof);
                card.Profession = StripLeadingCardIndex(card.ProfessionRaw);
            }
        }

        if (string.IsNullOrWhiteSpace(card.Department))
        {
            var dept = ExtractAfter(text, "Подразделение");
            if (!string.IsNullOrWhiteSpace(dept))
            {
                dept = CutAtLabels(dept, "опасностей", "Дата", "Общий риск", "Наименование");
                var dateHit = DateRx.Match(dept);
                if (dateHit.Success)
                {
                    dept = dept[..dateHit.Index];
                }

                dept = CutAtLabels(dept, "опасностей");
                card.Department = CleanLabelArtifacts(dept);
            }
        }

        if (card.AssessmentDate is null)
        {
            var dateHit = DateRx.Match(text);
            if (dateHit.Success &&
                int.TryParse(dateHit.Groups["d"].Value, out var d) &&
                int.TryParse(dateHit.Groups["m"].Value, out var m) &&
                int.TryParse(dateHit.Groups["y"].Value, out var y) &&
                d is >= 1 and <= 31 && m is >= 1 and <= 12 && y is >= 1990 and <= 2100)
            {
                try
                {
                    card.AssessmentDate = new DateTime(y, m, d);
                }
                catch
                {
                    // ignore
                }
            }
        }

        if (string.IsNullOrWhiteSpace(card.OverallRisk))
        {
            var risk = ExtractAfter(text, "Общий риск");
            if (!string.IsNullOrWhiteSpace(risk))
            {
                card.OverallRisk = CleanLabelArtifacts(CutAtLabels(risk, "Номер", "Карта", "Меры"));
            }
        }
    }

    private static void AppendSignatures(ParsedRiskCard card, List<List<string>> rows)
    {
        foreach (var cells in rows)
        {
            var rowText = string.Join(' ', cells);
            var names = ExtractFios(rowText);
            if (names.Count == 0)
            {
                continue;
            }

            if (rowText.Contains("Карту составил", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(card.CompiledBy))
                {
                    card.CompiledBy = names[0];
                }

                foreach (var n in names.Skip(1))
                {
                    AddUnique(card.WorkingGroupMembers, n);
                }
            }
            else
            {
                foreach (var n in names)
                {
                    AddUnique(card.WorkingGroupMembers, n);
                }
            }
        }
    }

    private static void AppendAcquainted(ParsedRiskCard card, List<List<string>> rows)
    {
        foreach (var cells in rows)
        {
            foreach (var name in ExtractFios(string.Join(' ', cells)))
            {
                AddUnique(card.AcquaintedNames, name);
            }
        }
    }

    private static List<string> ExtractFios(string text)
    {
        var cleaned = text
            .Replace("(Фамилия И.О.)", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("(подпись)", " ", StringComparison.OrdinalIgnoreCase);
        // Убрать латинский мусор перед ФИО (GjgjdGПопов → Попов)
        cleaned = Regex.Replace(cleaned, @"[A-Za-z]{2,}(?=[А-ЯЁ])", " ");
        var result = new List<string>();
        foreach (Match m in FioRx.Matches(cleaned))
        {
            AddUnique(result, CollapseSpaces(m.Groups["fio"].Value));
        }

        return result;
    }

    private static bool LooksLikeHazardHeader(IReadOnlyList<string> cells) =>
        LooksLikeHazardHeader(string.Join(' ', cells));

    private static bool LooksLikeHazardHeader(string text) =>
        text.Contains("Номер опасности", StringComparison.OrdinalIgnoreCase) &&
        (text.Contains("Наименование опасности", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("Тяжесть", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeHazardNumber(string? raw) =>
        CollapseSpaces(raw ?? string.Empty).TrimEnd('.');

    private static string StripLeadingCardIndex(string raw)
    {
        var t = CollapseSpaces(raw);
        t = Regex.Replace(t, @"^\d{1,3}[\.\)]?\s+", string.Empty);
        return CollapseSpaces(t);
    }

    private static string ExtractBetween(string text, string startLabel, string endLabel)
    {
        var start = text.IndexOf(startLabel, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return string.Empty;
        }

        start += startLabel.Length;
        var end = text.IndexOf(endLabel, start, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? text[start..] : text[start..end];
    }

    private static string ExtractAfter(string text, string label)
    {
        var start = text.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        return start < 0 ? string.Empty : text[(start + label.Length)..];
    }

    private static string CutAtLabels(string text, params string[] labels)
    {
        var cut = text.Length;
        foreach (var label in labels)
        {
            var idx = text.IndexOf(label, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && idx < cut)
            {
                cut = idx;
            }
        }

        return text[..cut];
    }

    private static string CleanLabelArtifacts(string value)
    {
        var t = CollapseSpaces(value)
            .Replace("(профессии)", " ", StringComparison.OrdinalIgnoreCase);
        t = Regex.Replace(t, @"\d{8,}", " ");
        return CollapseSpaces(t).Trim(' ', '.', ';', ':', '—', '-');
    }

    private static string StripWordJunk(string value) =>
        string.IsNullOrEmpty(value) ? string.Empty : Regex.Replace(value, @"\d{8,}", " ");

    private static void AddUnique(List<string> list, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (list.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        list.Add(value);
    }

    /// <summary>Ячейки строки без ведущих/хвостовых пустых; схлопывает внутренние пробелы.</summary>
    private static List<string> GetMeaningfulCells(TableRow row)
    {
        var cells = row.Elements<TableCell>()
            .Select(cell => CollapseSpaces(StripWordJunk(cell.InnerText ?? string.Empty)))
            .ToList();

        // Убрать ведущие пустые (служебная колонка в шаблонах вроде ИНВИКТУС).
        while (cells.Count > 0 && string.IsNullOrWhiteSpace(cells[0]))
        {
            cells.RemoveAt(0);
        }

        while (cells.Count > 0 && string.IsNullOrWhiteSpace(cells[^1]))
        {
            cells.RemoveAt(cells.Count - 1);
        }

        return cells;
    }

    private static string CollapseSpaces(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
