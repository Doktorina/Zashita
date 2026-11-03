using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Zashita.Web.Services;

/// <summary>
/// OCR сканов заключений медосмотра через Windows.Media.Ocr (русский язык).
/// Нужен пакет OCR для русского в Windows («Языковые параметры» → Русский → Распознавание текста).
/// Сканы часто повёрнуты на 90° — пробуем 0/90/180/270 и берём лучший текст.
/// </summary>
public static class MedConclusionWindowsOcr
{
    // Сканы бланков МО часто лежат «на боку» → 270° даёт читаемый текст раньше остальных.
    private static readonly BitmapRotation[] Rotations =
    [
        BitmapRotation.Clockwise270Degrees,
        BitmapRotation.Clockwise90Degrees,
        BitmapRotation.None,
        BitmapRotation.Clockwise180Degrees
    ];

    /// <summary>Распознать текст с изображений (JPEG/PNG) из PDF без текстового слоя.</summary>
    public static string? TryRecognizeImages(IReadOnlyList<byte[]> images)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240) || images.Count == 0)
        {
            return null;
        }

        try
        {
            return RecognizeImagesAsync(images).GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> RecognizeImagesAsync(IReadOnlyList<byte[]> images)
    {
        var engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ru"))
                     ?? OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var bytes in images)
        {
            if (bytes is not { Length: > 32 })
            {
                continue;
            }

            var best = await RecognizeBestOrientationAsync(engine, bytes);
            if (!string.IsNullOrWhiteSpace(best))
            {
                parts.Add(best);
            }
        }

        return parts.Count == 0 ? null : string.Join('\n', parts);
    }

    private static async Task<string?> RecognizeBestOrientationAsync(OcrEngine engine, byte[] bytes)
    {
        string? bestText = null;
        var bestScore = -1;

        foreach (var rotation in Rotations)
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream);
            var transform = new BitmapTransform { Rotation = rotation };

            // Крупные сканы (>2500px) уменьшаем — OCR стабильнее и быстрее.
            var w = decoder.PixelWidth;
            var h = decoder.PixelHeight;
            if (rotation is BitmapRotation.Clockwise90Degrees or BitmapRotation.Clockwise270Degrees)
            {
                (w, h) = (h, w);
            }

            if (Math.Max(w, h) > 2500)
            {
                var scale = 2500.0 / Math.Max(w, h);
                transform.ScaledWidth = (uint)Math.Max(1, Math.Round(w * scale));
                transform.ScaledHeight = (uint)Math.Max(1, Math.Round(h * scale));
                transform.InterpolationMode = BitmapInterpolationMode.Fant;
            }

            using var bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            var result = await engine.RecognizeAsync(bitmap);
            if (result?.Lines is null || result.Lines.Count == 0)
            {
                continue;
            }

            var sb = new StringBuilder();
            foreach (var line in result.Lines)
            {
                var t = (line.Text ?? string.Empty).Trim();
                if (t.Length == 0)
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append('\n');
                }

                sb.Append(t);
            }

            var text = sb.ToString();
            var score = ScoreOcrText(text);
            if (score > bestScore)
            {
                bestScore = score;
                bestText = text;
            }

            // Достаточно уверенно прочитали бланк — остальные повороты не гоняем.
            if (score >= 200)
            {
                break;
            }
        }

        // Без ключевых слов бланка — не доверяем «шуму» OCR.
        return bestScore >= 60 ? bestText : null;
    }

    /// <summary>Ключевые слова бланка важнее сырого числа кириллицы (иначе побеждает зеркало).</summary>
    private static int ScoreOcrText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var cyr = 0;
        foreach (var ch in text)
        {
            if (ch is (>= 'а' and <= 'я') or (>= 'А' and <= 'Я') or 'ё' or 'Ё')
            {
                cyr++;
            }
        }

        var lower = text.ToLowerInvariant().Replace('ё', 'е');
        var keywords = 0;
        if (lower.Contains("заключение", StringComparison.Ordinal)) keywords += 100;
        if (lower.Contains("медицинск", StringComparison.Ordinal)) keywords += 40;
        if (lower.Contains("дата выдачи", StringComparison.Ordinal)) keywords += 80;
        if (lower.Contains("противопоказан", StringComparison.Ordinal)) keywords += 50;
        if (lower.Contains("годен", StringComparison.Ordinal)) keywords += 30;
        if (lower.Contains("фамилия", StringComparison.Ordinal)) keywords += 25;
        if (lower.Contains("професси", StringComparison.Ordinal)
            || lower.Contains("должность", StringComparison.Ordinal))
        {
            keywords += 20;
        }

        if (lower.Contains("работодател", StringComparison.Ordinal)) keywords += 15;

        // Без маркеров бланка кириллица почти не считается (анти-зеркало/анти-шум).
        if (keywords == 0)
        {
            return Math.Min(cyr / 20, 30);
        }

        return keywords + Math.Min(cyr, 200);
    }
}
