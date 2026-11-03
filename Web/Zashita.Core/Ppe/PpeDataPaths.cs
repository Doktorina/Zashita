namespace Zashita.Core.Ppe;

public sealed class PpeDataPaths
{
    public PpeDataPaths(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        Directory.CreateDirectory(dataDirectory);
    }

    public string DataDirectory { get; }
    public string IssueStandardsFile => Path.Combine(DataDirectory, "issue-standards.json");
    public string SavedCardsFile => Path.Combine(DataDirectory, "saved-sios-cards.json");
    public string NomenclatureFile => Path.Combine(DataDirectory, "ppe-nomenclature.json");
    public string EtnNomenclatureFile => Path.Combine(DataDirectory, "etn-nomenclature.json");
    public string EtnNomenclatureSourceFile => Path.Combine(DataDirectory, "nomenclature-etn.xlsx");
    public string PollutantsEtnFile => Path.Combine(DataDirectory, "pollutants-etn.json");
    public string PollutantsEtnSourceFile => Path.Combine(DataDirectory, "pollutants-etn.xlsx");
    public string ChangesFile => Path.Combine(DataDirectory, "ppe-changes.json");
    public string WriteoffFile => Path.Combine(DataDirectory, "ppe-writeoff.json");
    public string AcceptanceFile => Path.Combine(DataDirectory, "ppe-acceptance.json");
    public string ProvisionFile => Path.Combine(DataDirectory, "ppe-provision.json");
    public string ReferenceCatalogFile => Path.Combine(DataDirectory, "ppe-reference-catalog.json");
    public string InternalOrdersFile => Path.Combine(DataDirectory, "ppe-internal-orders.json");
}
