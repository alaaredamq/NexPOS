namespace Core.Entities.Sales;

/// <summary>How the till sorts a payment method by its name, the same rule the payment dialog uses to ask for cash handed over.</summary>
public enum PaymentKind
{
    Cash,
    Card,
    Other,
}

public static class PaymentKinds
{
    /// <summary>
    /// "Cash" (any name with "cash" in it) is money in the drawer; "Card" is a card machine; anything else (Customer Debt, Whish, ...)
    /// is neither. A sale whose method is unknown counts as cash, as every sale did before methods were told apart.
    /// </summary>
    public static PaymentKind Of(string? methodName)
    {
        if (string.IsNullOrWhiteSpace(methodName)) return PaymentKind.Cash;
        var name = methodName.ToLowerInvariant();
        if (name.Contains("cash")) return PaymentKind.Cash;
        if (name.Contains("card")) return PaymentKind.Card;
        return PaymentKind.Other;
    }
}
