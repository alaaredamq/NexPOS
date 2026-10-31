using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;
using Web.Api.Infrastructure;

namespace Web.Attributes;

/// <summary>
/// Authorization attribute that requires the user to have a specific role
/// </summary>
public class RequireRoleAttribute : Attribute, IAuthorizationFilter
{
    private readonly string _role;

    public RequireRoleAttribute(string role)
    {
        _role = role ?? throw new ArgumentNullException(nameof(role));
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // Skip authorization if user is not authenticated
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = ApiAuthorizationFailure.Unauthenticated(context);
            return;
        }

        // Check if user has the required role
        var roles = context.HttpContext.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        
        if (!roles.Contains(_role))
        {
            AccessDeniedAudit.Record(context, $"role {_role}");
            context.Result = ApiAuthorizationFailure.Denied(context);
        }
    }
}

/// <summary>
/// Authorization attribute that requires the user to have any of the specified roles
/// </summary>
public class RequireAnyRoleAttribute : Attribute, IAuthorizationFilter
{
    private readonly string[] _roles;

    public RequireAnyRoleAttribute(params string[] roles)
    {
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
        if (_roles.Length == 0)
            throw new ArgumentException("At least one role must be specified.", nameof(roles));
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // Skip authorization if user is not authenticated
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = ApiAuthorizationFailure.Unauthenticated(context);
            return;
        }

        // Check if user has any of the required roles
        var userRoles = context.HttpContext.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        
        if (!_roles.Any(role => userRoles.Contains(role)))
        {
            AccessDeniedAudit.Record(context, $"any role of [{string.Join(", ", _roles)}]");
            context.Result = ApiAuthorizationFailure.Denied(context);
        }
    }
}

/// <summary>
/// Authorization attribute that requires the user to have all of the specified roles
/// </summary>
public class RequireAllRolesAttribute : Attribute, IAuthorizationFilter
{
    private readonly string[] _roles;

    public RequireAllRolesAttribute(params string[] roles)
    {
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
        if (_roles.Length == 0)
            throw new ArgumentException("At least one role must be specified.", nameof(roles));
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // Skip authorization if user is not authenticated
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = ApiAuthorizationFailure.Unauthenticated(context);
            return;
        }

        // Check if user has all of the required roles
        var userRoles = context.HttpContext.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        
        if (!_roles.All(role => userRoles.Contains(role)))
        {
            AccessDeniedAudit.Record(context, $"all roles of [{string.Join(", ", _roles)}]");
            context.Result = ApiAuthorizationFailure.Denied(context);
        }
    }
}