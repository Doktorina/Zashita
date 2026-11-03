namespace Zashita.Core.Security;

public static class SecurePath
{
    public static string CombineUnder(string rootDirectory, string fileName)
    {
        var root = Path.GetFullPath(rootDirectory);
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName) ||
            safeName is "." or ".." ||
            safeName.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Недопустимое имя файла.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(root, safeName));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Путь выходит за пределы разрешённой директории.");
        }

        return fullPath;
    }
}
