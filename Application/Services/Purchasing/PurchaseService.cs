using Application.IServices.Base;
using Application.IServices.Products;
using Application.IServices.Purchasing;
using Core.Auditing;
using Core.Entities.Products;
using Core.Entities.Purchasing;
using Core.Entities.Sales;
using Core.Enum;
using Core.ICache;
using Core.IRepositories.Products;
using Core.Time;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Purchasing;

public class PurchaseService : IPurchaseService
{
    private const int MoneyDecimals = 2;

    private readonly ErpDbContext _db;
    private readonly IProductRepository _products;
    private readonly IProductHistoryService _history;
    private readonly ICurrencyConversionService _conversion;
    private readonly ICacheService _cache;
    private readonly IAuditTrail _audit;
    private readonly ILogger<PurchaseService> _logger;

    public PurchaseService(
        ErpDbContext db,
        IProductRepository products,
        IProductHistoryService history,
        ICurrencyConversionService conversion,
        ICacheService cache,
        IAuditTrail audit,
        ILogger<PurchaseService> logger)
    {
        _db = db;
        _products = products;
        _history = history;
        _conversion = conversion;
        _cache = cache;
        _audit = audit;
        _logger = logger;
    }

    // ---- save -------------------------------------------------------------------------------------------------

    public async Task<Purchase> CreateAsync(NewPurchase request, int userId)
    {
        if (request.Lines.Count == 0)
            throw new PurchaseRuleException(PurchaseRuleException.NoLines, "A purchase needs at least one product.");
        if (request.Lines.GroupBy(l => l.ProductId).Any(g => g.Count() > 1))
            throw new PurchaseRuleException(PurchaseRuleException.DuplicateProduct, "A product appears twice on the purchase.");
        if (request.Lines.Any(l => l.Quantity <= 0 || l.UnitCost < 0) || request.DiscountPercent is < 0 or > 100 || request.AmountPaid < 0)
            throw new PurchaseRuleException(PurchaseRuleException.BadAmount, "Quantities must be above zero and amounts cannot be negative.");

        var today = DateTime.Now.Date;
        if (request.PurchaseDay.Date > today)
            throw new PurchaseRuleException(PurchaseRuleException.FutureDate, "The purchase date cannot be in the future.");

        var supplier = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SupplierId && !s.IsDeleted)
            ?? throw new PurchaseRuleException(PurchaseRuleException.UnknownSupplier, "That supplier no longer exists.");
        if (!supplier.IsActive)
            throw new PurchaseRuleException(PurchaseRuleException.InactiveSupplier, "That supplier is inactive.", supplier.Name);

        var ids = request.Lines.Select(l => l.ProductId).ToList();
        var products = await _db.Products.AsNoTracking().Where(p => ids.Contains(p.Id) && !p.IsDeleted).ToDictionaryAsync(p => p.Id);
        var missing = ids.FirstOrDefault(id => !products.ContainsKey(id));
        if (missing != 0)
            throw new PurchaseRuleException(PurchaseRuleException.UnknownProduct, "A product on the purchase no longer exists.", missing.ToString());

        var items = request.Lines.Select(line =>
        {
            var product = products[line.ProductId];
            return new PurchaseItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Barcode = product.Barcode,
                Unit = product.Unit,
                Quantity = line.Quantity,
                UnitCost = line.UnitCost,
                LineTotal = Math.Round(line.Quantity * line.UnitCost, MoneyDecimals),
                UpdatedCostPrice = line.UpdateCostPrice,
            };
        }).ToList();

        var subtotal = items.Sum(i => i.LineTotal);
        var discount = Math.Round(subtotal * request.DiscountPercent / 100m, MoneyDecimals);
        var total = subtotal - discount;
        if (request.AmountPaid > total)
            throw new PurchaseRuleException(PurchaseRuleException.PaidTooMuch, "The amount paid is more than the purchase total.");

        // Paying from the till needs the till to be open: checked up front so nothing is half-saved.
        CashDrawerSession? drawer = null;
        if (request.PayFromDrawer && request.AmountPaid > 0)
        {
            drawer = await _db.Set<CashDrawerSession>().AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId && s.IsOpen && !s.IsDeleted)
                ?? throw new PurchaseRuleException(PurchaseRuleException.DrawerNotOpen, "Open your cash drawer first to pay from it.");
        }

        // The picked day at midday, so it stays on that day in any time zone; today keeps the real time.
        var moment = request.PurchaseDay.Date == today ? DateTime.UtcNow : request.PurchaseDay.Date.AddHours(12).LocalToUtc();

        var purchase = new Purchase
        {
            SupplierId = supplier.Id,
            UserId = userId,
            CurrencyId = supplier.CurrencyId,
            PurchaseDate = moment,
            SupplierReference = Clean(request.SupplierReference),
            Notes = Clean(request.Notes),
            Subtotal = subtotal,
            DiscountPercent = request.DiscountPercent,
            DiscountAmount = discount,
            Total = total,
            AmountPaid = request.AmountPaid,
            Status = PurchaseStatus.Received,
            Items = items,
        };

        var costChanges = new List<(ProductSnapshot Before, int ProductId)>();

        // Same shape as checkout: one transaction around every write, created inside the execution strategy so a retry re-runs all of it.
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            costChanges.Clear();
            await using var transaction = await _db.Database.BeginTransactionAsync();

            _db.Purchases.Add(purchase);
            await _db.SaveChangesAsync();

            var number = purchase.Number;
            foreach (var item in purchase.Items)
            {
                var before = ProductSnapshot.Of(products[item.ProductId]);
                await _products.RestoreStockAsync(item.ProductId, item.Quantity);

                if (item.UpdatedCostPrice)
                {
                    var product = products[item.ProductId];
                    var cost = Math.Round(await _conversion.ConvertAmountAsync(item.UnitCost, purchase.CurrencyId, product.CurrencyId, supplier.Id), 4);
                    await _db.Products.Where(p => p.Id == product.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.CostPrice, (decimal?)cost).SetProperty(p => p.UpdatedAt, DateTime.UtcNow));
                    costChanges.Add((before with { StockQuantity = before.StockQuantity + item.Quantity }, item.ProductId));
                }
            }

            var owed = purchase.Total - purchase.AmountPaid;
            var tracked = await _db.Suppliers.FirstAsync(s => s.Id == supplier.Id);
            if (purchase.Total > 0)
                _db.SupplierDebtTransactions.Add(new SupplierDebtTransaction(supplier.Id, userId, purchase.Total, SupplierDebtTransactionType.Purchase,
                    LedgerNote(number, purchase.SupplierReference, "goods received")));
            if (purchase.AmountPaid > 0)
                _db.SupplierDebtTransactions.Add(new SupplierDebtTransaction(supplier.Id, userId, -purchase.AmountPaid, SupplierDebtTransactionType.Payment,
                    LedgerNote(number, purchase.SupplierReference, "paid on receipt")));
            tracked.Balance += owed;

            // What was paid in cash leaves the drawer: a cash drop on the open session lowers its expected cash, shows on the
            // drawer page and the Z-report, and is in the shop's main currency whatever currency the supplier is paid in.
            CashDrop? drop = null;
            if (drawer is not null)
            {
                var mainCurrencyId = await _conversion.GetSystemDefaultCurrencyIdAsync();
                var inMain = Math.Round(await _conversion.ConvertAmountAsync(purchase.AmountPaid, purchase.CurrencyId, mainCurrencyId, supplier.Id), MoneyDecimals);
                drop = new CashDrop(drawer.Id, userId, inMain, $"Supplier payment {number}", purchase.SupplierReference is null ? supplier.Name : $"{supplier.Name} ({purchase.SupplierReference})");
                _db.Set<CashDrop>().Add(drop);
            }

            await _db.SaveChangesAsync();
            if (drop is not null)
            {
                purchase.CashDropId = drop.Id;
                await _db.SaveChangesAsync();
            }
            await transaction.CommitAsync();
        });

        // After the commit: the history lines and the POS cache are conveniences, they must never undo a saved purchase.
        foreach (var item in purchase.Items)
        {
            await _history.RecordMovementAsync(item.ProductId, ProductHistoryType.Received, item.Quantity, purchaseId: purchase.Id,
                reason: purchase.SupplierReference);
        }
        foreach (var (before, productId) in costChanges)
        {
            var after = await _db.Products.AsNoTracking().FirstAsync(p => p.Id == productId);
            await _history.RecordChangesAsync(before, after, new ProductChangeContext(Reason: purchase.Number));
        }
        await _cache.ClearPOSCachesAsync();

        _audit.LogBusiness(AuditActions.PurchaseReceived,
            $"Purchase {purchase.Number} from '{supplier.Name}' received: {items.Count} product(s), total {total:0.##}, paid {request.AmountPaid:0.##}{(purchase.CashDropId is not null ? " from the cash drawer" : "")}",
            "Purchase", entityId: purchase.Id, entityLabel: purchase.Number,
            details: new
            {
                purchaseId = purchase.Id,
                supplierId = supplier.Id,
                supplier = supplier.Name,
                supplierReference = purchase.SupplierReference,
                subtotal,
                discount,
                total,
                paid = request.AmountPaid,
                cashDropId = purchase.CashDropId,
                lines = items.Select(i => new { i.ProductId, i.ProductName, i.Quantity, i.UnitCost, i.UpdatedCostPrice }),
            });

        _logger.LogInformation("Purchase {Number} from supplier {SupplierId} received by user {UserId}: {Lines} line(s), total {Total}",
            purchase.Number, supplier.Id, userId, items.Count, total);

        return (await GetAsync(purchase.Id))!;
    }

    // ---- cancel -----------------------------------------------------------------------------------------------

    public async Task<Purchase> CancelAsync(int purchaseId, int userId, string? reason)
    {
        var cleanReason = Clean(reason) ?? "No reason given";
        var moved = new List<(int ProductId, decimal Quantity)>();
        var number = Purchase.FormatNumber(purchaseId);
        var supplierName = string.Empty;

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            moved.Clear();
            await using var transaction = await _db.Database.BeginTransactionAsync();

            var purchase = await _db.Purchases.Include(p => p.Items).Include(p => p.Supplier).FirstOrDefaultAsync(p => p.Id == purchaseId)
                ?? throw new ArgumentException($"Purchase {purchaseId} not found.");
            number = purchase.Number;
            supplierName = purchase.Supplier.Name;
            if (purchase.Status == PurchaseStatus.Cancelled)
                throw new PurchaseRuleException(PurchaseRuleException.AlreadyCancelled, "That purchase is already cancelled.", number);

            foreach (var item in purchase.Items)
            {
                // Refuses when part of the goods was already sold: stock must never go below zero because of a cancel.
                if (!await _products.TryDeductStockAsync(item.ProductId, item.Quantity))
                    throw new PurchaseRuleException(PurchaseRuleException.StockSold,
                        $"Not enough '{item.ProductName}' left in stock to take back {item.Quantity:0.####}; part of it was already sold.", item.ProductName);
                moved.Add((item.ProductId, item.Quantity));
            }

            // Only the goods are undone. A payment that was made stays in the ledger as a real payment, so a purchase paid in
            // full and then cancelled leaves the supplier owing that money back.
            if (purchase.Total > 0)
                _db.SupplierDebtTransactions.Add(new SupplierDebtTransaction(purchase.SupplierId, userId, -purchase.Total, SupplierDebtTransactionType.ManualAdjustment,
                    LedgerNote(number, purchase.SupplierReference, "cancelled")));
            purchase.Supplier.Balance -= purchase.Total;

            purchase.Status = PurchaseStatus.Cancelled;
            purchase.CancelledAt = DateTime.UtcNow;
            purchase.CancelledByUserId = userId;
            purchase.CancelReason = cleanReason;
            purchase.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        foreach (var (productId, quantity) in moved)
        {
            await _history.RecordMovementAsync(productId, ProductHistoryType.StockRemoved, -quantity, purchaseId: purchaseId,
                reason: $"{number} cancelled: {cleanReason}");
        }
        await _cache.ClearPOSCachesAsync();

        _audit.LogBusiness(AuditActions.PurchaseCancelled,
            $"Purchase {number} from '{supplierName}' cancelled: {moved.Count} product(s) taken back out of stock",
            "Purchase", entityId: purchaseId, entityLabel: number,
            details: new { purchaseId, reason = cleanReason, lines = moved.Select(m => new { m.ProductId, m.Quantity }) });

        _logger.LogInformation("Purchase {Number} cancelled by user {UserId}", number, userId);
        return (await GetAsync(purchaseId))!;
    }

    // ---- read -------------------------------------------------------------------------------------------------

    public async Task<Purchase?> GetAsync(int purchaseId)
        => await _db.Purchases.AsNoTracking()
            .Include(p => p.Items)
            .Include(p => p.Supplier)
            .Include(p => p.Currency)
            .Include(p => p.User)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == purchaseId);

    public async Task<(List<PurchaseListRow> Rows, int Total)> SearchAsync(PurchaseFilter filter, string? sort, bool descending, int skip, int take)
    {
        var query = Filtered(filter);
        var total = await query.CountAsync();

        var ordered = sort?.ToLowerInvariant() switch
        {
            "number" => Order(query, p => p.Id, descending),
            "supplier" => Order(query, p => p.Supplier.Name, descending),
            "total" => Order(query, p => p.Total, descending),
            "status" => Order(query, p => p.Status, descending),
            "reference" => Order(query, p => p.SupplierReference ?? "", descending),
            _ => Order(query, p => p.PurchaseDate, descending),
        };

        var rows = await ordered.ThenByDescending(p => p.Id)
            .Skip(skip).Take(take)
            .Select(p => new PurchaseListRow(p.Id, p.PurchaseDate, p.SupplierId, p.Supplier.Name, p.SupplierReference,
                p.Currency.Notation, p.Items.Count, p.Total, p.AmountPaid, p.Status))
            .ToListAsync();
        return (rows, total);

        static IOrderedQueryable<Purchase> Order<TKey>(IQueryable<Purchase> source, System.Linq.Expressions.Expression<Func<Purchase, TKey>> key, bool desc)
            => desc ? source.OrderByDescending(key) : source.OrderBy(key);
    }

    public async Task<PurchaseSummary> SummaryAsync(PurchaseFilter filter)
    {
        // The status filter does not apply: the tiles always say how many were cancelled next to what was bought.
        var query = Filtered(filter with { Status = null });
        var counts = await query.GroupBy(p => p.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();

        // SQLite cannot sum decimals in SQL, so the money is summed here. A filtered range holds a few hundred rows at most.
        var received = await query.Where(p => p.Status == PurchaseStatus.Received)
            .Select(p => new { p.CurrencyId, p.Currency.Notation, p.Total, p.AmountPaid })
            .ToListAsync();
        var totals = received.GroupBy(p => new { p.CurrencyId, p.Notation })
            .OrderBy(g => g.Key.CurrencyId)
            .Select(g => new PurchaseCurrencyTotal(g.Key.CurrencyId, g.Key.Notation, g.Sum(p => p.Total), g.Sum(p => p.AmountPaid)))
            .ToList();

        return new PurchaseSummary(
            counts.Where(c => c.Status == PurchaseStatus.Received).Sum(c => c.Count),
            counts.Where(c => c.Status == PurchaseStatus.Cancelled).Sum(c => c.Count),
            totals);
    }

    public async Task<List<Purchase>> ExportAsync(PurchaseFilter filter)
        => await Filtered(filter)
            .Include(p => p.Supplier).Include(p => p.Currency).Include(p => p.Items)
            .AsSplitQuery()
            .OrderByDescending(p => p.PurchaseDate).ThenByDescending(p => p.Id)
            .ToListAsync();

    public async Task<Dictionary<int, string>> UserNamesAsync(IEnumerable<int> userIds)
    {
        var ids = userIds.Distinct().ToList();
        var users = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.FirstName, u.LastName, u.Username }).ToListAsync();
        return users.ToDictionary(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } full ? full : u.Username);
    }

    // ---- helpers ----------------------------------------------------------------------------------------------

    private IQueryable<Purchase> Filtered(PurchaseFilter filter)
    {
        var query = _db.Purchases.AsNoTracking().AsQueryable();
        if (filter.SupplierId is { } supplierId) query = query.Where(p => p.SupplierId == supplierId);
        if (filter.Status is { } status) query = query.Where(p => p.Status == status);
        if (filter.FromUtc is { } from) query = query.Where(p => p.PurchaseDate >= from);
        if (filter.ToUtc is { } to) query = query.Where(p => p.PurchaseDate <= to);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            var number = term.StartsWith("PUR-", StringComparison.OrdinalIgnoreCase) ? term[4..] : term;
            var id = int.TryParse(number, out var parsed) ? parsed : -1;
            var pattern = $"%{term}%";
            query = query.Where(p =>
                p.Id == id ||
                EF.Functions.Like(p.Supplier.Name, pattern) ||
                (p.SupplierReference != null && EF.Functions.Like(p.SupplierReference, pattern)));
        }

        return query;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string LedgerNote(string number, string? reference, string what)
        => string.IsNullOrEmpty(reference) ? $"{number}: {what}" : $"{number} ({reference}): {what}";
}
