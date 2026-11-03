namespace Zashita.Core.Training;

public sealed class TrainingKindProgramMappingRow
{
    public string KindId { get; set; } = string.Empty;
    public string KindName { get; set; } = string.Empty;
    public string ProgramName { get; set; } = string.Empty;
}

public static class TrainingKindProgramMapping
{
    public static List<TrainingKindProgramMappingRow> BuildRows(
        IReadOnlyList<TrainingKindNode> kinds,
        IReadOnlyList<TrainingProgramMappingLine> saved)
    {
        var byKind = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in saved)
        {
            var kindKey = ResolveKindKey(line);
            if (string.IsNullOrWhiteSpace(kindKey))
            {
                continue;
            }

            byKind[kindKey] = line.TargetProgram?.Trim() ?? string.Empty;
        }

        return kinds
            .Where(kind => !kind.IsGroup)
            .Where(kind => !string.IsNullOrWhiteSpace(kind.Name))
            .OrderBy(kind => kind.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(kind =>
            {
                var name = kind.Name.Trim();
                byKind.TryGetValue(name, out var program);
                if (string.IsNullOrWhiteSpace(program))
                {
                    byKind.TryGetValue(kind.Id, out program);
                }

                return new TrainingKindProgramMappingRow
                {
                    KindId = kind.Id,
                    KindName = name,
                    ProgramName = program ?? string.Empty
                };
            })
            .ToList();
    }

    public static List<TrainingProgramMappingLine> ToStorage(IEnumerable<TrainingKindProgramMappingRow> rows) =>
        rows
            .Where(row => !string.IsNullOrWhiteSpace(row.KindName))
            .Select(row => new TrainingProgramMappingLine
            {
                Id = string.IsNullOrWhiteSpace(row.KindId) ? Guid.NewGuid().ToString("N") : row.KindId,
                SourceProgram = row.KindName.Trim(),
                TargetKind = row.KindName.Trim(),
                TargetProgram = row.ProgramName?.Trim() ?? string.Empty
            })
            .ToList();

    public static string? ResolveProgram(IReadOnlyList<TrainingProgramMappingLine> mappings, string? kindName)
    {
        if (string.IsNullOrWhiteSpace(kindName))
        {
            return null;
        }

        var key = kindName.Trim();
        foreach (var line in mappings)
        {
            if (!string.Equals(ResolveKindKey(line), key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var program = line.TargetProgram?.Trim();
            if (!string.IsNullOrWhiteSpace(program))
            {
                return program;
            }
        }

        return null;
    }

    public static Dictionary<string, string> BuildLookup(IReadOnlyList<TrainingProgramMappingLine> mappings)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in mappings)
        {
            var kindKey = ResolveKindKey(line);
            var program = line.TargetProgram?.Trim();
            if (string.IsNullOrWhiteSpace(kindKey) || string.IsNullOrWhiteSpace(program))
            {
                continue;
            }

            lookup[kindKey] = program;
        }

        return lookup;
    }

    private static string ResolveKindKey(TrainingProgramMappingLine line)
    {
        if (!string.IsNullOrWhiteSpace(line.TargetKind))
        {
            return line.TargetKind.Trim();
        }

        return line.SourceProgram?.Trim() ?? string.Empty;
    }
}
