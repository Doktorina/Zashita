namespace Zashita.Core.Print;

public sealed record PrintJobRequest(
    string Title,
    string? SheetName,
    string Format);

public static class PrintFormats
{
    public const string HtmlPreview = "html";
    public const string Pdf = "pdf";
    public const string Excel = "xlsx";
}
