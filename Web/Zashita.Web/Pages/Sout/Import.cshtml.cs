using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Sout;

[RequireLogin]
public class ImportModel : PageModel
{
    private readonly OrganizationStore _orgs;
    private readonly SoutXtmImportService _xtmImport;

    public ImportModel(OrganizationStore orgs, SoutXtmImportService xtmImport)
    {
        _orgs = orgs;
        _xtmImport = xtmImport;
    }

    public string Organization { get; private set; } = string.Empty;
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public IReadOnlyList<SoutImportFileInfo> UploadedFiles { get; private set; } = [];

    public void OnGet() => LoadPage();

    public IActionResult OnPostUpload(IFormFile? uploadFile)
    {
        LoadPage();

        if (uploadFile is null || uploadFile.Length == 0)
        {
            Message = "Выберите файл результатов для загрузки.";
            IsError = true;
            return Page();
        }

        using var stream = uploadFile.OpenReadStream();
        var result = _xtmImport.Import(Organization, stream, uploadFile.FileName);
        Message = result.Message;
        IsError = !result.Success;
        UploadedFiles = _xtmImport.ListFiles(Organization);
        return Page();
    }

    public IActionResult OnGetDownload(string fileName)
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        var path = _xtmImport.ResolveFilePath(Organization, fileName);
        if (path is null)
        {
            return NotFound();
        }

        var downloadName = Path.GetFileName(path);
        return PhysicalFile(path, "application/octet-stream", downloadName);
    }

    public IActionResult OnGetPreview(string fileName)
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        var path = _xtmImport.ResolveFilePath(Organization, fileName);
        if (path is null)
        {
            return NotFound();
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        var contentType = ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".txt" or ".xml" or ".xtm" or ".csv" => "text/plain; charset=utf-8",
            ".html" or ".htm" => "text/html; charset=utf-8",
            _ => "application/octet-stream"
        };

        // Word/Excel в браузере обычно не открываются — отдаём скачивание.
        if (ext is ".doc" or ".docx" or ".xls" or ".xlsx")
        {
            return PhysicalFile(path, contentType, Path.GetFileName(path));
        }

        return PhysicalFile(path, contentType);
    }

    public IActionResult OnPostDelete(string fileName)
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        if (_xtmImport.DeleteFile(Organization, fileName))
        {
            TempData["Message"] = $"Файл «{Path.GetFileName(fileName)}» удалён.";
        }
        else
        {
            TempData["Message"] = "Файл не найден.";
            TempData["IsError"] = "1";
        }

        return RedirectToPage();
    }

    private void LoadPage()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Загрузка результатов";
        ViewData["Subtitle"] = Organization;
        UploadedFiles = _xtmImport.ListFiles(Organization);
        if (string.IsNullOrWhiteSpace(Message))
        {
            Message = TempData["Message"] as string;
            IsError = string.Equals(TempData["IsError"] as string, "1", StringComparison.Ordinal);
        }
    }
}
