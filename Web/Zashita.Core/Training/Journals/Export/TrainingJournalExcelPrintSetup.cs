using NPOI.SS.UserModel;

namespace Zashita.Core.Training.Journals.Export;

public static class TrainingJournalExcelPrintSetup
{
    public static void ApplyLandscapePrintSetup(ISheet sheet)
    {
        var printSetup = sheet.PrintSetup;
        printSetup.Landscape = true;
        printSetup.PaperSize = (short)PaperSize.A4;
        printSetup.FitWidth = 1;
        printSetup.FitHeight = 0;
        sheet.FitToPage = false;
        sheet.Autobreaks = false;
    }
}
