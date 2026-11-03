namespace Zashita.Web.Pages.Suot;

public sealed class SuotPanelActionsModel
{
    public string? EditHref { get; init; }

    public string? DeleteId { get; init; }

    public string? DeleteConfirm { get; init; }

    public string? SecondaryHref { get; init; }

    public string? SecondaryLabel { get; init; }

    public IReadOnlyDictionary<string, string?> HiddenFields { get; init; } =
        new Dictionary<string, string?>(StringComparer.Ordinal);
}
