using Zashita.Core.Suot;

namespace Zashita.Web.Pages.Suot;

public sealed class SuotIndicatorTreeRow
{
    public SuotIndicator Indicator { get; init; } = new();

    public int Depth { get; init; }

    public bool HasChildren { get; init; }

    public string? ParentId { get; init; }
}

public static class SuotIndicatorTreeBuilder
{
    public static IReadOnlyList<SuotIndicatorTreeRow> Build(IEnumerable<SuotIndicator> source)
    {
        var items = source.ToList();
        var childrenByParent = items
            .Where(i => !string.IsNullOrWhiteSpace(i.ParentId))
            .GroupBy(i => i.ParentId!, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
                StringComparer.Ordinal);

        var roots = items
            .Where(i => string.IsNullOrWhiteSpace(i.ParentId) || !items.Any(p => p.Id == i.ParentId))
            .OrderBy(i => i.IsGroup ? 0 : 1)
            .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var rows = new List<SuotIndicatorTreeRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var root in roots)
        {
            Append(root, 0, null, rows, childrenByParent, seen);
        }

        foreach (var item in items.Where(i => seen.Add(i.Id)))
        {
            Append(item, 0, item.ParentId, rows, childrenByParent, seen);
        }

        return rows;
    }

    private static void Append(
        SuotIndicator item,
        int depth,
        string? parentId,
        List<SuotIndicatorTreeRow> rows,
        IReadOnlyDictionary<string, List<SuotIndicator>> childrenByParent,
        HashSet<string> seen)
    {
        if (!seen.Add(item.Id))
        {
            return;
        }

        var children = childrenByParent.TryGetValue(item.Id, out var list) ? list : [];
        rows.Add(new SuotIndicatorTreeRow
        {
            Indicator = item,
            Depth = depth,
            HasChildren = children.Count > 0,
            ParentId = parentId
        });

        foreach (var child in children)
        {
            Append(child, depth + 1, item.Id, rows, childrenByParent, seen);
        }
    }
}
