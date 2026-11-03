using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Excel;
using Zashita.Core.Security;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Data;

[RequireLogin]
public class KristinaUploadModel : PageModel
{
    private const long MaxUploadBytes = 20 * 1024 * 1024;
    private readonly KristinaProfessionsStore _kristina;
    private readonly IWebHostEnvironment _env;

    public KristinaUploadModel(KristinaProfessionsStore kristina, IWebHostEnvironment env)
    {
        _kristina = kristina;
        _env = env;
    }

    public string? Message { get; set; }
    public bool IsError { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(IFormFile? kristina, CancellationToken cancellationToken)
    {
        if (kristina is null || kristina.Length == 0)
        {
            IsError = true;
            Message = "Выберите файл Excel.";
            return Page();
        }

        if (kristina.Length > MaxUploadBytes)
        {
            IsError = true;
            Message = "Файл слишком большой (максимум 20 МБ).";
            return Page();
        }

        var name = Path.GetFileName(kristina.FileName);
        if (!name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
        {
            IsError = true;
            Message = "Поддерживаются только файлы .xlsx и .xls.";
            return Page();
        }

        var appData = Path.Combine(_env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(appData);
        var target = SecurePath.CombineUnder(appData, "kristina-professions.xlsx");

        try
        {
            // Сохраняем в фиксированное имя, чтобы KristinaProfessionsStore всегда находил файл.
            await using (var fs = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                await kristina.CopyToAsync(fs, cancellationToken);
            }

            // Сбрасываем кеш, чтобы новые данные начали работать сразу.
            _kristina.Invalidate();

            var tempWorkbook = new ExcelWorkbookStore();
            await using (var stream = kristina.OpenReadStream())
            {
                tempWorkbook.LoadFromStream(stream, kristina.FileName);
            }

            Message = $"Кристина обновлена: листов {tempWorkbook.SheetNames.Count}.";
        }
        catch (Exception ex)
        {
            IsError = true;
            Message = $"Ошибка загрузки: {ex.Message}";
        }

        return Page();
    }
}

