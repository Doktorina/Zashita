using System.IO.Compression;
using System.Xml.Linq;

namespace Zashita.Core.Risks;

/// <summary>
/// Починка .docx с битыми связями (часто отсутствуют header/footer после выгрузки из Telegram/редакторов).
/// Без этого Open XML падает: «Specified part does not exist in the package».
/// </summary>
public static class DocxPackageRepair
{
    private static readonly XNamespace RelNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Копия потока в память; при необходимости удаляет битые Relationship.</summary>
    public static MemoryStream ToRepairedMemoryStream(Stream input)
    {
        var ms = new MemoryStream();
        input.CopyTo(ms);
        ms.Position = 0;

        if (!LooksLikeZip(ms))
        {
            ms.Position = 0;
            return ms;
        }

        try
        {
            RepairInPlace(ms);
        }
        catch
        {
            // Если починить не удалось — вернём исходную копию (ошибка всплывёт при OpenXml).
            ms.Position = 0;
        }

        ms.Position = 0;
        return ms;
    }

    public static void RepairInPlace(MemoryStream package)
    {
        package.Position = 0;
        using var zip = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);
        var existing = new HashSet<string>(
            zip.Entries.Select(e => NormalizePath(e.FullName)),
            StringComparer.OrdinalIgnoreCase);

        var relEntries = zip.Entries
            .Where(e => e.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.FullName)
            .ToList();

        foreach (var relPath in relEntries)
        {
            FixRelationships(zip, relPath, existing);
        }
    }

    private static void FixRelationships(ZipArchive zip, string relPath, HashSet<string> existing)
    {
        var entry = zip.GetEntry(relPath);
        if (entry is null)
        {
            return;
        }

        string xml;
        using (var reader = new StreamReader(entry.Open()))
        {
            xml = reader.ReadToEnd();
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch
        {
            return;
        }

        if (doc.Root is null)
        {
            return;
        }

        var baseFolder = GetRelationshipsBaseFolder(relPath);
        var removed = false;
        foreach (var rel in doc.Root.Elements(RelNs + "Relationship").ToList())
        {
            if (string.Equals(rel.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = rel.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(target) ||
                target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var resolved = NormalizePath(CombineZipPath(baseFolder, target));
            if (existing.Contains(resolved))
            {
                continue;
            }

            rel.Remove();
            removed = true;
        }

        if (!removed)
        {
            return;
        }

        entry.Delete();
        var created = zip.CreateEntry(relPath);
        using var writer = new StreamWriter(created.Open());
        doc.Save(writer);
    }

    private static string GetRelationshipsBaseFolder(string relPath)
    {
        // word/_rels/document.xml.rels → word/
        // _rels/.rels → (package root)
        var normalized = NormalizePath(relPath);
        var idx = normalized.LastIndexOf("/_rels/", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return string.Empty;
        }

        return normalized[..idx];
    }

    private static string CombineZipPath(string baseFolder, string target)
    {
        var t = target.Replace('\\', '/');
        while (t.StartsWith("./", StringComparison.Ordinal))
        {
            t = t[2..];
        }

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(baseFolder))
        {
            parts.AddRange(baseFolder.Split('/', StringSplitOptions.RemoveEmptyEntries));
        }

        foreach (var part in t.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }

                continue;
            }

            parts.Add(part);
        }

        return string.Join('/', parts);
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static bool LooksLikeZip(Stream stream)
    {
        if (!stream.CanSeek || stream.Length < 4)
        {
            return false;
        }

        var pos = stream.Position;
        Span<byte> header = stackalloc byte[4];
        var read = stream.Read(header);
        stream.Position = pos;
        return read == 4 && header[0] == (byte)'P' && header[1] == (byte)'K';
    }
}
