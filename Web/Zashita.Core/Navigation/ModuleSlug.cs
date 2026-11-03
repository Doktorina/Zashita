using System.Text;

namespace Zashita.Core.Navigation;

public static class ModuleSlug
{
    public static string ToSlug(string title)
    {
        var sb = new StringBuilder(title.Length);
        var pendingDash = false;

        foreach (var ch in title.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingDash)
                {
                    sb.Append('-');
                    pendingDash = false;
                }

                sb.Append(char.ToLowerInvariant(ch));
                continue;
            }

            if (ch is ' ' or '_' or '/' or '\\' or '.' or ',')
            {
                pendingDash = true;
            }
        }

        return sb.Length == 0 ? "item" : sb.ToString();
    }

    public static ModulePageDefinition? FindModule(string slug) =>
        ModuleCatalog.All.FirstOrDefault(m => ToSlug(m.ModuleName) == slug);

    public static string GetModuleSlug(ModulePageDefinition module) => ToSlug(module.ModuleName);
}
