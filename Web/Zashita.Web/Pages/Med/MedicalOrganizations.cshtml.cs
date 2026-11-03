using System.Net.Mail;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Med;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med;

[RequireLogin]
public class MedicalOrganizationsModel : PageModel
{
    private readonly MedicalOrganizationStore _store;
    private readonly MedContextService _med;

    public MedicalOrganizationsModel(MedicalOrganizationStore store, MedContextService med)
    {
        _store = store;
        _med = med;
    }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<MedicalOrganizationRecord> Rows { get; private set; } = [];
    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? EditId { get; set; }

    [BindProperty]
    public string Name { get; set; } = string.Empty;

    [BindProperty] public string Address { get; set; } = string.Empty;

    [BindProperty]
    public string Email { get; set; } = string.Empty;
    [BindProperty] public string Phone { get; set; } = string.Empty;
    [BindProperty] public string Ogrn { get; set; } = string.Empty;
    [BindProperty] public string Inn { get; set; } = string.Empty;
    [BindProperty] public string Kpp { get; set; } = string.Empty;
    [BindProperty] public string LicenseInfo { get; set; } = string.Empty;
    [BindProperty] public string HeadPosition { get; set; } = string.Empty;
    [BindProperty] public string HeadName { get; set; } = string.Empty;
    [BindProperty] public bool IsDefault { get; set; }

    public void OnGet()
    {
        LoadPage();
        Message = TempData["Message"] as string;
        IsError = string.Equals(TempData["IsError"] as string, "1", StringComparison.Ordinal);
    }

    public IActionResult OnPostSave()
    {
        Organization = _med.GetOrganization(HttpContext);
        Name = (Name ?? string.Empty).Trim();
        Address = (Address ?? string.Empty).Trim();
        Email = (Email ?? string.Empty).Trim();
        Phone = (Phone ?? string.Empty).Trim();
        Ogrn = (Ogrn ?? string.Empty).Trim();
        Inn = (Inn ?? string.Empty).Trim();
        Kpp = (Kpp ?? string.Empty).Trim();
        LicenseInfo = (LicenseInfo ?? string.Empty).Trim();
        HeadPosition = (HeadPosition ?? string.Empty).Trim();
        HeadName = (HeadName ?? string.Empty).Trim();

        if (!ValidateForm())
        {
            IsError = true;
            Message = "Исправьте ошибки в форме.";
            LoadPage();
            return Page();
        }

        var id = string.IsNullOrWhiteSpace(EditId) ? Guid.NewGuid().ToString("N") : EditId!;
        try
        {
            _store.Upsert(new MedicalOrganizationRecord
            {
                Id = id,
                Organization = string.Empty,
                Name = Name,
                Address = Address,
                Email = Email,
                Phone = Phone,
                Ogrn = Ogrn,
                Inn = Inn,
                Kpp = Kpp,
                LicenseInfo = LicenseInfo,
                HeadPosition = HeadPosition,
                HeadName = HeadName,
                IsDefault = IsDefault
            });
        }
        catch (Exception ex)
        {
            IsError = true;
            Message = $"Не удалось сохранить: {ex.Message}";
            LoadPage();
            return Page();
        }

        TempData["Message"] = "Медицинская организация сохранена.";
        return RedirectToPage(new { editId = id });
    }

    public IActionResult OnPostDelete(string deleteId)
    {
        if (!string.IsNullOrWhiteSpace(deleteId))
        {
            _store.Delete(deleteId);
            TempData["Message"] = "Запись удалена.";
        }

        return RedirectToPage();
    }

    private bool ValidateForm()
    {
        ModelState.Clear();

        if (string.IsNullOrWhiteSpace(Name))
        {
            ModelState.AddModelError(nameof(Name), "Укажите наименование медицинской организации.");
        }

        if (!string.IsNullOrWhiteSpace(Email) && !IsValidEmail(Email))
        {
            ModelState.AddModelError(nameof(Email), "Укажите корректный e-mail или оставьте поле пустым.");
        }

        return ModelState.IsValid;
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var parsed = new MailAddress(email.Trim());
            return parsed.Address.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void LoadPage()
    {
        Organization = _med.GetOrganization(HttpContext);
        ViewData["ActiveModule"] = "медосмотры";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Медицинские организации";
        ViewData["Subtitle"] = Organization;
        Rows = _store.LoadByOrganization(Organization);

        if (!string.IsNullOrWhiteSpace(EditId) &&
            string.IsNullOrWhiteSpace(Name) &&
            !IsError)
        {
            var row = _store.GetById(EditId);
            if (row is not null)
            {
                Name = row.Name;
                Address = row.Address;
                Email = row.Email;
                Phone = row.Phone;
                Ogrn = row.Ogrn;
                Inn = row.Inn;
                Kpp = row.Kpp;
                LicenseInfo = row.LicenseInfo;
                HeadPosition = row.HeadPosition;
                HeadName = row.HeadName;
                IsDefault = row.IsDefault;
            }
        }
    }
}
