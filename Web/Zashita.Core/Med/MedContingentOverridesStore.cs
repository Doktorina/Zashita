using System.Text.Json;

namespace Zashita.Core.Med;

public sealed class MedContingentOverridesStore
{
    private readonly MedDataPaths _paths;
    private readonly object _sync = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public MedContingentOverridesStore(MedDataPaths paths) => _paths = paths;

    public IReadOnlyList<MedContingentPathologyOverride> Load(string organization)
    {
        lock (_sync)
        {
            var data = LoadUnlocked();
            if (!data.ByOrganization.TryGetValue(organization.Trim(), out var list) || list is null)
            {
                return [];
            }

            return list.Select(Clone).ToList();
        }
    }

    public void SaveOrganization(string organization, IEnumerable<MedContingentPathologyOverride> overrides)
    {
        var org = organization.Trim();
        if (string.IsNullOrWhiteSpace(org))
        {
            return;
        }

        lock (_sync)
        {
            var data = LoadUnlocked();
            data.ByOrganization[org] = overrides
                .Where(o => !string.IsNullOrWhiteSpace(o.Profession))
                .Select(Clone)
                .ToList();
            PersistUnlocked(data);
        }
    }

    public MedContingentPrintSettings LoadPrintSettings(string organization)
    {
        lock (_sync)
        {
            var data = LoadUnlocked();
            if (!data.PrintByOrganization.TryGetValue(organization.Trim(), out var settings) || settings is null)
            {
                return new MedContingentPrintSettings();
            }

            return new MedContingentPrintSettings
            {
                Overrides = new Dictionary<string, string>(settings.Overrides ?? [], StringComparer.OrdinalIgnoreCase)
            };
        }
    }

    public void SavePrintOverrides(string organization, IDictionary<string, string> overrides)
    {
        var org = organization.Trim();
        if (string.IsNullOrWhiteSpace(org))
        {
            return;
        }

        lock (_sync)
        {
            var data = LoadUnlocked();
            if (!data.PrintByOrganization.TryGetValue(org, out var settings) || settings is null)
            {
                settings = new MedContingentPrintSettings();
                data.PrintByOrganization[org] = settings;
            }

            settings.Overrides = new Dictionary<string, string>(overrides, StringComparer.OrdinalIgnoreCase);
            PersistUnlocked(data);
        }
    }

    public IReadOnlyList<MedContingentNamedOverride> LoadNamed(string organization)
    {
        lock (_sync)
        {
            var data = LoadUnlocked();
            if (!data.NamedByOrganization.TryGetValue(organization.Trim(), out var list) || list is null)
            {
                return [];
            }

            return list.Select(CloneNamed).ToList();
        }
    }

    public void SaveNamed(string organization, IEnumerable<MedContingentNamedOverride> overrides)
    {
        var org = organization.Trim();
        if (string.IsNullOrWhiteSpace(org))
        {
            return;
        }

        lock (_sync)
        {
            var data = LoadUnlocked();
            data.NamedByOrganization[org] = overrides
                .Where(o => !string.IsNullOrWhiteSpace(o.PersonKey))
                .GroupBy(o => o.PersonKey.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => CloneNamed(g.Last()))
                .ToList();
            PersistUnlocked(data);
        }
    }

    public static Dictionary<string, MedContingentNamedOverride> IndexNamedByKey(
        IEnumerable<MedContingentNamedOverride> overrides) =>
        overrides
            .Where(o => !string.IsNullOrWhiteSpace(o.PersonKey))
            .GroupBy(o => o.PersonKey.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => CloneNamed(g.Last()), StringComparer.OrdinalIgnoreCase);

    public MedContingentPageHints LoadPageHints(string organization)
    {
        lock (_sync)
        {
            var data = LoadUnlocked();
            if (!data.HintsByOrganization.TryGetValue(organization.Trim(), out var hints) || hints is null)
            {
                return new MedContingentPageHints();
            }

            return CloneHints(hints);
        }
    }

    public void SavePageHints(string organization, MedContingentPageHints hints)
    {
        var org = organization.Trim();
        if (string.IsNullOrWhiteSpace(org) || hints is null)
        {
            return;
        }

        lock (_sync)
        {
            var data = LoadUnlocked();
            data.HintsByOrganization[org] = CloneHints(hints);
            PersistUnlocked(data);
        }
    }

    public static string RowKey(string department, string profession) =>
        $"{(department ?? string.Empty).Trim()}|{NormalizeProfession(profession)}";

    public static Dictionary<string, MedContingentPathologyOverride> IndexByRow(
        IEnumerable<MedContingentPathologyOverride> overrides) =>
        overrides
            .GroupBy(o => RowKey(o.Department, o.Profession), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => Clone(g.Last()), StringComparer.OrdinalIgnoreCase);

    public static string NormalizeProfession(string? profession)
    {
        var value = (profession ?? string.Empty).Trim().TrimEnd('*').Trim();
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value;
    }

    private MedContingentOverridesFile LoadUnlocked()
    {
        try
        {
            if (!File.Exists(_paths.ContingentOverridesFile))
            {
                return new MedContingentOverridesFile();
            }

            var json = File.ReadAllText(_paths.ContingentOverridesFile);
            var data = JsonSerializer.Deserialize<MedContingentOverridesFile>(json, JsonOpts)
                       ?? new MedContingentOverridesFile();
            data.ByOrganization ??= new Dictionary<string, List<MedContingentPathologyOverride>>(StringComparer.OrdinalIgnoreCase);
            data.PrintByOrganization ??= new Dictionary<string, MedContingentPrintSettings>(StringComparer.OrdinalIgnoreCase);
            data.HintsByOrganization ??= new Dictionary<string, MedContingentPageHints>(StringComparer.OrdinalIgnoreCase);
            data.NamedByOrganization ??= new Dictionary<string, List<MedContingentNamedOverride>>(StringComparer.OrdinalIgnoreCase);
            return data;
        }
        catch
        {
            return new MedContingentOverridesFile();
        }
    }

    private void PersistUnlocked(MedContingentOverridesFile data) =>
        File.WriteAllText(_paths.ContingentOverridesFile, JsonSerializer.Serialize(data, JsonOpts));

    private static MedContingentPageHints CloneHints(MedContingentPageHints source) => new()
    {
        ContingentsHintHtml = source.ContingentsHintHtml ?? string.Empty,
        ContingentsHintStyle = source.ContingentsHintStyle ?? string.Empty,
        NamedHintHtml = source.NamedHintHtml ?? string.Empty,
        NamedHintStyle = source.NamedHintStyle ?? string.Empty
    };

    private static MedContingentNamedOverride CloneNamed(MedContingentNamedOverride source) => new()
    {
        PersonKey = (source.PersonKey ?? string.Empty).Trim(),
        Removed = source.Removed,
        FullName = source.FullName,
        BirthDate = source.BirthDate,
        Snils = source.Snils,
        Department = source.Department,
        Profession = source.Profession,
        ExperienceTotal = source.ExperienceTotal,
        ExperienceInOrg = source.ExperienceInOrg,
        HarmfulFactors = source.HarmfulFactors,
        ActualLevel = source.ActualLevel,
        ConditionsClass = source.ConditionsClass,
        Order988n = source.Order988n,
        Order29n = source.Order29n,
        LastExamLpu = source.LastExamLpu,
        LastExamCpp = source.LastExamCpp
    };

    private static MedContingentPathologyOverride Clone(MedContingentPathologyOverride source) => new()
    {
        Department = source.Department?.Trim() ?? string.Empty,
        Profession = NormalizeProfession(source.Profession),
        EnvironmentFactors = source.EnvironmentFactors,
        Order988n = source.Order988n,
        Order29n = source.Order29n,
        ExamTotal = source.ExamTotal is null ? null : Clamp(source.ExamTotal.Value),
        ExamWomen = source.ExamWomen is null ? null : Clamp(source.ExamWomen.Value),
        LpuTotal = source.LpuTotal is null ? null : Clamp(source.LpuTotal.Value),
        LpuWomen = source.LpuWomen is null ? null : Clamp(source.LpuWomen.Value),
        PathologyTotal = Clamp(source.PathologyTotal),
        PathologyWomen = Clamp(source.PathologyWomen),
        AccidentTotal = Clamp(source.AccidentTotal),
        AccidentWomen = Clamp(source.AccidentWomen),
        ExceedLimitTotal = Clamp(source.ExceedLimitTotal),
        ExceedLimitWomen = Clamp(source.ExceedLimitWomen),
        OccupationalDiseaseTotal = Clamp(source.OccupationalDiseaseTotal),
        OccupationalDiseaseWomen = Clamp(source.OccupationalDiseaseWomen),
        SpecialConditionsTotal = Clamp(source.SpecialConditionsTotal),
        SpecialConditionsWomen = Clamp(source.SpecialConditionsWomen),
        Experience5YearsTotal = Clamp(source.Experience5YearsTotal),
        Experience5YearsWomen = Clamp(source.Experience5YearsWomen),
        Age40Total = Clamp(source.Age40Total),
        Age40Women = Clamp(source.Age40Women),
        MissingDataTotal = Clamp(source.MissingDataTotal),
        MissingDataWomen = Clamp(source.MissingDataWomen),
        CellStyles = source.CellStyles is null
            ? null
            : new Dictionary<string, string>(source.CellStyles, StringComparer.OrdinalIgnoreCase)
    };

    private static int Clamp(int value) => value < 0 ? 0 : value;
}
