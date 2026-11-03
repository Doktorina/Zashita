namespace Zashita.Web.Services;

public static class RisksModuleLinks
{
    private static readonly Dictionary<string, string> ExactRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Справочники
        ["Перечень рабочих мест"] = "/Risks/Workplaces",
        ["Рабочие места"] = "/Risks/Workplaces",
        ["Реестр опасностей и мер по исключению, снижению и контролю уровней рисков"] = "/Risks/HazardCatalog",
        ["Реестр опасностей"] = "/Risks/HazardCatalog",

        // Оценка рисков
        ["Карты оценки рисков"] = "/Risks/Workplaces?view=assessment",
        ["Карта оценки рисков"] = "/Risks/Workplaces?view=assessment",
        ["Карты идентификации"] = "/Risks/Documents/SelectWorkplaces?purpose=cards",
        ["Карты идентификации рисков"] = "/Risks/Documents/SelectWorkplaces?purpose=cards",
        // Заполнение карт остаётся в меню модуля «Оценка рисков», не дублируем на хабе документов.
        ["Отчет по ОПР"] = "/Risks/Reports/Status",
        ["Отчет по рискам"] = "/Risks/Reports/Status",
        ["Отчеты по рискам"] = "/Risks/Reports",
        ["Перечень рабочих мест, подлежащих оценке профессиональных рисков"] = "/Risks/AssessmentList",
        ["График идентификации опасностей и оценки рисков в подразделениях"] = "/Risks/IdentificationSchedule",
        ["Перечень объектов без оценки рисков"] = "/Risks/AssessmentList?needFilter=notconducted",

        // Внеплановая / документы
        ["Документы для оценки рисков"] = "/Risks/Documents",
        ["Приказ на оценку рисков"] = "/Risks/Documents?tab=order",
        ["Приказ на проведение внеплановой оценки рисков"] = "/Risks/Documents?tab=order",
        ["Распоряжение на внеплановую оценку рисков"] = "/Risks/Documents?tab=order",
        ["Распоряжение на оценку рисков"] = "/Risks/Documents?tab=order",
        ["Внесение изменений"] = "/Risks/Changes",

        // Проведение
        ["Подготовка к проведению оценки рисков"] = "/Risks/Documents?tab=preparation",
        ["Перечень РМ (выбор)"] = "/Risks/Documents/SelectWorkplaces",
        ["Реестр опасностей (выбор)"] = "/Risks/Documents/SelectHazards",
        ["Перечень мер управления"] = "/Risks/Documents/OrgDocPreview?kind=measures",
        ["Методика оценки рисков"] = "/Risks/Documents/RegulationTemplate",
        ["Положение (методика)"] = "/Risks/Documents/RegulationTemplate",
        ["Титульный лист"] = "/Risks/Documents/OrgDocPreview?kind=title-page",
        ["Титульный лист и положение"] = "/Risks/Documents/RegulationTemplate",
        ["Комиссия"] = "/Risks/Commissions",
        ["Комиссии"] = "/Risks/Commissions"
    };

    public static bool TryResolve(string itemTitle, out string? url)
    {
        if (ExactRoutes.TryGetValue(itemTitle.Trim(), out url))
        {
            return true;
        }

        return RisksItemRouter.TryResolveByTitle(itemTitle, out url);
    }
}
