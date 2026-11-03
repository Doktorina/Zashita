using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training;

/// <summary>
/// Пакетное редактирование записей проведения (из Планирования).
/// Отдельный журнал в меню больше не показывается.
/// </summary>
[RequireLogin]
public class ConductModel : PageModel
{
    private const string BatchTempKey = "ConductBatchJson";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    private readonly TrainingConductStore _store;
    private readonly OrganizationStore _orgs;

    public ConductModel(TrainingConductStore store, OrganizationStore orgs)
    {
        _store = store;
        _orgs = orgs;
    }

    [BindProperty]
    public List<TrainingConductRecord> Records { get; set; } = [];

    public string Organization { get; private set; } = string.Empty;
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public IReadOnlyList<string> ProtocolMovementOptions { get; } = ["готов", "на подписи", "—"];
    public IReadOnlyList<string> UpdStatusOptions { get; } = ["УПД есть", "УПД нет", "—"];

    public IActionResult OnGet()
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        Message = TempData["Message"] as string;

        if (TempData.Peek(BatchTempKey) is string json && !string.IsNullOrWhiteSpace(json))
        {
            Records = JsonSerializer.Deserialize<List<TrainingConductRecord>>(json, JsonOpts) ?? [];
            // Keep batch until successful save / cancel.
            TempData.Keep(BatchTempKey);
        }

        if (Records.Count == 0)
        {
            TempData["Message"] = "Выберите сотрудников в Планировании и нажмите «Отметить про проведение».";
            return RedirectToPage("/Training/Planning");
        }

        return Page();
    }

    public IActionResult OnPostSave()
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        Organization = _orgs.GetSelectedOrganization(HttpContext);

        if (Records is null || Records.Count == 0)
        {
            TempData["Message"] = "Нет записей для сохранения.";
            return RedirectToPage("/Training/Planning");
        }

        var saved = 0;
        foreach (var raw in Records)
        {
            if (string.IsNullOrWhiteSpace(raw.Person) || string.IsNullOrWhiteSpace(raw.Program))
            {
                continue;
            }

            var record = Normalize(raw, Organization);
            _store.Upsert(record);
            saved++;
        }

        TempData.Remove(BatchTempKey);
        if (saved == 0)
        {
            IsError = true;
            Message = "Укажите физ. лицо и программу хотя бы в одной форме.";
            return Page();
        }

        TempData["Message"] = $"Сохранено записей: {saved}. Данные обновлены в отчёте по обучению.";
        return RedirectToPage("/Training/Status");
    }

    public IActionResult OnPostDelete()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        var deleted = 0;
        foreach (var raw in Records ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw.Id))
            {
                continue;
            }

            _store.Delete(raw.Id);
            deleted++;
        }

        TempData.Remove(BatchTempKey);
        TempData["Message"] = deleted > 0
            ? $"Удалено записей: {deleted}."
            : "Нечего удалять — записи ещё не были сохранены.";
        return RedirectToPage("/Training/Planning");
    }

    public IActionResult OnPostCancel()
    {
        TempData.Remove(BatchTempKey);
        return RedirectToPage("/Training/Planning");
    }

    /// <summary>Вызывается из Планирования: кладёт пакет форм в TempData и редиректит сюда.</summary>
    public static void StoreBatch(ITempDataDictionary tempData, IReadOnlyList<TrainingConductRecord> records)
    {
        tempData[BatchTempKey] = JsonSerializer.Serialize(records, JsonOpts);
    }

    public static TrainingConductRecord BuildDraft(
        TrainingConductStore store,
        string organization,
        string person,
        string program,
        string department,
        string profession,
        string? conductId,
        string? trainingDateText,
        string? nextTrainingDateText,
        string? certificateNumber,
        string? comment,
        string? protocolMovement = null,
        string? updStatus = null)
    {
        TrainingConductRecord? existing = null;
        if (!string.IsNullOrWhiteSpace(conductId))
        {
            existing = store.LoadAll()
                .FirstOrDefault(r => string.Equals(r.Id, conductId, StringComparison.Ordinal));
        }

        existing ??= store.LoadAll().FirstOrDefault(r =>
            string.Equals(r.Organization, organization, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Person, person, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Program, program, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            if (string.IsNullOrWhiteSpace(existing.Department))
            {
                existing.Department = department;
            }

            if (string.IsNullOrWhiteSpace(existing.Profession))
            {
                existing.Profession = profession;
            }

            if (!string.IsNullOrWhiteSpace(protocolMovement))
            {
                existing.ProtocolMovement = TrainingConductValues.NormalizeProtocolMovement(protocolMovement);
            }

            if (!string.IsNullOrWhiteSpace(updStatus))
            {
                existing.UpdStatus = TrainingConductValues.NormalizeUpdStatus(updStatus);
            }

            return existing;
        }

        return new TrainingConductRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Organization = organization,
            Department = department?.Trim() ?? string.Empty,
            Profession = profession?.Trim() ?? string.Empty,
            Person = person?.Trim() ?? string.Empty,
            Program = program?.Trim() ?? string.Empty,
            TrainingType = "Обучение",
            Result = "Сдал",
            TrainingDate = ParseDate(trainingDateText) ?? DateTime.Today,
            NextTrainingDate = ParseDate(nextTrainingDateText),
            CertificateNumber = certificateNumber?.Trim() ?? string.Empty,
            ProtocolMovement = TrainingConductValues.NormalizeProtocolMovement(protocolMovement),
            UpdStatus = TrainingConductValues.NormalizeUpdStatus(updStatus),
            Comment = comment?.Trim() ?? string.Empty
        };
    }

    private static TrainingConductRecord Normalize(TrainingConductRecord raw, string fallbackOrg) =>
        new()
        {
            Id = string.IsNullOrWhiteSpace(raw.Id) ? Guid.NewGuid().ToString("N") : raw.Id.Trim(),
            Organization = string.IsNullOrWhiteSpace(raw.Organization) ? fallbackOrg : raw.Organization.Trim(),
            Department = raw.Department?.Trim() ?? string.Empty,
            Profession = raw.Profession?.Trim() ?? string.Empty,
            Person = raw.Person.Trim(),
            Program = raw.Program.Trim(),
            TrainingType = string.IsNullOrWhiteSpace(raw.TrainingType) ? "Обучение" : raw.TrainingType.Trim(),
            Result = string.IsNullOrWhiteSpace(raw.Result) ? "Сдал" : raw.Result.Trim(),
            TrainingDate = raw.TrainingDate,
            NextTrainingDate = raw.NextTrainingDate,
            ProtocolNumber = raw.ProtocolNumber?.Trim() ?? string.Empty,
            CertificateNumber = raw.CertificateNumber?.Trim() ?? string.Empty,
            ProtocolMovement = TrainingConductValues.NormalizeProtocolMovement(raw.ProtocolMovement),
            UpdStatus = TrainingConductValues.NormalizeUpdStatus(raw.UpdStatus),
            Comment = raw.Comment?.Trim() ?? string.Empty
        };

    private static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateTime.TryParseExact(text.Trim(), ["dd.MM.yyyy", "yyyy-MM-dd"], Ru, DateTimeStyles.None, out var d))
        {
            return d.Date;
        }

        return DateTime.TryParse(text, Ru, DateTimeStyles.None, out d) ? d.Date : null;
    }
}
