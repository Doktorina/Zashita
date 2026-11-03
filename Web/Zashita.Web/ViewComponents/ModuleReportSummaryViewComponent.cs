using Microsoft.AspNetCore.Mvc;
using Zashita.Web.Services;

namespace Zashita.Web.ViewComponents;

public sealed class ModuleReportSummaryViewComponent : ViewComponent
{
    private readonly HomeReportSummaryService _summaries;
    private readonly RisksWorkspaceContext _risksWorkspace;

    public ModuleReportSummaryViewComponent(
        HomeReportSummaryService summaries,
        RisksWorkspaceContext risksWorkspace)
    {
        _summaries = summaries;
        _risksWorkspace = risksWorkspace;
    }

    public IViewComponentResult Invoke(string module, string organization, string? reportUrl = null, DateTime? asOf = null)
    {
        if (string.IsNullOrWhiteSpace(organization))
        {
            return Content(string.Empty);
        }

        var org = organization.Trim();
        var moduleKey = (module ?? string.Empty).Trim();
        var isRisks = IsRisksModule(moduleKey);
        string? riskObjectId = null;
        string? scopeHint = null;
        if (isRisks)
        {
            riskObjectId = _risksWorkspace.GetSelectedObjectId(HttpContext, org);
            scopeHint = BuildRiskScopeHint(org, riskObjectId);
        }

        var data = _summaries.LoadModule(moduleKey, org, asOf, riskObjectId);
        var resolvedUrl = reportUrl;
        if (string.IsNullOrWhiteSpace(resolvedUrl) &&
            ModuleHomeReports.TryGet(NormalizeSlug(moduleKey), out var report))
        {
            resolvedUrl = report.Url;
        }

        if (isRisks && !string.IsNullOrWhiteSpace(resolvedUrl) && !string.IsNullOrWhiteSpace(riskObjectId))
        {
            var sep = resolvedUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            resolvedUrl = $"{resolvedUrl}{sep}objectId={Uri.EscapeDataString(riskObjectId)}";
        }

        var charts = _summaries.LoadFamiliarizationCharts(moduleKey, org, asOf, riskObjectId);
        var view = ModuleReportSummaryCatalog.Build(moduleKey, data, resolvedUrl, charts, scopeHint);
        return View(view);
    }

    private string BuildRiskScopeHint(string organization, string? riskObjectId)
    {
        if (RisksWorkspaceContext.IsAllObjects(riskObjectId))
        {
            return "Показаны данные по организации (режим «Все объекты»). Переключите объект сверху — сводка обновится.";
        }

        var name = _risksWorkspace.DisplayName(organization, riskObjectId);
        return $"Аналитика по объекту «{name}».";
    }

    private static bool IsRisksModule(string module) =>
        module.Equals("риски", StringComparison.OrdinalIgnoreCase)
        || module.Equals("risks", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSlug(string module) =>
        module.Trim().ToLowerInvariant() switch
        {
            "медосмотры" or "med" => "медосмотры",
            "обучение" or "training" => "обучение",
            "риски" or "risks" => "риски",
            "специальная оценка" or "спецоценка" or "sout" => "специальная-оценка",
            _ => module.Trim().ToLowerInvariant()
        };
}
