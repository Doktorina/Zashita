namespace Zashita.Core.Ppe;

public static class PpeCardTypes
{
    public const string PersonalSiz = "Личная карточка учета выдачи СИЗ";
    public const string PersonalDsiz = "Личная карточка учета выдачи ДСИЗ и смывающих средств";
    public const string PersonalSios = "Личная карточка учета выдачи СиОС";
    public const string DutySiz = "Карточка учета выдачи дежурных СИЗ";
    public const string CardHubMenuTitle = "Выдача спецодежды, СИЗ";
    public const string ProvisionMenuTitle = "Обеспечение спецодеждой, СИЗ";

    public static IReadOnlyList<string> All { get; } = [PersonalSiz, PersonalDsiz, DutySiz];

    public static bool IsDutyCard(string documentName) =>
        documentName.Contains("дежурн", StringComparison.OrdinalIgnoreCase);

    public static bool IncludesDsiz(string documentName) =>
        !IsDutyCard(documentName) && (
            documentName.Contains("ДСИЗ", StringComparison.OrdinalIgnoreCase) ||
            documentName.Contains("смыв", StringComparison.OrdinalIgnoreCase) ||
            documentName.Contains("СиОС", StringComparison.OrdinalIgnoreCase));

    public static bool IncludesSiz(string documentName) => !IncludesDsiz(documentName);

    public static string? ResolveMenuTitle(string menuTitle)
    {
        var t = menuTitle.Trim();
        var lower = t.ToLowerInvariant().Replace('ё', 'е');

        if (t.Equals(PersonalSiz, StringComparison.OrdinalIgnoreCase) ||
            (lower.Contains("личн") && lower.Contains("карточ") && lower.Contains("сиз") &&
             !lower.Contains("дсиз") && !lower.Contains("сиос") && !lower.Contains("смыв") && !lower.Contains("дежурн")))
        {
            return PersonalSiz;
        }

        if (t.Equals(PersonalSios, StringComparison.OrdinalIgnoreCase) ||
            (lower.Contains("личн") && lower.Contains("карточ") && lower.Contains("сиос") && !lower.Contains("дсиз")))
        {
            // Карточка СиОС убрана из выбора — открываем обычную СИЗ.
            return PersonalSiz;
        }

        if (t.Equals(PersonalDsiz, StringComparison.OrdinalIgnoreCase) ||
            (lower.Contains("личн") && lower.Contains("карточ") &&
             (lower.Contains("дсиз") || lower.Contains("смыв"))))
        {
            return PersonalDsiz;
        }

        if (t.Equals(DutySiz, StringComparison.OrdinalIgnoreCase) ||
            (lower.Contains("карточ") && lower.Contains("дежурн")))
        {
            return DutySiz;
        }

        if (t.Equals(CardHubMenuTitle, StringComparison.OrdinalIgnoreCase) ||
            (lower.Contains("карточ") && lower.Contains("сиз") && lower.Contains("дсиз") && !lower.Contains("личн")))
        {
            return null;
        }

        if (lower.Contains("выдач") && lower.Contains("спецодежд") && lower.Contains("сиз") &&
            !lower.Contains("распоряж") && !lower.Contains("приемк"))
        {
            return null;
        }

        return null;
    }
}

