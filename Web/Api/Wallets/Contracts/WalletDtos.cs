using System.ComponentModel.DataAnnotations;

namespace Web.Api.Wallets.Contracts;

/// <summary>A wallet (today: the shop's Whish Money account) with the money it holds.</summary>
public sealed record WalletDto
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    /// <summary><c>Whish</c>.</summary>
    public required string Provider { get; init; }

    /// <summary>The shop's number on the provider (the phone customers send Whish money to).</summary>
    public string? AccountNumber { get; init; }

    /// <summary>The payment method whose sales land here (the till offers it at checkout).</summary>
    public required int PaymentMethodId { get; init; }

    public required string PaymentMethodName { get; init; }

    public required bool IsActive { get; init; }

    /// <summary>One line per currency: the default currency first.</summary>
    public required IReadOnlyList<WalletBalanceDto> Balances { get; init; }
}

public sealed record WalletBalanceDto
{
    public required int CurrencyId { get; init; }

    public required string CurrencyCode { get; init; }

    public required string CurrencyNotation { get; init; }

    /// <summary>Money in minus money out, over the whole history.</summary>
    public required decimal Balance { get; init; }

    /// <summary>Rows not yet ticked off against the provider's statement.</summary>
    public required int UncheckedCount { get; init; }

    public required decimal UncheckedAmount { get; init; }
}

/// <summary>One movement of money. <see cref="Amount"/> is signed: positive is money in.</summary>
public sealed record WalletTransactionDto
{
    public required int Id { get; init; }

    /// <summary><c>Sale</c>, <c>SaleReversal</c>, <c>Deposit</c>, <c>Withdrawal</c>, <c>SupplierPayment</c>, <c>Fee</c> or <c>Adjustment</c>.</summary>
    public required string Type { get; init; }

    public required decimal Amount { get; init; }

    public required int CurrencyId { get; init; }

    public required string CurrencyNotation { get; init; }

    public string? Reference { get; init; }

    public string? Notes { get; init; }

    public int? OrderId { get; init; }

    /// <summary>The receipt of the sale (Sale / SaleReversal rows).</summary>
    public string? ReceiptNumber { get; init; }

    public int? SupplierId { get; init; }

    public string? SupplierName { get; init; }

    public required string UserName { get; init; }

    public required DateTime OccurredAt { get; init; }

    public required bool IsChecked { get; init; }

    public DateTime? CheckedAt { get; init; }
}

/// <summary>Sets up the shop's Whish wallet. The "Whish" payment method is made too (or the existing one is used).</summary>
public sealed class CreateWalletRequest
{
    [StringLength(100)]
    public string? Name { get; set; }

    /// <summary>The shop's Whish number (phone), shown to the cashier at checkout.</summary>
    [StringLength(50)]
    public string? AccountNumber { get; set; }
}

public sealed class UpdateWalletRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(50)]
    public string? AccountNumber { get; set; }

    /// <summary>Off: sales with its payment method stop landing here. The history stays.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>Money moved by hand. Sales are never entered here: checkout records them.</summary>
public sealed class WalletMovementRequest
{
    /// <summary><c>Deposit</c>, <c>Withdrawal</c>, <c>SupplierPayment</c>, <c>Fee</c> or <c>Adjustment</c>.</summary>
    [Required]
    public string Type { get; set; } = string.Empty;

    /// <summary>Always positive; the type says the direction (and <see cref="MoneyIn"/> does for an adjustment).</summary>
    [Range(0.01, 1_000_000_000)]
    public decimal Amount { get; set; }

    /// <summary>Only read for an <c>Adjustment</c>: true adds to the balance, false takes away.</summary>
    public bool MoneyIn { get; set; }

    public int CurrencyId { get; set; }

    [StringLength(100)]
    public string? Reference { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    /// <summary>The supplier paid, for a <c>SupplierPayment</c> (optional: on Basic there is no supplier list, so the notes name them).</summary>
    public int? SupplierId { get; set; }

    /// <summary>A local date (<c>2026-11-21</c>) when the money moved on an earlier day; empty = now.</summary>
    public DateOnly? Date { get; set; }
}

public sealed class WalletCheckRequest
{
    [Required, MinLength(1), MaxLength(500)]
    public List<int> Ids { get; set; } = new();

    public bool Checked { get; set; } = true;
}

public sealed record WalletCheckResultDto
{
    public required int Changed { get; init; }
}
