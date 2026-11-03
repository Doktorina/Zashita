namespace Zashita.Core.Risks;

public static class SoutOrganizationBootstrap
{
    public static void Ensure(
        string organization,
        WorkplaceStore workplaces,
        WorkplaceAssignmentStore assignments,
        SoutPreparationStore preparation,
        SoutAnnualPlanStore annualPlans)
    {
        var org = organization.Trim();
        if (string.IsNullOrWhiteSpace(org))
        {
            return;
        }

        // Список РМ ведётся из штата 1С («Из штата 1С»), демо больше не подставляем.

        // Закрепления сотрудников — только из реальных данных / 1С, демо не создаём.
        _ = workplaces;
        _ = assignments;

        // Демо-документы подготовки и годовых планов больше не подставляем:
        // при удалении плана bootstrap восстанавливал его по номеру (ОТ-00000003 и т.д.).
        _ = preparation;
        _ = annualPlans;
    }
}
