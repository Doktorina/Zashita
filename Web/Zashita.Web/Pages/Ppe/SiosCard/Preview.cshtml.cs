using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Ppe;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Ppe.SiosCard;

[RequireLogin]
public class PreviewModel : PageModel
{
    private readonly SavedSiosCardsStore _cards;
    private readonly OrganizationEmployeesStore _employees;

    public PreviewModel(SavedSiosCardsStore cards, OrganizationEmployeesStore employees)
    {
        _cards = cards;
        _employees = employees;
    }

    public IndexModel.WizardState State { get; private set; } = new();
    public string? Message { get; private set; }
    public bool IsSavedView { get; private set; }
    public bool ShowSizeForm { get; private set; }
    public bool IsDutyCard { get; private set; }
    public IReadOnlyList<string> Departments { get; private set; } = [];
    public string SizeChartsJson { get; private set; } = "{}";

    [BindProperty(SupportsGet = true)]
    public string? Id { get; set; }

    [BindProperty]
    public string CardNumber { get; set; } = "1";

    [BindProperty]
    public string EmployeeName { get; set; } = string.Empty;

    [BindProperty]
    public string Profession { get; set; } = string.Empty;

    [BindProperty]
    public string TabNumber { get; set; } = string.Empty;

    [BindProperty]
    public string Department { get; set; } = string.Empty;

    [BindProperty]
    public string ResponsiblePerson { get; set; } = string.Empty;

    [BindProperty]
    public string WorkplaceId { get; set; } = string.Empty;

    [BindProperty]
    public string NormsOrderText { get; set; } = string.Empty;

    [BindProperty]
    public DateTime HireDate { get; set; }

    [BindProperty]
    public DateTime? ProfessionChangeDate { get; set; }

    [BindProperty]
    public DateTime PpeIssueDate { get; set; }

    [BindProperty]
    public List<SiosPrintRowDto> PrintRows { get; set; } = [];

    [BindProperty]
    public string Sex { get; set; } = "муж.";

    [BindProperty]
    public string Height { get; set; } = string.Empty;

    [BindProperty]
    public string ClothesSize { get; set; } = string.Empty;

    [BindProperty]
    public string ShoeSize { get; set; } = string.Empty;

    [BindProperty]
    public string HeadwearSize { get; set; } = string.Empty;

    [BindProperty]
    public string GloveSize { get; set; } = string.Empty;

    [BindProperty]
    public string GasMaskSize { get; set; } = string.Empty;

    public string HireDateIso => HireDate == default ? string.Empty : HireDate.ToString("yyyy-MM-dd");
    public string ProfessionChangeDateIso => ProfessionChangeDate?.ToString("yyyy-MM-dd") ?? string.Empty;
    public string PpeIssueDateIso => (PpeIssueDate == default ? DateTime.Today : PpeIssueDate).ToString("yyyy-MM-dd");

    public string CardPreviewUrl { get; private set; } = string.Empty;

    public IActionResult OnGet()
    {
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;

        if (!string.IsNullOrWhiteSpace(Id))
        {
            var saved = _cards.GetById(Id);
            if (saved is null)
            {
                return RedirectToPage("/Ppe/SavedCards");
            }

            ApplySaved(saved);
            PrepareSizeForm();
            CardPreviewUrl = BuildCardPreviewUrl();
            PersistPreviewToSession();
            return Page();
        }

        State = LoadState();
        if (State.PrintRows.Count == 0)
        {
            return RedirectToPage("/Ppe/CardSelect");
        }

        IsDutyCard = PpeCardTypes.IsDutyCard(State.DocumentName);
        CardNumber = string.IsNullOrWhiteSpace(State.CardNumber)
            ? _cards.SuggestNextCardNumber(State.Organization, State.DocumentName).ToString()
            : State.CardNumber;
        PrintRows = State.PrintRows.Select(ClonePrintRow).ToList();
        PpeIssueDate = State.PpeIssueDate == default ? DateTime.Today : State.PpeIssueDate;
        ProfessionChangeDate = State.ProfessionChangeDate == default ? null : State.ProfessionChangeDate;
        HireDate = State.HireDate == default ? default : State.HireDate;
        EmployeeName = State.SelectedEmployees.FirstOrDefault() ?? string.Empty;
        Profession = State.Profession;
        ApplyEmployeeDefaults();
        TabNumber = State.TabNumber;
        Department = State.Department;
        ResponsiblePerson = State.ResponsiblePerson;
        WorkplaceId = State.WorkplaceId;
        NormsOrderText = State.NormsOrderText;
        ApplySizeFieldsFromState(State);
        if (!IsDutyCard && string.IsNullOrWhiteSpace(Height))
        {
            EnsureDefaultSizes();
        }

        PrepareSizeForm();
        PersistPreviewToSession();
        CardPreviewUrl = BuildCardPreviewUrl();
        return Page();
    }

    public IActionResult OnGetPrint()
    {
        PersistPreviewToSession();
        return RedirectToPage("/Ppe/SiosCard/Print", new { id = Id });
    }

    public IActionResult OnPostSave()
    {
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;

        var hire = HireDate == default ? default : HireDate.Date;
        var profChange = ProfessionChangeDate?.Date ?? default;
        var issueDate = PpeIssueDate == default ? DateTime.Today : PpeIssueDate.Date;
        var sizes = BuildSizes();
        EnsureStateForPost();
        ApplyPrintRowsToState();
        ApplyIdentityToState();

        if (!string.IsNullOrWhiteSpace(Id))
        {
            var existing = _cards.GetById(Id);
            if (existing is not null)
            {
                existing.CardNumber = CardNumber;
                existing.TabNumber = TabNumber;
                existing.Department = Department;
                existing.ResponsiblePerson = ResponsiblePerson;
                existing.WorkplaceId = WorkplaceId;
                existing.NormsOrderText = NormsOrderText;
                existing.HireDate = hire;
                existing.ProfessionChangeDate = profChange;
                existing.PpeIssueDate = issueDate;
                existing.Profession = string.IsNullOrWhiteSpace(Profession) ? existing.Profession : Profession.Trim();
                if (!string.IsNullOrWhiteSpace(EmployeeName))
                {
                    existing.Employees = [EmployeeName.Trim()];
                }

                existing.PrintRows = State.PrintRows.Select(ClonePrintRow).ToList();
                if (!PpeCardTypes.IsDutyCard(existing.DocumentName))
                {
                    ApplySizeToRecord(existing, sizes, Sex);
                }

                _cards.Upsert(existing);
                ApplySaved(existing);
                SyncSessionFromSaved(existing);
                Message = "Карточка обновлена.";
                PrepareSizeForm();
                CardPreviewUrl = BuildCardPreviewUrl();
                PersistPreviewToSession();
                return Page();
            }
        }

        State = LoadState();
        ApplyPrintRowsToState();
        ApplyIdentityToState();
        var record = new SavedSiosCardRecord
        {
            Organization = State.Organization,
            DocumentName = State.DocumentName,
            Profession = State.Profession,
            Employees = State.SelectedEmployees,
            PrintRows = State.PrintRows,
            CardNumber = CardNumber,
            TabNumber = TabNumber,
            Department = Department,
            ResponsiblePerson = ResponsiblePerson,
            WorkplaceId = WorkplaceId,
            NormsOrderText = NormsOrderText,
            HireDate = hire,
            ProfessionChangeDate = profChange,
            PpeIssueDate = issueDate,
            UseDsizSinglePageLayout = PpeCardTypes.IncludesDsiz(State.DocumentName)
        };
        if (!PpeCardTypes.IsDutyCard(State.DocumentName))
        {
            ApplySizeToRecord(record, sizes, Sex);
        }

        _cards.Upsert(record);
        ApplySaved(record);
        SyncSessionFromSaved(record);
        Message = "Карточка сохранена.";
        PrepareSizeForm();
        CardPreviewUrl = BuildCardPreviewUrl();
        PersistPreviewToSession();
        return Page();
    }

    public IActionResult OnPostRefreshPreview()
    {
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;

        EnsureStateForPost();
        if (State.PrintRows.Count == 0 && PrintRows.Count == 0)
        {
            return BadRequest();
        }

        IsDutyCard = PpeCardTypes.IsDutyCard(State.DocumentName);
        if (PpeIssueDate == default)
        {
            PpeIssueDate = DateTime.Today;
        }

        ApplyPrintRowsToState();
        ApplyIdentityToState();
        PersistPreviewToSession();
        return new OkResult();
    }

    public IActionResult OnPostPrint()
    {
        EnsureStateForPost();
        ApplyPrintRowsToState();
        ApplyIdentityToState();
        PersistPreviewToSession();
        return RedirectToPage("/Ppe/SiosCard/Print");
    }

    private void ApplySaved(SavedSiosCardRecord saved)
    {
        IsSavedView = true;
        Id = saved.Id;
        State = new IndexModel.WizardState
        {
            DocumentName = saved.DocumentName,
            Organization = saved.Organization,
            Profession = saved.Profession,
            SelectedEmployees = saved.Employees,
            PrintRows = saved.PrintRows.Select(ClonePrintRow).ToList()
        };
        PrintRows = State.PrintRows;
        IsDutyCard = PpeCardTypes.IsDutyCard(saved.DocumentName);
        CardNumber = saved.CardNumber;
        EmployeeName = saved.Employees.FirstOrDefault() ?? string.Empty;
        Profession = saved.Profession;
        TabNumber = saved.TabNumber;
        Department = saved.Department;
        ResponsiblePerson = saved.ResponsiblePerson;
        WorkplaceId = saved.WorkplaceId;
        NormsOrderText = saved.NormsOrderText;
        HireDate = saved.HireDate == default ? default : saved.HireDate;
        ProfessionChangeDate = saved.ProfessionChangeDate == default ? null : saved.ProfessionChangeDate;
        PpeIssueDate = saved.PpeIssueDate == default ? DateTime.Today : saved.PpeIssueDate;
        Sex = string.IsNullOrWhiteSpace(saved.Sex) ? "муж." : saved.Sex;
        Height = saved.Height;
        ClothesSize = saved.ClothesSize;
        ShoeSize = saved.ShoeSize;
        HeadwearSize = saved.HeadwearSize;
        GloveSize = saved.GloveSize;
        GasMaskSize = saved.GasMaskSize;
        if (!IsDutyCard && string.IsNullOrWhiteSpace(Height))
        {
            EnsureDefaultSizes();
        }
    }

    private void PrepareSizeForm()
    {
        IsDutyCard = PpeCardTypes.IsDutyCard(State.DocumentName);
        ShowSizeForm = !IsDutyCard && !PpeCardTypes.IncludesDsiz(State.DocumentName);
        SizeChartsJson = JsonSerializer.Serialize(new
        {
            male = PpeSizeReference.GetChart(false),
            female = PpeSizeReference.GetChart(true)
        });
        Departments = DepartmentSelect.Merge(
            DepartmentSelect.FromEmployees(_employees, State.Organization),
            string.IsNullOrWhiteSpace(Department) ? null : [Department]);
    }

    private void EnsureDefaultSizes()
    {
        var employee = State.SelectedEmployees.FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(Sex))
        {
            Sex = PpeWizardSizes.SexLabel(PpeSizeReference.InferredFemale(employee));
        }

        var defaults = PpeWizardSizes.Build(Sex, Height, ClothesSize, ShoeSize, HeadwearSize, GloveSize, GasMaskSize);
        Height = defaults.Height;
        ClothesSize = defaults.ClothesSize;
        ShoeSize = defaults.ShoeSize;
        HeadwearSize = defaults.HeadwearSize;
        GloveSize = defaults.GloveSize;
        GasMaskSize = defaults.GasMaskSize;
    }

    private EmployeePpeSizes BuildSizes() =>
        PpeWizardSizes.Build(Sex, Height, ClothesSize, ShoeSize, HeadwearSize, GloveSize, GasMaskSize);

    private static void ApplySizeToRecord(SavedSiosCardRecord record, EmployeePpeSizes sizes, string sex)
    {
        record.Sex = PpeWizardSizes.SexLabel(PpeWizardSizes.IsFemaleSex(sex));
        record.Height = sizes.Height;
        record.ClothesSize = sizes.ClothesSize;
        record.ShoeSize = sizes.ShoeSize;
        record.HeadwearSize = sizes.HeadwearSize;
        record.GloveSize = sizes.GloveSize;
        record.GasMaskSize = sizes.GasMaskSize;
        record.Size = sizes.UniformSize;
        record.SizesByEmployee = PpeWizardSizes.MapForEmployees(
            record.Employees,
            record.Sex,
            sizes.Height,
            sizes.ClothesSize,
            sizes.ShoeSize,
            sizes.HeadwearSize,
            sizes.GloveSize,
            sizes.GasMaskSize);
    }

    private void ApplySizeFieldsFromState(IndexModel.WizardState state)
    {
        Sex = state.Sex;
        Height = state.Height;
        ClothesSize = state.ClothesSize;
        ShoeSize = state.ShoeSize;
        HeadwearSize = state.HeadwearSize;
        GloveSize = state.GloveSize;
        GasMaskSize = state.GasMaskSize;
    }

    private void PersistPreviewToSession()
    {
        var state = LoadState();
        if (State.PrintRows.Count > 0)
        {
            state.DocumentName = State.DocumentName;
            state.Organization = State.Organization;
            state.Profession = State.Profession;
            state.SelectedEmployees = State.SelectedEmployees.ToList();
            state.PrintRows = State.PrintRows.Select(ClonePrintRow).ToList();
        }
        else if (PrintRows.Count > 0)
        {
            state.PrintRows = PrintRows.Select(ClonePrintRow).ToList();
        }

        ApplyIdentityToState();
        if (!string.IsNullOrWhiteSpace(Profession))
        {
            state.Profession = Profession.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(State.Profession))
        {
            state.Profession = State.Profession;
        }

        if (!string.IsNullOrWhiteSpace(EmployeeName))
        {
            state.SelectedEmployees = [EmployeeName.Trim()];
        }
        else if (State.SelectedEmployees.Count > 0)
        {
            state.SelectedEmployees = State.SelectedEmployees.ToList();
        }

        state.CardNumber = CardNumber;
        state.TabNumber = TabNumber;
        state.Department = Department;
        state.ResponsiblePerson = ResponsiblePerson;
        state.WorkplaceId = WorkplaceId;
        state.NormsOrderText = NormsOrderText;
        state.HireDate = HireDate == default ? default : HireDate.Date;
        state.ProfessionChangeDate = ProfessionChangeDate?.Date ?? default;
        state.PpeIssueDate = PpeIssueDate == default ? DateTime.Today : PpeIssueDate.Date;
        if (!PpeCardTypes.IsDutyCard(state.DocumentName))
        {
            var sizes = BuildSizes();
            state.Sex = Sex;
            state.Height = sizes.Height;
            state.ClothesSize = sizes.ClothesSize;
            state.ShoeSize = sizes.ShoeSize;
            state.HeadwearSize = sizes.HeadwearSize;
            state.GloveSize = sizes.GloveSize;
            state.GasMaskSize = sizes.GasMaskSize;
        }

        state.SavedCardId = string.IsNullOrWhiteSpace(Id) ? null : Id;
        SiosCardWizardSession.Save(HttpContext.Session, state);
    }

    private void EnsureStateForPost()
    {
        var loaded = LoadState();
        if (!string.IsNullOrWhiteSpace(Id))
        {
            var saved = _cards.GetById(Id);
            if (saved is not null && (loaded.PrintRows.Count == 0 || !string.Equals(loaded.SavedCardId, Id, StringComparison.Ordinal)))
            {
                State = new IndexModel.WizardState
                {
                    DocumentName = saved.DocumentName,
                    Organization = saved.Organization,
                    Profession = saved.Profession,
                    SelectedEmployees = saved.Employees.ToList(),
                    PrintRows = saved.PrintRows.Select(ClonePrintRow).ToList(),
                    CardNumber = saved.CardNumber,
                    TabNumber = saved.TabNumber,
                    Department = saved.Department,
                    ResponsiblePerson = saved.ResponsiblePerson,
                    WorkplaceId = saved.WorkplaceId,
                    NormsOrderText = saved.NormsOrderText,
                    HireDate = saved.HireDate,
                    ProfessionChangeDate = saved.ProfessionChangeDate,
                    PpeIssueDate = saved.PpeIssueDate,
                    Sex = saved.Sex,
                    Height = saved.Height,
                    ClothesSize = saved.ClothesSize,
                    ShoeSize = saved.ShoeSize,
                    HeadwearSize = saved.HeadwearSize,
                    GloveSize = saved.GloveSize,
                    GasMaskSize = saved.GasMaskSize,
                    SavedCardId = saved.Id
                };
                return;
            }
        }

        State = loaded;
        if (State.PrintRows.Count == 0 && PrintRows.Count > 0)
        {
            State.PrintRows = PrintRows.Select(ClonePrintRow).ToList();
        }
    }

    private void ApplyIdentityToState()
    {
        if (!string.IsNullOrWhiteSpace(Profession))
        {
            State.Profession = Profession.Trim();
        }

        if (!string.IsNullOrWhiteSpace(EmployeeName))
        {
            State.SelectedEmployees = [EmployeeName.Trim()];
        }
    }

    private string BuildCardPreviewUrl()
    {
        var baseUrl = Url.Page("/Ppe/SiosCard/Print", new { preview = true }) ?? "/Ppe/SiosCard/Print";
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?preview=true&";
        return $"{baseUrl}{separator}_={DateTime.UtcNow.Ticks}";
    }

    private void SyncSessionFromSaved(SavedSiosCardRecord saved)
    {
        var state = LoadState();
        state.DocumentName = saved.DocumentName;
        state.Organization = saved.Organization;
        state.Profession = saved.Profession;
        state.SelectedEmployees = saved.Employees.ToList();
        state.PrintRows = saved.PrintRows.Select(ClonePrintRow).ToList();
        state.CardNumber = saved.CardNumber;
        state.TabNumber = saved.TabNumber;
        state.Department = saved.Department;
        state.ResponsiblePerson = saved.ResponsiblePerson;
        state.WorkplaceId = saved.WorkplaceId;
        state.NormsOrderText = saved.NormsOrderText;
        state.HireDate = saved.HireDate;
        state.ProfessionChangeDate = saved.ProfessionChangeDate;
        state.PpeIssueDate = saved.PpeIssueDate;
        state.Sex = saved.Sex;
        state.Height = saved.Height;
        state.ClothesSize = saved.ClothesSize;
        state.ShoeSize = saved.ShoeSize;
        state.HeadwearSize = saved.HeadwearSize;
        state.GloveSize = saved.GloveSize;
        state.GasMaskSize = saved.GasMaskSize;
        state.SavedCardId = saved.Id;
        SiosCardWizardSession.Save(HttpContext.Session, state);
    }

    private void ApplyEmployeeDefaults()
    {
        var employeeName = State.SelectedEmployees.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(employeeName))
        {
            return;
        }

        var employee = _employees.Get(State.Organization, includeInactive: true)
            .FirstOrDefault(e => string.Equals(e.Name.Trim(), employeeName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (employee is null)
        {
            return;
        }

        if (HireDate == default && employee.EmploymentDate.HasValue)
        {
            HireDate = employee.EmploymentDate.Value.Date;
        }

        if (string.IsNullOrWhiteSpace(Department) && !string.IsNullOrWhiteSpace(employee.Department))
        {
            Department = employee.Department.Trim();
        }
    }

    private void ApplyPrintRowsToState()
    {
        if (PrintRows.Count == 0)
        {
            return;
        }

        State.PrintRows = PrintRows.Select(ClonePrintRow).ToList();
    }

    private static SiosPrintRowDto ClonePrintRow(SiosPrintRowDto row) => new()
    {
        Name = row.Name,
        Norm = row.Norm,
        NormPoint = row.NormPoint,
        IssueMethod = row.IssueMethod,
        ShelfLife = row.ShelfLife,
        ModelDescription = row.ModelDescription
    };

    private IndexModel.WizardState LoadState() => SiosCardWizardSession.Load(HttpContext.Session);
}
