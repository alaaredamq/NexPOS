using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

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
            context.Result = new RedirectToActionResult("Login", "Account", new { 
                returnUrl = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString 
            });
            return;
        }

        // Check if user has the required role
        var roles = context.HttpContext.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        
        if (!roles.Contains(_role))
        {
            context.Result = new RedirectToActionResult("AccessDenied", "Account", null);
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
            context.Result = new RedirectToActionResult("Login", "Account", new { 
                returnUrl = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString 
            });
            return;
        }

        // Check if user has any of the required roles
        var userRoles = context.HttpContext.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        
        if (!_roles.Any(role => userRoles.Contains(role)))
        {
            context.Result = new RedirectToActionResult("AccessDenied", "Account", null);
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
            context.Result = new RedirectToActionResult("Login", "Account", new { 
                returnUrl = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString 
            });
            return;
        }

        // Check if user has all of the required roles
        var userRoles = context.HttpContext.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        
        if (!_roles.All(role => userRoles.Contains(role)))
        {
            context.Result = new RedirectToActionResult("AccessDenied", "Account", null);
        }
    }
}