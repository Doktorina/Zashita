using System.Text.Json;
using Zashita.Core.Employees;
using Zashita.Core.Incidents;

namespace Zashita.Core.Risks;

public class WorkplaceStore
{
    private readonly string _file;
    private readonly object _sync = new();
    private List<WorkplaceRecord>? _cache;
    private DateTime _cacheFileWriteUtc = DateTime.MinValue;
    private bool _maintenanceApplied;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> FileLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public WorkplaceStore(RiskDataPaths paths) : this(paths.WorkplacesFile)
    {
    }

    /// <summary>Для наследников / отдельного файла (например, перечень Рисков).</summary>
    public WorkplaceStore(string filePath)
    {
        _file = filePath;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private object Gate => FileLocks.GetOrAdd(Path.GetFullPath(_file), _ => new object());

    public List<WorkplaceRecord> LoadAll()
    {
        lock (Gate)
        {
            // Без глубокого JSON-клона: на больших risk-workplaces это убивает CPU.
            return [.. LoadAllLocked()];
        }
    }

    public List<WorkplaceRecord> LoadByOrganization(string organization) =>
        LoadAll()
            .Where(r => string.IsNullOrWhiteSpace(organization) ||
                        string.Equals(r.Organization, organization, StringComparison.OrdinalIgnoreCase) ||
                        OrganizationMatcher.Matches(organization, r.Organization) ||
                        OrganizationMatcher.Matches(r.Organization, organization))
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public List<WorkplaceRecord> LoadTreeByOrganization(string organization) =>
        LoadByOrganization(organization);

    public void SaveForOrganization(string organization, IEnumerable<WorkplaceRecord> records)
    {
        var org = organization.Trim();
        lock (Gate)
        {
            var existing = LoadAllLocked();
            var priorById = existing
                .Where(r => !string.IsNullOrWhiteSpace(r.Id))
                .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var list = existing
                .Where(r => !SameOrganizationKey(r.Organization, org))
                .ToList();

            // Входной перечень тоже может содержать копии одного Id — не размножаем.
            var incoming = records
                .Where(r => r is not null)
                .GroupBy(r => r.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())
                .ToList();

            foreach (var record in incoming)
            {
                record.Organization = org;
                record.ModifiedAt = DateTime.UtcNow;
                if (record.CreatedAt == default)
                {
                    record.CreatedAt = DateTime.UtcNow;
                }

                priorById.TryGetValue(record.Id ?? string.Empty, out var prior);
                PreserveRiskCardIfEmpty(record, prior);
                list.Add(record);
            }

            SaveAllLocked(list);
        }
    }

    public void SaveAll(IEnumerable<WorkplaceRecord> records)
    {
        lock (Gate)
        {
            SaveAllLocked(records.ToList());
        }
    }

    public void RenameOrganization(string oldName, string newName)
    {
        var from = (oldName ?? string.Empty).Trim();
        var to = (newName ?? string.Empty).Trim();
        if (from.Length == 0 || to.Length == 0
            || string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (Gate)
        {
            var list = LoadAllLocked();
            var changed = false;
            foreach (var record in list)
            {
                if (!SameOrganizationKey(record.Organization, from)
                    && !string.Equals(record.Organization, from, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                record.Organization = to;
                record.ModifiedAt = DateTime.UtcNow;
                changed = true;
            }

            if (changed)
            {
                SaveAllLocked(list);
            }
        }
    }

    public WorkplaceRecord? GetById(string id)
    {
        lock (Gate)
        {
            var found = LoadAllLocked()
                .FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            return found;
        }
    }

    public void Upsert(WorkplaceRecord record)
    {
        lock (Gate)
        {
            var list = LoadAllLocked();
            record.ModifiedAt = DateTime.UtcNow;
            var idx = list.FindIndex(x => string.Equals(x.Id, record.Id, StringComparison.Ordinal));
            if (idx >= 0)
            {
                PreserveRiskCardIfEmpty(record, list[idx]);
                record.CreatedAt = list[idx].CreatedAt;
                list[idx] = record;
            }
            else
            {
                record.CreatedAt = DateTime.UtcNow;
                list.Add(record);
            }

            SaveAllLocked(list);
        }
    }

    public void Delete(string id)
    {
        lock (Gate)
        {
            var list = LoadAllLocked();
            list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            SaveAllLocked(list);
        }
    }

    /// <summary>Оставляет одну запись на Id (более наполненную).</summary>
    private static int DeduplicateById(List<WorkplaceRecord> records)
    {
        if (records.Count < 2)
        {
            return 0;
        }

        var before = records.Count;
        var kept = new Dictionary<string, WorkplaceRecord>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        var withoutId = new List<WorkplaceRecord>();

        foreach (var record in records)
        {
            var id = record.Id?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id))
            {
                withoutId.Add(record);
                continue;
            }

            if (!kept.TryGetValue(id, out var existing))
            {
                kept[id] = record;
                order.Add(id);
                continue;
            }

            var existingScore = WorkplaceKeepScore(existing);
            var nextScore = WorkplaceKeepScore(record);
            if (nextScore >= existingScore)
            {
                kept[id] = record;
            }
        }

        records.Clear();
        foreach (var id in order)
        {
            records.Add(kept[id]);
        }

        records.AddRange(withoutId);
        return before - records.Count;
    }

    private static int WorkplaceKeepScore(WorkplaceRecord w) =>
        (w.RiskLines?.Count ?? 0)
        + (string.IsNullOrWhiteSpace(w.OkCode) ? 0 : 5)
        + (w.SoutAssessmentDate is null ? 0 : 5)
        + (string.IsNullOrWhiteSpace(w.SoutReportNumber) ? 0 : 3)
        + (w.SoutCard?.EmployeeIds?.Count ?? 0)
        + (string.IsNullOrWhiteSpace(w.ConditionsClass) ? 0 : 2);

    private List<WorkplaceRecord> LoadAllLocked()
    {
        try
        {
            if (!File.Exists(_file))
            {
                _cache = [];
                _cacheFileWriteUtc = DateTime.MinValue;
                return _cache;
            }

            var writeUtc = File.GetLastWriteTimeUtc(_file);
            if (_cache is not null && writeUtc == _cacheFileWriteUtc)
            {
                return _cache;
            }

            var json = File.ReadAllText(_file);
            var list = JsonSerializer.Deserialize<List<WorkplaceRecord>>(json, JsonOpts) ?? [];
            foreach (var record in list)
            {
                record.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                record.RiskLines ??= [];
            }

            // Дубли с одним Id (после сбоев Save) — оставляем одну карту.
            if (DeduplicateById(list) > 0)
            {
                _maintenanceApplied = false; // дать maintenance ещё раз на уже уникальном списке
            }

            // Тяжёлую чистку делаем один раз за жизнь процесса, не при каждом чтении.
            if (!_maintenanceApplied)
            {
                var changed = DeduplicateById(list) > 0;
                if (SoutWorkplaceLegacyCleanup.Purge(list))
                {
                    changed = true;
                }

                if (SoutWorkplaceDepartmentConsolidator.CollapseAllOrganizations(list) > 0)
                {
                    changed = true;
                }

                // Пустые оболочки той же профессии рядом с заполненной картой.
                foreach (var org in list
                             .Select(r => r.Organization)
                             .Where(o => !string.IsNullOrWhiteSpace(o))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .ToList())
                {
                    if (SoutWorkplaceStaffSync.CollapseBlankProfessionShells(list, org!) > 0)
                    {
                        changed = true;
                    }
                }

                _maintenanceApplied = true;
                if (changed)
                {
                    SaveAllLocked(list);
                    return _cache!;
                }
            }

            _cache = list;
            _cacheFileWriteUtc = writeUtc;
            return _cache;
        }
        catch
        {
            // Не затираем кэш пустым списком при сбое чтения — иначе следующий Save
            // может перезаписать файл только текущей организацией.
            if (_cache is not null)
            {
                return _cache;
            }

            _cache = [];
            _cacheFileWriteUtc = DateTime.MinValue;
            return _cache;
        }
    }

    private void SaveAllLocked(List<WorkplaceRecord> records)
    {
        DeduplicateById(records);

        // Защита от «обнуления» карт без повторного чтения файла с диска на каждое сохранение.
        MergeFilledCardsFromPrior(records);

        var dir = Path.GetDirectoryName(_file);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(records, JsonOpts);
        var temp = _file + ".tmp";
        File.WriteAllText(temp, json);
        File.Copy(temp, _file, overwrite: true);
        try
        {
            File.Delete(temp);
        }
        catch
        {
            // ignore cleanup
        }

        _cache = records;
        _cacheFileWriteUtc = File.GetLastWriteTimeUtc(_file);
    }

    private static int CountFilledCards(IEnumerable<WorkplaceRecord> records) =>
        records.Count(r => !r.IsGroup && r.RiskLines is { Count: > 0 });

    private void MergeFilledCardsFromPrior(List<WorkplaceRecord> records)
    {
        var prior = _cache;
        if (prior is null || prior.Count == 0)
        {
            return;
        }

        var priorFilledCount = CountFilledCards(prior);
        if (priorFilledCount == 0)
        {
            return;
        }

        // Обычное сохранение карты/перечня — не трогаем.
        if (CountFilledCards(records) >= priorFilledCount
            && records.Count >= prior.Count / 2)
        {
            return;
        }

        var priorFilled = prior.Where(r => !r.IsGroup && r.RiskLines is { Count: > 0 }).ToList();

        var byId = records
            .Where(r => !string.IsNullOrWhiteSpace(r.Id))
            .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        static string OrgKey(string? s) => (s ?? string.Empty).Trim();
        static string ProfKey(WorkplaceRecord w) =>
            (string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession).Trim();

        var byOrgProf = records
            .Where(r => !r.IsGroup)
            .GroupBy(r => (OrgKey(r.Organization), ProfKey(r)), new OrgProfComparer())
            .ToDictionary(g => g.Key, g => g.First(), new OrgProfComparer());

        foreach (var filled in priorFilled)
        {
            if (byId.TryGetValue(filled.Id, out var sameId))
            {
                PreserveRiskCardIfEmpty(sameId, filled);
                continue;
            }

            var key = (OrgKey(filled.Organization), ProfKey(filled));
            if (byOrgProf.TryGetValue(key, out var sameProf))
            {
                PreserveRiskCardIfEmpty(sameProf, filled);
                continue;
            }

            if (!records.Any(r => SameOrganizationKey(r.Organization, filled.Organization ?? string.Empty)))
            {
                records.Add(filled);
            }
        }
    }

    private sealed class OrgProfComparer : IEqualityComparer<(string Org, string Prof)>
    {
        public bool Equals((string Org, string Prof) x, (string Org, string Prof) y) =>
            SameOrganizationKey(x.Org, y.Org)
            && string.Equals(x.Prof.Trim(), y.Prof.Trim(), StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Org, string Prof) obj) =>
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Prof.Trim());
    }

    /// <summary>
    /// Защита от случайного затирания карты: пустой RiskLines при сохранении перечня/синхронизации
    /// не должен обнулять уже заполненные строки.
    /// </summary>
    private static void PreserveRiskCardIfEmpty(WorkplaceRecord incoming, WorkplaceRecord? prior)
    {
        if (prior is null)
        {
            return;
        }

        var incomingEmpty = incoming.RiskLines is null || incoming.RiskLines.Count == 0;
        var priorFilled = prior.RiskLines is { Count: > 0 };
        if (incomingEmpty && priorFilled)
        {
            incoming.RiskLines = prior.RiskLines;
            if (incoming.RiskAssessmentDate is null)
            {
                incoming.RiskAssessmentDate = prior.RiskAssessmentDate;
            }

            if (string.IsNullOrWhiteSpace(incoming.RiskCardNumber))
            {
                incoming.RiskCardNumber = prior.RiskCardNumber;
            }

            if (string.IsNullOrWhiteSpace(incoming.OverallRisk))
            {
                incoming.OverallRisk = prior.OverallRisk;
            }

            if (string.IsNullOrWhiteSpace(incoming.RiskPeriodicity))
            {
                incoming.RiskPeriodicity = prior.RiskPeriodicity;
            }
        }
    }

    private static bool SameOrganizationKey(string? left, string right)
    {
        var a = (left ?? string.Empty).Trim();
        var b = (right ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
               || OrganizationMatcher.Matches(a, b)
               || OrganizationMatcher.Matches(b, a);
    }
}

public sealed class WorkplaceAssignmentStore
{
    private readonly RiskDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public WorkplaceAssignmentStore(RiskDataPaths paths) => _paths = paths;

    public List<WorkplaceAssignmentDocument> LoadDocuments() => LoadDocumentsCore();

    public List<WorkplaceAssignmentDocument> LoadDocumentsByOrganization(string organization) =>
        LoadDocumentsCore()
            .Where(r => string.IsNullOrWhiteSpace(organization) ||
                        string.Equals(r.Organization, organization, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Date)
            .ThenByDescending(r => r.Number, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public void SaveDocuments(IEnumerable<WorkplaceAssignmentDocument> records) =>
        File.WriteAllText(_paths.AssignmentsFile, JsonSerializer.Serialize(records.ToList(), JsonOpts));

    public void UpsertDocument(WorkplaceAssignmentDocument record)
    {
        var list = LoadDocumentsCore();
        record.ModifiedAt = DateTime.UtcNow;
        var idx = list.FindIndex(x => string.Equals(x.Id, record.Id, StringComparison.Ordinal));
        if (idx >= 0)
        {
            record.CreatedAt = list[idx].CreatedAt;
            list[idx] = record;
        }
        else
        {
            record.CreatedAt = DateTime.UtcNow;
            list.Add(record);
        }

        SaveDocuments(list);
    }

    public void DeleteDocument(string id)
    {
        var list = LoadDocumentsCore();
        list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        SaveDocuments(list);
    }

    public string SuggestNextNumber() =>
        IncidentNumberHelper.NextNumber("ЗРМ-", LoadDocumentsCore().Select(r => r.Number), pad: 6);

    public List<WorkplaceAssignmentRecord> LoadAll() => Flatten(LoadDocumentsCore());

    public List<WorkplaceAssignmentRecord> LoadByOrganization(string organization) =>
        Flatten(LoadDocumentsByOrganization(organization));

    public void SaveAll(IEnumerable<WorkplaceAssignmentRecord> records)
    {
        var docs = records
            .GroupBy(r => r.Id, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                return new WorkplaceAssignmentDocument
                {
                    Id = first.Id,
                    Organization = first.Organization,
                    Number = first.Number,
                    Date = first.Date,
                    OperationType = first.OperationType,
                    Department = first.Department,
                    Lines = group.Select(line => new WorkplaceAssignmentLine
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Employee = line.Employee,
                        WorkplaceId = line.WorkplaceId,
                        WorkplaceName = line.WorkplaceName,
                        WorkplaceNumber = string.Empty,
                        AssignedFrom = line.AssignedFrom ?? line.Date,
                        AssignedTo = line.AssignedTo
                    }).ToList(),
                    CreatedAt = first.CreatedAt,
                    ModifiedAt = DateTime.UtcNow
                };
            })
            .ToList();
        SaveDocuments(docs);
    }

    public void Upsert(WorkplaceAssignmentRecord record)
    {
        var docs = LoadDocumentsCore();
        var doc = docs.FirstOrDefault(x => string.Equals(x.Id, record.Id, StringComparison.Ordinal));
        if (doc is null)
        {
            doc = new WorkplaceAssignmentDocument
            {
                Id = record.Id,
                Organization = record.Organization,
                Number = record.Number,
                Date = record.Date,
                OperationType = record.OperationType,
                Department = record.Department,
                CreatedAt = record.CreatedAt
            };
            docs.Add(doc);
        }

        doc.Organization = record.Organization;
        doc.Number = record.Number;
        doc.Date = record.Date;
        doc.OperationType = record.OperationType;
        doc.Department = record.Department;
        doc.ModifiedAt = DateTime.UtcNow;
        doc.Lines =
        [
            new WorkplaceAssignmentLine
            {
                Id = Guid.NewGuid().ToString("N"),
                Employee = record.Employee,
                WorkplaceId = record.WorkplaceId,
                WorkplaceName = record.WorkplaceName,
                AssignedFrom = record.AssignedFrom ?? record.Date,
                AssignedTo = record.AssignedTo
            }
        ];

        SaveDocuments(docs);
    }

    public void Delete(string id) => DeleteDocument(id);

    private List<WorkplaceAssignmentDocument> LoadDocumentsCore()
    {
        try
        {
            if (!File.Exists(_paths.AssignmentsFile))
            {
                return [];
            }

            var json = File.ReadAllText(_paths.AssignmentsFile);
            List<WorkplaceAssignmentDocument> docs;
            if (json.Contains("\"Lines\"", StringComparison.Ordinal))
            {
                docs = JsonSerializer.Deserialize<List<WorkplaceAssignmentDocument>>(json, JsonOpts) ?? [];
            }
            else
            {
                var legacy = JsonSerializer.Deserialize<List<WorkplaceAssignmentRecord>>(json, JsonOpts) ?? [];
                if (legacy.Count == 0)
                {
                    return [];
                }

                docs = MigrateLegacy(legacy);
                SaveDocuments(docs);
                return docs;
            }

            if (PurgeLegacyDemoEmployees(docs))
            {
                SaveDocuments(docs);
            }

            return docs;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Удаляет захардкоженных демо-сотрудников, которые раньше сидились во все организации.</summary>
    private static bool PurgeLegacyDemoEmployees(List<WorkplaceAssignmentDocument> docs)
    {
        var changed = false;
        for (var i = docs.Count - 1; i >= 0; i--)
        {
            var doc = docs[i];
            doc.Lines ??= [];
            var before = doc.Lines.Count;
            doc.Lines = doc.Lines.Where(line => !IsLegacyDemoEmployee(line.Employee)).ToList();
            if (doc.Lines.Count != before)
            {
                changed = true;
                doc.ModifiedAt = DateTime.UtcNow;
            }

            // Пустой демо-документ ЗРМ-000001 «Бригада ремонта Ижевск» — целиком убрать.
            if (doc.Lines.Count == 0 &&
                string.Equals(doc.Number, "ЗРМ-000001", StringComparison.OrdinalIgnoreCase) &&
                (doc.Department?.Contains("Бригада ремонта", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                docs.RemoveAt(i);
                changed = true;
            }
        }

        return changed;
    }

    private static bool IsLegacyDemoEmployee(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length == 0)
        {
            return false;
        }

        string[] markers =
        [
            "Кирпичников Василий",
            "Кирпичников В.И",
            "Лепина Мария",
            "Красильникова Алена",
            "Красильникова Алина",
            "Плотников Петр Петрович",
            "Плотников П.П",
            "Иванов Иван Иванович",
            "Петров Петр Петрович",
            "Сидоров Сидор Сидорович",
            "Кузнецов Алексей Викторович",
            "Морозов Дмитрий Сергеевич"
        ];

        return markers.Any(m => n.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    private static List<WorkplaceAssignmentDocument> MigrateLegacy(IEnumerable<WorkplaceAssignmentRecord> legacy) =>
        legacy.Select(record => new WorkplaceAssignmentDocument
        {
            Id = record.Id,
            Organization = record.Organization,
            Number = record.Number,
            Date = record.Date,
            OperationType = record.OperationType,
            Department = record.Department,
            CreatedAt = record.CreatedAt,
            ModifiedAt = record.ModifiedAt,
            Lines =
            [
                new WorkplaceAssignmentLine
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Employee = record.Employee,
                    WorkplaceId = record.WorkplaceId,
                    WorkplaceName = record.WorkplaceName,
                    AssignedFrom = record.AssignedFrom ?? record.Date,
                    AssignedTo = record.AssignedTo
                }
            ]
        }).ToList();

    private static List<WorkplaceAssignmentRecord> Flatten(IEnumerable<WorkplaceAssignmentDocument> documents)
    {
        var rows = new List<WorkplaceAssignmentRecord>();
        foreach (var document in documents)
        {
            if (document.Lines.Count == 0)
            {
                rows.Add(new WorkplaceAssignmentRecord
                {
                    Id = document.Id,
                    Organization = document.Organization,
                    Number = document.Number,
                    Date = document.Date,
                    OperationType = document.OperationType,
                    Department = document.Department,
                    CreatedAt = document.CreatedAt,
                    ModifiedAt = document.ModifiedAt
                });
                continue;
            }

            foreach (var line in document.Lines)
            {
                rows.Add(new WorkplaceAssignmentRecord
                {
                    Id = document.Id,
                    Organization = document.Organization,
                    Number = document.Number,
                    Date = document.Date,
                    OperationType = document.OperationType,
                    Department = document.Department,
                    Employee = line.Employee,
                    WorkplaceId = line.WorkplaceId,
                    WorkplaceName = line.WorkplaceName,
                    AssignedFrom = line.AssignedFrom,
                    AssignedTo = line.AssignedTo,
                    CreatedAt = document.CreatedAt,
                    ModifiedAt = document.ModifiedAt
                });
            }
        }

        return rows;
    }
}
