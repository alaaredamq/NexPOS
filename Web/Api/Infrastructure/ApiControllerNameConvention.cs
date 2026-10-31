using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Web.Api.Infrastructure;

/// <summary>
/// Gives the API's controllers a route-value name of <c>Api.&lt;Name&gt;</c>. The audit trail names a request
/// "POST &lt;controller&gt;/&lt;action&gt;", and the legacy <c>AuthController</c>, <c>CartsController</c> ... would otherwise
/// be indistinguishable from their versioned successors in the audit viewer ("POST Auth/Login" twice).
/// </summary>
public sealed class ApiControllerNameConvention : IControllerModelConvention
{
    public const string Prefix = "Api.";

    public void Apply(ControllerModel controller)
    {
        if (typeof(ApiControllerBase).IsAssignableFrom(controller.ControllerType) && !controller.ControllerName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            controller.ControllerName = Prefix + controller.ControllerName;
        }
    }
}
