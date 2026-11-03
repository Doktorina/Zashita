using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Zashita.Core.Training.Journals.Export;

/// <summary>Шрифт Arial, крупный титул, компактная таблица, верхнее поле 2 см, отступы в ячейках.</summary>
public static class TrainingJournalWordTypography
{
    public const string FontName = "Arial";

    /// <summary>Титул (половинные пункты Word): 13 pt.</summary>
    public const string TitleHalfPoints = "26";

    /// <summary>Подписи титула, даты (11 pt).</summary>
    public const string TitleSecondaryHalfPoints = "22";

    /// <summary>Таблица — заголовки и данные (10 pt).</summary>
    public const string TableHalfPoints = "20";

    /// <summary>Верхнее поле 2 см (twips).</summary>
    public const int TopMarginTwips = 1134;

    public const int SideMarginTwips = 720;
    public const int BottomMarginTwips = 1134;

    /// <summary>Отступ текста от границ ячейки (~1,5 мм).</summary>
    public const string CellMarginDxa = "85";

    public static RunProperties CreateArialRun(string halfPoints, bool bold = false, bool boldCs = false)
    {
        var props = new RunProperties(
            new RunFonts
            {
                Ascii = FontName,
                HighAnsi = FontName,
                ComplexScript = FontName
            },
            new FontSize { Val = halfPoints },
            new FontSizeComplexScript { Val = halfPoints });

        if (bold)
        {
            props.Append(new Bold());
        }

        if (boldCs)
        {
            props.Append(new BoldComplexScript());
        }

        return props;
    }

    public static TableCellMargin DefaultTableCellMargin() =>
        new(
            new TopMargin { Width = CellMarginDxa, Type = TableWidthUnitValues.Dxa },
            new BottomMargin { Width = CellMarginDxa, Type = TableWidthUnitValues.Dxa },
            new LeftMargin { Width = CellMarginDxa, Type = TableWidthUnitValues.Dxa },
            new RightMargin { Width = CellMarginDxa, Type = TableWidthUnitValues.Dxa });

    public static TableCellMargin CellPadding() =>
        new(
            new TopMargin { Width = CellMarginDxa, Type = TableWidthUnitValues.Dxa },
            new BottomMargin { Width = CellMarginDxa, Type = TableWidthUnitValues.Dxa },
            new LeftMargin { Width = CellMarginDxa, Type = TableWidthUnitValues.Dxa },
            new RightMargin { Width = CellMarginDxa, Type = TableWidthUnitValues.Dxa });

    public static SectionProperties CreateLandscapeSection() =>
        new(
            new PageSize
            {
                Width = 16838U,
                Height = 11906U,
                Orient = PageOrientationValues.Landscape
            },
            new PageMargin
            {
                Top = TopMarginTwips,
                Right = SideMarginTwips,
                Bottom = BottomMarginTwips,
                Left = SideMarginTwips,
                Header = 708,
                Footer = 708,
                Gutter = 0
            });

    public static void ApplyPageMargins(Body body)
    {
        foreach (var section in body.Descendants<SectionProperties>())
        {
            var margin = section.GetFirstChild<PageMargin>();
            if (margin is null)
            {
                section.PrependChild(new PageMargin
                {
                    Top = TopMarginTwips,
                    Right = SideMarginTwips,
                    Bottom = BottomMarginTwips,
                    Left = SideMarginTwips
                });
            }
            else
            {
                margin.Top = TopMarginTwips;
                margin.Right = SideMarginTwips;
                margin.Bottom = BottomMarginTwips;
                margin.Left = SideMarginTwips;
            }

            var pageSize = section.GetFirstChild<PageSize>();
            if (pageSize is not null)
            {
                pageSize.Width = 16838U;
                pageSize.Height = 11906U;
                pageSize.Orient = PageOrientationValues.Landscape;
            }
        }

    }

    /// <summary>Убирает пустую портретную страницу из-за лишнего sectPr или пустых абзацев в конце.</summary>
    public static void RemoveTrailingBlankSections(Body body)
    {
        var paragraphSections = body.Descendants<ParagraphProperties>()
            .Select(p => p.GetFirstChild<SectionProperties>())
            .Where(s => s is not null)
            .ToList();

        if (paragraphSections.Count > 0)
        {
            foreach (var bodySection in body.Elements<SectionProperties>().ToList())
            {
                bodySection.Remove();
            }
        }

        while (true)
        {
            var last = body.Elements().LastOrDefault();
            switch (last)
            {
                case null:
                    return;
                case SectionProperties:
                    if (paragraphSections.Count > 0)
                    {
                        last.Remove();
                        continue;
                    }

                    return;
                case Paragraph paragraph when IsRemovableTrailingParagraph(paragraph):
                    paragraph.Remove();
                    continue;
                default:
                    return;
            }
        }
    }

    private static bool IsRemovableTrailingParagraph(Paragraph paragraph)
    {
        if (!string.IsNullOrWhiteSpace(paragraph.InnerText))
        {
            return false;
        }

        if (paragraph.Descendants<Break>().Any(b => b.Type?.Value == BreakValues.Page))
        {
            return false;
        }

        var props = paragraph.GetFirstChild<ParagraphProperties>();
        if (props?.GetFirstChild<SectionProperties>() is not null)
        {
            return false;
        }

        return true;
    }

    public static void ApplyTemplateDocumentStyle(Body body)
    {
        ApplyPageMargins(body);
        RemoveTrailingBlankSections(body);

        var journalTable = body.Elements<Table>()
            .LastOrDefault(t => t.Elements<TableGrid>().FirstOrDefault()?.Elements<GridColumn>().Count() >= 11);

        if (journalTable is not null)
        {
            ApplyJournalTableStyle(journalTable);
        }

        var titleTable = body.Elements<Table>().FirstOrDefault();
        if (titleTable is not null && titleTable != journalTable)
        {
            ApplyTitleTableStyle(titleTable);
        }

        foreach (var paragraph in body.Elements<Paragraph>())
        {
            if (paragraph.Descendants<Break>().Any(b => b.Type?.Value == BreakValues.Page))
            {
                continue;
            }

            if (journalTable is not null && IsDescendantOf(paragraph, journalTable))
            {
                continue;
            }

            if (titleTable is not null && IsDescendantOf(paragraph, titleTable))
            {
                continue;
            }

            ApplyTitleParagraphStyle(paragraph);
        }
    }

    private static void ApplyJournalTableStyle(Table table)
    {
        var tblPr = table.GetFirstChild<TableProperties>() ?? table.PrependChild(new TableProperties());
        var cellMar = tblPr.GetFirstChild<TableCellMargin>();
        if (cellMar is null)
        {
            tblPr.AppendChild(DefaultTableCellMargin());
        }
        else
        {
            SetMargin(cellMar.GetFirstChild<TopMargin>());
            SetMargin(cellMar.GetFirstChild<BottomMargin>());
            SetMargin(cellMar.GetFirstChild<LeftMargin>());
            SetMargin(cellMar.GetFirstChild<RightMargin>());
        }

        var rowIndex = 0;
        foreach (var row in table.Elements<TableRow>())
        {
            var isHeaderBlock = rowIndex <= 2;
            foreach (var cell in row.Elements<TableCell>())
            {
                var cellProps = cell.GetFirstChild<TableCellProperties>() ?? cell.PrependChild(new TableCellProperties());
                if (cellProps.GetFirstChild<TableCellMargin>() is null)
                {
                    cellProps.AppendChild(CellPadding());
                }

                foreach (var run in cell.Descendants<Run>())
                {
                    ApplyRunStyle(run, TableHalfPoints, isHeaderBlock, isHeaderBlock);
                }

                foreach (var para in cell.Descendants<Paragraph>())
                {
                    var indent = para.GetFirstChild<ParagraphProperties>()?.GetFirstChild<Indentation>();
                    indent?.Remove();
                }
            }

            rowIndex++;
        }
    }

    private static void ApplyTitleTableStyle(Table table)
    {
        foreach (var run in table.Descendants<Run>())
        {
            ApplyRunStyle(run, TitleSecondaryHalfPoints, bold: true, boldCs: true);
        }
    }

    private static void ApplyTitleParagraphStyle(Paragraph paragraph)
    {
        var text = paragraph.InnerText;
        var isMainHeading = text.Contains("ЖУРНАЛ", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("УЧЕТА", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("ПРИСВОЕНИЯ", StringComparison.OrdinalIgnoreCase);
        var halfPoints = isMainHeading ? TitleHalfPoints : TitleSecondaryHalfPoints;
        var bold = isMainHeading || text.Contains('«');

        foreach (var run in paragraph.Descendants<Run>())
        {
            ApplyRunStyle(run, halfPoints, bold, bold);
        }
    }

    private static void ApplyRunStyle(Run run, string halfPoints, bool bold, bool boldCs)
    {
        var props = run.GetFirstChild<RunProperties>();
        if (props is null)
        {
            props = new RunProperties();
            run.PrependChild(props);
        }

        var fonts = props.GetFirstChild<RunFonts>();
        if (fonts is null)
        {
            props.PrependChild(new RunFonts
            {
                Ascii = FontName,
                HighAnsi = FontName,
                ComplexScript = FontName
            });
        }
        else
        {
            fonts.Ascii = FontName;
            fonts.HighAnsi = FontName;
            fonts.ComplexScript = FontName;
        }

        var size = props.GetFirstChild<FontSize>();
        if (size is null)
        {
            props.AppendChild(new FontSize { Val = halfPoints });
        }
        else
        {
            size.Val = halfPoints;
        }

        var sizeCs = props.GetFirstChild<FontSizeComplexScript>();
        if (sizeCs is null)
        {
            props.AppendChild(new FontSizeComplexScript { Val = halfPoints });
        }
        else
        {
            sizeCs.Val = halfPoints;
        }

        if (bold)
        {
            if (props.GetFirstChild<Bold>() is null)
            {
                props.AppendChild(new Bold());
            }
        }

        if (boldCs && props.GetFirstChild<BoldComplexScript>() is null)
        {
            props.AppendChild(new BoldComplexScript());
        }
    }

    private static void SetMargin(OpenXmlElement? margin)
    {
        if (margin is TopMargin top)
        {
            top.Width = CellMarginDxa;
            top.Type = TableWidthUnitValues.Dxa;
        }
        else if (margin is BottomMargin bottom)
        {
            bottom.Width = CellMarginDxa;
            bottom.Type = TableWidthUnitValues.Dxa;
        }
        else if (margin is LeftMargin left)
        {
            left.Width = CellMarginDxa;
            left.Type = TableWidthUnitValues.Dxa;
        }
        else if (margin is RightMargin right)
        {
            right.Width = CellMarginDxa;
            right.Type = TableWidthUnitValues.Dxa;
        }
    }

    private static bool IsDescendantOf(OpenXmlElement element, OpenXmlElement ancestor)
    {
        for (var current = element.Parent; current is not null; current = current.Parent)
        {
            if (current == ancestor)
            {
                return true;
            }
        }

        return false;
    }
}

