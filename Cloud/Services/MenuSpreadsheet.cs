using System.Globalization;
using ClosedXML.Excel;
using Cloud.Data;
using Microsoft.EntityFrameworkCore;

namespace Cloud.Services;

/// <summary>
/// The online menu's items as an Excel file, for the provider filling a shop's menu for them (admin API only; owners never see it).
/// <para>
/// The template has an Items sheet (Section, Name, Description, Price, a price column per order type, Hidden, Sold out) with dropdowns,
/// a Guide sheet with an example, and a hidden Lists sheet feeding the dropdowns.
/// </para>
/// <para>
/// Import reads the same columns. A row whose section and name match a dish already on the menu updates it (empty cells keep what it
/// has); any other row adds a dish, and a section that does not exist yet is made. A dish that came from the POS keeps its name,
/// price and section, which the POS owns. The file is checked first (nothing saved) and applied only when asked; a row with a problem
/// is skipped, the others go in.
/// </para>
/// </summary>
public sealed class MenuSpreadsheet(ControlDbContext db, TimeProvider time)
{
    public const int MaxRows = 5000;
    public const int MaxBytes = 10 * 1024 * 1024;
    private const int MaxIssues = 200;
    private const decimal MaxPrice = 1_000_000_000_000m;

    private const string ItemsSheet = "Items";
    private const string GuideSheet = "Guide";
    private const string ListsSheet = "Lists";
    private const string TypePricePrefix = "Price: ";

    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private enum Col { Section, Name, Description, Price, Hidden, SoldOut }

    private static readonly Dictionary<string, Col> HeaderAliases = new()
    {
        ["section"] = Col.Section, ["category"] = Col.Section, ["القسم"] = Col.Section, ["الفئة"] = Col.Section,
        ["name"] = Col.Name, ["item"] = Col.Name, ["itemname"] = Col.Name, ["dish"] = Col.Name, ["product"] = Col.Name, ["الاسم"] = Col.Name, ["الصنف"] = Col.Name,
        ["description"] = Col.Description, ["details"] = Col.Description, ["الوصف"] = Col.Description,
        ["price"] = Col.Price, ["السعر"] = Col.Price,
        ["hidden"] = Col.Hidden, ["hide"] = Col.Hidden, ["مخفي"] = Col.Hidden,
        ["soldout"] = Col.SoldOut, ["نفد"] = Col.SoldOut,
    };

    private static readonly string[] TrueWords = ["yes", "y", "true", "1", "نعم"];
    private static readonly string[] FalseWords = ["no", "n", "false", "0", "لا"];

    // ---- the template ----

    /// <summary>The empty Items sheet, shaped for this menu: its sections in the dropdown and one price column per order type.</summary>
    public async Task<byte[]> TemplateAsync(Menu? menu)
    {
        var sections = menu is null ? [] : await db.MenuSections.Where(s => s.MenuId == menu.Id).OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
            .Select(s => s.Name).ToListAsync();
        var types = menu is null ? [] : await db.MenuOrderTypes.Where(t => t.MenuId == menu.Id).OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
            .Select(t => t.Name).ToListAsync();
        var currency = menu?.Currency ?? "$";

        using var book = new XLWorkbook();
        var items = book.AddWorksheet(ItemsSheet);
        var guide = book.AddWorksheet(GuideSheet);
        var lists = book.AddWorksheet(ListsSheet);

        var headers = new List<(string Name, double Width)>
        {
            ("Section", 22), ("Name", 30), ("Description", 44), ("Price", 12),
        };
        headers.AddRange(types.Select(t => (TypePricePrefix + t, Math.Max(14, t.Length + 9.0))));
        headers.Add(("Hidden", 10));
        headers.Add(("Sold out", 10));
        for (var c = 0; c < headers.Count; c++)
        {
            var cell = items.Cell(1, c + 1);
            cell.Value = headers[c].Name;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF7");
            items.Column(c + 1).Width = headers[c].Width;
        }
        items.SheetView.FreezeRows(1);
        var lastRow = MaxRows + 1;
        var priceFrom = (int)Col.Price + 1;
        var priceTo = priceFrom + types.Count;
        items.Range(2, priceFrom, lastRow, priceTo).Style.NumberFormat.Format = "0.00##";
        items.Range(2, (int)Col.Description + 1, lastRow, (int)Col.Description + 1).Style.Alignment.WrapText = true;

        lists.Cell(1, 1).Value = "Sections";
        for (var i = 0; i < sections.Count; i++) lists.Cell(i + 2, 1).Value = sections[i];
        lists.Cell(1, 2).Value = "Yes/No";
        lists.Cell(2, 2).Value = "Yes";
        lists.Cell(3, 2).Value = "No";
        lists.Visibility = XLWorksheetVisibility.Hidden;

        if (sections.Count > 0)
        {
            var v = items.Range(2, (int)Col.Section + 1, lastRow, (int)Col.Section + 1).CreateDataValidation();
            v.List(lists.Range(2, 1, sections.Count + 1, 1));
            Configure(v, strict: false, "New section", "This section is not on the menu yet. It will be added when you import. Continue?");
        }
        var prices = items.Range(2, priceFrom, lastRow, priceTo).CreateDataValidation();
        prices.Decimal.EqualOrGreaterThan(0);
        Configure(prices, strict: true, "Price", "Enter a number, 0 or more, without the currency.");
        var flagsFrom = priceTo + 1;
        var flags = items.Range(2, flagsFrom, lastRow, flagsFrom + 1).CreateDataValidation();
        flags.List(lists.Range(2, 2, 3, 2));
        Configure(flags, strict: true, "Yes or No", "Pick Yes or No, or leave it empty.");

        WriteGuide(guide, types, currency);
        items.SetTabActive();

        using var output = new MemoryStream();
        book.SaveAs(output);
        return output.ToArray();
    }

    private static void Configure(IXLDataValidation v, bool strict, string title, string message)
    {
        v.IgnoreBlanks = true;
        v.ShowErrorMessage = true;
        v.ErrorStyle = strict ? XLErrorStyle.Stop : XLErrorStyle.Warning;
        v.ErrorTitle = title;
        v.ErrorMessage = message;
    }

    private static void WriteGuide(IXLWorksheet guide, List<string> types, string currency)
    {
        string[] lines =
        [
            "How to fill this file",
            "",
            "• One row per dish, on the Items sheet. Only Name is required, and Price for a new dish.",
            "• Section: the menu section it goes in (Breakfast, Drinks...). A section that is not on the menu yet is added. Empty = no section.",
            "• Price: a number only, in the menu's currency (" + currency + "). 0 is allowed.",
            types.Count > 0
                ? "• \"" + TypePricePrefix + "...\" columns: the dish's own price for that order type. Leave empty to follow the type's rule."
                : "• This menu has no order types (dine in, delivery...). Add them in the menu editor first if dishes need their own price per type.",
            "• Hidden: Yes keeps the dish off the public menu. Sold out: Yes shows it as sold out. Empty = No.",
            "• A row with the same Section and Name as a dish already on the menu updates that dish; empty cells keep what it has.",
            "• Dishes that come from the POS keep their name, price and section: change those on the POS.",
            "• Pictures are added afterwards, in the menu editor.",
            "",
            "Example",
        ];
        guide.Column(1).Width = 22;
        guide.Column(2).Width = 28;
        guide.Column(3).Width = 40;
        guide.Column(4).Width = 10;
        for (var i = 0; i < lines.Length; i++)
        {
            var cell = guide.Cell(i + 1, 1);
            cell.Value = lines[i];
            if (i == 0) { cell.Style.Font.FontSize = 14; cell.Style.Font.Bold = true; }
            if (lines[i] == "Example") cell.Style.Font.Bold = true;
        }
        object[][] example =
        [
            ["Section", "Name", "Description", "Price"],
            ["Breakfast", "Manakish zaatar", "Thyme, sesame and olive oil", 2.5],
            ["Breakfast", "Labneh sandwich", "With mint and cucumber", 3],
            ["Drinks", "Fresh orange juice", "", 3.5],
        ];
        var at = lines.Length + 1;
        for (var r = 0; r < example.Length; r++)
        for (var c = 0; c < example[r].Length; c++)
        {
            var cell = guide.Cell(at + r, c + 1);
            switch (example[r][c])
            {
                case string s: cell.Value = s; break;
                case double d: cell.Value = d; break;
                case int n: cell.Value = n; break;
            }
            if (r == 0) { cell.Style.Font.Bold = true; cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF7"); }
        }
    }

    // ---- import ----

    /// <summary>
    /// One dish as the file gave it, all as text so a review can show and fix exactly what was typed ("cheap", "maybe"). The provider's
    /// console sends these back after editing them. <c>TypePrices</c> is keyed by the order type's id. <c>Row</c> is the file's row number.
    /// </summary>
    public sealed class ImportRow
    {
        public int Row { get; set; }
        public string? Section { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Price { get; set; }
        public Dictionary<string, string?>? TypePrices { get; set; }
        public string? Hidden { get; set; }
        public string? SoldOut { get; set; }
    }

    /// <summary>What happens to one row: <c>add</c>, <c>update</c>, <c>same</c> (already like this) or <c>skip</c> (a problem; see Message).</summary>
    public sealed record RowResult(ImportRow Input, string Status, string? Message, bool FromPos, string? PriceText, List<(string Type, string Price)> TypePriceTexts);

    public sealed record Issue(int Row, string Message, bool Skipped);

    public sealed class Result
    {
        public bool Applied { get; init; }
        public int Rows { get; set; }
        public int Added { get; set; }
        public int Updated { get; set; }
        public int Unchanged { get; set; }
        public int Skipped { get; set; }
        public List<string> NewSections { get; } = [];
        public List<Issue> Issues { get; } = [];
        public bool MoreIssues { get; set; }
        public List<RowResult> Items { get; } = [];
        public List<(int Id, string Name)> OrderTypes { get; } = [];
        public List<string> Sections { get; } = [];
        public string Currency { get; set; } = "$";
    }

    /// <summary>The file could not be read at all: its sentence is shown as is.</summary>
    public sealed class FileRefused(string message) : Exception(message);

    /// <summary>
    /// Reads the file and checks every row against the client's menu. With <paramref name="apply"/> the good rows are saved (a menu is
    /// made first if the client has none); without it nothing changes and the result says what would happen to each row.
    /// </summary>
    public async Task<Result> ImportAsync(Client client, Stream file, bool apply)
    {
        var menu = await db.Menus.AsNoTracking().FirstOrDefaultAsync(m => m.ClientId == client.Id);
        var types = menu is null ? [] : await db.MenuOrderTypes.AsNoTracking().Where(t => t.MenuId == menu.Id).ToListAsync();
        var rows = await ReadAsync(file, types);
        return await ImportAsync(client, rows, apply);
    }

    /// <summary>The same for rows already read (and perhaps edited in the review): checked again with the same rules, every time.</summary>
    public async Task<Result> ImportAsync(Client client, List<ImportRow> rows, bool apply)
    {
        if (rows.Count == 0) throw new FileRefused("There are no dishes to import.");
        if (rows.Count > MaxRows) throw new FileRefused($"There are more than {MaxRows} dishes. Split them into smaller files.");
        if (!apply) return await RunAsync(client, rows, apply: false);

        // New sections are saved before their dishes (a dish knows its section by id), so both go in one transaction. On SQL Server the
        // execution strategy may run the block again after a dropped connection, so each run starts from a clean slate and redoes it all.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync();
            var result = await RunAsync(client, rows, apply: true);
            await tx.CommitAsync();
            return result;
        });
    }

    private async Task<Result> RunAsync(Client client, List<ImportRow> rows, bool apply)
    {
        var menu = await db.Menus.FirstOrDefaultAsync(m => m.ClientId == client.Id);
        var types = menu is null ? [] : await db.MenuOrderTypes.Where(t => t.MenuId == menu.Id).OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToListAsync();
        var typeById = types.ToDictionary(t => t.Id.ToString(CultureInfo.InvariantCulture));
        var sections = menu is null ? [] : await db.MenuSections.Where(s => s.MenuId == menu.Id).OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();
        var sectionByName = sections.GroupBy(s => MenuCatalogMerge.Same(s.Name)).ToDictionary(g => g.Key, g => g.First());
        var items = menu is null ? [] : await db.MenuItems.Where(i => i.MenuId == menu.Id).Include(i => i.Branches).Include(i => i.TypePrices).ToListAsync();
        var known = sections.Select(s => s.Id).ToHashSet();
        // A dish whose section is gone counts as in no section, as the editor shows it.
        var itemByKey = items.GroupBy(i => Key(i.SectionId is { } sid && known.Contains(sid) ? sid : 0, i.Name)).ToDictionary(g => g.Key, g => g.First());

        var currency = menu?.Currency ?? "$";
        var result = new Result { Applied = apply, Currency = currency };
        result.OrderTypes.AddRange(types.Select(t => (t.Id, t.Name)));
        result.Sections.AddRange(sections.Select(s => s.Name));
        var now = time.GetUtcNow().UtcDateTime;
        if (apply && menu is null) menu = await MenuRules.CreateAsync(db, client, null, now);

        var newSections = new Dictionary<string, MenuSection>();
        var toAdd = new List<(MenuItem Item, MenuSection? NewSection)>();
        var firstRowOf = new Dictionary<string, int>();
        var nextSection = (sections.Count == 0 ? -1 : sections.Max(s => s.SortOrder)) + 1;
        var nextItem = (items.Count == 0 ? -1 : items.Max(i => i.SortOrder)) + 1;

        void Report(int row, string message, bool skipped)
        {
            if (result.Issues.Count < MaxIssues) result.Issues.Add(new Issue(row, message, skipped));
            else result.MoreIssues = true;
        }

        foreach (var row in rows)
        {
            result.Rows++;
            var errors = new List<string>();

            var name = (row.Name ?? "").Trim();
            if (name.Length == 0) errors.Add("Name is empty.");
            else if (name.Length > 200) errors.Add("Name is longer than 200 characters.");
            var sectionName = (row.Section ?? "").Trim();
            if (sectionName.Length > 120) errors.Add("Section is longer than 120 characters.");
            var description = (row.Description ?? "").Trim();
            if (description.Length > 500) errors.Add("Description is longer than 500 characters.");

            var price = ReadPrice(row.Price, "Price", errors);
            var typePrices = new Dictionary<int, decimal>();
            foreach (var (key, text) in row.TypePrices ?? [])
            {
                if (!typeById.TryGetValue(key, out var type)) { if (!string.IsNullOrWhiteSpace(text)) errors.Add("That order type is not on this menu any more."); continue; }
                if (ReadPrice(text, "The " + type.Name + " price", errors) is { } tp) typePrices[type.Id] = tp;
            }
            var hidden = ReadFlag(row.Hidden, "Hidden", errors);
            var soldOut = ReadFlag(row.SoldOut, "Sold out", errors);

            // Which section, and is the dish on the menu already? A section this import makes has no dishes yet.
            var sectionKey = MenuCatalogMerge.Same(sectionName);
            var isNewSection = sectionName.Length > 0 && !sectionByName.ContainsKey(sectionKey);
            var section = sectionName.Length == 0 ? null : sectionByName.GetValueOrDefault(sectionKey) ?? newSections.GetValueOrDefault(sectionKey);
            var fileKey = sectionKey + "|" + MenuCatalogMerge.Same(name);
            MenuItem? existing = null;
            if (name.Length > 0)
            {
                if (firstRowOf.TryGetValue(fileKey, out var first)) errors.Add($"This dish is on row {first} already.");
                else if (!isNewSection) existing = itemByKey.GetValueOrDefault(Key(section?.Id ?? 0, name));
            }
            if (existing is null && price is null && !errors.Any(e => e.StartsWith("Price", StringComparison.Ordinal)))
                errors.Add("Price is required for a new dish.");

            var fromPos = existing is { Branches.Count: > 0 };
            var priceText = price is { } pt ? MenuRules.Price(pt, currency) : existing is null ? null : MenuRules.Price(existing.Price, existing.Currency ?? currency);
            var typeTexts = typePrices.Select(p => (types.First(t => t.Id == p.Key).Name, MenuRules.Price(p.Value, currency))).ToList();
            if (errors.Count > 0)
            {
                result.Skipped++;
                var message = string.Join(" ", errors);
                Report(row.Row, message, true);
                result.Items.Add(new RowResult(row, "skip", message, fromPos, priceText, typeTexts));
                continue;
            }
            firstRowOf[fileKey] = row.Row;

            if (isNewSection && section is null)
            {
                section = new MenuSection { MenuId = menu?.Id ?? 0, Name = sectionName, SortOrder = nextSection++, PictureAs = MenuSectionPicture.Banner };
                newSections[sectionKey] = section;
                result.NewSections.Add(sectionName);
                if (apply) db.MenuSections.Add(section);
            }

            if (existing is null)
            {
                result.Added++;
                result.Items.Add(new RowResult(row, "add", null, false, priceText, typeTexts));
                if (!apply) continue;
                var item = new MenuItem
                {
                    MenuId = menu!.Id, Name = name, Description = description.Length > 0 ? description : null, Price = price!.Value,
                    SectionId = isNewSection ? null : section?.Id, SortOrder = nextItem++, IsHidden = hidden ?? false, IsSoldOut = soldOut ?? false,
                };
                foreach (var (typeId, value) in typePrices) item.TypePrices.Add(new MenuItemTypePrice { OrderTypeId = typeId, Price = value });
                toAdd.Add((item, isNewSection ? section : null));
                continue;
            }

            // An update: only the cells that were filled in, and never what the POS owns.
            var changed = false;
            string? note = null;
            if (price is { } p && p != existing.Price)
            {
                if (fromPos)
                {
                    note = "This dish comes from the POS, so its price stays " + MenuRules.Price(existing.Price, existing.Currency ?? currency) + ". Change it on the POS.";
                    priceText = MenuRules.Price(existing.Price, existing.Currency ?? currency);
                    Report(row.Row, note, false);
                }
                else { changed = true; if (apply) existing.Price = p; }
            }
            if (description.Length > 0 && description != existing.Description) { changed = true; if (apply) existing.Description = description; }
            if (hidden is { } h && h != existing.IsHidden) { changed = true; if (apply) existing.IsHidden = h; }
            if (soldOut is { } so && so != existing.IsSoldOut) { changed = true; if (apply) existing.IsSoldOut = so; }
            foreach (var (typeId, value) in typePrices)
            {
                var own = existing.TypePrices.FirstOrDefault(x => x.OrderTypeId == typeId);
                if (own?.Price == value) continue;
                changed = true;
                if (!apply) continue;
                if (own is null) existing.TypePrices.Add(new MenuItemTypePrice { OrderTypeId = typeId, Price = value });
                else own.Price = value;
            }
            if (changed) result.Updated++; else result.Unchanged++;
            result.Items.Add(new RowResult(row, changed ? "update" : "same", note, fromPos, priceText, typeTexts));
        }

        if (apply && menu is not null)
        {
            menu.UpdatedAt = now;
            await db.SaveChangesAsync(); // the new sections get their ids
            foreach (var (item, newSection) in toAdd)
            {
                if (newSection is not null) item.SectionId = newSection.Id;
                db.MenuItems.Add(item);
            }
            await db.SaveChangesAsync();
        }
        return result;
    }

    private static string Key(int sectionId, string name) => sectionId.ToString(CultureInfo.InvariantCulture) + "|" + MenuCatalogMerge.Same(name);

    private static decimal? ReadPrice(string? text, string what, List<string> errors)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return null;
        if (!TryParsePrice(t, out var value)) { errors.Add($"{what} \"{t}\" is not a number."); return null; }
        if (value is < 0 or > MaxPrice) { errors.Add($"{what} must be 0 or more."); return null; }
        return Math.Round(value, 4);
    }

    /// <summary>"12.50", "$12.50", "450,000 LBP", "12,5": the currency is dropped; a comma is a thousands mark before three digits, else the decimal point.</summary>
    internal static bool TryParsePrice(string text, out decimal value)
    {
        var digits = new string(text.Where(ch => char.IsDigit(ch) || ch is '.' or ',' or '-').ToArray());
        if (digits.Contains('.')) digits = digits.Replace(",", "");
        else if (digits.Contains(','))
        {
            var parts = digits.Split(',');
            digits = parts.Skip(1).All(p => p.Length == 3) ? string.Concat(parts) : string.Join(".", parts);
        }
        return decimal.TryParse(digits, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)
               && digits.Count(ch => ch == '.') <= 1;
    }

    private static bool? ReadFlag(string? text, string what, List<string> errors)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return null;
        if (TrueWords.Contains(t, StringComparer.OrdinalIgnoreCase)) return true;
        if (FalseWords.Contains(t, StringComparer.OrdinalIgnoreCase)) return false;
        errors.Add($"{what} \"{t}\" should be Yes or No.");
        return null;
    }

    /// <summary>Header spellings are compared on letters and digits only: "Sold out", "sold_out" and "SoldOut" are the same.</summary>
    private static string Norm(string header) => new(header.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static async Task<List<ImportRow>> ReadAsync(Stream file, List<MenuOrderType> types)
    {
        using var copy = new MemoryStream();
        await file.CopyToAsync(copy);
        if (copy.Length == 0) throw new FileRefused("The file is empty.");
        if (copy.Length > MaxBytes) throw new FileRefused("The file is larger than 10 MB. Split it into smaller files.");
        copy.Position = 0;

        XLWorkbook book;
        try { book = new XLWorkbook(copy); }
        catch (Exception)
        {
            throw new FileRefused("This is not an Excel file that can be read. Save it as an Excel workbook (.xlsx) and try again.");
        }
        using (book)
        {
            var sheet = book.Worksheets.FirstOrDefault(w => w.Name.Equals(ItemsSheet, StringComparison.OrdinalIgnoreCase))
                        ?? book.Worksheets.FirstOrDefault(w => w.Visibility == XLWorksheetVisibility.Visible);
            var used = sheet?.RangeUsed();
            if (sheet is null || used is null) throw new FileRefused("The Items sheet is empty.");

            var headerRow = used.FirstRow().RowNumber();
            var lastCol = used.LastColumn().ColumnNumber();
            var columns = new Dictionary<Col, int>();
            var typeColumns = new Dictionary<int, string>(); // column -> order type id
            for (var c = 1; c <= lastCol; c++)
            {
                var raw = sheet.Cell(headerRow, c).GetString().Trim();
                if (raw.Length == 0) continue;
                if (raw.StartsWith(TypePricePrefix.TrimEnd(), StringComparison.OrdinalIgnoreCase) && raw.Contains(':'))
                {
                    var typeName = raw[(raw.IndexOf(':') + 1)..].Trim();
                    if (typeName.Length > 0)
                    {
                        var type = types.FirstOrDefault(t => MenuCatalogMerge.Same(t.Name) == MenuCatalogMerge.Same(typeName))
                            ?? throw new FileRefused($"The column \"{TypePricePrefix}{typeName}\" names no order type of this menu. Download the template again, or add that order type in the menu editor first.");
                        typeColumns[c] = type.Id.ToString(CultureInfo.InvariantCulture);
                        continue;
                    }
                }
                if (HeaderAliases.TryGetValue(Norm(raw), out var col)) columns.TryAdd(col, c);
            }
            if (!columns.ContainsKey(Col.Name))
                throw new FileRefused("The first row must be the header, with at least a \"Name\" column. Start from the template.");

            var rows = new List<ImportRow>();
            var lastRow = used.LastRow().RowNumber();
            for (var r = headerRow + 1; r <= lastRow; r++)
            {
                string Text(Col col) => columns.TryGetValue(col, out var c) ? CellText(sheet.Cell(r, c)) : "";
                var row = new ImportRow
                {
                    Row = r, Section = Text(Col.Section), Name = Text(Col.Name), Description = Text(Col.Description),
                    Price = Text(Col.Price), Hidden = Text(Col.Hidden), SoldOut = Text(Col.SoldOut),
                };
                foreach (var (c, typeId) in typeColumns)
                {
                    var text = CellText(sheet.Cell(r, c));
                    if (text.Length > 0) (row.TypePrices ??= [])[typeId] = text;
                }
                var blank = row.Section!.Length == 0 && row.Name!.Length == 0 && row.Description!.Length == 0 && row.Price!.Length == 0 && row.TypePrices is null;
                if (blank) continue;
                rows.Add(row);
                if (rows.Count > MaxRows) throw new FileRefused($"The file has more than {MaxRows} dishes. Split it into smaller files.");
            }
            if (rows.Count == 0) throw new FileRefused("The Items sheet has no dishes under its header.");
            return rows;
        }
    }

    /// <summary>A cell as text; a formula's result, never the formula. A number is written plainly ("2.5"), whatever the cell's format.</summary>
    private static string CellText(IXLCell cell)
    {
        if (cell.DataType == XLDataType.Number) return cell.GetDouble().ToString(CultureInfo.InvariantCulture);
        if (cell.DataType == XLDataType.Boolean) return cell.GetBoolean() ? "yes" : "no";
        try { return cell.GetFormattedString().Trim(); }
        catch { return cell.CachedValue.ToString().Trim(); }
    }
}
