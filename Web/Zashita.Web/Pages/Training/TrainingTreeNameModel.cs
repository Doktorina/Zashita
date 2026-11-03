namespace Zashita.Web.Pages.Training;

public sealed class TrainingTreeNameModel
{
    public int Depth { get; set; }
    public bool IsGroup { get; set; }
    public string Name { get; set; } = string.Empty;
    public string InputClass { get; set; } = "ppe-input";
    public bool Disabled { get; set; }
    public string? ListId { get; set; }
    public string? Placeholder { get; set; }
}
