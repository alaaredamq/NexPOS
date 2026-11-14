using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Core.Auditing;
using Sync;
using Web.Api.Customers;
using Web.Api.Customers.Contracts;
using Web.Api.Inventory;
using Web.Api.Inventory.Contracts;
using Web.Api.Infrastructure;
using Web.Api.Products;
using Web.Api.Products.Contracts;
using Web.Api.Suppliers;
using Web.Api.Suppliers.Contracts;

namespace Web.Api.Online.Commands;

/// <summary>
/// Applies a change the owner made from the phone. The body is the same request the till's own API takes, and the work is done
/// by the same service, so the rules, the product history and the audit trail are exactly those of the till's screens.
/// A refusal is a normal answer (not an error): it travels back to the phone with the reason.
/// </summary>
public abstract class OnlineCommandHandler<TRequest>(IAuditContext audit) : IOnlineCommandHandler where TRequest : class
{
    public abstract string Type { get; }

    public async Task<CommandResult> HandleAsync(JsonElement payload, CommandContext context, CancellationToken ct)
    {
        TRequest? request;
        try { request = payload.Deserialize<TRequest>(SyncWire.Json); }
        catch (JsonException) { request = null; }
        if (request is null)
            return CommandResult.Reject(CommandErrorCodes.BadPayload, "The request was not in a form the shop PC understands.");

        // The DTO attributes (required, length, range) normally run in the MVC pipeline; this path has none, so run them here.
        var problems = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), problems, validateAllProperties: true))
            return Invalid(problems
                .SelectMany(p => (p.MemberNames.Any() ? p.MemberNames : [""]).Select(m => (Field: m, Message: p.ErrorMessage ?? "")))
                .GroupBy(x => ApiModelState.CamelCase(x.Field))
                .ToDictionary(g => g.Key, g => g.Select(x => x.Message).Distinct().ToArray()));

        // Rows and history this writes are attributed to the person on the phone, not to an anonymous "System".
        using var scope = audit.BeginSystemScope("Mobile:" + context.RequestedBy);
        return await ApplyAsync(request, ct);
    }

    protected abstract Task<CommandResult> ApplyAsync(TRequest request, CancellationToken ct);

    protected static CommandResult Invalid(IDictionary<string, string[]> errors)
        => CommandResult.Reject(CommandErrorCodes.Validation, "The shop did not accept some of the values.", new { errors });

    protected static CommandResult Invalid(string field, string message)
        => Invalid(new Dictionary<string, string[]> { [ApiModelState.CamelCase(field)] = [message] });
}

public sealed class CreateCustomerHandler(ICustomerApiService customers, IAuditContext audit) : OnlineCommandHandler<CustomerRequest>(audit)
{
    public override string Type => "customer.create";

    protected override async Task<CommandResult> ApplyAsync(CustomerRequest request, CancellationToken ct)
    {
        try
        {
            var created = await customers.CreateAsync(request);
            return CommandResult.Ok(new { id = created.Id, name = $"{created.FirstName} {created.LastName}".Trim() });
        }
        catch (CustomerRuleException ex)
        {
            return Invalid(ex.Field, ex.Message);
        }
    }
}

public sealed class CreateSupplierHandler(ISupplierApiService suppliers, IAuditContext audit) : OnlineCommandHandler<SupplierRequest>(audit)
{
    public override string Type => "supplier.create";

    protected override async Task<CommandResult> ApplyAsync(SupplierRequest request, CancellationToken ct)
    {
        try
        {
            var created = await suppliers.CreateAsync(request);
            return CommandResult.Ok(new { id = created.Id, name = created.Name });
        }
        catch (SupplierCurrencyException)
        {
            return Invalid(nameof(SupplierRequest.CurrencyId), "Unknown currency.");
        }
    }
}

public sealed class CreateProductHandler(IProductCatalogService catalog, IAuditContext audit) : OnlineCommandHandler<ProductSaveRequest>(audit)
{
    public override string Type => "product.create";

    protected override async Task<CommandResult> ApplyAsync(ProductSaveRequest request, CancellationToken ct)
    {
        // Same extra rules as POST /api/v1/products: a barcode may belong to one product only, price tiers, pack settings.
        var errors = await catalog.ValidateAsync(request, id: null);
        if (errors.Count > 0) return Invalid(errors);

        var created = await catalog.CreateAsync(request);
        return CommandResult.Ok(new { id = created.Id, name = created.Name, barcode = created.Barcode });
    }
}

/// <summary>
/// What the phone may change on an existing product. Every field but the id is optional: only what is sent changes, and everything
/// else (barcode, category, supplier, price tiers, pack settings) stays as the shop has it.
/// </summary>
public sealed class ProductEditRequest
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }

    [StringLength(255, MinimumLength = 2, ErrorMessage = "Prod_NameLength")]
    public string? Name { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Selling price must be greater than 0")]
    public decimal? SellingPrice { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Purchase price must be greater than 0")]
    public decimal? PurchasePrice { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? MinStockLevel { get; set; }

    public bool? IsActive { get; set; }
}

public sealed class UpdateProductHandler(IProductCatalogService catalog, IAuditContext audit) : OnlineCommandHandler<ProductEditRequest>(audit)
{
    public override string Type => "product.update";

    protected override async Task<CommandResult> ApplyAsync(ProductEditRequest request, CancellationToken ct)
    {
        var current = await catalog.GetAsync(request.ProductId);
        if (current is null) return CommandResult.Reject(CommandErrorCodes.NotFound, "That product no longer exists in the shop.");

        // Start from the product as it is now, so a field the phone did not mention (and the price tiers) is not wiped.
        var save = new ProductSaveRequest
        {
            Name = request.Name?.Trim() ?? current.Name,
            Description = current.Description,
            Barcode = current.Barcode,
            CategoryId = current.CategoryId,
            SupplierId = current.SupplierId,
            CurrencyId = current.CurrencyId,
            PurchasePrice = request.PurchasePrice ?? current.PurchasePrice,
            SellingPrice = request.SellingPrice ?? current.SellingPrice,
            StockQuantity = current.StockQuantity,
            MinStockLevel = request.MinStockLevel ?? current.MinStockLevel,
            IsActive = request.IsActive ?? current.IsActive,
            Unit = current.Unit,
            IsPack = current.IsPack,
            PackSize = current.PackSize,
            SingleProductId = current.SingleProductId,
            PriceTiers = current.PriceTiers.Select(t => new ProductPriceTierDto { Name = t.Name, Price = t.Price }).ToList(),
        };

        var errors = await catalog.ValidateAsync(save, request.ProductId);
        if (errors.Count > 0) return Invalid(errors);

        var updated = await catalog.UpdateAsync(request.ProductId, save);
        return updated is null
            ? CommandResult.Reject(CommandErrorCodes.NotFound, "That product no longer exists in the shop.")
            : CommandResult.Ok(new { id = updated.Id, name = updated.Name, sellingPrice = updated.SellingPrice });
    }
}

/// <summary>
/// A stock change from the phone. <c>add</c> and <c>remove</c> are relative (safe when sales happen meanwhile); <c>set</c> is a stock
/// count: the shop works out the difference against what it has at that moment, so the result is exactly the counted figure.
/// </summary>
public sealed class StockChangeRequest
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }

    [Required]
    [RegularExpression("(?i)^(add|remove|set)$")]
    public string Type { get; set; } = "";

    [Range(0, 1_000_000_000)]
    public decimal Quantity { get; set; }

    [StringLength(500)]
    public string? Reason { get; set; }
}

public sealed class AdjustStockHandler(IProductCatalogService catalog, IInventoryApiService inventory, IAuditContext audit) : OnlineCommandHandler<StockChangeRequest>(audit)
{
    public override string Type => "stock.adjust";

    protected override async Task<CommandResult> ApplyAsync(StockChangeRequest request, CancellationToken ct)
    {
        var current = await catalog.GetAsync(request.ProductId);
        if (current is null) return CommandResult.Reject(CommandErrorCodes.NotFound, "That product no longer exists in the shop.");

        var kind = request.Type.ToLowerInvariant();
        var delta = kind switch
        {
            "add" => request.Quantity,
            "remove" => -request.Quantity,
            _ => request.Quantity - current.StockQuantity,
        };
        if (delta == 0)
            return CommandResult.Ok(new { productId = current.Id, name = current.Name, previous = current.StockQuantity, now = current.StockQuantity });
        if (kind != "set" && request.Quantity <= 0) return Invalid(nameof(StockChangeRequest.Quantity), "Enter a quantity above 0.");

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? (kind == "set" ? "Stock count from the owner's phone" : null) : request.Reason;
        try
        {
            var result = await inventory.AdjustAsync(request.ProductId,
                new StockAdjustmentRequest { Type = delta > 0 ? "add" : "remove", Quantity = Math.Abs(delta), Reason = reason });
            return result is null
                ? CommandResult.Reject(CommandErrorCodes.NotFound, "That product no longer exists in the shop.")
                : CommandResult.Ok(new { productId = current.Id, name = current.Name, previous = result.PreviousStock, now = result.NewStock });
        }
        catch (InsufficientStockException ex)
        {
            return Invalid(nameof(StockChangeRequest.Quantity), ex.Message);
        }
    }
}

public static class OnlineCommandHandlerRegistration
{
    /// <summary>Teaches the shop PC the kinds of change the phone can ask for. Add a handler here to add a kind.</summary>
    public static IServiceCollection AddOnlineCommandHandlers(this IServiceCollection services)
    {
        services.AddScoped<IOnlineCommandHandler, CreateCustomerHandler>();
        services.AddScoped<IOnlineCommandHandler, CreateSupplierHandler>();
        services.AddScoped<IOnlineCommandHandler, CreateProductHandler>();
        services.AddScoped<IOnlineCommandHandler, UpdateProductHandler>();
        services.AddScoped<IOnlineCommandHandler, AdjustStockHandler>();
        return services;
    }
}
