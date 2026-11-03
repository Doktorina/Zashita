using Zashita.Core.Navigation;
using Zashita.Core.Training;

namespace Zashita.Web.Services;

public static class TrainingItemRouter
{
    private const string TrainingSlug = "обучение";

    public static bool TryResolveRedirect(string moduleSlug, string? itemTitle, out string? url) =>
        TryResolveRedirect(moduleSlug, itemTitle, itemSlug: null, out url);

    public static bool TryResolveRedirect(string moduleSlug, string? itemTitle, string? itemSlug, out string? url)
    {
        url = null;
        if (!IsTrainingModule(moduleSlug))
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

        // Любой пункт модуля «Обучение» — хотя бы каталог отчётов, не заглушка Item.
        url = "/Training/Reports";
        return true;
    }

    private static string? SlugToText(string? slug) =>
        string.IsNullOrWhiteSpace(slug) ? null : slug.Replace('-', ' ');

    private static bool TryResolveByToken(string t, out string? url)
    {
        url = null;
        var lower = t.ToLowerInvariant().Replace('ё', 'е');

        if (t.Equals("Журналы по обучению", StringComparison.Ordinal) ||
            t.Equals("Отчеты по обучению", StringComparison.Ordinal) ||
            (lower.Contains("журнал") && lower.Contains("обучен")) ||
            string.Equals(t, ModuleMenuRegistry.TrainingMainSectionTitle, StringComparison.OrdinalIgnoreCase) ||
            (lower.Contains("обучен") && lower.Contains("инструктаж") && lower.Contains("стажиров")))
        {
            url = "/Training/Reports";
            return true;
        }

        if (lower.Contains("помощник") && lower.Contains("обучен"))
        {
            url = "/Training/Assistants";
            return true;
        }

        if ((lower.Contains("реестр") && lower.Contains("электро")) ||
            (lower.Contains("проверк") && lower.Contains("знан") && lower.Contains("эб")) ||
            (lower.Contains("комисси") && lower.Contains("эб")))
        {
            url = lower.Contains("комисси") ? "/Electrical/Commissions" : "/Electrical/Register";
            return true;
        }

        // Главный отчёт модуля — таблица состояния
        if ((lower.Contains("отчет") && lower.Contains("обучен") && !lower.Contains("отчеты")) ||
            (lower.Contains("состояние") && lower.Contains("обучен")))
        {
            url = "/Training/Status";
            return true;
        }

        if (lower.Contains("архив") && lower.Contains("обучен"))
        {
            url = "/Training/Archive";
            return true;
        }

        if (lower.Contains("отчет") && lower.Contains("обучен"))
        {
            url = "/Training/Reports";
            return true;
        }

        if ((lower.Contains("квалификац") && lower.Contains("требован")) ||
            (lower.Contains("стандарт") && lower.Contains("обучен")))
        {
            url = "/Training/Standards";
            return true;
        }

        if (lower.Contains("проведен") && !lower.Contains("состояние"))
        {
            url = "/Training/Planning";
            return true;
        }

        if (lower.Contains("виды") && lower.Contains("планов"))
        {
            url = "/Training/PlanTypes";
            return true;
        }

        if (t.Equals("Календарь потребностей", StringComparison.Ordinal) ||
            (lower.Contains("календар") && lower.Contains("потреб")))
        {
            url = "/Training/Calendar";
            return true;
        }

        if (t.Equals("Заявки на обучение", StringComparison.Ordinal) ||
            (lower.Contains("заявк") && lower.Contains("обучен")))
        {
            url = "/Training/Applications";
            return true;
        }

        if (t.Equals("Планирование", StringComparison.Ordinal) ||
            (lower.Contains("планирование") && lower.Contains("обучен") && lower.Contains("инструктаж")))
        {
            url = "/Training/Planning";
            return true;
        }

        if (lower.Contains("годовой") && lower.Contains("план") ||
            lower.Contains("план обучен") ||
            lower.Contains("план проверок") ||
            (lower.Contains("планируем") && lower.Contains("инструктаж")))
        {
            url = "/Training/Planned";
            return true;
        }

        if (lower.Contains("сценари"))
        {
            url = "/Training/Planned";
            return true;
        }

        if (lower.Contains("виды") && lower.Contains("обучен"))
        {
            url = "/Training/Kinds";
            return true;
        }

        if (lower.Contains("программ") && lower.Contains("курс"))
        {
            url = "/Training/Programs";
            return true;
        }

        if (lower.Contains("соответств") &&
            ((lower.Contains("видов") && lower.Contains("работ"))
             || (lower.Contains("видов") && lower.Contains("программ"))
             || (lower.Contains("программ") && lower.Contains("обучен"))))
        {
            url = "/Training/WorkTypeMapping";
            return true;
        }

        return false;
    }

    public static string? ResolveReportUrl(string title)
    {
        var lower = title.ToLowerInvariant().Replace('ё', 'е');

        if ((lower.Contains("состояние") && lower.Contains("обучен")) ||
            (lower.Contains("отчет") && lower.Contains("обучен") && !lower.Contains("отчеты")))
        {
            return "/Training/Status";
        }

        if (lower.Contains("планируем") || lower.Contains("план обучен") ||
            lower.Contains("план проверок") || (lower.Contains("план") && lower.Contains("инструктаж")))
        {
            return "/Training/Planned";
        }

        if (lower.Contains("заявк") && lower.Contains("обучен"))
        {
            return "/Training/Applications";
        }

        if (lower.Contains("планирование") && lower.Contains("обучен"))
        {
            return "/Training/Planning";
        }

        if (lower.Contains("истори") && lower.Contains("печат"))
        {
            return "/Training/PrintHistory";
        }

        if (lower.Contains("журнал"))
        {
            return $"/Training/Journal?title={Uri.EscapeDataString(title)}";
        }

        return "/Training/Reports";
    }

    public static bool IsTrainingModule(string moduleSlug) =>
        moduleSlug.Equals(TrainingSlug, StringComparison.OrdinalIgnoreCase);
}
