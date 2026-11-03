namespace Zashita.Core.Training;

public sealed record TrainingCatalogTreeItem<T>(
    T Node,
    int Depth,
    bool HasChildren);

public static class TrainingCatalogTree
{
    public static List<TrainingCatalogTreeItem<T>> Order<T>(
        IEnumerable<T> nodes,
        Func<T, string> getId,
        Func<T, string?> getParentId,
        Func<T, bool> isGroup,
        Func<T, string> getName)
    {
        var list = nodes
            .GroupBy(getId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (list.Count == 0)
        {
            return [];
        }

        var childrenByParent = list
            .GroupBy(node => getParentId(node) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var result = new List<TrainingCatalogTreeItem<T>>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Walk(string? parentId, int depth)
        {
            var key = parentId ?? string.Empty;
            if (!childrenByParent.TryGetValue(key, out var children))
            {
                return;
            }

            foreach (var child in SortNodes(children, isGroup, getName))
            {
                var id = getId(child);
                if (!visited.Add(id))
                {
                    continue;
                }

                var hasChildren = childrenByParent.ContainsKey(id);
                result.Add(new TrainingCatalogTreeItem<T>(child, depth, hasChildren));
                if (isGroup(child))
                {
                    Walk(id, depth + 1);
                }
            }
        }

        Walk(null, 0);

        foreach (var node in SortNodes(list.Where(node => !visited.Contains(getId(node))).ToList(), isGroup, getName))
        {
            var id = getId(node);
            if (!visited.Add(id))
            {
                continue;
            }

            var hasChildren = childrenByParent.ContainsKey(id);
            result.Add(new TrainingCatalogTreeItem<T>(node, 0, hasChildren));
            if (isGroup(node))
            {
                Walk(id, 1);
            }
        }

        return result;
    }

    private static IEnumerable<T> SortNodes<T>(
        IReadOnlyList<T> nodes,
        Func<T, bool> isGroup,
        Func<T, string> getName) =>
        nodes
            .OrderByDescending(isGroup)
            .ThenBy(getName, StringComparer.CurrentCultureIgnoreCase);
}
