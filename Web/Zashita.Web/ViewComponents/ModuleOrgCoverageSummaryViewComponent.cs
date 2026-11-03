using Microsoft.AspNetCore.Mvc;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.ViewComponents;

public sealed class ModuleOrgCoverageSummaryViewComponent : ViewComponent
{
    private readonly HomeReportSummaryService _summaries;

    public ModuleOrgCoverageSummaryViewComponent(HomeReportSummaryService summaries) =>
        _summaries = summaries;

    public IViewComponentResult Invoke(
        string module,
        IReadOnlyList<string> organizations,
        string? reportUrl = null,
        DateTime? asOf = null)
    {
        if (organizations is null || organizations.Count == 0)
        {
            return Content(string.Empty);
        }

        var rows = _summaries.LoadModuleOrgCoverage(module, organizations, asOf);
        var catalog = ModuleReportSummaryCatalog.Build(
            module,
            new HomeReportSummaryBuilder.ModuleSummary(0, 0, 0, 0, 0),
            reportUrl);

        var resolvedUrl = reportUrl;
        if (string.IsNullOrWhiteSpace(resolvedUrl) &&
            ModuleHomeReports.TryGet(NormalizeSlug(module), out var report))
        {
            resolvedUrl = report.Url;
        }

        var view = new ModuleOrgCoverageSummaryView(
            catalog.ModuleKey,
            catalog.Title,
            string.IsNullOrWhiteSpace(resolvedUrl) ? catalog.ReportUrl : resolvedUrl.Trim(),
            catalog.ReportButtonLabel,
            rows,
            rows.Count(r => r.Status == HomeReportSummaryBuilder.OrgCoverageStatus.Complete),
            rows.Count(r => r.Status == HomeReportSummaryBuilder.OrgCoverageStatus.Partial),
            rows.Count(r => r.Status == HomeReportSummaryBuilder.OrgCoverageStatus.None),
            rows.Count(r => r.Status == HomeReportSummaryBuilder.OrgCoverageStatus.Overdue),
            rows.Count(r => r.Status == HomeReportSummaryBuilder.OrgCoverageStatus.Empty));

        return View(view);
    }

    private static string NormalizeSlug(string module) =>
        module.Trim().ToLowerInvariant() switch
        {
            "медосмотры" or "med" => "медосмотры",
            "обучение" or "training" => "обучение",
            "риски" or "risks" => "риски",
            "специальная оценка" or "спецоценка" or "sout" or "специальная-оценка" => "специальная-оценка",
            _ => module.Trim().ToLowerInvariant()
        };
}
