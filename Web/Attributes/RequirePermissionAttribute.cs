using Core.Auditing;
using Core.Entities.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;
using Web.Api.Infrastructure;

namespace Web.Attributes;

/// <summary>
/// Records a refused request in the audit trail. These attributes are authorization filters that
/// short-circuit before any action runs, so nothing downstream would ever see the refusal otherwise.
/// </summary>
internal static class AccessDeniedAudit
{
    public static void Record(AuthorizationFilterContext context, string requirement)
    {
        try
        {
            var trail = context.HttpContext.RequestServices.GetService<IAuditTrail>();
            if (trail is null) return;

            var route = context.RouteData.Values;
            var target = $"{route["controller"]}/{route["action"]}";

            trail.LogSecurity(AuditActions.AccessDenied,
                $"Access denied to {target}: requires {requirement}",
                AuditOutcome.Denied, AuditLogSeverity.Warning,
                details: new
                {
                    target,
                    requirement,
                    path = context.HttpContext.Request.Path.Value,
                    method = context.HttpContext.Request.Method
                });
        }
        catch (Exception)
        {
            // Auditing the refusal must never change the refusal.
        }
    }
}

/// <summary>
/// Authorization attribute that requires the user to have a specific permission
/// </summary>
public class RequirePermissionAttribute : Attribute, IAuthorizationFilter
{
    private readonly string _permission;

    public RequirePermissionAttribute(string permission)
    {
        _permission = permission ?? throw new ArgumentNullException(nameof(permission));
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // Skip authorization if user is not authenticated
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = ApiAuthorizationFailure.Unauthenticated(context);
            return;
        }

        // Check if user has the required permission
        var permissions = context.HttpContext.User.FindAll("Permission").Select(c => c.Value).ToList();

        if (!permissions.Contains(_permission))
        {
            AccessDeniedAudit.Record(context, _permission);
            context.Result = ApiAuthorizationFailure.Denied(context);
        }
    }
}

/// <summary>
/// Authorization attribute that requires the user to have any of the specified permissions
/// </summary>
public class RequireAnyPermissionAttribute : Attribute, IAuthorizationFilter
{
    private readonly string[] _permissions;

    public RequireAnyPermissionAttribute(params string[] permissions)
    {
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        if (_permissions.Length == 0)
            throw new ArgumentException("At least one permission must be specified.", nameof(permissions));
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // Skip authorization if user is not authenticated
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = ApiAuthorizationFailure.Unauthenticated(context);
            return;
        }

        // Check if user has any of the required permissions
        var userPermissions = context.HttpContext.User.FindAll("Permission").Select(c => c.Value).ToList();

        if (!_permissions.Any(permission => userPermissions.Contains(permission)))
        {
            AccessDeniedAudit.Record(context, $"any of [{string.Join(", ", _permissions)}]");
            context.Result = ApiAuthorizationFailure.Denied(context);
        }
    }
}

/// <summary>
/// Authorization attribute that requires the user to have all of the specified permissions
/// </summary>
public class RequireAllPermissionsAttribute : Attribute, IAuthorizationFilter
{
    private readonly string[] _permissions;

    public RequireAllPermissionsAttribute(params string[] permissions)
    {
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        if (_permissions.Length == 0)
            throw new ArgumentException("At least one permission must be specified.", nameof(permissions));
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // Skip authorization if user is not authenticated
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = ApiAuthorizationFailure.Unauthenticated(context);
            return;
        }

        // Check if user has all of the required permissions
        var userPermissions = context.HttpContext.User.FindAll("Permission").Select(c => c.Value).ToList();

        if (!_permissions.All(permission => userPermissions.Contains(permission)))
        {
            AccessDeniedAudit.Record(context, $"all of [{string.Join(", ", _permissions)}]");
            context.Result = ApiAuthorizationFailure.Denied(context);
        }
    }
}
