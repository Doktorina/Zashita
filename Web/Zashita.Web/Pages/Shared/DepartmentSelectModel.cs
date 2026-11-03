namespace Zashita.Web.Pages.Shared;

public sealed class DepartmentSelectModel
{
    public string Name { get; init; } = "Department";
    public string? Value { get; init; }
    public IReadOnlyList<string> Options { get; init; } = [];
    public string? Label { get; init; } = "Подразделение";
    public string? EmptyLabel { get; init; } = "— не указано —";
    public bool Optional { get; init; } = true;
    public bool Required { get; init; }
    public bool AutoSubmit { get; init; }
    public string? CssClass { get; init; } = "ppe-field";
    public string InputClass { get; init; } = "ppe-input";
}
