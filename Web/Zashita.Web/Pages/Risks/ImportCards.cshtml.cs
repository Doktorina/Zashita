using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks;

[RequireLogin]
public class ImportCardsModel : PageModel
{
    private readonly RiskCardDocxImportService _import;
    private readonly RiskWorkplaceStore _workplaces;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;

    public ImportCardsModel(
        RiskCardDocxImportService import,
        RiskWorkplaceStore workplaces,
        OrganizationStore orgs,
        OrganizationEmployeesService employees)
    {
        _import = import;
        _workplaces = workplaces;
        _orgs = orgs;
        _employees = employees;
    }

    public string Organization { get; private set; } = string.Empty;
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public RiskCardImportDraft? Draft { get; private set; }
    public IReadOnlyList<SelectListItem> WorkplaceOptions { get; private set; } = [];

    public void OnGet(string? draftId = null)
    {
        LoadShell();
        if (!string.IsNullOrWhiteSpace(draftId))
        {
            Draft = _import.LoadDraft(draftId);
            if (Draft is null)
            {
                Message = "Черновик импорта не найден или устарел — загрузите файлы снова.";
                IsError = true;
            }
        }
    }

    public IActionResult OnPostUpload(List<IFormFile>? uploadFiles, bool autoApply = false, bool overwriteExisting = false)
    {
        LoadShell();
        var files = (uploadFiles ?? [])
            .Where(f => f is { Length: > 0 })
            .ToList();

        if (files.Count == 0)
        {
            Message = "Выберите один или несколько файлов Word (.docx).";
            IsError = true;
            return Page();
        }

        var bad = files.FirstOrDefault(f =>
            !string.Equals(Path.GetExtension(f.FileName), ".docx", StringComparison.OrdinalIgnoreCase));
        if (bad is not null)
        {
            Message = $"Нужны только .docx (не подошёл «{bad.FileName}»).";
            IsError = true;
            return Page();
        }

        try
        {
            var opened = new List<(Stream Stream, string FileName, IDisposable Keep)>();
            try
            {
                foreach (var f in files)
                {
                    var ms = new MemoryStream();
                    f.CopyTo(ms);
                    ms.Position = 0;
                    opened.Add((ms, f.FileName, ms));
                }

                var payloads = opened.Select(x => (x.Stream, x.FileName)).ToList();

                if (autoApply)
                {
                    var result = _import.ImportFilesAuto(Organization, payloads, overwriteExisting);
                    if (result.Applied == 0)
                    {
                        TempData["Message"] = string.Join(" ",
                            new[] { $"Записано: 0. Создано РМ: {result.Created}. Пропущено: {result.Skipped}." }
                                .Concat(result.Messages.Take(12)));
                        TempData["IsError"] = "1";
                        return RedirectToPage();
                    }

                    // Успешный импорт — без длинного итога на экране.
                    return RedirectToPage("/Risks/Workplaces", new { view = "assessment" });
                }

                Draft = _import.CreateDraftFromFiles(Organization, payloads);
                if (Draft.Items.Count == 0)
                {
                    Message = "В файлах не найдено карт с таблицами опасностей.";
                    IsError = true;
                    return Page();
                }

                var matched = Draft.Items.Count(i => !string.IsNullOrWhiteSpace(i.MatchedWorkplaceId));
                Message =
                    $"Файлов: {files.Count}. Карт: {Draft.Items.Count} (сопоставлено с РМ: {matched}). Проверьте и нажмите «Записать в карты» — или загрузите снова с галочкой «сразу записать».";
                return Page();
            }
            finally
            {
                foreach (var item in opened)
                {
                    item.Keep.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Message = $"Не удалось разобрать файлы: {ex.Message}";
            IsError = true;
            return Page();
        }
    }

    public IActionResult OnPostApply(string draftId)
    {
        LoadShell();
        Draft = _import.LoadDraft(draftId);
        if (Draft is null)
        {
            Message = "Черновик не найден — загрузите файлы снова.";
            IsError = true;
            return Page();
        }

        var selected = new HashSet<string>(
            Request.Form["selected"].Select(x => x?.Trim() ?? string.Empty)
                .Where(x => x.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var overwrite = new HashSet<string>(
            Request.Form["overwrite"].Select(x => x?.Trim() ?? string.Empty)
                .Where(x => x.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var workplaceByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Draft.Items)
        {
            var key = $"wp_{item.ItemKey}";
            var wp = Request.Form[key].ToString()?.Trim() ?? string.Empty;
            if (wp.Length > 0)
            {
                workplaceByKey[item.ItemKey] = wp;
            }
        }

        if (selected.Count == 0)
        {
            Message = "Отметьте хотя бы одну карту для записи.";
            IsError = true;
            return Page();
        }

        var result = _import.Apply(Draft, workplaceByKey, selected, overwrite);
        _import.DeleteDraft(Draft.Id);
        Draft = null;

        if (result.Applied == 0)
        {
            TempData["Message"] = string.Join(" ",
                new[] { $"Записано: 0. Создано РМ: {result.Created}. Пропущено: {result.Skipped}." }
                    .Concat(result.Messages.Take(8)));
            TempData["IsError"] = "1";
            return RedirectToPage(new { });
        }

        return RedirectToPage("/Risks/Workplaces", new { view = "assessment" });
    }

    private void LoadShell()
    {
        Organization = _orgs.ResolveCanonicalOrganization(_orgs.GetSelectedOrganization(HttpContext))
            ?? _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "риски";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Импорт карт из Word";
        ViewData["Subtitle"] = Organization;

        WorkplaceOptions = _workplaces.LoadAll()
            .Where(w => !w.IsGroup)
            .OrderBy(w => w.Organization, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(w => w.Department, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(w => string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession,
                StringComparer.CurrentCultureIgnoreCase)
            .Select(w => new SelectListItem
            {
                Value = w.Id,
                Text = $"{w.Organization} · {(string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession)}" +
                       (string.IsNullOrWhiteSpace(w.Department) ? "" : $" / {w.Department}")
            })
            .ToList();

        if (string.IsNullOrWhiteSpace(Message))
        {
            Message = TempData["Message"] as string;
            IsError = string.Equals(TempData["IsError"] as string, "1", StringComparison.Ordinal);
        }

        SyncStaffFromOneC(Organization);
    }

    private void SyncStaffFromOneC(string organization)
    {
        if (string.IsNullOrWhiteSpace(organization))
        {
            return;
        }

        var staff = _employees.LoadForReport(organization);
        SoutWorkplaceStaffSync.SyncMissingFromStaff(organization, staff, _workplaces);
        SoutWorkplaceStaffSync.SyncWorkerCountsFromStaff(organization, staff, _workplaces);
    }
}
