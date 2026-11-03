namespace Zashita.Core.Navigation;

public sealed record ModuleSection(string Title, IReadOnlyList<string> Items);

public sealed record ModulePageDefinition(
    string ModuleName,
    IReadOnlyList<string> TopLinks,
    IReadOnlyList<ModuleSection> LeftSections,
    IReadOnlyList<ModuleSection> RightSections);
