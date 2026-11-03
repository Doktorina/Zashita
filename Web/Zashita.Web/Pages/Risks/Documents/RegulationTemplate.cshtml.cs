using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks.Documents;

[RequireLogin]
public class RegulationTemplateModel : PageModel
{
    private readonly RiskRegulationTemplateStore _templates;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;

    public RegulationTemplateModel(
        RiskRegulationTemplateStore templates,
        OrganizationStore orgs,
        OrganizationEmployeesService employees)
    {
        _templates = templates;
        _orgs = orgs;
        _employees = employees;
    }

    public string Organization { get; private set; } = string.Empty;
    public string PreviewHtml { get; private set; } = string.Empty;
    public bool HasCustom { get; private set; }
    public DateTime? SavedAtUtc { get; private set; }
    public string? Message { get; private set; }
    public string? Error { get; private set; }
    public string DirectorHint { get; private set; } = string.Empty;

    [BindProperty]
    public IFormFile? UploadFile { get; set; }

    public void OnGet()
    {
        Load();
    }

    public IActionResult OnPostUpload()
    {
        Organization = ResolveOrganization();
        if (string.IsNullOrWhiteSpace(Organization))
        {
            TempData["Error"] = "Выберите организацию перед загрузкой файла.";
            return RedirectToPage();
        }

        if (UploadFile is null || UploadFile.Length == 0)
        {
            TempData["Error"] = "Выберите файл .docx.";
            return RedirectToPage();
        }

        try
        {
            using var stream = UploadFile.OpenReadStream();
            _templates.ImportFile(Organization, stream, UploadFile.FileName);
            TempData["Message"] = $"Шаблон загружен из «{UploadFile.FileName}».";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage();
    }

    public IActionResult OnPostReset()
    {
        Organization = ResolveOrganization();
        if (string.IsNullOrWhiteSpace(Organization))
        {
            TempData["Error"] = "Выберите организацию.";
            return RedirectToPage();
        }

        _templates.ResetToDefault(Organization);
        TempData["Message"] = "Восстановлен шаблон положения по умолчанию.";
        return RedirectToPage();
    }

    private string ResolveOrganization()
    {
        var sessionOrg = _orgs.GetSelectedOrganization(HttpContext);
        if (!string.IsNullOrWhiteSpace(sessionOrg))
        {
            return sessionOrg;
        }

        return _orgs.GetVisibleOrganizations(HttpContext).FirstOrDefault() ?? string.Empty;
    }

    private void Load()
    {
        Organization = ResolveOrganization();
        ViewData["ActiveModule"] = "риски";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Положение (методика)";
        ViewData["Subtitle"] = string.IsNullOrWhiteSpace(Organization) ? "Выберите организацию" : Organization;

        HasCustom = _templates.HasCustomTemplate(Organization);
        SavedAtUtc = _templates.LastWriteUtc(Organization);
        DirectorHint = string.IsNullOrWhiteSpace(Organization)
            ? string.Empty
            : RiskPrintChrome.ResolveDirectorSignInitials(Organization, _employees.Load(Organization));

        var body = _templates.GetHtmlOrDefault(Organization);
        PreviewHtml = RiskOrgDocumentsPrintBuilder.ApplyPlaceholders(
            body,
            Organization,
            DirectorHint);

        Message = TempData["Message"] as string;
        Error = TempData["Error"] as string;
        if (string.IsNullOrWhiteSpace(Organization))
        {
            Error ??= "Выберите конкретную организацию (не «Все») — шаблон привязан к юрлицу.";
        }
    }
}
