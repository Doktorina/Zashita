using System.Text;

namespace Zashita.Web.Services;

internal static class MedStatusAuditLog
{
    public static void Write(string contentRoot, MedStatusAuditReport report)
    {
        if (report.TreeDuplicates.Count == 0
            && report.AmbiguousRosterMatches.Count == 0
            && report.GarbageExamNames.Count == 0)
        {
            return;
        }

        try
        {
            var dir = Path.Combine(contentRoot, "App_Data");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "med-status-audit.log");
            File.WriteAllText(path, Format(report), Encoding.UTF8);
        }
        catch
        {
            // ignore logging failures
        }
    }

    public static void Print(MedStatusAuditReport report)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Write(Format(report));
    }

    private static string Format(MedStatusAuditReport report)
    {
        var writer = new StringBuilder();
        writer.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Med status audit");
        writer.AppendLine($"Organizations: {report.OrganizationCount}, employees: {report.EmployeeCount}");
        writer.AppendLine($"Tree duplicates: {report.TreeDuplicates.Count}");
        foreach (var issue in report.TreeDuplicates)
        {
            writer.AppendLine($"  {issue.Organization} | {issue.EmployeeName}");
            writer.AppendLine($"    departments: {string.Join(" / ", issue.Departments)}");
            writer.AppendLine($"    professions: {string.Join(" / ", issue.Professions)}");
            writer.AppendLine($"    names: {string.Join(" / ", issue.NameVariants)}");
        }

        writer.AppendLine($"Ambiguous roster: {report.AmbiguousRosterMatches.Count}");
        foreach (var issue in report.AmbiguousRosterMatches)
        {
            writer.AppendLine($"  {issue.Organization}: {issue.NameA} <> {issue.NameB}");
        }

        writer.AppendLine($"Garbage exam names: {report.GarbageExamNames.Count}");
        foreach (var name in report.GarbageExamNames)
        {
            writer.AppendLine($"  {name}");
        }

        return writer.ToString();
    }
}
