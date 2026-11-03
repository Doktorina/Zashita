namespace Zashita.Core.Compliance;

public sealed class ComplianceDataPaths
{
    public ComplianceDataPaths(string appDataRoot)
    {
        var dir = Path.Combine(appDataRoot, "compliance");
        Directory.CreateDirectory(dir);
        InspectionActsFile = Path.Combine(dir, "inspection-acts.json");
    }

    public string InspectionActsFile { get; }
}
