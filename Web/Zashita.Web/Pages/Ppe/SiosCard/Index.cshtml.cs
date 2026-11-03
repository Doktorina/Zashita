using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Ppe;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Ppe.SiosCard;

[RequireLogin]
public class IndexModel : PageModel
{
    private readonly OrganizationStore _orgs;
    private readonly PpeEmployeesService _employees;
    private readonly IssueStandardsStore _standards;
    private readonly PpeNomenclatureStore _nomenclature;
    private readonly Appendix1NormsStore _appendix1;
    private readonly SavedSiosCardsStore _cards;

    public IndexModel(
        OrganizationStore orgs,
        PpeEmployeesService employees,
        IssueStandardsStore standards,
        PpeNomenclatureStore nomenclature,
        Appendix1NormsStore appendix1,
        SavedSiosCardsStore cards)
    {
        _orgs = orgs;
        _employees = employees;
        _standards = standards;
        _nomenclature = nomenclature;
        _appendix1 = appendix1;
        _cards = cards;
    }

    [BindProperty(SupportsGet = true)]
    public string? Doc { get; set; }

    [BindProperty]
    public string? Profession { get; set; }

    [BindProperty]
    public List<string> SelectedEmployees { get; set; } = [];

    [BindProperty]
    public string? StandardId { get; set; }

    [BindProperty]
    public List<string> SelectedItems { get; set; } = [];

    [BindProperty(SupportsGet = true)]
    public int Step { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? StandardIdFromCreate { get; set; }

    public string DocumentName { get; private set; } = PpeCardTypes.PersonalSiz;
    public string Organization { get; private set; } = string.Empty;
    public bool IsDutyCard { get; private set; }
    public IReadOnlyDictionary<string, List<string>> Professions { get; private set; } = new Dictionary<string, List<string>>();
    public IReadOnlyList<IssueStandard> Templates { get; private set; } = [];
    public IReadOnlyList<string> EmployeesForProfession { get; private set; } = [];
    public IReadOnlyList<PpePickItem> PickItems { get; private set; } = [];
    public string? Message { get; private set; }

    public IActionResult OnGet()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        DocumentName = string.IsNullOrWhiteSpace(Doc) ? PpeCardTypes.PersonalSiz : Doc;
        IsDutyCard = PpeCardTypes.IsDutyCard(DocumentName);
        Professions = _employees.GetByProfession(Organization);
        LoadStep();

        if (!string.IsNullOrWhiteSpace(StandardIdFromCreate) && Step == 3 && !IsDutyCard)
        {
            var preview = TryOpenPreview();
            if (preview is not null)
            {
                return preview;
            }
        }

        return Page();
    }

    public IActionResult OnPostNext()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        DocumentName = string.IsNullOrWhiteSpace(Doc) ? PpeCardTypes.PersonalSiz : Doc;
        IsDutyCard = PpeCardTypes.IsDutyCard(DocumentName);
        Professions = _employees.GetByProfession(Organization);
        if (Step < 1) Step = 1;
        PrepareViewData();
        var state = LoadState();
        state.DocumentName = DocumentName;
        state.Organization = Organization;

        if (Step == 1)
        {
            if (string.IsNullOrWhiteSpace(Profession))
            {
                Message = "Выберите профессию.";
                return Page();
            }
            state.Profession = Profession;
            state.Step = 2;
            SaveState(state);
            return RedirectToPage(new { doc = DocumentName, step = 2 });
        }

        if (Step == 2)
        {
            state.SelectedEmployees = SelectedEmployees.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
            if (state.SelectedEmployees.Count == 0)
            {
                Message = "Выберите хотя бы одного сотрудника.";
                return Page();
            }
            state.Step = 3;
            SaveState(state);
            return RedirectToPage(new { doc = DocumentName, step = 3 });
        }

        if (Step == 3)
        {
            if (IsDutyCard)
            {
                var items = SelectedItems.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (items.Count == 0)
                {
                    Message = "Отметьте хотя бы одну позицию для карточки.";
                    return Page();
                }

                var picks = PickItems.Where(p => items.Contains(p.Name, StringComparer.OrdinalIgnoreCase)).ToList();
                state.PrintRows = picks.Select(p =>
                {
                    var row = PpeWizardCatalog.ToPrintRow(p, PpeCardTypes.IncludesDsiz(DocumentName));
                    row.IssueMethod = "Дежурная";
                    if (string.IsNullOrWhiteSpace(row.ShelfLife))
                    {
                        row.ShelfLife = PpeNormPointResolver.DeriveShelfLife(row.Norm, isDsiz: false);
                    }

                    return row;
                }).ToList();
                state.CardNumber = _cards.SuggestNextCardNumber(state.Organization, state.DocumentName).ToString();
                SiosCardWizardSession.ClearPreviewFields(state);
                SaveState(state);
                return RedirectToPage("/Ppe/SiosCard/Preview");
            }

            var standardId = !string.IsNullOrWhiteSpace(StandardId)
                ? StandardId
                : state.StandardId;
            var standard = _standards.LoadAll().FirstOrDefault(s => s.Id == standardId);
            if (standard is null)
            {
                Message = "Выберите шаблон выдачи или создайте новый.";
                return Page();
            }

            if (!TryFillPrintRowsFromStandard(standard, state, out var fillError))
            {
                Message = fillError;
                return Page();
            }

            SaveState(state);
            return RedirectToPage("/Ppe/SiosCard/Preview");
        }

        return Page();
    }

    private IActionResult? TryOpenPreview()
    {
        var state = LoadState();
        state.DocumentName = DocumentName;
        state.Organization = Organization;

        if (string.IsNullOrWhiteSpace(StandardId))
        {
            return null;
        }

        if (state.SelectedEmployees.Count == 0)
        {
            Message = "Сначала выберите сотрудников на шаге 2.";
            return null;
        }

        var standard = _standards.LoadAll().FirstOrDefault(s => s.Id == StandardId);
        if (standard is null)
        {
            Message = "Созданный шаблон не найден. Выберите шаблон вручную.";
            return null;
        }

        if (!TryFillPrintRowsFromStandard(standard, state, out var fillError))
        {
            Message = fillError;
            return null;
        }

        SaveState(state);
        return RedirectToPage("/Ppe/SiosCard/Preview");
    }

    private bool TryFillPrintRowsFromStandard(IssueStandard standard, WizardState state, out string? error)
    {
        error = null;
        if (standard.Items.Count == 0)
        {
            error = "В выбранном шаблоне нет позиций СИЗ. Добавьте позиции в шаблоне выдачи.";
            return false;
        }

        state.StandardId = standard.Id;
        var isDsiz = PpeCardTypes.IncludesDsiz(state.DocumentName);
        var nomenclature = _nomenclature.LoadForEditor(_standards.LoadAll());
        var appendixEntries = _appendix1.LoadAll();
        var profession = !string.IsNullOrWhiteSpace(standard.Profession) ? standard.Profession : state.Profession;
        state.PrintRows = standard.Items.Select(name =>
        {
            var norm = PpeNormPointResolver.ResolveNorm(name, profession, nomenclature, appendixEntries);
            return new SiosPrintRowDto
            {
                Name = name,
                Norm = norm,
                NormPoint = PpeNormPointResolver.ResolveFromNomenclature(name, nomenclature),
                ModelDescription = PpeNormPointResolver.ResolveModelDescription(name, nomenclature),
                IssueMethod = isDsiz ? "Дозирующая выдача средств" : "Персонализированная",
                ShelfLife = PpeNormPointResolver.DeriveShelfLife(norm, isDsiz)
            };
        }).ToList();
        state.CardNumber = _cards.SuggestNextCardNumber(state.Organization, state.DocumentName).ToString();
        SiosCardWizardSession.ClearPreviewFields(state);
        return true;
    }

    public IActionResult OnPostBack()
    {
        var state = LoadState();
        state.Step = Math.Max(1, state.Step - 1);
        SaveState(state);
        return RedirectToPage(new { doc = state.DocumentName, step = state.Step });
    }

    private void LoadStep()
    {
        var state = LoadState();
        if (int.TryParse(Request.Query["step"], out var s)) Step = s;
        else if (Step < 1) Step = state.Step;
        if (Step < 1) Step = 1;
        if (Step > 3) Step = 3;
        state.Step = Step;
        Profession ??= state.Profession;
        SelectedEmployees = state.SelectedEmployees.Count > 0 ? state.SelectedEmployees : SelectedEmployees;
        if (!string.IsNullOrWhiteSpace(StandardIdFromCreate))
        {
            StandardId = StandardIdFromCreate;
            state.StandardId = StandardIdFromCreate;
        }
        else
        {
            StandardId ??= state.StandardId;
        }

        SelectedItems = state.SelectedItems.Count > 0 ? state.SelectedItems : SelectedItems;
        PrepareViewData();
        SaveState(state);
    }

    private void PrepareViewData()
    {
        if (!string.IsNullOrWhiteSpace(Profession))
        {
            EmployeesForProfession = _employees.GetEmployeesForProfession(Organization, Profession);
        }

        PickItems = PpeWizardCatalog.GetPickItems(DocumentName, _nomenclature.LoadForEditor(_standards.LoadAll()));

        if (IsDutyCard)
        {
            Templates = [];
            return;
        }

        var includeSiz = PpeCardTypes.IncludesSiz(DocumentName);
        var includeDsiz = PpeCardTypes.IncludesDsiz(DocumentName);
        Templates = _standards.LoadAll()
            .Where(t => string.Equals(t.Organization.Trim(), Organization, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(t.Organization)
                || OrganizationMatcher.Matches(Organization, t.Organization))
            .Where(t => Step < 3
                || string.IsNullOrWhiteSpace(Profession)
                || string.Equals(t.Profession.Trim(), Profession, StringComparison.OrdinalIgnoreCase)
                || t.Profession.Contains(Profession ?? "", StringComparison.OrdinalIgnoreCase)
                || (Profession ?? "").Contains(t.Profession.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(t => (includeSiz && IssueStandard.IncludesSiz(t.Category)) || (includeDsiz && IssueStandard.IncludesDsiz(t.Category)))
            .OrderByDescending(t => t.CreatedAt)
            .ToList();

        // Если по профессии шаблонов нет — показать все шаблоны организации.
        if (Step == 3 && Templates.Count == 0 && !string.IsNullOrWhiteSpace(Profession))
        {
            Templates = _standards.LoadAll()
                .Where(t => string.Equals(t.Organization.Trim(), Organization, StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(t.Organization)
                    || OrganizationMatcher.Matches(Organization, t.Organization))
                .Where(t => (includeSiz && IssueStandard.IncludesSiz(t.Category)) || (includeDsiz && IssueStandard.IncludesDsiz(t.Category)))
                .OrderByDescending(t => t.CreatedAt)
                .ToList();
        }

        if (Step == 3 && !string.IsNullOrWhiteSpace(StandardId))
        {
            var selected = _standards.LoadAll().FirstOrDefault(s => s.Id == StandardId);
            if (selected is not null && Templates.All(t => t.Id != selected.Id))
            {
                Templates = [selected, ..Templates];
            }
        }

        if (Step == 3 && !string.IsNullOrWhiteSpace(StandardId))
        {
            var std = Templates.FirstOrDefault(t => t.Id == StandardId);
            if (std is not null && SelectedItems.Count == 0)
            {
                SelectedItems = std.Items.ToList();
            }
        }
    }

    private WizardState LoadState() => SiosCardWizardSession.Load(HttpContext.Session);

    private void SaveState(WizardState state) => SiosCardWizardSession.Save(HttpContext.Session, state);

    public sealed class WizardState
    {
        public int Step { get; set; } = 1;
        public string DocumentName { get; set; } = PpeCardTypes.PersonalSiz;
        public string Organization { get; set; } = string.Empty;
        public string Profession { get; set; } = string.Empty;
        public List<string> SelectedEmployees { get; set; } = [];
        public string? StandardId { get; set; }
        public List<string> SelectedItems { get; set; } = [];
        public List<SiosPrintRowDto> PrintRows { get; set; } = [];
        public string CardNumber { get; set; } = "1";
        public string TabNumber { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string ResponsiblePerson { get; set; } = string.Empty;
        public string WorkplaceId { get; set; } = string.Empty;
        public string NormsOrderText { get; set; } = string.Empty;
        public DateTime HireDate { get; set; }
        public DateTime ProfessionChangeDate { get; set; }
        public DateTime PpeIssueDate { get; set; }
        public string Sex { get; set; } = "муж.";
        public string Height { get; set; } = string.Empty;
        public string ClothesSize { get; set; } = string.Empty;
        public string ShoeSize { get; set; } = string.Empty;
        public string HeadwearSize { get; set; } = string.Empty;
        public string GloveSize { get; set; } = string.Empty;
        public string GasMaskSize { get; set; } = string.Empty;
        public string? SavedCardId { get; set; }
    }
}
