using Zashita.Core.Med;

namespace Zashita.Web.Services;

/// <summary>
/// Подписи направлений МО: основное хранилище App_Data/med/signatures,
/// зеркало в wwwroot/uploads/med-signatures для старых ссылок.
/// </summary>
public sealed class MedDirectionSignatureStorage
{
    public const string PublicUrlPrefix = "/uploads/med-signatures/";

    private readonly MedDataPaths _paths;
    private readonly IWebHostEnvironment _env;

    public MedDirectionSignatureStorage(MedDataPaths paths, IWebHostEnvironment env)
    {
        _paths = paths;
        _env = env;
        Directory.CreateDirectory(_paths.SignaturesFolder);
        Directory.CreateDirectory(WwwRootFolder);
    }

    private string WwwRootFolder =>
        Path.Combine(_env.WebRootPath, "uploads", "med-signatures");

    public static string NormalizeStoredUrl(string? url)
    {
        var value = url?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var q = value.IndexOf('?', StringComparison.Ordinal);
        if (q >= 0)
        {
            value = value[..q];
        }

        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return value.StartsWith(PublicUrlPrefix, StringComparison.OrdinalIgnoreCase)
            ? value
            : string.Empty;
    }

    public string Save(string directionId, Stream content, string extension)
    {
        var ext = NormalizeExtension(extension);
        var safeId = SanitizeId(directionId);
        var fileName = safeId + ext;
        var durablePath = Path.Combine(_paths.SignaturesFolder, fileName);
        var publicPath = Path.Combine(WwwRootFolder, fileName);

        Directory.CreateDirectory(_paths.SignaturesFolder);
        Directory.CreateDirectory(WwwRootFolder);

        using (var fs = File.Create(durablePath))
        {
            content.CopyTo(fs);
        }

        // Зеркало для совместимости со старыми URL /uploads/...
        try
        {
            File.Copy(durablePath, publicPath, overwrite: true);
        }
        catch
        {
            // App_Data важнее зеркала.
        }

        // Убрать старые расширения той же карточки.
        foreach (var oldExt in new[] { ".png", ".jpg", ".jpeg", ".webp" })
        {
            if (string.Equals(oldExt, ext, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DeleteFileIfExists(Path.Combine(_paths.SignaturesFolder, safeId + oldExt));
            DeleteFileIfExists(Path.Combine(WwwRootFolder, safeId + oldExt));
        }

        return PublicUrlPrefix + fileName;
    }

    public void Delete(string? storedUrl)
    {
        var url = NormalizeStoredUrl(storedUrl);
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var name = Path.GetFileName(url);
        if (string.IsNullOrWhiteSpace(name) || name.Contains("..", StringComparison.Ordinal))
        {
            return;
        }

        DeleteFileIfExists(Path.Combine(_paths.SignaturesFolder, name));
        DeleteFileIfExists(Path.Combine(WwwRootFolder, name));
    }

    /// <summary>
    /// Читает байты подписи: сначала App_Data, потом wwwroot.
    /// При необходимости поднимает зеркало wwwroot из App_Data.
    /// </summary>
    public bool TryRead(string? storedUrl, string? directionId, out byte[] bytes, out string mime, out string publicUrl)
    {
        bytes = [];
        mime = "image/png";
        publicUrl = NormalizeStoredUrl(storedUrl);

        var path = ResolveExistingPath(storedUrl, directionId, out publicUrl);
        if (path is null)
        {
            return false;
        }

        try
        {
            bytes = ReadWithRetry(path) ?? [];
            if (bytes.Length == 0)
            {
                return false;
            }

            mime = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };

            // Подтянуть зеркало, если пропало из wwwroot.
            EnsureWwwRootMirror(path, Path.GetFileName(path));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>data: URL для предпросмотра/PDF/Word — без битой ссылки на отсутствующий файл.</summary>
    public string? TryResolveDataUrl(string? storedUrl, string? directionId = null)
    {
        var url = storedUrl?.Trim() ?? string.Empty;
        if (url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        if (!TryRead(storedUrl, directionId, out var bytes, out var mime, out _))
        {
            return null;
        }

        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }

    public string? FindPublicUrlByDirectionId(string? directionId)
    {
        var safeId = SanitizeId(directionId ?? string.Empty);
        if (string.IsNullOrWhiteSpace(safeId))
        {
            return null;
        }

        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" })
        {
            var durable = Path.Combine(_paths.SignaturesFolder, safeId + ext);
            if (File.Exists(durable))
            {
                EnsureWwwRootMirror(durable, safeId + ext);
                return PublicUrlPrefix + safeId + ext;
            }

            var www = Path.Combine(WwwRootFolder, safeId + ext);
            if (File.Exists(www))
            {
                // Поднять в App_Data, если там пусто.
                try
                {
                    if (!File.Exists(durable))
                    {
                        File.Copy(www, durable, overwrite: false);
                    }
                }
                catch
                {
                    // ignore
                }

                return PublicUrlPrefix + safeId + ext;
            }
        }

        return null;
    }

    private string? ResolveExistingPath(string? storedUrl, string? directionId, out string publicUrl)
    {
        publicUrl = NormalizeStoredUrl(storedUrl);
        if (!string.IsNullOrWhiteSpace(publicUrl))
        {
            var name = Path.GetFileName(publicUrl);
            if (!string.IsNullOrWhiteSpace(name) && !name.Contains("..", StringComparison.Ordinal))
            {
                var durable = Path.Combine(_paths.SignaturesFolder, name);
                if (File.Exists(durable))
                {
                    return durable;
                }

                var www = Path.Combine(WwwRootFolder, name);
                if (File.Exists(www))
                {
                    return www;
                }
            }
        }

        var recovered = FindPublicUrlByDirectionId(directionId);
        if (string.IsNullOrWhiteSpace(recovered))
        {
            publicUrl = string.Empty;
            return null;
        }

        publicUrl = recovered;
        var recoveredName = Path.GetFileName(recovered);
        var recoveredDurable = Path.Combine(_paths.SignaturesFolder, recoveredName);
        if (File.Exists(recoveredDurable))
        {
            return recoveredDurable;
        }

        var recoveredWww = Path.Combine(WwwRootFolder, recoveredName);
        return File.Exists(recoveredWww) ? recoveredWww : null;
    }

    private void EnsureWwwRootMirror(string sourcePath, string fileName)
    {
        try
        {
            Directory.CreateDirectory(WwwRootFolder);
            var target = Path.Combine(WwwRootFolder, fileName);
            if (!File.Exists(target))
            {
                File.Copy(sourcePath, target, overwrite: false);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string SanitizeId(string directionId)
    {
        var safe = string.Concat((directionId ?? string.Empty)
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
        return string.IsNullOrWhiteSpace(safe) ? Guid.NewGuid().ToString("N") : safe;
    }

    private static string NormalizeExtension(string? extension)
    {
        var ext = (extension ?? string.Empty).Trim().ToLowerInvariant();
        if (!ext.StartsWith('.'))
        {
            ext = "." + ext;
        }

        return ext is ".png" or ".jpg" or ".jpeg" or ".webp" ? ext : ".png";
    }

    private static void DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static byte[]? ReadWithRetry(string path)
    {
        for (var i = 0; i < 3; i++)
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (IOException) when (i < 2)
            {
                Thread.Sleep(40 * (i + 1));
            }
        }

        return null;
    }
}
