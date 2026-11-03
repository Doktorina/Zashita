using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Zashita.Core.Employees;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training;

[RequireLogin]
public class ApplicationsModel : PageModel
{
    private readonly TrainingApplicationEditor _editor;
    private readonly TrainingPlanningStore _store;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationRequisitesStore _requisites;
    private readonly OrganizationEmployeesService _employees;

    public ApplicationsModel(
        TrainingApplicationEditor editor,
        TrainingPlanningStore store,
        OrganizationStore orgs,
        OrganizationRequisitesStore requisites,
        OrganizationEmployeesService employees)
    {
        _editor = editor;
        _store = store;
        _orgs = orgs;
        _requisites = requisites;
        _employees = employees;
    }

    [BindProperty(SupportsGet = true)]
    public string? EditId { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Create { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PrintId { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<TrainingPlanningRecord> Rows { get; private set; } = [];
    public IReadOnlyList<string> Departments { get; private set; } = [];
    public IReadOnlyList<string> Sections { get; private set; } = [];
    public IReadOnlyList<string> ProgramOptions { get; private set; } = [];
    public IReadOnlyList<string> KindOptions { get; private set; } = [];
    public IReadOnlyDictionary<string, string> KindProgramLookup { get; private set; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public TrainingPlanningRecord EditRecord { get; private set; } = new();
    public IReadOnlyList<OrganizationEmployee> OrganizationEmployees { get; private set; } = [];
    public TrainingPlanningRecord? SelectedPrintRecord =>
        string.IsNullOrWhiteSpace(PrintId) ? null : _store.GetById(PrintId);
    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    public void OnGet()
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        Message = TempData["Message"] as string;
        LoadPage();
    }

    public IActionResult OnGetFromNeed(string? person, string? program, string? department, string? profession)
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        Create = true;
        LoadPage();

        var employee = TrainingApplicationEditor.FindEmployee(OrganizationEmployees, person, department, profession);
        var kind = program?.Trim() ?? string.Empty;
        var mappedProgram = _editor.ResolveProgramForKind(kind);
        EditRecord.Department = department?.Trim() ?? string.Empty;
        EditRecord.TrainingKind = kind;
        EditRecord.Content = !string.IsNullOrWhiteSpace(mappedProgram)
            ? $"Планирование: {mappedProgram}"
            : $"Планирование: {kind}".Trim().TrimEnd(':');
        EditRecord.Participants =
        [
            TrainingApplicationEditor.BuildParticipantFromEmployee(
                employee,
                person,
                profession,
                !string.IsNullOrWhiteSpace(mappedProgram) ? mappedProgram : kind)
        ];

        return Page();
    }

    public IActionResult OnGetDownload(string id, string? mode)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return NotFound();
        }

        var record = _store.GetById(id);
        if (record is null)
        {
            return NotFound();
        }

        var printMode = TrainingApplicationPrintModes.Normalize(mode);
        EnrichForPrint(record);
        var bodyHtml = TrainingApplicationPrintBuilder.Build(record, printMode)
            .Replace("training-app-edit-hint", "training-app-edit-hint ppe-no-print", StringComparison.Ordinal);
        // Убираем подсказку правки из Word.
        bodyHtml = System.Text.RegularExpressions.Regex.Replace(
            bodyHtml,
            @"<p[^>]*class=""[^""]*training-app-edit-hint[^""]*""[^>]*>[\s\S]*?</p>",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var baseName = TrainingApplicationDownloadFileName.Build(record);
        var fileName = baseName + ".doc";

        // Стили встраиваем целиком: Word не подгружает /css/... из .doc.
        // <title> = имя файла: Word «Сохранить как» берёт его, если браузер открыл документ напрямую.
        var document =
            "<!DOCTYPE html><html xmlns:o=\"urn:schemas-microsoft-com:office:office\" " +
            "xmlns:w=\"urn:schemas-microsoft-com:office:word\" lang=\"ru\"><head>" +
            "<meta charset=\"utf-8\" />" +
            "<meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\" />" +
            $"<title>{System.Net.WebUtility.HtmlEncode(baseName)}</title>" +
            "<!--[if gte mso 9]><xml><w:WordDocument><w:View>Print</w:View>" +
            "<w:Zoom>100</w:Zoom><w:DoNotOptimizeForBrowser/></w:WordDocument></xml><![endif]-->" +
            "<style>" + TrainingApplicationWordExportCss.LandscapeDoc + "</style></head><body>" +
            $"<div class=\"Section1\">{bodyHtml}</div></body></html>";

        var bytes = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(document))
            .ToArray();

        // filename= только ASCII (иначе заголовок ломается), кириллица — в filename*=UTF-8''.
        // Атрибут download на ссылке «Сохранить» дублирует то же имя для Chrome.
        var asciiName = TrainingApplicationDownloadFileName.AsciiFallback(fileName);
        Response.Headers.ContentDisposition =
            "attachment; filename=\"" + asciiName + "\"; filename*=UTF-8''" + Uri.EscapeDataString(fileName);
        return File(bytes, "application/msword");
    }

    private void EnrichForPrint(TrainingPlanningRecord record) =>
        TrainingApplicationPrintEnrichment.Apply(record, _requisites, _employees);

    public IActionResult OnPostSave(
        string? editId,
        DateTime? documentDate,
        string? number,
        string? organization,
        string? department,
        string? section,
        DateTime? executionDate,
        string? trainingKind,
        string? content,
        string? organizationDetails,
        string? objectNote,
        string? contactPersonName,
        string? contactPersonPhone,
        string? contactPersonEmail,
        string? contactPersonPosition,
        string? recipientOrganization,
        string? recipientName,
        string[]? participantFullName,
        string[]? participantPosition,
        string[]? participantBirthDate,
        string[]? participantEmploymentDate,
        string[]? participantSnils,
        string[]? participantTrainingDirection,
        string[]? agreementFullName,
        string[]? agreementPosition)
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        Organization = _orgs.GetSelectedOrganization(HttpContext);

        if (string.IsNullOrWhiteSpace(trainingKind))
        {
            IsError = true;
            Message = "Укажите вид обучения, инструктажа или проверки знаний.";
            EditId = editId;
            LoadPage();
            EditRecord = _editor.BuildRecord(
                Organization,
                editId,
                documentDate,
                number,
                organization,
                department,
                section,
                executionDate,
                trainingKind,
                content,
                organizationDetails,
                objectNote,
                contactPersonName,
                contactPersonPhone,
                contactPersonEmail,
                contactPersonPosition,
                recipientOrganization,
                recipientName,
                participantFullName,
                participantPosition,
                participantBirthDate,
                participantEmploymentDate,
                participantSnils,
                participantTrainingDirection,
                agreementFullName,
                agreementPosition);
            return Page();
        }

        var record = _editor.BuildRecord(
            Organization,
            editId,
            documentDate,
            number,
            organization,
            department,
            section,
            executionDate,
            trainingKind,
            content,
            organizationDetails,
            objectNote,
            contactPersonName,
            contactPersonPhone,
            contactPersonEmail,
            contactPersonPosition,
            recipientOrganization,
            recipientName,
            participantFullName,
            participantPosition,
            participantBirthDate,
            participantEmploymentDate,
            participantSnils,
            participantTrainingDirection,
            agreementFullName,
            agreementPosition);
        TrainingApplicationPrintEnrichment.ApplyToForm(record, _requisites);

        _editor.Save(record);
        TempData["Message"] = "Заявка сохранена.";
        return RedirectToPage(ListRoute(printId: record.Id, editId: record.Id));
    }

    public IActionResult OnPostDelete(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            _editor.Delete(id);
            TempData["Message"] = "Заявка удалена.";
        }

        return RedirectToPage(ListRoute());
    }

    public RouteValueDictionary ListRoute(string? printId = null, string? editId = null) =>
        new()
        {
            ["q"] = Q,
            ["printId"] = printId ?? PrintId,
            ["editId"] = editId ?? EditId
        };

    private void LoadPage()
    {
        if (string.IsNullOrWhiteSpace(Organization))
        {
            Organization = _orgs.GetSelectedOrganization(HttpContext);
        }

        var hasEditTarget = !string.IsNullOrWhiteSpace(EditId)
            && !string.Equals(EditId, "new", StringComparison.OrdinalIgnoreCase);
        var wantsNew = !hasEditTarget
            && (Create
                || string.IsNullOrWhiteSpace(EditId)
                || string.Equals(EditId, "new", StringComparison.OrdinalIgnoreCase));
        var context = _editor.LoadContext(Organization, EditId, wantsNew, Q);
        Rows = context.Rows;
        EditRecord = context.EditRecord;
        TrainingApplicationPrintEnrichment.ApplyToForm(EditRecord, _requisites);
        EditId = context.EditId;
        Departments = context.Departments;
        Sections = context.Sections;
        ProgramOptions = context.ProgramOptions;
        KindOptions = context.KindOptions;
        KindProgramLookup = context.KindProgramLookup;
        OrganizationEmployees = context.OrganizationEmployees;
    }
}
