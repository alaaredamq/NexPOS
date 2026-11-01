using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Web.Api.Infrastructure;

namespace Web.Api.Pos;

/// <summary>
/// A business rule of the till said no (a custom price below cost, a total that does not match, a closed drawer ...).
/// The service throws it with the status and the stable <c>code</c> the UI branches on, plus the <c>SharedResource</c> key of the
/// message; <see cref="PosRuleExceptionFilterAttribute"/> (on <see cref="PosControllerBase"/>) turns it into the problem JSON of
/// <c>Web/Api/README.md</c>, localized to the request's language. (The shared exception mapper is frozen after G0 and would call this a 500.)
/// </summary>
public sealed class PosRuleException : Exception
{
    public int Status { get; }

    public string Code { get; }

    /// <summary>Resource key of the message in <c>SharedResource</c> (English text for the log).</summary>
    public string ResourceKey { get; }

    public object[] Args { get; }

    /// <summary>Optional: the request field the message is about, for <c>errors{field:[msg]}</c> on a 400.</summary>
    public string? Field { get; }

    public PosRuleException(int status, string code, string resourceKey, string? field = null, params object[] args)
        : base(resourceKey)
    {
        Status = status;
        Code = code;
        ResourceKey = resourceKey;
        Field = field;
        Args = args;
    }

    /// <summary>A refusal whose message already comes from a lower service (not localizable here): <paramref name="message"/> is used as is.</summary>
    public static PosRuleException Raw(int status, string code, string message, string? field = null)
        => new(status, code, "\0" + message, field);
}

/// <summary>Turns a <see cref="PosRuleException"/> into the API's problem JSON, in the language of the request.</summary>
public sealed class PosRuleExceptionFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not PosRuleException rule) return;

        var http = context.HttpContext;
        var message = rule.ResourceKey.StartsWith('\0')
            ? rule.ResourceKey[1..]
            : Format(ApiProblem.Localize(http, rule.ResourceKey), rule.Args);

        ProblemDetails problem = rule.Status == StatusCodes.Status400BadRequest && rule.Field is not null
            ? ApiProblem.Validation(http, new Dictionary<string, string[]> { [ApiModelState.CamelCase(rule.Field)] = new[] { message } }, message)
            : ApiProblem.Create(http, rule.Status, rule.Code, message);
        problem.Extensions["code"] = rule.Code; // a 400 with a field is still the specific rule, not just "validation_failed"

        context.Result = new ObjectResult(problem) { StatusCode = rule.Status, ContentTypes = { ApiProblem.ContentType } };
        context.ExceptionHandled = true;
    }

    private static string Format(string template, object[] args)
        => args.Length == 0 ? template : string.Format(System.Globalization.CultureInfo.CurrentCulture, template, args);
}

/// <summary>Base of the till's controllers: signed-in users, JSON, and <see cref="PosRuleException"/> answered as problem JSON.</summary>
[PosRuleExceptionFilter]
public abstract class PosControllerBase : ApiControllerBase
{
}

/// <summary>Codes of the point of sale on top of the shared ones in <c>ApiCodes</c>.</summary>
public static class PosCodes
{
    public const string ProductNotFound = "product_not_found";
    public const string CurrencyNotFound = "currency_not_found";
    public const string CustomerNotFound = "customer_not_found";
    public const string DuplicateProduct = "duplicate_product";
    public const string PriceNotAvailable = "price_not_available";
    public const string PriceBelowMinimum = "price_below_minimum";
    public const string InvalidDiscount = "invalid_discount";
    public const string InvalidAmount = "invalid_amount";
    public const string PaymentMethodInvalid = "payment_method_invalid";
    public const string DebtRequiresCustomer = "debt_requires_customer";
    public const string TotalMismatch = "total_mismatch";
    public const string InsufficientStock = "insufficient_stock";
    public const string DrawerNotOpen = "drawer_not_open";
    public const string IdempotencyKeyReused = "idempotency_key_reused";
    public const string CheckoutFailed = "checkout_failed";
    public const string CartNotFound = "cart_not_found";
    public const string OrderNotFound = "order_not_found";
}
