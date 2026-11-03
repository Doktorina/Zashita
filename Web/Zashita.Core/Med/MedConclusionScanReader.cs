using System.Text;
using System.Text.RegularExpressions;

namespace Zashita.Core.Med;

/// <summary>
/// Чтение скана/PDF заключения медосмотра для отчёта.
/// Берёт три поля: заключение, дату выдачи заключения, пункты факторов.
/// </summary>
public static class MedConclusionScanReader
{
    private static readonly Regex DateRx = new(
        @"(?<d>\d{1,2})\s*[.\-/]\s*(?<m>\d{1,2})\s*[.\-/]\s*(?<y>20\d{2})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DateSpacedRx = new(
        @"(?<d>\d{1,2})\s+(?<m>\d{1,2})\s+(?<y>20\d{2})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ItemRx = new(
        @"\bп\.?\s*(?<n>\d+(?:\.\d+){0,3})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public sealed record Result(
        string? Conclusion,
        DateTime? IssueDate,
        IReadOnlyList<string> ItemNumbers,
        string? RawText);

    /// <summary>PDF или изображение → поля для отчёта.</summary>
    public static Result Read(Stream content, string? fileExtension)
    {
        ArgumentNullException.ThrowIfNull(content);

        var ext = (fileExtension ?? string.Empty).Trim();
        if (ext.Length > 0 && !ext.StartsWith('.'))
        {
            ext = "." + ext;
        }

        var text = ExtractText(content, ext);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new Result(null, null, [], null);
        }

        var normalized = NormalizeDocument(text);
        return new Result(
            MapConclusion(normalized),
            MapIssueDate(normalized),
            MapItemNumbers(normalized),
            normalized);
    }

    /// <summary>Уже есть текст (OCR / слой PDF) → только разбор полей.</summary>
    public static Result ReadFromText(string? documentText)
    {
        if (string.IsNullOrWhiteSpace(documentText))
        {
            return new Result(null, null, [], null);
        }

        var normalized = NormalizeDocument(documentText);
        return new Result(
            MapConclusion(normalized),
            MapIssueDate(normalized),
            MapItemNumbers(normalized),
            normalized);
    }

    private static string? ExtractText(Stream content, string ext)
    {
        try
        {
            if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                if (content.CanSeek)
                {
                    content.Position = 0;
                }

                return MedConclusionPdfImportParser.TryExtractPlainText(content);
            }

            if (IsImageExtension(ext))
            {
                var bytes = ReadAllBytes(content);
                return MedConclusionPdfImportParser.ImageOcrFallback?.Invoke([bytes]);
            }

            // Неизвестное расширение: пробуем как PDF, потом как картинку.
            if (content.CanSeek)
            {
                content.Position = 0;
            }

            var asPdf = MedConclusionPdfImportParser.TryExtractPlainText(content);
            if (!string.IsNullOrWhiteSpace(asPdf))
            {
                return asPdf;
            }

            var img = ReadAllBytes(content);
            return MedConclusionPdfImportParser.ImageOcrFallback?.Invoke([img]);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsImageExtension(string ext) =>
        ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".tif", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase);

    private static byte[] ReadAllBytes(Stream content)
    {
        if (content is MemoryStream ms)
        {
            return ms.ToArray();
        }

        using var copy = new MemoryStream();
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        content.CopyTo(copy);
        return copy.ToArray();
    }

    private static string NormalizeDocument(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch is '\r')
            {
                continue;
            }

            if (ch is '\u00a0' or '\u00ad')
            {
                sb.Append(' ');
                continue;
            }

            sb.Append(ch);
        }

        var lines = sb.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => Regex.Replace(l, @"\s+", " ").Trim())
            .Where(l => l.Length > 0);

        return string.Join('\n', lines);
    }

    // ─── Заключение ───────────────────────────────────────────────────────────

    /// <summary>
    /// «Медицинские противопоказания не выявлены» → Годен;
    /// противопоказания выявлены / не годен → Не годен;
    /// ограниченно годен → Ограниченно годен.
    /// </summary>
    public static string? MapConclusion(string? text)
    {
        var t = Fold(text);
        if (t.Length < 4)
        {
            return null;
        }

        // OCR-опечатки вокруг «не выявлены».
        t = Regex.Replace(t, @"не\s*выя\w*", "не выявлены");
        t = Regex.Replace(t, @"п\w{0,3}тивопоказ\w*", "противопоказания");

        if (HasNoContraindications(t))
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
            || HasContraindications(t))
        {
            return "Не годен";
        }

        if (t.Contains("годен", StringComparison.Ordinal)
            && !t.Contains("не годен", StringComparison.Ordinal))
        {
            return "Годен";
        }

        return null;
    }

    private static bool HasNoContraindications(string t) =>
        Regex.IsMatch(t, @"противопоказани\w*\s+не\s+выявлен")
        || Regex.IsMatch(t, @"не\s+выявлен\w*\s+[^.…]{0,40}противопоказан")
        || t.Contains("медицинских противопоказаний не выявлено", StringComparison.Ordinal)
        || t.Contains("медицинские противопоказания не выявлены", StringComparison.Ordinal)
        || t.Contains("противопоказаний не выявлено", StringComparison.Ordinal)
        || t.Contains("противопоказания не выявлены", StringComparison.Ordinal)
        || (t.Contains("противопоказан", StringComparison.Ordinal)
            && Regex.IsMatch(t, @"не\s+выявлен"));

    private static bool HasContraindications(string t)
    {
        if (!t.Contains("противопоказан", StringComparison.Ordinal))
        {
            return false;
        }

        return Regex.IsMatch(t, @"противопоказани\w*\s+выявлен")
               || t.Contains("имеются медицинские противопоказания", StringComparison.Ordinal)
               || t.Contains("выявлены медицинские противопоказания", StringComparison.Ordinal)
               || t.Contains("выявлено медицинское противопоказание", StringComparison.Ordinal);
    }

    // ─── Дата выдачи заключения ───────────────────────────────────────────────

    /// <summary>
    /// Источник: строка «Дата выдачи Заключения, печать медицинской организации»
    /// и дата на ней / на следующей строке. Не «Дата подписи» ЭЦП и не срок сертификата.
    /// </summary>
    public static DateTime? MapIssueDate(string? text)
    {
        var lines = SplitLines(text);
        if (lines.Count == 0)
        {
            return null;
        }

        // 1) Явная метка бланка — ищем снизу вверх (дата внизу бланка).
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var lower = Fold(lines[i]);
            if (!IsIssueDateLabel(lower))
            {
                continue;
            }

            var d = FindDateNear(lines, i, lookAhead: 3);
            if (d is not null)
            {
                return d;
            }
        }

        // 2) Запас: любая «дата выдач…» без чужого контекста.
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var lower = Fold(lines[i]);
            if (!lower.Contains("выдач", StringComparison.Ordinal)
                && !lower.Contains("водач", StringComparison.Ordinal))
            {
                continue;
            }

            if (IsForbiddenDateContext(lower))
            {
                continue;
            }

            var d = FindDateNear(lines, i, lookAhead: 3);
            if (d is not null)
            {
                return d;
            }
        }

        return null;
    }

    private static bool IsIssueDateLabel(string lower)
    {
        if (IsForbiddenDateContext(lower))
        {
            return false;
        }

        // Полная формулировка бланка (в т.ч. OCR-искажения).
        if ((lower.Contains("дата выдач", StringComparison.Ordinal)
             || lower.Contains("дата водач", StringComparison.Ordinal))
            && (lower.Contains("заключен", StringComparison.Ordinal)
                || lower.Contains("заточен", StringComparison.Ordinal)
                || lower.Contains("печать", StringComparison.Ordinal)
                || lower.Contains("медицин", StringComparison.Ordinal)))
        {
            return true;
        }

        if (lower.Contains("дата", StringComparison.Ordinal)
            && lower.Contains("заключен", StringComparison.Ordinal)
            && !lower.Contains("подпис", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool IsForbiddenDateContext(string lower) =>
        lower.Contains("дата подписи", StringComparison.Ordinal)
        || lower.Contains("действителен", StringComparison.Ordinal)
        || lower.Contains("лиценз", StringComparison.Ordinal)
        || lower.Contains("рожден", StringComparison.Ordinal)
        || lower.Contains("огрн", StringComparison.Ordinal)
        || Regex.IsMatch(lower, @"от\s+\d{1,2}\s*[.\-/]\s*\d{1,2}\s*[.\-/]\s*20\d{2}");

    private static DateTime? FindDateNear(IReadOnlyList<string> lines, int labelIndex, int lookAhead)
    {
        for (var j = labelIndex; j <= Math.Min(lines.Count - 1, labelIndex + lookAhead); j++)
        {
            var line = lines[j];
            var lower = Fold(line);
            if (j > labelIndex && IsForbiddenDateContext(lower) && !IsIssueDateLabel(lower))
            {
                continue;
            }

            foreach (var d in EnumerateDates(line))
            {
                if (IsForbiddenDateContext(lower) && !IsIssueDateLabel(lower))
                {
                    continue;
                }

                // Срок «с … по …» — не дата выдачи.
                if (lower.Contains(" по ", StringComparison.Ordinal)
                    && lower.Contains("с ", StringComparison.Ordinal)
                    && EnumerateDates(line).Count() >= 2)
                {
                    continue;
                }

                return d;
            }
        }

        return null;
    }

    private static IEnumerable<DateTime> EnumerateDates(string line)
    {
        foreach (Match m in DateRx.Matches(line).Cast<Match>().Concat(DateSpacedRx.Matches(line)))
        {
            if (!int.TryParse(m.Groups["d"].Value, out var d)
                || !int.TryParse(m.Groups["m"].Value, out var mo)
                || !int.TryParse(m.Groups["y"].Value, out var y))
            {
                continue;
            }

            if (y is < 2000 or > 2100 || mo is < 1 or > 12 || d is < 1 or > 31)
            {
                continue;
            }

            DateTime dt;
            try
            {
                dt = new DateTime(y, mo, d);
            }
            catch
            {
                continue;
            }

            yield return dt.Date;
        }
    }

    // ─── Пункты факторов ──────────────────────────────────────────────────────

    public static IReadOnlyList<string> MapItemNumbers(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var lines = SplitLines(text);
        var factorBlock = ExtractFactorBlock(lines);
        var source = factorBlock.Length > 0 ? factorBlock : text;

        var items = new List<string>();
        foreach (Match m in ItemRx.Matches(source))
        {
            var n = MedItemNumber.Normalize(m.Groups["n"].Value);
            if (n.Length == 0)
            {
                continue;
            }

            // Номера разделов бланка «4. Место работы», «5. …», «6. …» без подпункта — мусор,
            // если рядом нет настоящего п.X.Y.
            if (n is "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "9")
            {
                continue;
            }

            if (!items.Contains(n, StringComparer.OrdinalIgnoreCase))
            {
                items.Add(n);
            }
        }

        return DropParentOnly(items);
    }

    private static string ExtractFactorBlock(IReadOnlyList<string> lines)
    {
        var start = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var lower = Fold(lines[i]);
            if (lower.Contains("вредн", StringComparison.Ordinal)
                || lower.Contains("опасн", StringComparison.Ordinal)
                || (lower.Contains("видов работ", StringComparison.Ordinal)
                    && lower.Contains("наименован", StringComparison.Ordinal)))
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return string.Empty;
        }

        var end = Math.Min(lines.Count, start + 8);
        for (var i = start + 1; i < lines.Count; i++)
        {
            var lower = Fold(lines[i]);
            if (lower.Contains("результат", StringComparison.Ordinal)
                || lower.Contains("противопоказан", StringComparison.Ordinal)
                || lower.StartsWith("7.", StringComparison.Ordinal)
                || lower.Contains("группа здоровья", StringComparison.Ordinal))
            {
                end = i;
                break;
            }
        }

        return string.Join('\n', lines.Skip(start).Take(end - start));
    }

    private static List<string> DropParentOnly(List<string> items)
    {
        if (items.Count <= 1)
        {
            return items;
        }

        return items
            .Where(item =>
            {
                var prefix = item + ".";
                return !items.Any(other =>
                    !string.Equals(other, item, StringComparison.OrdinalIgnoreCase)
                    && other.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();
    }

    private static List<string> SplitLines(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Replace('\r', '\n')
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(l => Regex.Replace(l, @"\s+", " ").Trim())
                .Where(l => l.Length > 0)
                .ToList();

    private static string Fold(string? value) =>
        (value ?? string.Empty).ToLowerInvariant().Replace('ё', 'е');
}
