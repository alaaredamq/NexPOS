using System.ComponentModel.DataAnnotations;

namespace Web.Api.Customers.Contracts;

/// <summary>One row of the customers list.</summary>
public sealed record CustomerDto
{
    public required int Id { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    /// <summary>Null when none was entered (the stored value is an empty string).</summary>
    public string? Email { get; init; }

    public string? PhoneNumber { get; init; }

    public string? Address { get; init; }

    /// <summary>Inactive customers stay on their old sales but are meant to be left alone.</summary>
    public required bool IsActive { get; init; }

    /// <summary>
    /// Running debt balance in the default currency. Positive: the customer owes the business. Negative: the business owes the customer.
    /// </summary>
    public required decimal Balance { get; init; }

    /// <summary>Orders placed by the customer; cancelled and returned (voided) orders are not purchases and are not counted.</summary>
    public required int OrdersCount { get; init; }

    /// <summary>Sum of the order totals counted by <see cref="OrdersCount"/>. As on the Razor page the amounts are added as they are, whatever their currency.</summary>
    public required decimal TotalSpent { get; init; }

    /// <summary>UTC.</summary>
    public required DateTime CreatedAt { get; init; }
}

/// <summary>The four tiles above the customers list (all customers, not just the current page).</summary>
public sealed record CustomerSummaryDto
{
    public required int Total { get; init; }

    public required int Active { get; init; }

    /// <summary>Customers whose balance is above zero.</summary>
    public required int Owing { get; init; }

    /// <summary>Sum of the positive balances: what customers owe the business.</summary>
    public required decimal TotalOwed { get; init; }

    /// <summary>Sum of all <see cref="CustomerDto.TotalSpent"/>.</summary>
    public required decimal TotalSpent { get; init; }

    /// <summary>Notation of the default currency (<c>$</c>, <c>L.L.</c>): the unit of the balances and totals.</summary>
    public required string CurrencyNotation { get; init; }
}

/// <summary>A customer with the figures of the details page.</summary>
public sealed record CustomerDetailsDto
{
    public required CustomerDto Customer { get; init; }

    public string? City { get; init; }

    public string? Country { get; init; }

    public string? PostalCode { get; init; }

    /// <summary>Notation of the default currency, for the balance and the totals.</summary>
    public required string CurrencyNotation { get; init; }

    /// <summary><see cref="CustomerDto.TotalSpent"/> / <see cref="CustomerDto.OrdersCount"/>, 0 without orders.</summary>
    public required decimal AverageOrder { get; init; }

    /// <summary>Date of the latest order (any status), UTC; null for a customer who never ordered.</summary>
    public DateTime? LastOrderAt { get; init; }

    /// <summary>All orders of the customer, any status, including cancelled ones.</summary>
    public required int AllOrdersCount { get; init; }

    /// <summary>The 10 latest orders, newest first.</summary>
    public required IReadOnlyList<CustomerOrderDto> RecentOrders { get; init; }
}

/// <summary>An order row of the customer details page.</summary>
public sealed record CustomerOrderDto
{
    public required int Id { get; init; }

    /// <summary>UTC.</summary>
    public required DateTime CreatedAt { get; init; }

    public required decimal TotalAmount { get; init; }

    /// <summary>Notation of the order's own currency.</summary>
    public required string CurrencyNotation { get; init; }

    /// <summary><c>Pending</c>, <c>Processing</c>, <c>Shipped</c>, <c>Delivered</c>, <c>Cancelled</c> or <c>Returned</c>.</summary>
    public required string Status { get; init; }
}

/// <summary>One entry of a customer's debt ledger. The amount is signed: positive raises what the customer owes.</summary>
public sealed record CustomerDebtTransactionDto
{
    public required int Id { get; init; }

    /// <summary>UTC.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary><c>Sale</c> (a sale put on the account), <c>Payment</c>, <c>ManualAdjustment</c> or <c>SaleReversal</c> (a voided sale).</summary>
    public required string Type { get; init; }

    public required decimal Amount { get; init; }

    public string? Note { get; init; }

    /// <summary>The sale this entry belongs to, for <c>Sale</c> and <c>SaleReversal</c>.</summary>
    public int? OrderId { get; init; }

    /// <summary>Username of the staff member who recorded it.</summary>
    public string? RecordedBy { get; init; }
}

/// <summary>The recorded entry and the balance after it.</summary>
public sealed record CustomerDebtResultDto
{
    public required CustomerDebtTransactionDto Transaction { get; init; }

    /// <summary>Balance after the entry; same sign convention as <see cref="CustomerDto.Balance"/>.</summary>
    public required decimal Balance { get; init; }
}

/// <summary>Body of create and update.</summary>
public sealed record CustomerRequest
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public required string FirstName { get; init; }

    [Required]
    [StringLength(100, MinimumLength = 2)]
    public required string LastName { get; init; }

    [EmailAddress]
    [StringLength(255)]
    public string? Email { get; init; }

    [Phone]
    [StringLength(20)]
    public string? PhoneNumber { get; init; }

    [StringLength(500)]
    public string? Address { get; init; }

    public bool IsActive { get; init; } = true;
}

/// <summary>Body of "add a debt transaction".</summary>
public sealed record CustomerDebtRequest
{
    /// <summary>Always positive; <see cref="CustomerOwesMore"/> gives the direction.</summary>
    [Range(0.01, 1_000_000_000)]
    public required decimal Amount { get; init; }

    /// <summary>
    /// True: the customer owes the business more (balance goes up). False: the customer paid, or the business owes them
    /// (balance goes down - and below zero when more is paid than was owed: no cap, as on the Razor page).
    /// </summary>
    public required bool CustomerOwesMore { get; init; }

    [StringLength(500)]
    public string? Note { get; init; }
}
