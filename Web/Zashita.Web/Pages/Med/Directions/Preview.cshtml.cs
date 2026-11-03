using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Med;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med.Directions;

[RequireLogin]
public class PreviewModel : PageModel
{
    private readonly MedDirectionStore _store;
    private readonly MedicalOrganizationStore _medicalOrganizations;
    private readonly OrganizationEmployeesService _employees;
    private readonly OrganizationRequisitesStore _requisites;
    private readonly MedDirectionSignatureStorage _signatures;
    private readonly IWebHostEnvironment _env;

    public PreviewModel(
        MedDirectionStore store,
        MedicalOrganizationStore medicalOrganizations,
        OrganizationEmployeesService employees,
        OrganizationRequisitesStore requisites,
        MedDirectionSignatureStorage signatures,
        IWebHostEnvironment env)
    {
        _store = store;
        _medicalOrganizations = medicalOrganizations;
        _employees = employees;
        _requisites = requisites;
        _signatures = signatures;
        _env = env;
    }

    public string Id { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string PreviewHtml { get; private set; } = string.Empty;
    public string PdfFileName { get; private set; } = string.Empty;
    public bool AutoDownloadPdf { get; private set; }
    public bool NotFound { get; private set; }
    public string? Message { get; private set; }

    public IActionResult OnGet(string id, bool downloadPdf = false) => LoadPage(id, downloadPdf);

    public IActionResult OnGetDownload(string id)
    {
        var document = _store.GetById(id);
        if (document is null)
        {
            return RedirectToPage(new { id });
        }

        EnsureMedicalOrg(document);
        EnsureHomeAddress(document);
        EnsureEmployerRequisites(document);

        var signatureSrc = ResolveSignatureSrc(document);
        var cssPath = Path.Combine(_env.WebRootPath, "css", "med-direction-print.css");
        var css = System.IO.File.Exists(cssPath)
            ? System.IO.File.ReadAllText(cssPath)
            : string.Empty;
        var html = MedDirectionPrintBuilder.BuildDownloadDocument(
            document, css, ResolveLogoDataUrl(), signatureSrc);

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(html)).ToArray();
        return File(bytes, "application/msword", BuildExportFileName(document, "doc"));
    }

    public IActionResult OnGetDownloadPdf(string id) =>
        RedirectToPage(new { id, downloadPdf = true });

    /// <summary>Отдать файл подписи (App_Data → зеркало wwwroot).</summary>
    public IActionResult OnGetSignature(string id)
    {
        var document = _store.GetById(id);
        if (document is null)
        {
            return NotFound();
        }

        if (!_signatures.TryRead(document.SignatureImageUrl, document.Id, out var bytes, out var mime, out _))
        {
            return NotFound();
        }

        return File(bytes, mime);
    }

    public IActionResult OnPostSave(string id)
    {
        var document = _store.GetById(id);
        if (document is null)
        {
            NotFound = true;
            return Page();
        }

        var keys = Request.Form["overrideKey"];
        var values = Request.Form["overrideValue"];
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i].ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(key)) continue;
            document.PrintOverrides[key] = i < values.Count ? values[i].ToString() ?? string.Empty : string.Empty;
        }

        _store.Upsert(document);
        Message = "Изменения сохранены.";
        return LoadPage(id);
    }

    private IActionResult LoadPage(string id, bool downloadPdf = false)
    {
        var document = _store.GetById(id);
        if (document is null)
        {
            NotFound = true;
            return Page();
        }

        var changed = SanitizeSignatureUrl(document)
                      | EnsureMedicalOrg(document)
                      | EnsureHomeAddress(document)
                      | EnsureEmployerRequisites(document);
        if (changed)
        {
            _store.Upsert(document);
        }

        Id = id;
        Title = $"Направление на медосмотр — {document.LastName}";
        PdfFileName = BuildExportFileName(document, "pdf");
        AutoDownloadPdf = downloadPdf;
        // В HTML всегда data: URL (или пусто) — PDF/печать не зависят от /uploads/...
        PreviewHtml = MedDirectionPrintBuilder.Build(document, ResolveSignatureSrc(document));
        return Page();
    }

    private bool SanitizeSignatureUrl(MedDirectionRecord document)
    {
        var url = document.SignatureImageUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            // Попробовать найти файл по id направления.
            var recoveredEmpty = _signatures.FindPublicUrlByDirectionId(document.Id);
            if (!string.IsNullOrWhiteSpace(recoveredEmpty))
            {
                document.SignatureImageUrl = recoveredEmpty;
                return true;
            }

            return false;
        }

        var changed = false;
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var recovered = _signatures.FindPublicUrlByDirectionId(document.Id);
            document.SignatureImageUrl = recovered ?? string.Empty;
            return true;
        }

        var q = url.IndexOf('?', StringComparison.Ordinal);
        if (q >= 0)
        {
            document.SignatureImageUrl = url[..q];
            url = document.SignatureImageUrl;
            changed = true;
        }

        var normalized = MedDirectionSignatureStorage.NormalizeStoredUrl(url);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            var recovered = _signatures.FindPublicUrlByDirectionId(document.Id);
            document.SignatureImageUrl = recovered ?? string.Empty;
            return true;
        }

        // Файл есть в App_Data/wwwroot — убедимся, что URL канонический.
        if (_signatures.TryRead(normalized, document.Id, out _, out _, out var publicUrl)
            && !string.IsNullOrWhiteSpace(publicUrl)
            && !string.Equals(document.SignatureImageUrl, publicUrl, StringComparison.OrdinalIgnoreCase))
        {
            document.SignatureImageUrl = publicUrl;
            return true;
        }

        // Файла нет — очистить битую ссылку (иначе «сломанная картинка» в бланке).
        if (!_signatures.TryRead(normalized, document.Id, out _, out _, out _))
        {
            var recovered = _signatures.FindPublicUrlByDirectionId(document.Id);
            document.SignatureImageUrl = recovered ?? string.Empty;
            return true;
        }

        return changed;
    }

    private string? ResolveLogoDataUrl()
    {
        var path = Path.Combine(_env.WebRootPath, "img", "med-org-logo.png");
        if (!System.IO.File.Exists(path))
        {
            return null;
        }

        var bytes = System.IO.File.ReadAllBytes(path);
        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }

    private string? ResolveSignatureSrc(MedDirectionRecord document) =>
        _signatures.TryResolveDataUrl(document.SignatureImageUrl, document.Id);

    private static string BuildExportFileName(MedDirectionRecord document, string extension)
    {
        var fio = string.Join('_', new[] { document.LastName, document.FirstName }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        if (string.IsNullOrWhiteSpace(fio))
        {
            fio = "направление";
        }

        return $"Направление_{fio}_{document.Date:yyyyMMdd}.{extension}";
    }

    private bool EnsureMedicalOrg(MedDirectionRecord document)
    {
        var needsFill =
            string.IsNullOrWhiteSpace(document.MedicalOrgName) ||
            string.IsNullOrWhiteSpace(document.MedicalOrgAddress) ||
            string.IsNullOrWhiteSpace(document.Ogrn) ||
            string.IsNullOrWhiteSpace(document.Inn) ||
            string.IsNullOrWhiteSpace(document.LicenseInfo);

        if (!needsFill)
        {
            return false;
        }

        var list = _medicalOrganizations.LoadByOrganization(document.Organization);
        MedicalOrganizationRecord? mo = null;
        if (!string.IsNullOrWhiteSpace(document.MedicalOrgName))
        {
            mo = list.FirstOrDefault(x =>
                string.Equals(x.Name, document.MedicalOrgName, StringComparison.OrdinalIgnoreCase));
        }

        mo ??= list.FirstOrDefault(x => x.IsDefault) ?? list.FirstOrDefault();
        if (mo is null)
        {
            return false;
        }

        var changed = false;
        if (string.IsNullOrWhiteSpace(document.MedicalOrgName))
        {
            document.MedicalOrgName = mo.Name ?? string.Empty;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(document.MedicalOrgDetails))
        {
            document.MedicalOrgDetails = mo.DetailsSummary ?? string.Empty;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(document.MedicalOrgAddress))
        {
            document.MedicalOrgAddress = mo.Address ?? string.Empty;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(document.Ogrn))
        {
            document.Ogrn = mo.Ogrn ?? string.Empty;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(document.Inn))
        {
            document.Inn = mo.Inn ?? string.Empty;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(document.Kpp))
        {
            document.Kpp = mo.Kpp ?? string.Empty;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(document.LicenseInfo))
        {
            document.LicenseInfo = mo.LicenseInfo ?? string.Empty;
            changed = true;
        }

        return changed;
    }

    private bool EnsureHomeAddress(MedDirectionRecord document)
    {
        if (!string.IsNullOrWhiteSpace(document.HomeAddress))
        {
            return false;
        }

        var fullName = string.Join(' ',
            new[] { document.LastName, document.FirstName, document.MiddleName }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return false;
        }

        var employee = _employees.FindByName(document.Organization, fullName);
        if (employee is null || string.IsNullOrWhiteSpace(employee.HomeAddress))
        {
            return false;
        }

        document.HomeAddress = employee.HomeAddress.Trim();
        return true;
    }

    private bool EnsureEmployerRequisites(MedDirectionRecord document)
    {
        var org = (document.Organization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return false;
        }

        var requisites = _requisites.Get(org);
        var changed = false;
        if (string.IsNullOrWhiteSpace(document.EmployerContacts)
            && !string.IsNullOrWhiteSpace(requisites.EmployerContacts))
        {
            document.EmployerContacts = requisites.EmployerContacts;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(document.OwnershipOkved)
            && !string.IsNullOrWhiteSpace(requisites.OwnershipOkved))
        {
            document.OwnershipOkved = requisites.OwnershipOkved;
            changed = true;
        }

        return changed;
    }
}
