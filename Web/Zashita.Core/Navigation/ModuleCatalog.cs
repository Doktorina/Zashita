namespace Zashita.Core.Navigation;

public static class ModuleCatalog
{
    public static IReadOnlyList<ModulePageDefinition> All => ModuleMenuRegistry.All;

    public static IEnumerable<(ModulePageDefinition Module, string ItemTitle)> EnumerateItems()
    {
        foreach (var module in All)
        {
            foreach (var title in ModuleMenuRegistry.EnumerateAllItemTitles(module))
            {
                yield return (module, title);
            }
        }
    }
}
