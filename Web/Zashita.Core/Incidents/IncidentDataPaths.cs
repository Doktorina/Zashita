namespace Zashita.Core.Incidents;

public sealed class IncidentDataPaths
{
    public IncidentDataPaths(string appDataFolder)
    {
        var folder = Path.Combine(appDataFolder, "incidents");
        Directory.CreateDirectory(folder);
        WorkplaceAccidentsFile = Path.Combine(folder, "workplace-accidents.json");
        InvestigationsFile = Path.Combine(folder, "accident-investigations.json");
        MicrotraumasFile = Path.Combine(folder, "microtraumas.json");
        MicrotraumaInvestigationsFile = Path.Combine(folder, "microtrauma-investigations.json");
        CatalogFile = Path.Combine(folder, "catalog.json");
    }

    public string WorkplaceAccidentsFile { get; }
    public string InvestigationsFile { get; }
    public string MicrotraumasFile { get; }
    public string MicrotraumaInvestigationsFile { get; }
    public string CatalogFile { get; }
}
