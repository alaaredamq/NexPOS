namespace Application.Exceptions;

/// <summary>
/// Thrown when a sale asks for more of a product than is in stock. Typed so the POS can offer the
/// cashier a "sell anyway" override instead of just showing a generic failure.
/// </summary>
public class InsufficientStockException : InvalidOperationException
{
    public InsufficientStockException(string message) : base(message)
    {
    }
}
