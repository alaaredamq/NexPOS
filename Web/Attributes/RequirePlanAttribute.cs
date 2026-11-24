using Application.IServices.Licensing;
using Common.Licensing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Web.Api.Infrastructure;

namespace Web.Attributes;

/// <summary>
/// Puts a controller or action in the Pro plan: <c>[RequirePlan(ProFeature.Inventory)]</c>. It runs next to
/// <c>[RequirePermission]</c> (the role decides who, the plan decides whether the shop has the page at all).
/// On Basic, an <c>/api/*</c> call answers 403 <c>plan_upgrade_required</c> with <c>feature</c>; a page request goes to the
/// SPA's upgrade screen. Nothing is deleted: the data behind a locked page is still there after an upgrade.
/// The menu shows the same pages with a lock (<see cref="Helpers.NavCatalog"/>), from the same <see cref="PlanRules"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePlanAttribute : Attribute, IAuthorizationFilter
{
    public RequirePlanAttribute(ProFeature feature) => Feature = feature;

    public ProFeature Feature { get; }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // Signing in comes first: [Authorize] / [RequirePermission] answer someone who is not signed in.
        if (context.HttpContext.User.Identity?.IsAuthenticated != true) return;

        var license = context.HttpContext.RequestServices.GetRequiredService<ILicenseService>().GetState();
        if (license.Includes(Feature)) return;

        AccessDeniedAudit.Record(context, $"the Pro plan ({Feature})");

        if (!ApiPaths.IsApi(context.HttpContext.Request))
        {
            context.Result = new RedirectResult(UpgradePath(Feature));
            return;
        }

        var problem = ApiProblem.Create(context.HttpContext, StatusCodes.Status403Forbidden, ApiCodes.PlanUpgradeRequired);
        problem.Extensions["feature"] = Feature.ToString();
        context.Result = new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentTypes = { ApiProblem.ContentType },
        };
    }

    /// <summary>The SPA screen that explains the feature is part of Pro.</summary>
    public static string UpgradePath(ProFeature feature) => $"/app/upgrade?feature={feature}";
}
