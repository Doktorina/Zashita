using System.Diagnostics;
using System.Text;

namespace Zashita.Web.Services;

/// <summary>
/// OCR через CLI <c>tesseract</c> (Linux/сервер). Нужны пакеты
/// <c>tesseract-ocr</c> + <c>tesseract-ocr-rus</c>, для PDF — <c>poppler-utils</c> (pdftoppm).
/// </summary>
public static class MedConclusionTesseractOcr
{
    /// <summary>Распознать текст с JPEG/PNG байтов (уже извлечённых из PDF).</summary>
    public static string? TryRecognizeImages(IReadOnlyList<byte[]> images)
    {
        if (images.Count == 0 || !IsTesseractAvailable())
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

            var text = RecognizeImageBytes(bytes);
            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(text);
            }
        }

        if (parts.Count == 0)
        {
            return null;
        }

        var joined = string.Join('\n', parts);
        return ScoreOcrText(joined) >= 60 ? joined : null;
    }

    private static bool? _tesseractOk;

    private static bool IsTesseractAvailable()
    {
        if (_tesseractOk is not null)
        {
            return _tesseractOk.Value;
        }

        try
        {
            var (code, _) = Run("tesseract", "--version", timeoutMs: 5000);
            _tesseractOk = code == 0;
        }
        catch
        {
            _tesseractOk = false;
        }

        return _tesseractOk.Value;
    }

    private static string? RecognizeImageBytes(byte[] bytes)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "zashita-ocr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var ext = DetectImageExtension(bytes);
            var imgPath = Path.Combine(tmpDir, "page" + ext);
            File.WriteAllBytes(imgPath, bytes);
            var outBase = Path.Combine(tmpDir, "out");

            // --psm 6: блок текста; -l rus+eng на бланках с цифрами.
            var (code, _) = Run(
                "tesseract",
                $"\"{imgPath}\" \"{outBase}\" -l rus+eng --psm 6",
                timeoutMs: 120_000);
            var txtPath = outBase + ".txt";
            if (code != 0 || !File.Exists(txtPath))
            {
                // Без eng, если нет языкового пакета.
                (code, _) = Run(
                    "tesseract",
                    $"\"{imgPath}\" \"{outBase}\" -l rus --psm 6",
                    timeoutMs: 120_000);
            }

            if (!File.Exists(txtPath))
            {
                return null;
            }

            return File.ReadAllText(txtPath, Encoding.UTF8);
        }
        catch
        {
            return null;
        }
        finally
        {
            try
            {
                Directory.Delete(tmpDir, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }

    private static string DetectImageExtension(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ".jpg";
        }

        if (bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return ".png";
        }

        return ".img";
    }

    private static (int ExitCode, string Output) Run(string fileName, string arguments, int timeoutMs)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        if (p is null)
        {
            return (-1, string.Empty);
        }

        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(timeoutMs))
        {
            try
            {
                p.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            return (-1, stdout + stderr);
        }

        return (p.ExitCode, stdout + stderr);
    }

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

        if (keywords == 0)
        {
            return Math.Min(cyr / 20, 30);
        }

        return keywords + Math.Min(cyr, 200);
    }
}
