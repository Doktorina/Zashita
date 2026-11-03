using System.Text.Json;
using Zashita.Web.Pages.Ppe.SiosCard;

namespace Zashita.Web.Services;

public static class SiosCardWizardSession
{
    public const string SessionKey = "Ppe.SiosCard.Wizard";

    public static IndexModel.WizardState Load(ISession session)
    {
        var json = session.GetString(SessionKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new IndexModel.WizardState();
        }

        return JsonSerializer.Deserialize<IndexModel.WizardState>(json) ?? new IndexModel.WizardState();
    }

    public static void Save(ISession session, IndexModel.WizardState state) =>
        session.SetString(SessionKey, JsonSerializer.Serialize(state));

    public static void Clear(ISession session) => session.Remove(SessionKey);

    /// <summary>Сбрасывает поля предпросмотра/печати — не трогает шаги мастера и строки норм.</summary>
    public static void ClearPreviewFields(IndexModel.WizardState state)
    {
        state.TabNumber = string.Empty;
        state.Department = string.Empty;
        state.ResponsiblePerson = string.Empty;
        state.WorkplaceId = string.Empty;
        state.NormsOrderText = string.Empty;
        state.HireDate = default;
        state.ProfessionChangeDate = default;
        state.PpeIssueDate = default;
        state.Sex = "муж.";
        state.Height = string.Empty;
        state.ClothesSize = string.Empty;
        state.ShoeSize = string.Empty;
        state.HeadwearSize = string.Empty;
        state.GloveSize = string.Empty;
        state.GasMaskSize = string.Empty;
        state.SavedCardId = null;
    }
}
