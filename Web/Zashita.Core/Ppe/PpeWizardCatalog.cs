namespace Zashita.Core.Ppe;

public sealed record PpePickItem(string Category, string Name, string Norm, string NormParagraph = "")
{
    public string Display => $"[{Category}] {Name}";
}

public static class PpeWizardCatalog
{
  private static readonly string[] DutyFallbackNames =
  [
      "Каска защитная",
      "Очки защитные общего назначения",
      "Перчатки защитные",
      "Обувь специальная для защиты от механических воздействий"
  ];

  public static IReadOnlyList<PpePickItem> GetPickItems(string documentName, IEnumerable<PpeNomenclatureItem> nomenclature)
  {
      if (PpeCardTypes.IsDutyCard(documentName))
      {
          var duty = nomenclature
              .Where(i => i.Category.Equals("СИЗ", StringComparison.OrdinalIgnoreCase))
              .Select(i => new PpePickItem("СИЗ", i.Name, string.IsNullOrWhiteSpace(i.Norm) ? "Определяется документами" : i.Norm, i.NormParagraph))
              .ToList();
          if (duty.Count > 0) return duty;

          return DutyFallbackNames
              .Select(n => new PpePickItem("СИЗ", n, "Определяется документами"))
              .ToList();
      }

      var includeSiz = PpeCardTypes.IncludesSiz(documentName);
      var includeDsiz = PpeCardTypes.IncludesDsiz(documentName);
      return nomenclature
          .Where(i => (includeSiz && i.Category.Equals("СИЗ", StringComparison.OrdinalIgnoreCase)) ||
                      (includeDsiz && i.Category.Equals("ДСИЗ", StringComparison.OrdinalIgnoreCase)))
          .Select(i => new PpePickItem(i.Category, i.Name, string.IsNullOrWhiteSpace(i.Norm) ? "По норме" : i.Norm, i.NormParagraph))
          .ToList();
  }

  public static SiosPrintRowDto ToPrintRow(PpePickItem item, bool isDsizCard)
  {
      var rowDsiz = item.Category.Equals("ДСИЗ", StringComparison.OrdinalIgnoreCase);
      var isDsiz = rowDsiz || isDsizCard;
      return new SiosPrintRowDto
      {
          Name = item.Name,
          Norm = item.Norm,
          NormPoint = PpeNormPointResolver.FormatParagraphForCard(item.NormParagraph),
          ModelDescription = (item.Name ?? string.Empty).Trim(),
          IssueMethod = rowDsiz ? "Дозирующая выдача средств" : "Персонализированная",
          ShelfLife = PpeNormPointResolver.DeriveShelfLife(item.Norm, isDsiz)
      };
  }
}
