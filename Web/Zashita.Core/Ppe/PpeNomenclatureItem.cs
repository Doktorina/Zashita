namespace Zashita.Core.Ppe;

public sealed class PpeNomenclatureItem
{
    /// <summary>СИЗ / ДСИЗ — для карточек и стандартов.</summary>
    public string Category { get; set; } = "СИЗ";

    /// <summary>Тип средства защиты из Приложения 1 ЕТН.</summary>
    public string ProtectionType { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Нормы выдачи на год (период).</summary>
    public string Norm { get; set; } = "По норме";

    public string NormParagraph { get; set; } = string.Empty;

    public string DisplayProtectionType =>
        string.IsNullOrWhiteSpace(ProtectionType) ? Category : ProtectionType;
}
