namespace Zashita.Core.Training.Journals;

using Zashita.Core.Training.Journals.Export;

public sealed class TrainingJournalExportService
{
    private readonly string _templatesDirectory;

    public TrainingJournalExportService(string templatesDirectory) =>
        _templatesDirectory = templatesDirectory;

    public (byte[] Content, string FileName, string ContentType) Export(
        TrainingJournalDefinition definition,
        IReadOnlyList<TrainingJournalEntry> entries,
        TrainingJournalParams parameters)
    {
        if (entries.Count == 0)
        {
            throw new InvalidOperationException("Нет записей для экспорта. Сначала сформируйте журнал.");
        }

        var stem = definition.ExportFileNameStem;
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var docPath = Path.Combine(Path.GetTempPath(), $"journal_{Guid.NewGuid():N}.docx");

        try
        {
            TrainingJournalWordGenerator.ExportToWord(
                entries, parameters, docPath, definition, _templatesDirectory);
            return (File.ReadAllBytes(docPath), $"{stem}_{stamp}.doc",
                "application/msword");
        }
        finally
        {
            if (File.Exists(docPath))
            {
                File.Delete(docPath);
            }
        }
    }
}
