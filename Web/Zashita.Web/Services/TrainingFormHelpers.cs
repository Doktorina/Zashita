namespace Zashita.Web.Services;

public static class TrainingFormHelpers
{
    public static string? NormalizeParentId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Флаг удаления строки каталога (в каждой строке hidden Delete=false, при удалении — true).</summary>
    public static bool IsMarkedForDelete(Microsoft.Extensions.Primitives.StringValues deletes, int index) =>
        index < deletes.Count &&
        string.Equals(deletes[index].ToString(), "true", StringComparison.OrdinalIgnoreCase);
}
