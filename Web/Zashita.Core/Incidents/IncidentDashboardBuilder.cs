namespace Zashita.Core.Incidents;

public static class IncidentDashboardBuilder
{
    public static IncidentHubStats Build(
        string organization,
        IEnumerable<WorkplaceAccidentRecord> accidents,
        IEnumerable<AccidentInvestigationRecord> investigations,
        IEnumerable<MicrotraumaRecord> microtraumas,
        IEnumerable<MicrotraumaInvestigationRecord> microInvestigations,
        DateTime today)
    {
        var workplace = Filter(accidents, organization).ToList();
        var inv = Filter(investigations, organization).ToList();
        var micro = Filter(microtraumas, organization).ToList();
        var microInv = Filter(microInvestigations, organization).ToList();

        var lastAccident = workplace
            .Select(r => r.IncidentDateTime?.Date ?? r.DocumentDate.Date)
            .DefaultIfEmpty(today)
            .Max();

        var daysWithout = lastAccident >= today ? 0 : (today - lastAccident).Days;

        return new IncidentHubStats
        {
            WorkplaceCount = workplace.Count,
            InvestigationCount = inv.Count,
            MicrotraumaCount = micro.Count,
            MicrotraumaInvestigationCount = microInv.Count,
            DaysWithoutAccident = daysWithout
        };
    }

    private static IEnumerable<T> Filter<T>(IEnumerable<T> source, string organization)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(organization))
        {
            return source;
        }

        return source.Where(item => item switch
        {
            WorkplaceAccidentRecord wa => string.Equals(wa.Organization, organization, StringComparison.OrdinalIgnoreCase),
            AccidentInvestigationRecord ai => string.Equals(ai.Organization, organization, StringComparison.OrdinalIgnoreCase),
            MicrotraumaRecord mt => string.Equals(mt.Organization, organization, StringComparison.OrdinalIgnoreCase),
            MicrotraumaInvestigationRecord mi => string.Equals(mi.Organization, organization, StringComparison.OrdinalIgnoreCase),
            _ => true
        });
    }
}
