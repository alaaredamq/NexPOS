using Application.IServices.Sales;
using Core.Auditing;
using Core.Entities.Sales;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Sales;

public sealed class WalletService : IWalletService
{
    public const string WhishMethodName = "Whish";

    private readonly ErpDbContext _db;
    private readonly IAuditTrail _audit;
    private readonly ILogger<WalletService> _logger;

    public WalletService(ErpDbContext db, IAuditTrail audit, ILogger<WalletService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    // ---- called by checkout and the void ----------------------------------------------------------------------------

    public async Task<WalletTransaction?> RecordSaleAsync(int paymentMethodId, int orderId, decimal amount, int currencyId, string? reference, int userId)
    {
        var wallet = await _db.Wallets.AsNoTracking()
            .FirstOrDefaultAsync(w => w.PaymentMethodId == paymentMethodId && w.IsActive && !w.IsDeleted);
        if (wallet is null) return null;

        var row = new WalletTransaction(wallet.Id, WalletTransactionType.Sale, amount, currencyId, userId, Clean(reference, 100))
        {
            OrderId = orderId,
        };
        _db.WalletTransactions.Add(row);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Sale {OrderId} of {Amount} put into wallet {WalletId}", orderId, amount, wallet.Id);
        return row;
    }

    public async Task<int> ReverseSaleAsync(int orderId, int userId, string reason)
    {
        var rows = await _db.WalletTransactions
            .Where(t => t.OrderId == orderId)
            .ToListAsync();

        var reversed = rows.Where(t => t.ReversesId != null).Select(t => t.ReversesId!.Value).ToHashSet();
        var open = rows.Where(t => t.Type == WalletTransactionType.Sale && !reversed.Contains(t.Id)).ToList();
        foreach (var sale in open)
        {
            _db.WalletTransactions.Add(new WalletTransaction(sale.WalletId, WalletTransactionType.SaleReversal, -sale.Amount, sale.CurrencyId, userId,
                sale.Reference, Clean(reason, 500))
            {
                OrderId = orderId,
                ReversesId = sale.Id,
            });
        }

        if (open.Count > 0) await _db.SaveChangesAsync();
        return open.Count;
    }

    public async Task<IReadOnlyDictionary<int, Wallet>> GetActiveByPaymentMethodAsync() =>
        await _db.Wallets.AsNoTracking()
            .Where(w => w.IsActive && !w.IsDeleted)
            .ToDictionaryAsync(w => w.PaymentMethodId);

    // ---- the wallet page ---------------------------------------------------------------------------------------------

    public async Task<List<Wallet>> GetAllAsync() =>
        await _db.Wallets.AsNoTracking()
            .Include(w => w.PaymentMethod)
            .Where(w => !w.IsDeleted)
            .OrderBy(w => w.Id)
            .ToListAsync();

    public async Task<Wallet?> GetAsync(int id) =>
        await _db.Wallets.AsNoTracking()
            .Include(w => w.PaymentMethod)
            .FirstOrDefaultAsync(w => w.Id == id && !w.IsDeleted);

    public async Task<List<WalletBalance>> GetBalancesAsync(int walletId)
    {
        // SQLite cannot SUM decimals in the database, so the amounts are added up here. A wallet has a few rows a day; this stays small.
        var rows = await _db.WalletTransactions.AsNoTracking()
            .Where(t => t.WalletId == walletId)
            .Select(t => new { t.CurrencyId, t.Amount, t.IsChecked })
            .ToListAsync();

        var currencies = await _db.Currencies.AsNoTracking().ToListAsync();

        // Every active currency gets a line, so a new wallet shows "0" for dollars and pounds instead of nothing.
        var ids = currencies.Where(c => c.IsActive).Select(c => c.Id).Union(rows.Select(r => r.CurrencyId)).Distinct();
        return ids
            .Select(id =>
            {
                var currency = currencies.FirstOrDefault(c => c.Id == id);
                var mine = rows.Where(r => r.CurrencyId == id).ToList();
                var unchecked_ = mine.Where(r => !r.IsChecked).ToList();
                return new WalletBalance(id, currency?.Code ?? "?", currency?.Notation ?? "", mine.Sum(r => r.Amount),
                    unchecked_.Count, unchecked_.Sum(r => r.Amount));
            })
            .OrderByDescending(b => currencies.FirstOrDefault(c => c.Id == b.CurrencyId)?.IsDefault ?? false)
            .ThenBy(b => b.CurrencyId)
            .ToList();
    }

    public async Task<Wallet> CreateWhishAsync(string name, string? accountNumber, int userId)
    {
        var method = await _db.PaymentMethods
            .FirstOrDefaultAsync(m => !m.IsDeleted && m.Name.ToLower() == WhishMethodName.ToLower());

        if (method is not null && await _db.Wallets.AnyAsync(w => w.PaymentMethodId == method.Id && !w.IsDeleted))
            throw new InvalidOperationException("A wallet already uses the Whish payment method.");

        var strategy = _db.Database.CreateExecutionStrategy();
        var wallet = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            if (method is null)
            {
                method = new PaymentMethod(WhishMethodName);
                _db.PaymentMethods.Add(method);
                await _db.SaveChangesAsync();
            }

            var created = new Wallet(Clean(name, 100) ?? WhishMethodName, WalletProvider.Whish, method.Id, Clean(accountNumber, 50));
            _db.Wallets.Add(created);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            return created;
        });

        _audit.LogBusiness(AuditActions.WalletCreated, $"Whish wallet '{wallet.Name}' set up (payment method '{method!.Name}')", nameof(Wallet),
            entityId: wallet.Id, entityLabel: wallet.Name, details: new { wallet.Id, wallet.Name, paymentMethodId = method.Id, userId });

        wallet.PaymentMethod = method;
        return wallet;
    }

    public async Task<Wallet?> UpdateAsync(int id, string name, string? accountNumber, bool isActive)
    {
        var wallet = await _db.Wallets.Include(w => w.PaymentMethod).FirstOrDefaultAsync(w => w.Id == id && !w.IsDeleted);
        if (wallet is null) return null;

        wallet.Name = Clean(name, 100) ?? wallet.Name;
        wallet.AccountNumber = Clean(accountNumber, 50);
        wallet.IsActive = isActive;
        wallet.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return wallet;
    }

    public Task<WalletTransactionPage> GetTransactionAsync(int walletId, int transactionId) =>
        GetTransactionsAsync(walletId, new WalletTransactionFilter(), 0, 1, transactionId);

    public Task<WalletTransactionPage> GetTransactionsAsync(int walletId, WalletTransactionFilter filter, int skip, int take) =>
        GetTransactionsAsync(walletId, filter, skip, take, null);

    private async Task<WalletTransactionPage> GetTransactionsAsync(int walletId, WalletTransactionFilter filter, int skip, int take, int? onlyId)
    {
        var query = _db.WalletTransactions.AsNoTracking().Where(t => t.WalletId == walletId);
        if (onlyId is { } id) query = query.Where(t => t.Id == id);

        if (filter.FromUtc is { } from) query = query.Where(t => t.OccurredAt >= from);
        if (filter.ToUtc is { } to) query = query.Where(t => t.OccurredAt <= to);
        if (filter.Type is { } type) query = query.Where(t => t.Type == type);
        if (filter.IsChecked is { } isChecked) query = query.Where(t => t.IsChecked == isChecked);
        if (filter.CurrencyId is { } currencyId) query = query.Where(t => t.CurrencyId == currencyId);

        var search = filter.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var like = search.ToLower();
            var orderIds = await _db.Receipts.AsNoTracking()
                .Where(r => r.ReceiptNumber.ToLower().Contains(like))
                .Select(r => r.OrderId)
                .Take(200)
                .ToListAsync();
            query = query.Where(t =>
                (t.Reference != null && t.Reference.ToLower().Contains(like))
                || (t.Notes != null && t.Notes.ToLower().Contains(like))
                || (t.OrderId != null && orderIds.Contains(t.OrderId.Value)));
        }

        var total = await query.CountAsync();
        var rows = await query
            .Include(t => t.Currency)
            .Include(t => t.User)
            .Include(t => t.Supplier)
            .OrderByDescending(t => t.OccurredAt).ThenByDescending(t => t.Id)
            .Skip(skip).Take(take)
            .ToListAsync();

        var saleOrders = rows.Where(t => t.OrderId != null).Select(t => t.OrderId!.Value).Distinct().ToList();
        var receipts = saleOrders.Count == 0
            ? new Dictionary<int, string>()
            : (await _db.Receipts.AsNoTracking()
                    .Where(r => saleOrders.Contains(r.OrderId) && !r.IsDeleted)
                    .Select(r => new { r.OrderId, r.Id, r.ReceiptNumber })
                    .ToListAsync())
                .GroupBy(r => r.OrderId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Id).First().ReceiptNumber);

        return new WalletTransactionPage(rows, total, receipts);
    }

    public async Task<WalletTransaction> AddMovementAsync(int walletId, WalletMovement movement, int userId)
    {
        if (movement.Type is WalletTransactionType.Sale or WalletTransactionType.SaleReversal)
            throw new ArgumentException("Sales are put into the wallet by checkout, not by hand.", nameof(movement));
        if (movement.SignedAmount == 0)
            throw new ArgumentException("The amount cannot be zero.", nameof(movement));

        var wallet = await _db.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == walletId && !w.IsDeleted)
            ?? throw new KeyNotFoundException($"Wallet {walletId} not found.");

        if (!await _db.Currencies.AnyAsync(c => c.Id == movement.CurrencyId))
            throw new ArgumentException("Pick the currency of the money.", "currencyId");
        if (movement.Type == WalletTransactionType.SupplierPayment && movement.SupplierId is { } supplierId
            && !await _db.Suppliers.AnyAsync(s => s.Id == supplierId && !s.IsDeleted))
            throw new ArgumentException("That supplier no longer exists.", "supplierId");

        var row = new WalletTransaction(walletId, movement.Type, movement.SignedAmount, movement.CurrencyId, userId,
            Clean(movement.Reference, 100), Clean(movement.Notes, 500))
        {
            SupplierId = movement.Type == WalletTransactionType.SupplierPayment ? movement.SupplierId : null,
        };
        if (movement.OccurredAtUtc is { } at) row.OccurredAt = at;

        _db.WalletTransactions.Add(row);
        await _db.SaveChangesAsync();

        _audit.LogBusiness(AuditActions.WalletMovement,
            $"{movement.Type} of {movement.SignedAmount:0.##} recorded in wallet '{wallet.Name}'", nameof(WalletTransaction),
            entityId: row.Id, entityLabel: wallet.Name,
            details: new { walletId, type = movement.Type.ToString(), amount = movement.SignedAmount, movement.CurrencyId, row.Reference, row.SupplierId });
        return row;
    }

    public async Task<int> SetCheckedAsync(int walletId, IReadOnlyCollection<int> transactionIds, bool isChecked, int userId)
    {
        if (transactionIds.Count == 0) return 0;

        var rows = await _db.WalletTransactions
            .Where(t => t.WalletId == walletId && transactionIds.Contains(t.Id) && t.IsChecked != isChecked)
            .ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var row in rows)
        {
            row.IsChecked = isChecked;
            row.CheckedAt = isChecked ? now : null;
            row.CheckedByUserId = isChecked ? userId : null;
        }

        if (rows.Count > 0)
        {
            await _db.SaveChangesAsync();
            _audit.LogBusiness(AuditActions.WalletChecked,
                $"{rows.Count} wallet row(s) marked as {(isChecked ? "checked" : "not checked")}", nameof(WalletTransaction),
                details: new { walletId, isChecked, ids = rows.Select(r => r.Id).ToList() });
        }

        return rows.Count;
    }

    private static string? Clean(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
