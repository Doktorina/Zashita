using Zashita.Core.Navigation;

namespace Zashita.Web.Services;

public static class RisksItemRouter
{
    private const string RisksSlug = "риски";

    public static bool TryResolveRedirect(string moduleSlug, string? itemTitle, string? itemSlug, out string? url)
    {
        url = null;
        if (!IsRisksModule(moduleSlug))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(itemTitle) && RisksModuleLinks.TryResolve(itemTitle, out url))
        {
            return true;
        }

        foreach (var token in new[] { itemTitle, SlugToText(itemSlug) })
        {
            if (!string.IsNullOrWhiteSpace(token) && TryResolveByToken(token, out url))
            {
                return true;
            }
        }

        url = "/Risks/Workplaces";
        return true;
    }

    public static bool TryResolveByTitle(string? itemTitle, out string? url)
    {
        url = null;
        if (string.IsNullOrWhiteSpace(itemTitle))
        {
            return false;
        }

        return TryResolveByToken(itemTitle, out url);
    }

    private static string? SlugToText(string? slug) =>
        string.IsNullOrWhiteSpace(slug) ? null : slug.Replace('-', ' ');

    private static bool TryResolveByToken(string title, out string? url)
    {
        url = null;
        var lower = title.ToLowerInvariant().Replace('ё', 'е');

        if (lower.Contains("внесение") && lower.Contains("изменен"))
        {
            url = "/Risks/Changes";
            return true;
        }

        if (lower.Contains("документ") && lower.Contains("оценк") && lower.Contains("риск"))
        {
            url = "/Risks/Documents";
            return true;
        }

        if ((lower.Contains("приказ") || lower.Contains("распоряж")) &&
            (lower.Contains("внепланов") || lower.Contains("оценк")))
        {
            url = "/Risks/Documents?tab=order";
            return true;
        }

        if (lower.Contains("подлежащ") && lower.Contains("оценк"))
        {
            url = "/Risks/AssessmentList";
            return true;
        }

        if (lower.Contains("без оценки") ||
            (lower.Contains("перечень") && lower.Contains("без") && lower.Contains("риск")))
        {
            url = "/Risks/AssessmentList?needFilter=notconducted";
            return true;
        }

        if (lower.Contains("график") && lower.Contains("идентификац"))
        {
            url = "/Risks/IdentificationSchedule";
            return true;
        }

        if (lower.Contains("карт") && lower.Contains("оценк") && lower.Contains("риск"))
        {
            url = "/Risks/Workplaces?view=assessment";
            return true;
        }

        if (lower.Contains("закреп") && lower.Contains("сотруд"))
        {
            // Закрепление в модуле «Риски» убрано — ведём к картам оценки.
            url = "/Risks/Workplaces?view=assessment";
            return true;
        }

        if (lower.Contains("подготов") && lower.Contains("оценк") && lower.Contains("риск"))
        {
            url = "/Risks/Documents?tab=preparation";
            return true;
        }

        if (lower.Contains("реестр") && lower.Contains("опасн"))
        {
            url = "/Risks/HazardCatalog";
            return true;
        }

        if (lower.Contains("отчет") && (lower.Contains("опр") || lower.Contains("риск")) && !lower.Contains("отчеты"))
        {
            url = "/Risks/Reports/Status";
            return true;
        }

        if ((lower.Contains("состоян") && lower.Contains("риск")) ||
            (lower.Contains("управлен") && lower.Contains("профессион") && lower.Contains("риск")))
        {
            url = "/Risks/Reports/Status";
            return true;
        }

        if (lower.Contains("перечень") && lower.Contains("рабоч") && lower.Contains("мест"))
        {
            url = "/Risks/Workplaces";
            return true;
        }

        if (lower.Contains("рабоч") && lower.Contains("мест"))
        {
            url = "/Risks/Workplaces";
            return true;
        }

        if (lower.Contains("комисс"))
        {
            url = "/Risks/Commissions";
            return true;
        }

        if (lower.Contains("отчеты") || lower.Contains("помощник"))
        {
            url = "/Risks/Reports";
            return true;
        }

        if (lower.Contains("отчет"))
        {
            url = "/Risks/Reports/Status";
            return true;
        }

        return false;
    }

    public static bool IsRisksModule(string moduleSlug)
    {
        if (string.IsNullOrWhiteSpace(moduleSlug))
        {
            return false;
        }

        var slug = moduleSlug.Trim();
        return slug.Equals(RisksSlug, StringComparison.OrdinalIgnoreCase) ||
               slug.Equals(ModuleSlug.ToSlug("Риски"), StringComparison.OrdinalIgnoreCase) ||
               slug.Contains("риск", StringComparison.OrdinalIgnoreCase);
    }
}
