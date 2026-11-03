using Zashita.Core.Navigation;

namespace Zashita.Web.Services;

public static class SoutItemRouter
{
    private const string SoutSlug = "специальная-оценка";

    public static bool TryResolveRedirect(string moduleSlug, string? itemTitle, string? itemSlug, out string? url)
    {
        url = null;
        if (!IsSoutModule(moduleSlug))
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

        return false;
    }

    private static string? SlugToText(string? slug) =>
        string.IsNullOrWhiteSpace(slug) ? null : slug.Replace('-', ' ');

    private static bool TryResolveByToken(string title, out string? url)
    {
        url = null;
        var lower = title.ToLowerInvariant().Replace('ё', 'е');

        if (title.Equals("Оценка рабочих мест по условиям труда", StringComparison.OrdinalIgnoreCase) ||
            (lower.Contains("оценк") && lower.Contains("услов")))
        {
            // Страница убрана — ведём к обычным рабочим местам / картам.
            url = "/Sout/Workplaces";
            return true;
        }

        if (title.Equals("Карты СОУТ", StringComparison.OrdinalIgnoreCase) ||
            (lower.Contains("карт") && lower.Contains("соут")))
        {
            url = "/Sout/Workplaces?view=cards";
            return true;
        }

        if (title.Equals("Сотрудники", StringComparison.OrdinalIgnoreCase))
        {
            url = "/Organizations";
            return true;
        }

        if (lower.Contains("закреп") && lower.Contains("сотруд"))
        {
            // Страница убрана — закрепление в отчёте по СОУТ / картах.
            url = "/Sout/Status";
            return true;
        }

        if (lower.Contains("шаблон") && (lower.Contains("печат") || lower.Contains("соут")))
        {
            url = "/Sout/Preparation/Templates";
            return true;
        }

        // Сразу хаб «Шаблоны для печати» (три вкладки), не список документов.
        if (lower.Contains("подготов") && (lower.Contains("соут") || lower.Contains("специаль")))
        {
            url = "/Sout/Preparation/Templates";
            return true;
        }

        if (lower.Contains("годов") && lower.Contains("план"))
        {
            url = "/Sout/Planned";
            return true;
        }

        if (lower.Contains("оборудован"))
        {
            url = "/Sout/Catalog?kind=equipment";
            return true;
        }

        if (lower.Contains("материал") || lower.Contains("сырь"))
        {
            url = "/Sout/Catalog?kind=materials";
            return true;
        }

        if (lower.Contains("класс") && lower.Contains("услов"))
        {
            url = "/Sout/ConditionsClasses";
            return true;
        }

        if (lower.Contains("комисси"))
        {
            url = "/Sout/Commissions";
            return true;
        }

        if (lower.Contains("импорт") && (lower.Contains("карт") || lower.Contains("word") || lower.Contains("docx")))
        {
            url = "/Sout/ImportCards";
            return true;
        }

        if (lower.Contains("загрузк") && lower.Contains("результат"))
        {
            url = "/Sout/Import";
            return true;
        }

        if (lower.Contains("загрузк") && (lower.Contains("соут") || lower.Contains("xtm") || lower.Contains("xml") || lower.Contains("файл")))
        {
            url = "/Sout/Import";
            return true;
        }

        if (lower.Contains("рабоч") && lower.Contains("мест"))
        {
            url = "/Sout/Workplaces";
            return true;
        }

        if (lower.Contains("оценк") && lower.Contains("услов"))
        {
            url = "/Sout/Workplaces";
            return true;
        }

        if (lower.Contains("состояни") || (lower.Contains("отчет") && lower.Contains("соут")) ||
            (lower.Contains("отчет") && lower.Contains("состояни")))
        {
            url = "/Sout/Status";
            return true;
        }

        if (lower.Contains("отчет") || lower.Contains("помощник"))
        {
            url = "/Sout/Status";
            return true;
        }

        return false;
    }

    public static bool IsSoutModule(string moduleSlug) =>
        moduleSlug.Equals(SoutSlug, StringComparison.OrdinalIgnoreCase) ||
        moduleSlug.Equals(ModuleSlug.ToSlug("Специальная оценка"), StringComparison.OrdinalIgnoreCase);
}
