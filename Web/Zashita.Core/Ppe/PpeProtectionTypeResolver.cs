namespace Zashita.Core.Ppe;

public static class PpeProtectionTypeResolver
{
    public static string Resolve(
        string itemName,
        string category,
        IReadOnlyDictionary<string, Appendix1NormEntry> appendixByName)
    {
        var name = itemName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        if (appendixByName.TryGetValue(name, out var exact))
        {
            return exact.ProtectionType;
        }

        var partial = appendixByName.Values
            .FirstOrDefault(entry =>
                name.Contains(entry.Name, StringComparison.OrdinalIgnoreCase) ||
                entry.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (partial is not null)
        {
            return partial.ProtectionType;
        }

        if (LooksLikeProtectionTypeName(name))
        {
            return name;
        }

        var dsizType = ResolveDsizType(name);
        if (!string.IsNullOrWhiteSpace(dsizType))
        {
            return dsizType;
        }

        var keywordType = ResolveByKeyword(name);
        if (!string.IsNullOrWhiteSpace(keywordType))
        {
            return keywordType;
        }

        return IsGenericType(category) ? string.Empty : category;
    }

    public static void Enrich(
        PpeNomenclatureItem item,
        IReadOnlyDictionary<string, Appendix1NormEntry> appendixByName)
    {
        if (!IsGenericType(item.ProtectionType))
        {
            if (string.IsNullOrWhiteSpace(item.NormParagraph) &&
                appendixByName.TryGetValue(item.Name ?? string.Empty, out var hit) &&
                !string.IsNullOrWhiteSpace(hit.NormParagraph))
            {
                item.NormParagraph = hit.NormParagraph;
            }

            return;
        }

        var resolved = Resolve(item.Name, item.Category, appendixByName);
        if (string.IsNullOrWhiteSpace(resolved) || IsGenericType(resolved))
        {
            return;
        }

        item.ProtectionType = resolved;
        item.Category = Appendix1NormsParser.GuessSizCategoryPublic(resolved, item.Name);

        if (string.IsNullOrWhiteSpace(item.NormParagraph) &&
            appendixByName.TryGetValue(item.Name ?? string.Empty, out var entry) &&
            !string.IsNullOrWhiteSpace(entry.NormParagraph))
        {
            item.NormParagraph = entry.NormParagraph;
        }
    }

    public static bool IsGenericType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var normalized = value.Trim().Replace(" ", "");
        return normalized.Equals("СИЗ", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("ДСИЗ", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("СИЗ+ДСИЗ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeProtectionTypeName(string name)
    {
        if (name.StartsWith("Средства ", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Одежда ", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Категория ", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Регенерирующ", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return name.Contains("типа", StringComparison.OrdinalIgnoreCase) &&
               name.Length >= 12;
    }

    private static string? ResolveDsizType(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("крем") && lower.Contains("защит"))
        {
            return "Средства защитного типа";
        }

        if (lower.Equals("мыло", StringComparison.OrdinalIgnoreCase) ||
            lower.StartsWith("мыло ", StringComparison.OrdinalIgnoreCase))
        {
            return "Средства очищающего типа";
        }

        if (lower.Contains("регенерир") || lower.Contains("восстанавлив"))
        {
            return "Средства регенерирующего (восстанавливающего) типа";
        }

        if (lower.Contains("биологическ"))
        {
            return "Средства для защиты от биологических факторов (микроорганизмов)";
        }

        if (lower.Contains("неустойчив") || lower.Contains("смыва"))
        {
            return "Средства для очищения от неустойчивых загрязнений и смывающие средства";
        }

        if (lower.Contains("устойчив") && lower.Contains("загрязн"))
        {
            return "Средства для очищения от устойчивых загрязнений";
        }

        if (lower.Contains("комбинирован") || lower.Contains("универсал"))
        {
            return "Средства комбинированного (универсального) действия";
        }

        return null;
    }

    private static string? ResolveByKeyword(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("ботинк") || lower.Contains("обув") || lower.Contains("сапог"))
        {
            return "Средства защиты ног";
        }

        if (lower.Contains("брюк") || lower.Contains("костюм") || lower.Contains("куртк") ||
            lower.Contains("жилет") || lower.Contains("халат"))
        {
            return "Одежда специальная защитная";
        }

        if (lower.Contains("перчат"))
        {
            return "Средства защиты рук";
        }

        if (lower.Contains("каск") || lower.Contains("головн") || lower.Contains("каскет"))
        {
            return "Средства защиты головы";
        }

        if (lower.Contains("очк"))
        {
            return "Средства защиты глаз";
        }

        return null;
    }
}
