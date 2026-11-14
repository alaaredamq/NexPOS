using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Core.Auditing;
using Sync;
using Web.Api.Customers;
using Web.Api.Customers.Contracts;
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

public static class OnlineCommandHandlerRegistration
{
    /// <summary>Teaches the shop PC the kinds of change the phone can ask for. Add a handler here to add a kind.</summary>
    public static IServiceCollection AddOnlineCommandHandlers(this IServiceCollection services)
    {
        services.AddScoped<IOnlineCommandHandler, CreateCustomerHandler>();
        services.AddScoped<IOnlineCommandHandler, CreateSupplierHandler>();
        services.AddScoped<IOnlineCommandHandler, CreateProductHandler>();
        return services;
    }
}
