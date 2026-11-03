namespace Zashita.Core.Med;

public sealed class MedConclusionScanStore
{
    public const long MaxBytes = 20 * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png", ".webp"
    };

    private readonly MedDataPaths _paths;

    public MedConclusionScanStore(MedDataPaths paths) => _paths = paths;

    public string Save(string examId, Stream content, string originalFileName)
    {
        if (string.IsNullOrWhiteSpace(examId))
        {
            throw new ArgumentException("Не указан идентификатор медосмотра.", nameof(examId));
        }

        var ext = Path.GetExtension(originalFileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(ext) || !AllowedExtensions.Contains(ext))
        {
            throw new InvalidOperationException("Допустимые форматы: PDF, JPG, PNG, WEBP.");
        }

        Directory.CreateDirectory(_paths.ScansFolder);
        var storedName = SanitizeExamId(examId) + ext.ToLowerInvariant();
        var fullPath = Path.Combine(_paths.ScansFolder, storedName);

        if (content.CanSeek && content.Length > MaxBytes)
        {
            throw new InvalidOperationException($"Файл больше {MaxBytes / (1024 * 1024)} МБ.");
        }

        using var target = File.Create(fullPath);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = content.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaxBytes)
            {
                target.Close();
                File.Delete(fullPath);
                throw new InvalidOperationException($"Файл больше {MaxBytes / (1024 * 1024)} МБ.");
            }

            target.Write(buffer, 0, read);
        }

        return storedName;
    }

    public string? GetFullPath(string? storedFileName)
    {
        if (string.IsNullOrWhiteSpace(storedFileName))
        {
            return null;
        }

        var safeName = Path.GetFileName(storedFileName);
        var fullPath = Path.Combine(_paths.ScansFolder, safeName);
        return File.Exists(fullPath) ? fullPath : null;
    }

    public void Delete(string? storedFileName)
    {
        var fullPath = GetFullPath(storedFileName);
        if (fullPath is not null)
        {
            File.Delete(fullPath);
        }
    }

    public void Migrate(string oldExamId, string newExamId, string? storedFileName)
    {
        if (string.IsNullOrWhiteSpace(storedFileName) ||
            string.Equals(oldExamId, newExamId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var oldPath = GetFullPath(storedFileName);
        if (oldPath is null)
        {
            return;
        }

        var ext = Path.GetExtension(storedFileName);
        var newName = SanitizeExamId(newExamId) + ext;
        var newPath = Path.Combine(_paths.ScansFolder, newName);
        if (File.Exists(newPath))
        {
            File.Delete(newPath);
        }

        File.Move(oldPath, newPath);
    }

    private static string SanitizeExamId(string examId) =>
        string.Concat(examId.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
}
