using System.Text.Json;
using Zashita.Core.Employees;
using Zashita.Core.Risks;

namespace Zashita.Web.Services;

/// <summary>Реквизиты организаций-работодателей (ИНН/ОГРН/банк) в App_Data.</summary>
public sealed class OrganizationRequisitesStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _dataFilePath;
    private readonly OrganizationPrintDetailsStore _printDetails;
    private readonly object _sync = new();
    private Dictionary<string, OrganizationRequisites> _data = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _loadedWriteTimeUtc = DateTime.MinValue;

    public OrganizationRequisitesStore(IWebHostEnvironment env, OrganizationPrintDetailsStore printDetails)
    {
        var appData = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(appData);
        _dataFilePath = Path.Combine(appData, "organization-requisites.json");
        _printDetails = printDetails;
        LoadUnlocked(force: true);
    }

    public OrganizationRequisites Get(string organization)
    {
        var org = (organization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return new OrganizationRequisites();
        }

        lock (_sync)
        {
            EnsureFreshUnlocked();
            var found = FindKeyUnlocked(org);
            var requisites = found is not null && _data.TryGetValue(found, out var stored)
                ? stored.Clone()
                : new OrganizationRequisites();

            requisites.Normalize();

            if (!requisites.HasBankDetails)
            {
                requisites.EnrichBankFromLetterhead(_printDetails.GetBankLetterhead(org));
            }

            return requisites;
        }
    }

    public string GetSummary(string organization) => Get(organization).FormatSummary();

    /// <summary>Банковская шапка для приказов: структурированные реквизиты или legacy letterhead.</summary>
    public string? ResolveBankLetterhead(string organization)
    {
        var requisites = Get(organization);
        if (requisites.HasBankDetails)
        {
            var formatted = requisites.FormatBankLetterhead();
            return RiskPrintChrome.IsFilledBankLetterhead(formatted) ? formatted : null;
        }

        var legacy = _printDetails.GetBankLetterhead(organization);
        return RiskPrintChrome.IsFilledBankLetterhead(legacy) ? legacy : null;
    }

    public IReadOnlyDictionary<string, string> GetSummaries(IEnumerable<string> organizations)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var org in organizations)
        {
            if (string.IsNullOrWhiteSpace(org))
            {
                continue;
            }

            result[org] = GetSummary(org);
        }

        return result;
    }

    public void Save(string organization, OrganizationRequisites? requisites)
    {
        var org = (organization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return;
        }

        var value = (requisites ?? new OrganizationRequisites()).Clone();
        value.Normalize();

        lock (_sync)
        {
            EnsureFreshUnlocked();
            var key = FindKeyUnlocked(org) ?? org;
            if (value.IsEmpty)
            {
                _data.Remove(key);
            }
            else
            {
                _data[key] = value;
            }

            PersistUnlocked();
        }

        if (value.HasBankDetails)
        {
            _printDetails.SaveBankLetterhead(org, value.FormatBankLetterhead());
        }
    }

    public void Rename(string oldName, string newName)
    {
        var from = (oldName ?? string.Empty).Trim();
        var to = (newName ?? string.Empty).Trim();
        if (from.Length == 0 || to.Length == 0 || string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_sync)
        {
            EnsureFreshUnlocked();
            var key = FindKeyUnlocked(from);
            if (key is null || !_data.TryGetValue(key, out var value))
            {
                return;
            }

            _data.Remove(key);
            var target = FindKeyUnlocked(to) ?? to;
            _data[target] = value;
            PersistUnlocked();
        }

        _printDetails.RenameOrganization(from, to);
    }

    public void Remove(string organization)
    {
        var org = (organization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return;
        }

        lock (_sync)
        {
            EnsureFreshUnlocked();
            var key = FindKeyUnlocked(org);
            if (key is null)
            {
                return;
            }

            _data.Remove(key);
            PersistUnlocked();
        }

        _printDetails.RemoveOrganization(org);
    }

    private string? FindKeyUnlocked(string organization)
    {
        if (_data.ContainsKey(organization))
        {
            return organization;
        }

        return _data.Keys.FirstOrDefault(k => OrganizationMatcher.Matches(organization, k));
    }

    private void EnsureFreshUnlocked()
    {
        try
        {
            if (!File.Exists(_dataFilePath))
            {
                if (_data.Count > 0 || _loadedWriteTimeUtc != DateTime.MinValue)
                {
                    LoadUnlocked(force: true);
                }

                return;
            }

            var writeTime = File.GetLastWriteTimeUtc(_dataFilePath);
            if (writeTime != _loadedWriteTimeUtc)
            {
                LoadUnlocked(force: true);
            }
        }
        catch
        {
            // оставляем текущий кэш
        }
    }

    private void LoadUnlocked(bool force)
    {
        if (!force)
        {
            return;
        }

        try
        {
            if (!File.Exists(_dataFilePath))
            {
                _data = new Dictionary<string, OrganizationRequisites>(StringComparer.OrdinalIgnoreCase);
                _loadedWriteTimeUtc = DateTime.MinValue;
                return;
            }

            var json = File.ReadAllText(_dataFilePath).TrimStart('\uFEFF');
            var loaded = JsonSerializer.Deserialize<Dictionary<string, OrganizationRequisites>>(json, JsonOpts);
            _data = loaded is null
                ? new Dictionary<string, OrganizationRequisites>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, OrganizationRequisites>(loaded, StringComparer.OrdinalIgnoreCase);
            _loadedWriteTimeUtc = File.GetLastWriteTimeUtc(_dataFilePath);
        }
        catch
        {
            _data = new Dictionary<string, OrganizationRequisites>(StringComparer.OrdinalIgnoreCase);
            _loadedWriteTimeUtc = DateTime.MinValue;
        }
    }

    private void PersistUnlocked()
    {
        File.WriteAllText(_dataFilePath, JsonSerializer.Serialize(_data, JsonOpts));
        try
        {
            _loadedWriteTimeUtc = File.GetLastWriteTimeUtc(_dataFilePath);
        }
        catch
        {
            _loadedWriteTimeUtc = DateTime.UtcNow;
        }
    }
}
