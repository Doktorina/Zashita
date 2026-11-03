using Zashita.Core.Navigation;

namespace Zashita.Web.Services;

public static class MedItemRouter
{
    private const string MedSlug = "медосмотры";

    public static bool TryResolveRedirect(string moduleSlug, string? itemTitle, string? itemSlug, out string? url)
    {
        url = null;
        if (!IsMedModule(moduleSlug))
        {
            return false;
        }

        foreach (var token in new[] { itemTitle, SlugToText(itemSlug) })
        {
            if (!string.IsNullOrWhiteSpace(token) && TryResolveByToken(token, out url))
            {
                return true;
            }
        }

        url = "/module/медосмотры";
        return true;
    }

    private static string? SlugToText(string? slug) =>
        string.IsNullOrWhiteSpace(slug) ? null : slug.Replace('-', ' ');

    private static bool TryResolveByToken(string title, out string? url)
    {
        url = null;
        var lower = title.ToLowerInvariant().Replace('ё', 'е');

        if (lower.Contains("импорт") && (lower.Contains("заключ") || lower.Contains("word") || lower.Contains("docx")))
        {
            url = "/Med/ImportConclusions";
            return true;
        }

        if (lower.Contains("отчет") && lower.Contains("медосмотр"))
        {
            url = "/Med/Status";
            return true;
        }

        if (lower is "список лиц" or "формирование поименного списка лиц" or "списки контингентов"
            || (lower.Contains("списк") && lower.Contains("контингент")))
        {
            url = lower.Contains("поименн") || lower.Contains("лпу")
                ? "/Med/ContingentLists?tab=named"
                : "/Med/ContingentLists?tab=contingents";
            return true;
        }

        if (lower.Contains("ограничен") && lower.Contains("физ"))
        {
            url = "/Med/Restrictions";
            return true;
        }

        if (lower.Contains("проведение") && lower.Contains("медосмотр"))
        {
            url = lower.Contains("ежесмен")
                ? "/Med/Exams?type=shift"
                : "/Med/Exams";
            return true;
        }

        if (lower.Contains("состояние") || lower.Contains("статус"))
        {
            url = "/Med/Status";
            return true;
        }

        if (lower.Contains("перечень факторов") && !lower.Contains("набор"))
        {
            url = "/Med/Factors";
            return true;
        }

        if (lower.Contains("календар") && lower.Contains("медосмотр"))
        {
            url = "/Med/Calendar";
            return true;
        }

        if ((lower.Contains("график") || lower.Contains("планирован")) && lower.Contains("медосмотр"))
        {
            url = "/Med/Schedule";
            return true;
        }

        if (lower.Contains("стандарт"))
        {
            url = "/Med/Standards";
            return true;
        }

        if ((lower.Contains("медицинск") || lower.Contains("медорганиза")) && lower.Contains("организа"))
        {
            url = "/Med/MedicalOrganizations";
            return true;
        }

        if (lower.Contains("направлен") && lower.Contains("медосмотр"))
        {
            url = "/Med/Directions";
            return true;
        }

        if (lower.Contains("настройк") && lower.Contains("медосмотр"))
        {
            url = "/module/медосмотры#med-setup";
            return true;
        }

        if (lower is "виды работ" || (lower.Contains("виды") && lower.Contains("работ") && !lower.Contains("соответств") && !lower.Contains("перечен") && !lower.Contains("набор")))
        {
            // Раздел убран из справочников — ведём на перечень факторов (прик. 29н).
            url = "/Med/Factors";
            return true;
        }

        if (lower.Contains("набор") && lower.Contains("фактор"))
        {
            url = "/Med/Reference?category=factor-sets";
            return true;
        }

        if (lower.Contains("противопоказан"))
        {
            url = "/Med/Reference?category=contraindications";
            return true;
        }

        if (lower.Contains("виды") && lower.Contains("ежесмен"))
        {
            url = "/Med/Reference?category=shift-types";
            return true;
        }

        if (lower.Contains("условн") && lower.Contains("обозначен"))
        {
            url = "/Med/Reference?category=shift-legend";
            return true;
        }

        if (lower.Contains("профзаболеван") && !lower.Contains("опрос") && !lower.Contains("расслед") &&
            !lower.Contains("мероприят"))
        {
            url = "/Med/Registry?kind=pfz";
            return true;
        }

        if (lower.Contains("опрос") && lower.Contains("профзаболеван"))
        {
            url = "/Med/Registry?kind=pfz-survey";
            return true;
        }

        if (lower.Contains("расследован") && lower.Contains("профзаболеван"))
        {
            url = "/Med/Registry?kind=pfz-investigation";
            return true;
        }

        if (lower.Contains("мероприят") && lower.Contains("профзаболеван"))
        {
            url = "/Med/Registry?kind=pfz-actions";
            return true;
        }

        var kind = ResolveRegistryKind(lower);
        if (kind is not null)
        {
            url = $"/Med/Registry?kind={Uri.EscapeDataString(kind)}&title={Uri.EscapeDataString(title)}";
            return true;
        }

        return false;
    }

    private static string? ResolveRegistryKind(string lower)
    {
        if (lower.Contains("стоимост")) return "cost";
        if (lower.Contains("стаж")) return "experience";
        if (lower.Contains("контингент") && lower.Contains("ежесмен")) return "shift-contingent";
        if (lower.Contains("контингент")) return "contingent";
        if (lower.Contains("исключен")) return "exceptions";
        if (lower.Contains("направлен") && lower.Contains("медосмотр")) return "direction";
        if (lower.Contains("профилактик") || lower.Contains("лечен")) return "prevention";
        if (lower.Contains("ежесмен") && lower.Contains("список")) return "shift-list";
        if (lower.Contains("соответств") && lower.Contains("элемент")) return "mo-mapping";
        if (lower.Contains("переход") && lower.Contains("перечен")) return "factor-migration";
        if (lower.Contains("соответств") && lower.Contains("видов") && lower.Contains("работ")) return "work-types";

        return "general";
    }

    public static bool IsMedModule(string moduleSlug) =>
        moduleSlug.Equals(MedSlug, StringComparison.OrdinalIgnoreCase) ||
        moduleSlug.Equals(ModuleSlug.ToSlug(ModuleUiCatalog.MedExams.ModuleName), StringComparison.OrdinalIgnoreCase);
}
