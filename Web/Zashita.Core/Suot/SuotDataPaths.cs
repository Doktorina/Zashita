namespace Zashita.Core.Suot;

public sealed class SuotDataPaths
{
    public SuotDataPaths(string appDataRoot)
    {
        var dir = Path.Combine(appDataRoot, "suot");
        Directory.CreateDirectory(dir);
        IndicatorsFile = Path.Combine(dir, "indicators.json");
        FactsFile = Path.Combine(dir, "indicator-facts.json");
    }

    public string IndicatorsFile { get; }
    public string FactsFile { get; }
}
