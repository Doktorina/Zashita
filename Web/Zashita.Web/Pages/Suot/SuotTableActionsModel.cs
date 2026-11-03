namespace Zashita.Web.Pages.Suot;

public sealed class SuotTableActionsModel
{
    public string? ViewHref { get; init; }

    public string? EditHref { get; init; }

    public string? DeleteId { get; init; }

    public string? DeleteConfirm { get; init; }

    public IReadOnlyDictionary<string, string?> HiddenFields { get; init; } =
        new Dictionary<string, string?>(StringComparer.Ordinal);
}
