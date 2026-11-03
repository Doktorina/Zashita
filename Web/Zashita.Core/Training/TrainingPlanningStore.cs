using System.Text.Json;

namespace Zashita.Core.Training;

public sealed class TrainingPlanningStore
{
    private readonly TrainingDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public TrainingPlanningStore(TrainingDataPaths paths) => _paths = paths;

    public List<TrainingPlanningRecord> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.PlanningFile))
            {
                return CreateSeed();
            }

            var json = File.ReadAllText(_paths.PlanningFile);
            var data = JsonSerializer.Deserialize<List<TrainingPlanningRecord>>(json, JsonOpts);
            if (data is not { Count: > 0 })
            {
                return CreateSeed();
            }

            foreach (var record in data)
            {
                record.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                record.Participants ??= [];
                record.AgreementPersons ??= [];
            }

            if (PurgeLegacyDemoParticipants(data))
            {
                SaveAll(data);
            }

            return data;
        }
        catch
        {
            return CreateSeed();
        }
    }

    private static bool PurgeLegacyDemoParticipants(List<TrainingPlanningRecord> records)
    {
        var changed = false;
        for (var i = records.Count - 1; i >= 0; i--)
        {
            var record = records[i];
            record.Participants ??= [];
            var before = record.Participants.Count;
            record.Participants = record.Participants
                .Where(p => !IsLegacyDemoPerson(p.FullName))
                .ToList();
            if (record.Participants.Count != before)
            {
                changed = true;
            }

            if (record.Participants.Count == 0 &&
                (string.IsNullOrWhiteSpace(record.Number) ||
                 (record.Content?.Contains("Планирование инструктажей", StringComparison.OrdinalIgnoreCase) ?? false)))
            {
                records.RemoveAt(i);
                changed = true;
            }
        }

        return changed;
    }

    private static bool IsLegacyDemoPerson(string? name)
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

    public TrainingPlanningRecord? GetById(string id) =>
        LoadAll().FirstOrDefault(record => record.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public void SaveAll(IEnumerable<TrainingPlanningRecord> records)
    {
        var data = records
            .Where(record => !string.IsNullOrWhiteSpace(record.Number) || !string.IsNullOrWhiteSpace(record.TrainingKind))
            .OrderByDescending(record => record.DocumentDate)
            .ThenByDescending(record => record.Number, StringComparer.OrdinalIgnoreCase)
            .ToList();
        File.WriteAllText(_paths.PlanningFile, JsonSerializer.Serialize(data, JsonOpts));
    }

    public void Upsert(TrainingPlanningRecord record)
    {
        var list = LoadAll();
        var index = list.FindIndex(item => item.Id.Equals(record.Id, StringComparison.Ordinal));
        if (index >= 0)
        {
            list[index] = record;
        }
        else
        {
            list.Add(record);
        }

        SaveAll(list);
    }

    public void Delete(string id)
    {
        var list = LoadAll();
        list.RemoveAll(item => item.Id.Equals(id, StringComparison.Ordinal));
        SaveAll(list);
    }

    /// <summary>Следующий номер заявки (цифры из существующих номеров + 1).</summary>
    public string AllocateNextNumber(string? prefix = null)
    {
        static long ParseDigits(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return 0;
            }

            var digits = new string(raw.Where(char.IsDigit).ToArray());
            return long.TryParse(digits, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
        }

        var max = LoadAll().Select(r => ParseDigits(r.Number)).DefaultIfEmpty(0).Max();
        var next = (max + 1).ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(prefix) ? next : prefix.Trim() + next;
    }

    public void RenameOrganization(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        var list = LoadAll();
        var changed = false;
        foreach (var record in list)
        {
            if (string.Equals(record.Organization, oldName, StringComparison.OrdinalIgnoreCase))
            {
                record.Organization = newName;
                changed = true;
            }
        }

        if (changed)
        {
            SaveAll(list);
        }
    }

    private static List<TrainingPlanningRecord> CreateSeed() => [];
}
