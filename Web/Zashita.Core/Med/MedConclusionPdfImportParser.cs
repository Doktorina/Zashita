using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Zashita.Core.Med;

/// <summary>Извлечение текста из PDF заключений медосмотра → общий разбор строк.</summary>
public static class MedConclusionPdfImportParser
{
    /// <summary>
    /// Опциональный OCR для картинок из PDF без текстового слоя
    /// (на Windows регистрируется в Program.cs через Windows.Media.Ocr).
    /// </summary>
    public static Func<IReadOnlyList<byte[]>, string?>? ImageOcrFallback { get; set; }

    public static IReadOnlyList<ParsedMedConclusion> Parse(Stream pdfStream, string? sourceFileName = null)
    {
        using var document = PdfDocument.Open(pdfStream);
        var lines = ExtractLines(document);
        var layeredText = lines.Count > 0 ? string.Join('\n', lines) : null;
        if (!LooksLikeConclusionForm(layeredText))
        {
            var ocr = TryOcrDocument(document);
            if (!string.IsNullOrWhiteSpace(ocr)
                && (LooksLikeConclusionForm(ocr) || lines.Count == 0))
            {
                lines = ocr
                    .Replace('\r', '\n')
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(Collapse)
                    .Where(t => t.Length > 0)
                    .ToList();
            }
        }

        if (lines.Count == 0)
        {
            throw new InvalidOperationException(
                "В PDF нет текстового слоя (скан-картинка), OCR не смог прочитать текст. Нужен PDF с текстом, .docx, либо установите русский пакет «Распознавание текста» Windows.");
        }

        var cards = MedConclusionDocxImportParser.ParseLines(lines);
        var file = Path.GetFileName(sourceFileName ?? string.Empty);
        foreach (var card in cards)
        {
            card.SourceFileName = file;
        }

        // Есть текст формы, но ФИО/дата не распознаны — не молчим «не найдено».
        if (cards.Count == 0 &&
            lines.Any(l => l.Contains("ЗАКЛЮЧЕНИЕ", StringComparison.OrdinalIgnoreCase) ||
                           l.Contains("медицинского", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Текст заключения в PDF найден, но не удалось прочитать ФИО. Проверьте, что в файле заполнены фамилия, имя и отчество.");
        }

        return cards;
    }

    /// <summary>
    /// Текст PDF для автозаполнения «Годен / Не годен / …» при загрузке скана.
    /// null — нет текстового слоя и OCR не сработал (или файл не читается).
    /// Если слой есть, но это мусор/пустые глифы — всё равно пробуем OCR по картинкам.
    /// </summary>
    public static string? TryExtractPlainText(Stream pdfStream)
    {
        try
        {
            using var document = PdfDocument.Open(pdfStream);
            var lines = ExtractLines(document);
            var layered = lines.Count > 0 ? string.Join('\n', lines) : null;
            if (LooksLikeConclusionForm(layered))
            {
                return layered;
            }

            var ocr = TryOcrDocument(document);
            if (!string.IsNullOrWhiteSpace(ocr))
            {
                // OCR лучше мусорного текстового слоя; иначе оставляем слой, если он хоть что-то дал.
                if (LooksLikeConclusionForm(ocr) || string.IsNullOrWhiteSpace(layered))
                {
                    return ocr;
                }
            }

            return layered;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Есть маркеры бланка заключения (кириллица + ключевые слова).</summary>
    private static bool LooksLikeConclusionForm(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 40)
        {
            return false;
        }

        var lower = text.ToLowerInvariant().Replace('ё', 'е');
        var markers = 0;
        if (lower.Contains("заключение", StringComparison.Ordinal)) markers++;
        if (lower.Contains("медицинск", StringComparison.Ordinal)) markers++;
        if (lower.Contains("противопоказан", StringComparison.Ordinal)) markers++;
        if (lower.Contains("фамилия", StringComparison.Ordinal)) markers++;
        if (lower.Contains("дата выдач", StringComparison.Ordinal)) markers++;
        if (lower.Contains("годен", StringComparison.Ordinal)) markers++;
        return markers >= 2;
    }

    private static string? TryOcrDocument(PdfDocument document)
    {
        if (ImageOcrFallback is null)
        {
            return null;
        }

        try
        {
            var images = ExtractEmbeddedImages(document);
            return images.Count == 0 ? null : ImageOcrFallback(images);
        }
        catch
        {
            return null;
        }
    }

    private static List<byte[]> ExtractEmbeddedImages(PdfDocument document)
    {
        var result = new List<byte[]>();
        foreach (var page in document.GetPages())
        {
            foreach (var image in page.GetImages())
            {
                if (image.TryGetPng(out var png) && png is { Length: > 32 })
                {
                    result.Add(png);
                    continue;
                }

                // Часто скан лежит как JPEG в RawBytes (FF D8 …).
                var raw = image.RawBytes;
                if (raw.Count > 32)
                {
                    result.Add(raw.ToArray());
                }
            }
        }

        // Крупные картинки сначала — обычно это сам бланк, не логотип.
        return result
            .OrderByDescending(b => b.Length)
            .Take(3)
            .ToList();
    }

    /// <summary>Результат заключения из PDF (без требования распознать ФИО).</summary>
    public static string? TryMapConclusion(Stream pdfStream) =>
        MedConclusionScanReader.Read(pdfStream, ".pdf").Conclusion;

    /// <summary>Заключение + дата выдачи заключения + пункты факторов из PDF-скана.</summary>
    public static (string? Conclusion, DateTime? ExamDate, IReadOnlyList<string> ItemNumbers) TryMapScanFields(
        Stream pdfStream)
    {
        var r = MedConclusionScanReader.Read(pdfStream, ".pdf");
        return (r.Conclusion, r.IssueDate, r.ItemNumbers);
    }

    private static List<string> ExtractLines(PdfDocument document)
    {
        var lines = new List<string>();
        foreach (var page in document.GetPages())
        {
            var pageLines = ExtractPageLines(page);
            if (pageLines.Count == 0)
            {
                var raw = (page.Text ?? string.Empty)
                    .Replace('\r', '\n')
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var line in raw)
                {
                    var c = Collapse(line);
                    if (c.Length > 0)
                    {
                        pageLines.Add(c);
                    }
                }
            }

            lines.AddRange(pageLines);
        }

        return MergeFragmentedTitleLines(lines);
    }

    private static List<string> ExtractPageLines(Page page)
    {
        var words = page.GetWords().ToList();
        if (words.Count == 0)
        {
            return [];
        }

        // Чуть крупнее допуск по Y — в бланках строки часто «пляшут».
        const double yTol = 3.5;
        return words
            .GroupBy(w => Math.Round(w.BoundingBox.Bottom / yTol) * yTol)
            .OrderByDescending(g => g.Key)
            .Select(g => string.Join(' ',
                g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)))
            .Select(Collapse)
            .Where(t => t.Length > 0)
            .ToList();
    }

    /// <summary>
    /// «ЗАКЛЮЧЕНИЕ ПРЕДВАРИТЕЛЬНОГО» и «ПЕРИОДИЧЕСКОГО … МЕДИЦИНСКОГО ОСМОТРА» часто на соседних строках.
    /// </summary>
    private static List<string> MergeFragmentedTitleLines(List<string> lines)
    {
        if (lines.Count < 2)
        {
            return lines;
        }

        var result = new List<string>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var cur = lines[i];
            var lower = cur.ToLowerInvariant().Replace('ё', 'е');
            if (lower.Contains("заключение") &&
                !lower.Contains("медицинского осмотра") &&
                i + 1 < lines.Count)
            {
                var next = lines[i + 1];
                var nextLower = next.ToLowerInvariant().Replace('ё', 'е');
                if (nextLower.Contains("медицинского") ||
                    nextLower.Contains("периодического") ||
                    nextLower.Contains("предварительного"))
                {
                    result.Add(Collapse(cur + " " + next));
                    i++;
                    continue;
                }
            }

            result.Add(cur);
        }

        return result;
    }

    private static string Collapse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(' ',
            value
                .Replace('\u00a0', ' ')
                .Replace('\u00ad', ' ')
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
