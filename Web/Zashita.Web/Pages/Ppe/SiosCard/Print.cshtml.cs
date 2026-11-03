using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Ppe;
using Zashita.Core.PrintHistory;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Ppe.SiosCard;

[RequireLogin]
public class PrintModel : PageModel
{
    private readonly SavedSiosCardsStore _cards;
    private readonly DocumentPrintHistoryStore _printHistory;

    public PrintModel(SavedSiosCardsStore cards, DocumentPrintHistoryStore printHistory)
    {
        _cards = cards;
        _printHistory = printHistory;
    }

    public string Html { get; private set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public bool Preview { get; set; }

    public IActionResult OnGet(string? id, bool preview = false)
    {
        Preview = preview;
        SiosCardPrintContext context;

        if (!string.IsNullOrWhiteSpace(id))
        {
            var saved = _cards.GetById(id);
            if (saved is null)
            {
                return RedirectToPage("/Ppe/SavedCards");
            }

            context = SiosCardRowMapper.FromSaved(saved);
        }
        else
        {
            var state = SiosCardWizardSession.Load(HttpContext.Session);
            if (state is null || state.PrintRows.Count == 0)
            {
                return RedirectToPage("/Ppe/CardSelect");
            }

            var isDsiz = PpeCardTypes.IncludesDsiz(state.DocumentName);
            var sizes = PpeWizardSizes.Build(
                state.Sex,
                state.Height,
                state.ClothesSize,
                state.ShoeSize,
                state.HeadwearSize,
                state.GloveSize,
                state.GasMaskSize);

            context = new SiosCardPrintContext
            {
                DocumentName = state.DocumentName,
                Organization = state.Organization,
                Profession = state.Profession,
                CardNumber = state.CardNumber,
                Department = state.Department,
                ResponsiblePerson = state.ResponsiblePerson,
                WorkplaceId = state.WorkplaceId,
                NormsOrderText = state.NormsOrderText,
                HireDate = state.HireDate == default ? default : state.HireDate,
                ProfessionChangeDate = state.ProfessionChangeDate == default ? default : state.ProfessionChangeDate,
                PpeIssueDate = state.PpeIssueDate == default ? DateTime.Today : state.PpeIssueDate,
                TabNumber = state.TabNumber,
                Employees = state.SelectedEmployees,
                Rows = SiosCardRowMapper.FromPrintRows(state.PrintRows, isDsiz),
                SizesByEmployee = PpeWizardSizes.MapForEmployees(
                    state.SelectedEmployees,
                    state.Sex,
                    sizes.Height,
                    sizes.ClothesSize,
                    sizes.ShoeSize,
                    sizes.HeadwearSize,
                    sizes.GloveSize,
                    sizes.GasMaskSize),
                Sex = string.IsNullOrWhiteSpace(state.Sex) ? null : state.Sex.Trim(),
                UseDsizSinglePageLayout = isDsiz
            };
        }

        context.EditablePreview = Preview;
        Html = SiosCardPrintBuilder.BuildHtmlDocument(context, autoPrint: !Preview);
        if (!Preview)
        {
            _printHistory.Append(
                category: "ppe",
                title: context.DocumentName,
                organization: context.Organization,
                detail: string.IsNullOrWhiteSpace(context.CardNumber)
                    ? context.Profession
                    : $"№ {context.CardNumber} · {context.Profession}",
                summary: "Печать карточки СИЗ",
                openUrl: string.IsNullOrWhiteSpace(id)
                    ? "/Ppe/SavedCards"
                    : $"/Ppe/SiosCard/Preview?id={Uri.EscapeDataString(id)}");
        }

        return Page();
    }
}
