using System.Text;
using System.Xml.Linq;

namespace Zashita.Core.Risks;

public sealed class SoutXtmImportService
{
    private static readonly HashSet<string> XmlExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xtm", ".xml"
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".pdf", ".xls", ".xlsx"
    };

    private readonly RiskDataPaths _paths;

    public SoutXtmImportService(RiskDataPaths paths) => _paths = paths;

    public SoutXtmImportResult Import(string organization, Stream content, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!XmlExtensions.Contains(extension) && !BinaryExtensions.Contains(extension))
        {
            return SoutXtmImportResult.Fail(
                "Поддерживаются файлы: XTM, XML, Word (.doc/.docx), PDF, Excel (.xls/.xlsx).");
        }

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        using var memory = new MemoryStream();
        content.CopyTo(memory);
        if (memory.Length == 0)
        {
            return SoutXtmImportResult.Fail("Файл пустой.");
        }

        var bytes = memory.ToArray();
        var savedPath = SaveFile(organization, fileName, bytes);

        if (XmlExtensions.Contains(extension))
        {
            try
            {
                var text = Encoding.UTF8.GetString(bytes);
                if (text.Length > 0 && text[0] == '\uFEFF')
                {
                    text = text[1..];
                }

                var document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
                var rootName = document.Root?.Name.LocalName ?? "неизвестно";
                return SoutXtmImportResult.Ok(fileName, rootName, savedPath);
            }
            catch (Exception ex)
            {
                return SoutXtmImportResult.Fail($"Не удалось прочитать XML/XTM: {ex.Message}");
            }
        }

        var kind = extension switch
        {
            ".doc" or ".docx" => "Word",
            ".pdf" => "PDF",
            ".xls" or ".xlsx" => "Excel",
            _ => "файл"
        };

        return SoutXtmImportResult.OkBinary(fileName, kind, savedPath);
    }

    public IReadOnlyList<SoutImportFileInfo> ListFiles(string organization)
    {
        var folder = GetOrgFolder(organization);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return Directory.GetFiles(folder)
            .Select(path =>
            {
                var info = new FileInfo(path);
                var ext = info.Extension.ToLowerInvariant();
                var kind = ext switch
                {
                    ".xtm" or ".xml" => "XTM/XML",
                    ".doc" or ".docx" => "Word",
                    ".pdf" => "PDF",
                    ".xls" or ".xlsx" => "Excel",
                    _ => "файл"
                };
                return new SoutImportFileInfo
                {
                    FileName = info.Name,
                    Kind = kind,
                    SizeBytes = info.Length,
                    UploadedAt = info.LastWriteTime
                };
            })
            .OrderByDescending(f => f.UploadedAt)
            .ToList();
    }

    public string? ResolveFilePath(string organization, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var safe = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(safe) || safe.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        var path = Path.Combine(GetOrgFolder(organization), safe);
        return File.Exists(path) ? path : null;
    }

    public bool DeleteFile(string organization, string fileName)
    {
        var path = ResolveFilePath(organization, fileName);
        if (path is null)
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    private string GetOrgFolder(string organization)
    {
        var root = Path.GetDirectoryName(_paths.WorkplacesFile)!;
        return Path.Combine(root, "imports", Sanitize(organization));
    }

    private string SaveFile(string organization, string fileName, byte[] bytes)
    {
        var folder = GetOrgFolder(organization);
        Directory.CreateDirectory(folder);
        var safeName = Sanitize(Path.GetFileName(fileName));
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = $"sout-{DateTime.UtcNow:yyyyMMddHHmmss}.bin";
        }

        var path = Path.Combine(folder, safeName);
        if (File.Exists(path))
        {
            var name = Path.GetFileNameWithoutExtension(safeName);
            var ext = Path.GetExtension(safeName);
            path = Path.Combine(folder, $"{name}-{DateTime.UtcNow:yyyyMMddHHmmss}{ext}");
        }

        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        return builder.ToString();
    }
}

public sealed class SoutImportFileInfo
{
    public string FileName { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTime UploadedAt { get; init; }

    public string SizeDisplay =>
        SizeBytes < 1024 ? $"{SizeBytes} Б"
        : SizeBytes < 1024 * 1024 ? $"{SizeBytes / 1024.0:0.#} КБ"
        : $"{SizeBytes / (1024.0 * 1024.0):0.#} МБ";
}

public sealed class SoutXtmImportResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? FileName { get; init; }
    public string? RootElement { get; init; }
    public string? SavedPath { get; init; }

    public static SoutXtmImportResult Ok(string fileName, string rootElement, string savedPath) => new()
    {
        Success = true,
        FileName = fileName,
        RootElement = rootElement,
        SavedPath = savedPath,
        Message = $"Файл «{fileName}» принят. Результаты загружены (корневой элемент: {rootElement})."
    };

    public static SoutXtmImportResult OkBinary(string fileName, string kind, string savedPath) => new()
    {
        Success = true,
        FileName = fileName,
        RootElement = kind,
        SavedPath = savedPath,
        Message = $"Файл «{fileName}» ({kind}) принят и сохранён в архив результатов."
    };

    public static SoutXtmImportResult Fail(string message) => new()
    {
        Success = false,
        Message = message
    };
}
