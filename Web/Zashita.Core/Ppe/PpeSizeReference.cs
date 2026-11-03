using System.Data;
using Zashita.Core.Excel;

namespace Zashita.Core.Ppe;

/// <summary>Типовые размеры СИЗ по полу с автоподхватом из размерчики.xlsx.</summary>
public static class PpeSizeReference
{
    private static readonly StringComparer Ord = StringComparer.OrdinalIgnoreCase;
    private static readonly object Sync = new();
    private static SizeDataset? _cached;
    private static string? _cachedPath;
    private static DateTime _cachedWriteTimeUtc;

    public static IReadOnlyList<string> MaleHeights => GetData().Male.Heights;
    public static IReadOnlyList<string> MaleClothesFull => GetData().Male.ClothesFull;
    public static IReadOnlyList<string> MaleShoes => GetData().Male.Shoes;
    public static IReadOnlyList<string> MaleHeadwear => GetData().Male.Headwear;
    public static IReadOnlyList<string> MaleGloves => GetData().Male.Gloves;

    public static IReadOnlyList<string> FemaleHeights => GetData().Female.Heights;
    public static IReadOnlyList<string> FemaleClothesFull => GetData().Female.ClothesFull;
    public static IReadOnlyList<string> FemaleShoes => GetData().Female.Shoes;
    public static IReadOnlyList<string> FemaleHeadwear => GetData().Female.Headwear;
    public static IReadOnlyList<string> FemaleGloves => GetData().Female.Gloves;

    public static string ShortClothesFromFull(string full)
    {
        var s = (full ?? string.Empty).Trim();
        var p = s.IndexOf('(', StringComparison.Ordinal);
        return p > 0 ? s[..p].Trim() : s;
    }

    public static bool InferredFemale(string employee)
    {
        var surname = (employee ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?
            .Trim() ?? string.Empty;

        return surname.EndsWith("ова", StringComparison.OrdinalIgnoreCase) ||
               surname.EndsWith("ева", StringComparison.OrdinalIgnoreCase) ||
               surname.EndsWith("ина", StringComparison.OrdinalIgnoreCase) ||
               surname.EndsWith("ая", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMaleClothesFull(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        MaleClothesFull.Any(x => Ord.Equals(x.Trim(), value.Trim()));

    public static bool IsFemaleClothesFull(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        FemaleClothesFull.Any(x => Ord.Equals(x.Trim(), value.Trim()));

    public static bool ValueInSet(string? value, IReadOnlyList<string> set) =>
        !string.IsNullOrWhiteSpace(value) &&
        set.Any(x => Ord.Equals(x.Trim(), value!.Trim()));

    public static void ApplyMaleRowForHeight(string height, out string uniform, out string clothes, out string shoe, out string head, out string glove)
    {
        var row = GetData().Male.GetRowForHeight(height);
        uniform = row.UniformSize;
        clothes = row.ClothesSize;
        shoe = row.ShoeSize;
        head = row.HeadwearSize;
        glove = row.GloveSize;
    }

    public static void ApplyFemaleRowForHeight(string height, out string uniform, out string clothes, out string shoe, out string head, out string glove)
    {
        var row = GetData().Female.GetRowForHeight(height);
        uniform = row.UniformSize;
        clothes = row.ClothesSize;
        shoe = row.ShoeSize;
        head = row.HeadwearSize;
        glove = row.GloveSize;
    }

    public static EmployeePpeSizes SanitizeForGender(string employee, EmployeePpeSizes s) =>
        SanitizeForGender(InferredFemale(employee), s);

    public static EmployeePpeSizes SanitizeForGender(bool female, EmployeePpeSizes s)
    {
        var heights = female ? FemaleHeights : MaleHeights;
        var height = (s.Height ?? string.Empty).Trim();
        if (!ValueInSet(height, heights))
        {
            height = female ? FemaleHeights[1] : MaleHeights[1];
        }

        var clothes = (s.ClothesSize ?? string.Empty).Trim();

        if (female)
        {
            if (IsMaleClothesFull(clothes) || string.IsNullOrWhiteSpace(clothes) || !IsFemaleClothesFull(clothes))
            {
                ApplyFemaleRowForHeight(height, out var u, out var c, out var sh, out var head, out var gl);
                return Pack(height, u, c, sh, head, gl, s);
            }

            ApplyFemaleRowForHeight(height, out _, out _, out var defSh, out var defHead, out var defGl);
            return new EmployeePpeSizes(
                Height: height,
                UniformSize: ShortClothesFromFull(clothes),
                ClothesSize: clothes,
                ShoeSize: ValueInSet(s.ShoeSize, FemaleShoes) ? s.ShoeSize!.Trim() : defSh,
                HeadwearSize: ValueInSet(s.HeadwearSize, FemaleHeadwear) ? s.HeadwearSize!.Trim() : defHead,
                GloveSize: ValueInSet(s.GloveSize, FemaleGloves) ? s.GloveSize!.Trim() : defGl,
                GasMaskSize: (s.GasMaskSize ?? string.Empty).Trim(),
                RespiratorSize: (s.RespiratorSize ?? string.Empty).Trim());
        }

        if (IsFemaleClothesFull(clothes) || string.IsNullOrWhiteSpace(clothes) || !IsMaleClothesFull(clothes))
        {
            ApplyMaleRowForHeight(height, out var u, out var c, out var sh, out var head, out var gl);
            return Pack(height, u, c, sh, head, gl, s);
        }

        ApplyMaleRowForHeight(height, out _, out _, out var defSh2, out var defHead2, out var defGl2);
        return new EmployeePpeSizes(
            Height: height,
            UniformSize: ShortClothesFromFull(clothes),
            ClothesSize: clothes,
            ShoeSize: ValueInSet(s.ShoeSize, MaleShoes) ? s.ShoeSize!.Trim() : defSh2,
            HeadwearSize: ValueInSet(s.HeadwearSize, MaleHeadwear) ? s.HeadwearSize!.Trim() : defHead2,
            GloveSize: ValueInSet(s.GloveSize, MaleGloves) ? s.GloveSize!.Trim() : defGl2,
            GasMaskSize: (s.GasMaskSize ?? string.Empty).Trim(),
            RespiratorSize: (s.RespiratorSize ?? string.Empty).Trim());
    }

    private static EmployeePpeSizes Pack(string height, string u, string c, string sh, string head, string gl, EmployeePpeSizes s) =>
        new(height, u, c, sh, head, gl, (s.GasMaskSize ?? string.Empty).Trim(), (s.RespiratorSize ?? string.Empty).Trim());

    public static EmployeePpeSizes DefaultsForEmployee(string employee) =>
        DefaultsForGender(InferredFemale(employee));

    public static EmployeePpeSizes DefaultsForGender(bool female)
    {
        var row = female ? GetData().Female.DefaultRow : GetData().Male.DefaultRow;
        return new EmployeePpeSizes(row.Height, row.UniformSize, row.ClothesSize, row.ShoeSize, row.HeadwearSize, row.GloveSize, row.GasMaskSize, "");
    }

    public static PpeSizeChart GetChart(bool female) =>
        female ? GetData().Female.ToChart() : GetData().Male.ToChart();

    private static SizeDataset GetData()
    {
        var path = ResolveWorkbookPath();
        var writeTime = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

        lock (Sync)
        {
            if (_cached is not null && string.Equals(_cachedPath, path, StringComparison.OrdinalIgnoreCase) && _cachedWriteTimeUtc == writeTime)
            {
                return _cached;
            }

            _cached = File.Exists(path) ? LoadFromWorkbook(path) : CreateFallbackDataset();
            _cachedPath = path;
            _cachedWriteTimeUtc = writeTime;
            return _cached;
        }
    }

    private static string ResolveWorkbookPath()
    {
        var appDataPath = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "ppe-sizes.xlsx");
        if (File.Exists(appDataPath))
        {
            return appDataPath;
        }

        var downloadsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "Telegram Desktop",
            "размерчики.xlsx");
        if (File.Exists(downloadsPath))
        {
            return downloadsPath;
        }

        return appDataPath;
    }

    private static SizeDataset LoadFromWorkbook(string path)
    {
        try
        {
            var store = new ExcelWorkbookStore();
            using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            store.LoadFromStream(fs, Path.GetFileName(path));
            var table = store.SheetNames.Select(store.GetSheet).FirstOrDefault(t => t is { Rows.Count: > 0 });
            return table is null ? CreateFallbackDataset() : ParseTable(table);
        }
        catch
        {
            return CreateFallbackDataset();
        }
    }

    private static SizeDataset ParseTable(DataTable table)
    {
        var male = new GenderSizeSet();
        var female = new GenderSizeSet();
        var current = male;
        var lastHeadwear = string.Empty;
        var lastGasMask = string.Empty;

        foreach (DataRow row in table.Rows)
        {
            var sex = Cell(row, 0);
            if (sex.Contains("жен", StringComparison.OrdinalIgnoreCase))
            {
                current = female;
                lastHeadwear = string.Empty;
                lastGasMask = string.Empty;
                continue;
            }

            if (sex.Contains("муж", StringComparison.OrdinalIgnoreCase))
            {
                current = male;
                lastHeadwear = string.Empty;
                lastGasMask = string.Empty;
                continue;
            }

            var height = NormalizeRange(Cell(row, 2));
            var clothes = Cell(row, 4);
            var shoe = Cell(row, 6);
            var head = Cell(row, 8);
            var gas = Cell(row, 10);
            var glove = Cell(row, 12);

            if (string.IsNullOrWhiteSpace(height) &&
                string.IsNullOrWhiteSpace(clothes) &&
                string.IsNullOrWhiteSpace(shoe) &&
                string.IsNullOrWhiteSpace(head) &&
                string.IsNullOrWhiteSpace(gas) &&
                string.IsNullOrWhiteSpace(glove))
            {
                continue;
            }

            current.AddClothes(clothes);
            current.AddShoe(shoe);
            current.AddHeadwear(head);
            current.AddGlove(glove);

            if (!string.IsNullOrWhiteSpace(head))
            {
                lastHeadwear = head;
            }

            if (!string.IsNullOrWhiteSpace(gas) && gas != "-")
            {
                lastGasMask = gas;
            }

            if (string.IsNullOrWhiteSpace(height))
            {
                continue;
            }

            current.AddHeightRow(new EmployeePpeSizes(
                Height: height,
                UniformSize: ShortClothesFromFull(clothes),
                ClothesSize: clothes,
                ShoeSize: shoe,
                HeadwearSize: string.IsNullOrWhiteSpace(head) ? lastHeadwear : head,
                GloveSize: glove,
                GasMaskSize: string.IsNullOrWhiteSpace(gas) || gas == "-" ? lastGasMask : gas,
                RespiratorSize: ""));
        }

        male.EnsureDefaults(CreateFallbackMaleRows());
        female.EnsureDefaults(CreateFallbackFemaleRows());
        return new SizeDataset(male, female);
    }

    private static string Cell(DataRow row, int index) =>
        index < row.Table.Columns.Count ? (row[index]?.ToString()?.Trim() ?? string.Empty) : string.Empty;

    private static string NormalizeRange(string value) =>
        (value ?? string.Empty).Trim().Replace(" - ", " – ");

    private static SizeDataset CreateFallbackDataset()
    {
        var male = new GenderSizeSet();
        foreach (var row in CreateFallbackMaleRows())
        {
            male.AddHeightRow(row);
            male.AddClothes(row.ClothesSize);
            male.AddShoe(row.ShoeSize);
            male.AddHeadwear(row.HeadwearSize);
            male.AddGlove(row.GloveSize);
        }

        male.AddClothes("60-62 (120-124)");
        male.AddClothes("64-66 (128-132)");
        male.AddClothes("68-70 (136-140)");
        male.AddShoe("42");
        male.AddShoe("43");
        male.AddShoe("44");
        male.AddShoe("45");
        male.AddShoe("46");
        male.AddShoe("47");
        male.AddGlove("10");
        male.AddGlove("11");
        male.AddGlove("12");

        var female = new GenderSizeSet();
        foreach (var row in CreateFallbackFemaleRows())
        {
            female.AddHeightRow(row);
            female.AddClothes(row.ClothesSize);
            female.AddShoe(row.ShoeSize);
            female.AddHeadwear(row.HeadwearSize);
            female.AddGlove(row.GloveSize);
        }

        female.AddClothes("56-58 (112-116)");
        female.AddClothes("60-62 (120-124)");
        female.AddClothes("64-66 (128-132)");
        female.AddShoe("39");
        female.AddShoe("40");
        female.AddShoe("41");
        female.AddShoe("42");
        female.AddGlove("10");

        return new SizeDataset(male, female);
    }

    private static IEnumerable<EmployeePpeSizes> CreateFallbackMaleRows()
    {
        yield return new EmployeePpeSizes("158 – 164", "44-46", "44-46 (88-92)", "38", "54-58", "6", "", "");
        yield return new EmployeePpeSizes("170 – 176", "48-50", "48-50 (96-100)", "39", "58-62", "7", "", "");
        yield return new EmployeePpeSizes("182 – 188", "52-54", "52-54 (104-108)", "40", "58-62", "8", "", "");
        yield return new EmployeePpeSizes("194 – 200", "56-58", "56-58 (112-116)", "41", "58-62", "9", "", "");
    }

    private static IEnumerable<EmployeePpeSizes> CreateFallbackFemaleRows()
    {
        yield return new EmployeePpeSizes("146 – 152", "40-42", "40-42 (80-84)", "35", "54-58", "6", "", "");
        yield return new EmployeePpeSizes("158 – 164", "44-46", "44-46 (88-92)", "36", "58-62", "7", "", "");
        yield return new EmployeePpeSizes("170 – 176", "48-50", "48-50 (96-100)", "37", "58-62", "8", "", "");
        yield return new EmployeePpeSizes("182 – 188", "52-54", "52-54 (104-108)", "38", "58-62", "9", "", "");
    }

    private sealed record SizeDataset(GenderSizeSet Male, GenderSizeSet Female);

    public sealed class PpeSizeChart
    {
        public required IReadOnlyList<string> Heights { get; init; }
        public required IReadOnlyList<string> ClothesFull { get; init; }
        public required IReadOnlyList<string> Shoes { get; init; }
        public required IReadOnlyList<string> Headwear { get; init; }
        public required IReadOnlyList<string> Gloves { get; init; }
        public required IReadOnlyDictionary<string, PpeSizeRow> ByHeight { get; init; }
    }

    public sealed record PpeSizeRow(
        string Height,
        string Clothes,
        string Shoe,
        string Headwear,
        string Glove,
        string GasMask);

    private sealed class GenderSizeSet
    {
        private readonly List<EmployeePpeSizes> _rows = [];
        private readonly List<string> _heights = [];
        private readonly List<string> _clothesFull = [];
        private readonly List<string> _shoes = [];
        private readonly List<string> _headwear = [];
        private readonly List<string> _gloves = [];

        public IReadOnlyList<string> Heights => _heights;
        public IReadOnlyList<string> ClothesFull => _clothesFull;
        public IReadOnlyList<string> Shoes => _shoes;
        public IReadOnlyList<string> Headwear => _headwear;
        public IReadOnlyList<string> Gloves => _gloves;
        public EmployeePpeSizes DefaultRow => _rows.Count > 1 ? _rows[1] : (_rows.Count == 1 ? _rows[0] : new EmployeePpeSizes());

        public void AddHeightRow(EmployeePpeSizes row)
        {
            if (string.IsNullOrWhiteSpace(row.Height))
            {
                return;
            }

            _rows.Add(row);
            AddUnique(_heights, row.Height);
        }

        public void AddClothes(string value) => AddUnique(_clothesFull, value);
        public void AddShoe(string value) => AddUnique(_shoes, value);
        public void AddHeadwear(string value) => AddUnique(_headwear, value);
        public void AddGlove(string value) => AddUnique(_gloves, value);

        public EmployeePpeSizes GetRowForHeight(string height)
        {
            var match = _rows.FirstOrDefault(r => string.Equals(r.Height, NormalizeRange(height), StringComparison.OrdinalIgnoreCase));
            return match ?? DefaultRow;
        }

        public void EnsureDefaults(IEnumerable<EmployeePpeSizes> rows)
        {
            if (_rows.Count > 0)
            {
                return;
            }

            foreach (var row in rows)
            {
                AddHeightRow(row);
                AddClothes(row.ClothesSize);
                AddShoe(row.ShoeSize);
                AddHeadwear(row.HeadwearSize);
                AddGlove(row.GloveSize);
            }
        }

        public PpeSizeChart ToChart() =>
            new()
            {
                Heights = _heights,
                ClothesFull = _clothesFull,
                Shoes = _shoes,
                Headwear = _headwear,
                Gloves = _gloves,
                ByHeight = _rows
                    .Where(r => !string.IsNullOrWhiteSpace(r.Height))
                    .ToDictionary(
                        r => r.Height,
                        r => new PpeSizeRow(r.Height, r.ClothesSize, r.ShoeSize, r.HeadwearSize, r.GloveSize, r.GasMaskSize),
                        StringComparer.OrdinalIgnoreCase)
            };

        private static void AddUnique(List<string> list, string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized) || normalized == "-")
            {
                return;
            }

            if (!list.Any(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(normalized);
            }
        }
    }
}
