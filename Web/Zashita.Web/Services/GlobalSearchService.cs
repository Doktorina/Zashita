using System.Text.Json;
using Zashita.Core.Navigation;

namespace Zashita.Web.Services;

public sealed record SearchHit(
    string Title,
    string Hint,
    string Url,
    string Kind = "item");

public sealed class GlobalSearchService
{
    private readonly OrganizationStore _organizations;
    private IReadOnlyList<SearchHit> _index = [];

    public GlobalSearchService(OrganizationStore organizations)
    {
        _organizations = organizations;
        RebuildIndex();
    }

    public void RebuildIndex() =>
        _index = BuildIndex(_organizations.GetNonHiddenOrganizations());

    public IReadOnlyList<SearchHit> Search(string? query, int limit = 30)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var q = query.Trim();
        return _index
            .Where(hit => hit.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || hit.Hint.Contains(q, StringComparison.OrdinalIgnoreCase)
                || hit.Kind.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(hit => hit.Kind == "module" ? 0 : hit.Kind == "section" ? 1 : 2)
            .ThenBy(hit => hit.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(limit)
            .ToList();
    }

    private static IReadOnlyList<SearchHit> BuildIndex(IReadOnlyList<string> organizations)
    {
        var hits = new List<SearchHit>();

        foreach (var item in ModuleUiCatalog.Sidebar)
        {
            if (item.IsHome)
            {
                hits.Add(new SearchHit(
                    item.Title,
                    "Главная",
                    "/",
                    "module"));
                continue;
            }

            hits.Add(new SearchHit(
                item.ShortTitle,
                $"Раздел: {item.Title}",
                $"/module/{item.Slug}",
                "module"));

            if (item.Definition is null)
            {
                continue;
            }

            foreach (var section in item.Definition.LeftSections.Concat(item.Definition.RightSections))
            {
                hits.Add(new SearchHit(
                    section.Title,
                    $"Блок: {item.ShortTitle}",
                    $"/module/{item.Slug}",
                    "section"));

                foreach (var title in section.Items)
                {
                    hits.Add(new SearchHit(
                        title,
                        $"{item.ShortTitle} → {section.Title}",
                        ModuleItemUrlResolver.Resolve(item.Slug, title),
                        "card"));
                }
            }

            foreach (var title in item.Definition.TopLinks)
            {
                hits.Add(new SearchHit(
                    title,
                    $"Раздел: {item.ShortTitle}",
                    ModuleItemUrlResolver.Resolve(item.Slug, title),
                    "card"));
            }
        }

        foreach (var org in organizations)
        {
            hits.Add(new SearchHit(
                org,
                "Организация",
                "/Organizations",
                "organization"));
        }

        hits.Add(new SearchHit(
            "Данные 1С",
            "Подключение Excel-файла",
            "/Data/Upload",
            "data"));

        hits.Add(new SearchHit(
            "Организации",
            "Справочник организаций",
            "/Organizations",
            "organization"));

        hits.Add(new SearchHit(
            "Соответствие видов и программ обучения",
            "Виды обучения → программы (курсы)",
            "/Training/WorkTypeMapping",
            "card"));

        hits.Add(new SearchHit(
            "Соответствие программ обучения",
            "Виды обучения → программы (курсы)",
            "/Training/WorkTypeMapping",
            "card"));

        hits.Add(new SearchHit(
            "Виды планов",
            "Обучение → Годовое планирование",
            "/Training/PlanTypes",
            "card"));

        hits.Add(new SearchHit(
            "Заявки на обучение",
            "Обучение → Заявки на обучение",
            "/Training/Applications",
            "card"));

        hits.Add(new SearchHit(
            "Планирование обучения, инструктажей, проверок знаний, стажировок",
            "Обучение → Планирование",
            "/Training/Planning",
            "card"));

        return hits
            .GroupBy(hit => $"{hit.Kind}:{hit.Title}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }
}
