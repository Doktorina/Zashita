namespace Zashita.Core.Risks;

public static class RiskOrganizationBootstrap
{
    public static void Ensure(
        string organization,
        RiskWorkplaceStore workplaces,
        RiskPreparationStore preparation,
        RiskUnscheduledOrderStore? unscheduledOrders = null)
    {
        var org = organization.Trim();
        if (string.IsNullOrWhiteSpace(org))
        {
            return;
        }

        // Список РМ модуля «Риски» — отдельный от СОУТ; демо не подставляем.
        _ = workplaces.LoadByOrganization(org);

        var existingPreparation = preparation.LoadByOrganization(org);
        if (existingPreparation.Count == 0)
        {
            foreach (var doc in RiskDemoDocuments.CreateRiskPreparation(org))
            {
                preparation.Upsert(doc);
            }
        }

        if (unscheduledOrders is null)
        {
            return;
        }

        var existingOrders = unscheduledOrders.LoadByOrganization(org);
        if (existingOrders.Count == 0)
        {
            foreach (var doc in RiskDemoDocuments.CreateUnscheduledOrders(org))
            {
                unscheduledOrders.Upsert(doc);
            }
        }
    }
}
