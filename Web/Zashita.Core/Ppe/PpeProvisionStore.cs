using System.Text.Json;

namespace Zashita.Core.Ppe;

public sealed class PpeProvisionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = string.Empty;
    public string Employee { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    public string PpeName { get; set; } = string.Empty;
    public DateTime? IssueDate { get; set; }
    public string IssuedQty { get; set; } = string.Empty;
}

public sealed class PpeProvisionStore
{
    private readonly PpeDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public PpeProvisionStore(PpeDataPaths paths) => _paths = paths;

    public List<PpeProvisionRecord> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.ProvisionFile))
            {
                return [];
            }

            var json = File.ReadAllText(_paths.ProvisionFile);
            return JsonSerializer.Deserialize<List<PpeProvisionRecord>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Upsert(string organization, string employee, string profession, string ppeName, DateTime? issueDate, string? issuedQty)
    {
        if (string.IsNullOrWhiteSpace(employee) || string.IsNullOrWhiteSpace(ppeName))
        {
            return;
        }

        var org = organization?.Trim() ?? string.Empty;
        var person = employee.Trim();
        var prof = profession?.Trim() ?? string.Empty;
        var name = ppeName.Trim();
        var qty = issuedQty?.Trim() ?? string.Empty;
        var list = LoadAll();
        var idx = list.FindIndex(x =>
            string.Equals(x.Organization, org, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Employee, person, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Profession, prof, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.PpeName, name, StringComparison.OrdinalIgnoreCase));

        if (!issueDate.HasValue && string.IsNullOrWhiteSpace(qty))
        {
            if (idx >= 0)
            {
                list.RemoveAt(idx);
                SaveAll(list);
            }

            return;
        }

        if (idx >= 0)
        {
            list[idx].IssueDate = issueDate?.Date;
            list[idx].IssuedQty = qty;
        }
        else
        {
            list.Add(new PpeProvisionRecord
            {
                Organization = org,
                Employee = person,
                Profession = prof,
                PpeName = name,
                IssueDate = issueDate?.Date,
                IssuedQty = qty
            });
        }

        SaveAll(list);
    }

    private void SaveAll(List<PpeProvisionRecord> items)
    {
        var json = JsonSerializer.Serialize(items
            .OrderBy(x => x.Organization, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Employee, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.PpeName, StringComparer.CurrentCultureIgnoreCase), JsonOpts);
        File.WriteAllText(_paths.ProvisionFile, json);
    }
}
