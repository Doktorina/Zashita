using System.Text.Json;
using Zashita.Core.Employees;

namespace Zashita.Core.Training;

public sealed class TrainingPersonStandardAssignment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = "";
    public string Person { get; set; } = "";
    public string Profession { get; set; } = "";
    public string StandardId { get; set; } = "";

    /// <summary>Объект / площадка — на уровне сотрудника в отчёте по обучению.</summary>
    public string ObjectNote { get; set; } = "";

    /// <summary>Отметка «Журнал» в строке сотрудника.</summary>
    public bool Journal { get; set; }

    /// <summary>Отметка «Практика» в строке сотрудника.</summary>
    public bool Practice { get; set; }
}

public sealed class TrainingPersonStandardAssignmentDocument
{
    public List<TrainingPersonStandardAssignment> Items { get; set; } = [];
}

public sealed class TrainingPersonStandardAssignmentStore
{
    private readonly TrainingDataPaths _paths;
    private readonly JsonFileListCache<TrainingPersonStandardAssignment> _cache = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public TrainingPersonStandardAssignmentStore(TrainingDataPaths paths) => _paths = paths;

    private string FilePath => Path.Combine(
        Path.GetDirectoryName(_paths.StandardsFile) ?? "",
        "training-person-standard-assignments.json");

    public List<TrainingPersonStandardAssignment> LoadAll() =>
        _cache.GetOrLoad(FilePath, LoadFromDisk);

    private List<TrainingPersonStandardAssignment> LoadFromDisk()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return [];
            }

            var json = File.ReadAllText(FilePath);
            var doc = JsonSerializer.Deserialize<TrainingPersonStandardAssignmentDocument>(json, JsonOpts);
            return doc?.Items ?? [];
        }
        catch
        {
            return [];
        }
    }

    public TrainingPersonStandardAssignment? Find(
        string organization,
        string person,
        string profession)
    {
        if (string.IsNullOrWhiteSpace(person))
        {
            return null;
        }

        return LoadAll().FirstOrDefault(a => Matches(a, organization, person, profession));
    }

    public string? GetStandardId(string organization, string person, string profession) =>
        Find(organization, person, profession)?.StandardId;

    public void Upsert(string organization, string person, string profession, string standardId)
    {
        SetAssignment(organization, person, profession, standardId);
    }

    /// <summary>Назначить стандарт сотруднику. Пустой <paramref name="standardId"/> — явный отказ от стандарта.</summary>
    public void SetAssignment(string organization, string person, string profession, string? standardId)
    {
        if (string.IsNullOrWhiteSpace(person))
        {
            return;
        }

        var list = LoadAll();
        var existing = list.FirstOrDefault(a => Matches(a, organization, person, profession));
        var std = (standardId ?? string.Empty).Trim();

        if (existing is null)
        {
            list.Add(new TrainingPersonStandardAssignment
            {
                Organization = organization.Trim(),
                Person = person.Trim(),
                Profession = profession.Trim(),
                StandardId = std
            });
        }
        else
        {
            existing.Organization = organization.Trim();
            existing.Profession = profession.Trim();
            existing.StandardId = std;
        }

        Persist(list);
    }

    /// <summary>Объект / Журнал / Практика в строке сотрудника отчёта.</summary>
    public void SetPersonExtras(
        string organization,
        string person,
        string profession,
        string? objectNote,
        bool journal,
        bool practice) =>
        SetPersonExtrasBatch(
        [
            new PersonExtrasUpdate(organization, person, profession, objectNote, journal, practice)
        ]);

    /// <summary>
    /// Пакетное обновление Object/Журнал/Практика — один LoadAll + один Persist
    /// (иначе отчёт по обучению пишет JSON на каждого человека).
    /// </summary>
    public void SetPersonExtrasBatch(IEnumerable<PersonExtrasUpdate> updates)
    {
        var pending = updates
            .Where(u => !string.IsNullOrWhiteSpace(u.Person))
            .ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var list = LoadAll();
        var changed = false;
        foreach (var u in pending)
        {
            var org = (u.Organization ?? string.Empty).Trim();
            var person = u.Person.Trim();
            var profession = (u.Profession ?? string.Empty).Trim();
            var objectNote = (u.ObjectNote ?? string.Empty).Trim();
            var existing = list.FirstOrDefault(a => Matches(a, org, person, profession));
            if (existing is null)
            {
                list.Add(new TrainingPersonStandardAssignment
                {
                    Organization = org,
                    Person = person,
                    Profession = profession,
                    StandardId = "",
                    ObjectNote = objectNote,
                    Journal = u.Journal,
                    Practice = u.Practice
                });
                changed = true;
                continue;
            }

            if (!string.Equals(existing.Organization, org, StringComparison.Ordinal)
                || !string.Equals(existing.Profession, profession, StringComparison.Ordinal)
                || !string.Equals(existing.ObjectNote, objectNote, StringComparison.Ordinal)
                || existing.Journal != u.Journal
                || existing.Practice != u.Practice)
            {
                existing.Organization = org;
                existing.Profession = profession;
                existing.ObjectNote = objectNote;
                existing.Journal = u.Journal;
                existing.Practice = u.Practice;
                changed = true;
            }
        }

        if (changed)
        {
            Persist(list);
        }
    }

    public readonly record struct PersonExtrasUpdate(
        string Organization,
        string Person,
        string Profession,
        string? ObjectNote,
        bool Journal,
        bool Practice);

    /// <summary>Снять все назначения стандартов у сотрудника (перевод должности).</summary>
    public void ClearPerson(string organization, string person)
    {
        if (string.IsNullOrWhiteSpace(person))
        {
            return;
        }

        var list = LoadAll();
        var next = list
            .Where(a => !(string.Equals(a.Person, person, StringComparison.OrdinalIgnoreCase)
                          && (string.IsNullOrWhiteSpace(organization)
                              || string.IsNullOrWhiteSpace(a.Organization)
                              || string.Equals(a.Organization, organization, StringComparison.OrdinalIgnoreCase)
                              || OrganizationMatcher.Matches(organization, a.Organization))))
            .ToList();
        if (next.Count == list.Count)
        {
            return;
        }

        Persist(next);
    }

    public void SaveAll(IEnumerable<TrainingPersonStandardAssignment> items)
    {
        var data = items
            .Where(a => !string.IsNullOrWhiteSpace(a.Person))
            .ToList();
        Persist(data);
    }

    private void Persist(List<TrainingPersonStandardAssignment> list)
    {
        File.WriteAllText(
            FilePath,
            JsonSerializer.Serialize(new TrainingPersonStandardAssignmentDocument { Items = list }, JsonOpts));
        _cache.Replace(list, FilePath);
    }

    private static bool Matches(
        TrainingPersonStandardAssignment a,
        string organization,
        string person,
        string profession) =>
        string.Equals(a.Person, person, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Profession, profession, StringComparison.OrdinalIgnoreCase)
        && (string.IsNullOrWhiteSpace(organization)
            || string.IsNullOrWhiteSpace(a.Organization)
            || string.Equals(a.Organization, organization, StringComparison.OrdinalIgnoreCase)
            || OrganizationMatcher.Matches(organization, a.Organization));

    /// <summary>
    /// После переименования юрлица у стандарта — переносим назначения со старого написания.
    /// </summary>
    public int RemapOrganization(string fromOrganization, string toOrganization)
    {
        var from = (fromOrganization ?? string.Empty).Trim();
        var to = (toOrganization ?? string.Empty).Trim();
        if (from.Length == 0 || to.Length == 0
            || string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var list = LoadAll();
        var changed = 0;
        foreach (var a in list)
        {
            if (!string.Equals(a.Organization.Trim(), from, StringComparison.OrdinalIgnoreCase)
                && !OrganizationMatcher.Matches(from, a.Organization))
            {
                continue;
            }

            a.Organization = to;
            changed++;
        }

        if (changed > 0)
        {
            SaveAll(list);
        }

        return changed;
    }
}
