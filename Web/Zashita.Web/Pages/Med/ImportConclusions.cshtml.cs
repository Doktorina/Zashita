using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Med;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med;

[RequireLogin]
public class ImportConclusionsModel : PageModel
{
    private readonly MedConclusionDocxImportService _import;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;

    public ImportConclusionsModel(
        MedConclusionDocxImportService import,
        OrganizationStore orgs,
        OrganizationEmployeesService employees)
    {
        _import = import;
        _orgs = orgs;
        _employees = employees;
    }

    public string Organization { get; private set; } = string.Empty;
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public MedConclusionImportDraft? Draft { get; private set; }

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
        var files = (uploadFiles ?? []).Where(f => f is { Length: > 0 }).ToList();
        if (files.Count == 0)
        {
            Message = "Выберите один или несколько файлов (.docx или .pdf).";
            IsError = true;
            return Page();
        }

        var bad = files.FirstOrDefault(f =>
        {
            var ext = Path.GetExtension(f.FileName);
            return !ext.Equals(".docx", StringComparison.OrdinalIgnoreCase)
                   && !ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        });
        if (bad is not null)
        {
            Message = $"Нужны .docx или .pdf (не подошёл «{bad.FileName}»).";
            IsError = true;
            return Page();
        }

        try
        {
            var opened = new List<MemoryStream>();
            try
            {
                var payloads = new List<(Stream Stream, string FileName)>();
                foreach (var f in files)
                {
                    var ms = new MemoryStream();
                    f.CopyTo(ms);
                    ms.Position = 0;
                    opened.Add(ms);
                    payloads.Add((ms, f.FileName));
                }

                var staff = _employees.LoadForMedReport(Organization);

                if (autoApply)
                {
                    var result = _import.ImportFilesAuto(Organization, payloads, overwriteExisting, staff);
                    StatusModel.InvalidateRowsCache();
                    TempData["Message"] = string.Join(" ",
                        new[] { $"Обновлено строк: {result.Applied}. Создано: {result.Created}. Пропущено: {result.Skipped}." }
                            .Concat(result.Messages.Take(10)));
                    if (result.Applied == 0 && result.Created == 0)
                    {
                        TempData["IsError"] = "1";
                        return RedirectToPage();
                    }

                    return RedirectToPage("/Med/Status");
                }

                Draft = _import.CreateDraftFromFiles(Organization, payloads, staff);
                if (Draft.Items.Count == 0)
                {
                    Message = "В файлах не найдено заключений («ЗАКЛЮЧЕНИЕ … МЕДИЦИНСКОГО ОСМОТРА»).";
                    IsError = true;
                    return Page();
                }

                Message =
                    $"Файлов: {files.Count}. Заключений: {Draft.Items.Count}. Проверьте и нажмите «Записать в отчёт».";
                return Page();
            }
            finally
            {
                foreach (var ms in opened)
                {
                    ms.Dispose();
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
            Request.Form["selected"].Select(x => x?.Trim() ?? string.Empty).Where(x => x.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        var overwrite = new HashSet<string>(
            Request.Form["overwrite"].Select(x => x?.Trim() ?? string.Empty).Where(x => x.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        if (selected.Count == 0)
        {
            Message = "Отметьте хотя бы одно заключение.";
            IsError = true;
            return Page();
        }

        var staff = _employees.LoadForMedReport(Organization);
        var result = _import.Apply(Draft, selected, overwrite, staff);
        _import.DeleteDraft(Draft.Id);
        StatusModel.InvalidateRowsCache();
        Draft = null;

        TempData["Message"] = string.Join(" ",
            new[] { $"Обновлено строк: {result.Applied}. Создано: {result.Created}. Пропущено: {result.Skipped}." }
                .Concat(result.Messages.Take(8)));
        if (result.Applied == 0 && result.Created == 0)
        {
            TempData["IsError"] = "1";
            return RedirectToPage();
        }

        return RedirectToPage("/Med/Status");
    }

    private void LoadShell()
    {
        Organization = _orgs.ResolveCanonicalOrganization(_orgs.GetSelectedOrganization(HttpContext))
            ?? _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "медосмотры";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Импорт заключений из Word";
        ViewData["Subtitle"] = Organization;

        if (string.IsNullOrWhiteSpace(Message))
        {
            Message = TempData["Message"] as string;
            IsError = string.Equals(TempData["IsError"] as string, "1", StringComparison.Ordinal);
        }
    }
}
