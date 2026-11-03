namespace Zashita.Core.Employees;

/// <summary>
/// Справочник организаций, которые должны оставаться в системе даже без строк в выгрузке 1С.
/// </summary>
public static class OrganizationCatalog
{
    /// <summary>
    /// Старые ООО, которые раньше добавлялись вручную при первом запуске.
    /// Не подмешиваются при синхронизации 1С; пустые записи удаляются при зачистке.
    /// </summary>
    public static IReadOnlyList<string> LegacyManualShellOrganizations { get; } =
    [
        "ООО «Внкторня Строй»",
        "ООО «Созвездие»",
        "ООО «Компания Строй Каркас»",
        "ООО «ИНВИКТУС»",
        "ООО «Новые метры»",
        "ООО «Е-Строй»",
        "ООО «СтандартСтрой»",
        "ООО «Отдел Строй»",
        "ООО «КСК» (Елеонская Е.В.)",
        "ООО «БЕТОН ЭКСПЕРТ»"
    ];

    /// <summary>ИП с коротким написанием — только для старого режима без 1С.</summary>
    public static IReadOnlyList<string> BuiltInOrganizations { get; } =
    [
        "ИП Яготин С.С.",
        "ИП Нигамаев Р.Ф.",
        "ИП Макаров Г.А.",
        "ИП Русанов К.В.",
        "ИП Кирюшкин Виталий Валерьевич",
        "ИП Васильев Александр Викторович",
        "ИП Черкасов С.А.",
        "ИП Шварц А.С.",
        "ИП Вахитов А.С.",
        "ИП Вахитов С.З.",
        "ИП Дубровский М.В.",
        "ИП Еремеев М.А.",
        "ИП Алешкевич А.Д.",
        "ИП Елеонский Д.Е.",
        "ИП Черкасова М.В.",
        "ИП Демченко Е.Г.",
        "ИП Репин В.И.",
        "ИП Лайковская Е.Ю.",
        "ИП Сартаков А.А.",
        "ИП Черепонянц О.С.",
        "ИП Черепонянц С.А.",
        "ИП Шитова О.Ю.",
        "ИП Овсепян М.П.",
        "ИП Лайковский Т.А.",
        "ИП Шубин Д.В.",
        "ИП Евдокимов А.А."
    ];

    /// <summary>Организации вне выгрузки 1С, но нужные в справочнике.</summary>
    public static IReadOnlyList<string> AdditionalExpectedOrganizations { get; } =
    [
        "ИП Кузнецов"
    ];

    public static bool IsLegacyManualShell(string organization)
    {
        var value = (organization ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return LegacyManualShellOrganizations.Any(legacy =>
            string.Equals(legacy, value, StringComparison.OrdinalIgnoreCase)
            || OrganizationMatcher.MatchesForCatalog(legacy, value));
    }

    public static IEnumerable<string> MergeWithCatalog(
        IEnumerable<string> fromOneC,
        IEnumerable<string>? kristinaOrganizations = null)
    {
        var merged = fromOneC.Concat(AdditionalExpectedOrganizations);

        if (kristinaOrganizations is not null)
        {
            merged = merged.Concat(kristinaOrganizations);
        }

        return OrganizationMatcher.DeduplicateOrganizations(
            OrganizationLegalEntitySplit.EnsurePreservedOrganizations(merged));
    }
}
