namespace Zashita.Core.Ppe;

public sealed class SiosPrintRowDto
{
    public string Name { get; set; } = string.Empty;
    public string Norm { get; set; } = string.Empty;
    public string IssueMethod { get; set; } = string.Empty;
    public string ShelfLife { get; set; } = string.Empty;
    public string NormPoint { get; set; } = string.Empty;
    /// <summary>Колонка 2 оборотной стороны: модель, марка, артикул.</summary>
    public string ModelDescription { get; set; } = string.Empty;
}

public sealed class SavedEditableRowDto
{
    public string Name { get; set; } = string.Empty;
    public string NormPoint { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Qty { get; set; } = string.Empty;
    public string Norm { get; set; } = string.Empty;
    public string IssueMethod { get; set; } = string.Empty;
    public string ReceiptSign { get; set; } = string.Empty;
    public string ShelfLife { get; set; } = string.Empty;
    public string ModelDescription { get; set; } = string.Empty;
    public string BackIssueDate { get; set; } = string.Empty;
    public string BackIssueQty { get; set; } = string.Empty;
    public string BackIssueSign { get; set; } = string.Empty;
    public string BackReturnDate { get; set; } = string.Empty;
    public string BackReturnQty { get; set; } = string.Empty;
    public string BackReturnSign { get; set; } = string.Empty;
    public string BackAct { get; set; } = string.Empty;
}

public sealed class SavedSiosCardRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string Organization { get; set; } = string.Empty;
    public string DocumentName { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    public List<string> Employees { get; set; } = [];
    public List<SiosPrintRowDto> PrintRows { get; set; } = [];
    public bool UseDsizSinglePageLayout { get; set; }
    public Dictionary<string, EmployeePpeSizes>? SizesByEmployee { get; set; }
    public string CardNumber { get; set; } = "1";
    public string TabNumber { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public DateTime HireDate { get; set; }
    public DateTime ProfessionChangeDate { get; set; }
    /// <summary>Дата выдачи СИЗ для колонки «дата» на обороте карточки.</summary>
    public DateTime PpeIssueDate { get; set; }
    public string Sex { get; set; } = string.Empty;
    public string Height { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string ClothesSize { get; set; } = string.Empty;
    public string ShoeSize { get; set; } = string.Empty;
    public string HeadwearSize { get; set; } = string.Empty;
    public string GloveSize { get; set; } = string.Empty;
    public string GasMaskSize { get; set; } = string.Empty;
    public string RespiratorSize { get; set; } = string.Empty;
    public string PpeExtra { get; set; } = string.Empty;
    public string ResponsiblePerson { get; set; } = string.Empty;
    public string ManagerSignature { get; set; } = string.Empty;
    /// <summary>Идентификатор рабочего места (карточка дежурных СИЗ).</summary>
    public string WorkplaceId { get; set; } = string.Empty;
    /// <summary>Номер и дата приказа об утверждении Норм (карточка дежурных СИЗ).</summary>
    public string NormsOrderText { get; set; } = string.Empty;
    public List<SavedEditableRowDto> Rows { get; set; } = [];
}

public sealed class SiosCardPrintContext
{
    public string DocumentName { get; init; } = PpeCardTypes.PersonalSiz;
    public string Organization { get; init; } = string.Empty;
    public string Profession { get; init; } = string.Empty;
    public string CardNumber { get; init; } = "1";
    public string TabNumber { get; init; } = string.Empty;
    public string Department { get; init; } = string.Empty;
    public string ResponsiblePerson { get; init; } = string.Empty;
    public string WorkplaceId { get; init; } = string.Empty;
    public string NormsOrderText { get; init; } = string.Empty;
    public DateTime HireDate { get; init; }
    public DateTime ProfessionChangeDate { get; init; }
    public DateTime PpeIssueDate { get; init; }
    public IReadOnlyList<string> Employees { get; init; } = [];
    public IReadOnlyList<SiosEditableRow> Rows { get; init; } = [];
    public IReadOnlyDictionary<string, EmployeePpeSizes>? SizesByEmployee { get; init; }
    public string? Sex { get; init; }
    public bool UseDsizSinglePageLayout { get; init; }
    /// <summary>Предпросмотр: ячейки на бланке можно править кликом.</summary>
    public bool EditablePreview { get; set; }
}

public sealed class SiosEditableRow
{
    public string Name { get; set; } = string.Empty;
    public string NormPoint { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Qty { get; set; } = string.Empty;
    public string Norm { get; set; } = string.Empty;
    public string IssueMethod { get; set; } = string.Empty;
    public string ShelfLife { get; set; } = string.Empty;
    public string ModelDescription { get; set; } = string.Empty;
    public string BackIssueDate { get; set; } = string.Empty;
    public string BackIssueQty { get; set; } = string.Empty;
    public string BackIssueSign { get; set; } = string.Empty;
    public string BackReturnDate { get; set; } = string.Empty;
    public string BackReturnQty { get; set; } = string.Empty;
    public string BackReturnSign { get; set; } = string.Empty;
    public string BackAct { get; set; } = string.Empty;
}

public static class SiosCardRowMapper
{
    public static List<SiosEditableRow> FromPrintRows(IEnumerable<SiosPrintRowDto> rows, bool isDsiz)
    {
        return rows.Select(r =>
        {
            var rowDsiz = isDsiz || string.Equals(r.ShelfLife, "1 год", StringComparison.OrdinalIgnoreCase);
            return new SiosEditableRow
            {
                Name = r.Name,
                NormPoint = r.NormPoint ?? string.Empty,
                Unit = string.Empty,
                Qty = r.Norm,
                Norm = r.Norm,
                IssueMethod = r.IssueMethod,
                ShelfLife = string.IsNullOrWhiteSpace(r.ShelfLife)
                    ? (rowDsiz ? "1 год" : "До износа")
                    : r.ShelfLife,
                ModelDescription = r.ModelDescription ?? string.Empty
            };
        }).ToList();
    }

    public static List<SiosEditableRow> FromSavedRows(IEnumerable<SavedEditableRowDto> rows) =>
        rows.Select(r => new SiosEditableRow
        {
            Name = r.Name,
            NormPoint = r.NormPoint,
            Unit = r.Unit,
            Qty = r.Qty,
            Norm = r.Norm,
            IssueMethod = r.IssueMethod,
            ShelfLife = r.ShelfLife,
            ModelDescription = r.ModelDescription,
            BackIssueDate = r.BackIssueDate,
            BackIssueQty = r.BackIssueQty,
            BackIssueSign = r.BackIssueSign,
            BackReturnDate = r.BackReturnDate,
            BackReturnQty = r.BackReturnQty,
            BackReturnSign = r.BackReturnSign,
            BackAct = r.BackAct
        }).ToList();

    public static SiosCardPrintContext FromSaved(SavedSiosCardRecord saved)
    {
        var isDsiz = saved.UseDsizSinglePageLayout || PpeCardTypes.IncludesDsiz(saved.DocumentName);
        var rows = saved.Rows.Count > 0
            ? FromSavedRows(saved.Rows)
            : FromPrintRows(saved.PrintRows, isDsiz);

        IReadOnlyDictionary<string, EmployeePpeSizes>? sizes = saved.SizesByEmployee is { Count: > 0 }
            ? saved.SizesByEmployee
            : null;

        var hire = saved.HireDate == default ? default : saved.HireDate.Date;
        var profChange = saved.ProfessionChangeDate == default ? default : saved.ProfessionChangeDate.Date;
        var issueDate = saved.PpeIssueDate == default ? DateTime.Today : saved.PpeIssueDate.Date;

        return new SiosCardPrintContext
        {
            DocumentName = saved.DocumentName,
            Organization = saved.Organization,
            Profession = saved.Profession,
            CardNumber = string.IsNullOrWhiteSpace(saved.CardNumber) ? "1" : saved.CardNumber.Trim(),
            TabNumber = saved.TabNumber,
            Department = saved.Department,
            ResponsiblePerson = saved.ResponsiblePerson,
            WorkplaceId = saved.WorkplaceId,
            NormsOrderText = saved.NormsOrderText,
            HireDate = hire,
            ProfessionChangeDate = profChange,
            PpeIssueDate = issueDate,
            Employees = saved.Employees,
            Rows = rows,
            SizesByEmployee = sizes ?? BuildSizesFromSavedFields(saved),
            Sex = string.IsNullOrWhiteSpace(saved.Sex) ? null : saved.Sex.Trim(),
            UseDsizSinglePageLayout = saved.UseDsizSinglePageLayout || isDsiz
        };
    }

    private static Dictionary<string, EmployeePpeSizes>? BuildSizesFromSavedFields(SavedSiosCardRecord saved)
    {
        if (string.IsNullOrWhiteSpace(saved.Height) &&
            string.IsNullOrWhiteSpace(saved.ClothesSize) &&
            string.IsNullOrWhiteSpace(saved.ShoeSize))
        {
            return null;
        }

        var sizes = PpeWizardSizes.Build(
            saved.Sex,
            saved.Height,
            saved.ClothesSize,
            saved.ShoeSize,
            saved.HeadwearSize,
            saved.GloveSize,
            saved.GasMaskSize);

        return PpeWizardSizes.MapForEmployees(
            saved.Employees,
            saved.Sex,
            sizes.Height,
            sizes.ClothesSize,
            sizes.ShoeSize,
            sizes.HeadwearSize,
            sizes.GloveSize,
            sizes.GasMaskSize);
    }
}
