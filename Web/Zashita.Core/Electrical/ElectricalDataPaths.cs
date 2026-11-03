namespace Zashita.Core.Electrical;

public sealed class ElectricalDataPaths
{
    public ElectricalDataPaths(string appDataRoot)
    {
        var dir = Path.Combine(appDataRoot, "electrical");
        Directory.CreateDirectory(dir);
        KnowledgeChecksFile = Path.Combine(dir, "knowledge-checks.json");
        CommissionsFile = Path.Combine(dir, "commissions.json");
    }

    public string KnowledgeChecksFile { get; }
    public string CommissionsFile { get; }
}
