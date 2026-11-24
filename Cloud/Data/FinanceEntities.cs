namespace Cloud.Data;

/// <summary>
/// Money the provider received: usually from a client (an Online renewal, a POS licence, a set-up visit), or, with no client,
/// other income such as capital put into the business. All amounts are in the one currency the portal shows (see FinanceRules).
/// </summary>
public class Payment
{
    public int Id { get; set; }
    /// <summary>
    /// Null for income that is not from a client (capital, a refund received, ...), and for a client that was since removed:
    /// the money was still received, so the payment stays and keeps <see cref="Payer"/>.
    /// </summary>
    public int? ClientId { get; set; }
    public Client? Client { get; set; }
    /// <summary>Who paid, as it read when recorded: the client's name, or a free-text source for other income.</summary>
    public string Payer { get; set; } = "";
    public decimal Amount { get; set; }
    /// <summary>The day the money came in, in the provider's own calendar.</summary>
    public DateOnly ReceivedOn { get; set; }
    /// <summary>Cash, Bank transfer, Whish, OMT, ... (free text; the form offers the usual ones).</summary>
    public string Method { get; set; } = "";
    /// <summary>What it was for: Online subscription, POS licence, Set-up, ... (free text; the form offers the usual ones).</summary>
    public string Purpose { get; set; } = "";
    /// <summary>Receipt or transfer number, so it can be found again.</summary>
    public string? Reference { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

/// <summary>Money the provider spent running the business: hosting, marketing, salaries, transport, ...</summary>
public class Expense
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    /// <summary>The day it was paid, in the provider's own calendar.</summary>
    public DateOnly SpentOn { get; set; }
    public string Category { get; set; } = "";
    /// <summary>Who was paid (the host, the printer shop, ...).</summary>
    public string? Payee { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

/// <summary>
/// The plan for a month: how much the provider means to collect and the most they mean to spend. A row applies from its month
/// onward until a later row replaces it, so a budget is set once and carries forward (see FinanceRules.BudgetFor).
/// </summary>
public class FinanceBudget
{
    public int Id { get; set; }
    /// <summary>First day of the month it starts applying.</summary>
    public DateOnly Month { get; set; }
    /// <summary>What to collect each month. Null means no target.</summary>
    public decimal? IncomeTarget { get; set; }
    /// <summary>The most to spend each month. Null means no limit.</summary>
    public decimal? SpendingLimit { get; set; }
    public DateTime UpdatedAt { get; set; }
}
