namespace Zashita.Core.Training;

public sealed class TrainingProgramMappingLine
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceProgram { get; set; } = string.Empty;
    public string TargetKind { get; set; } = string.Empty;
    public string TargetProgram { get; set; } = string.Empty;
}

public sealed record TrainingMappingGroup(string SourceProgram, IReadOnlyList<TrainingProgramMappingLine> Lines);

public sealed class TrainingAssistantEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? RouteUrl { get; set; }
}

public static class TrainingMappingGroups
{
    public static List<TrainingMappingGroup> Build(IReadOnlyList<TrainingProgramMappingLine> lines) =>
        lines
            .GroupBy(line => line.SourceProgram.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new TrainingMappingGroup(
                group.Key,
                group.OrderBy(line => line.TargetKind, StringComparer.CurrentCultureIgnoreCase).ToList()))
            .ToList();
}
