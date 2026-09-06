using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace Web.Attributes;

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
            context.Result = new RedirectToActionResult("Login", "Account", new { 
                returnUrl = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString 
            });
            return;
        }

        // Check if user has the required permission
        var permissions = context.HttpContext.User.FindAll("Permission").Select(c => c.Value).ToList();
        
        if (!permissions.Contains(_permission))
        {
            context.Result = new RedirectToActionResult("AccessDenied", "Account", null);
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
            context.Result = new RedirectToActionResult("Login", "Account", new { 
                returnUrl = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString 
            });
            return;
        }

        // Check if user has any of the required permissions
        var userPermissions = context.HttpContext.User.FindAll("Permission").Select(c => c.Value).ToList();
        
        if (!_permissions.Any(permission => userPermissions.Contains(permission)))
        {
            context.Result = new RedirectToActionResult("AccessDenied", "Account", null);
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
            context.Result = new RedirectToActionResult("Login", "Account", new { 
                returnUrl = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString 
            });
            return;
        }

        // Check if user has all of the required permissions
        var userPermissions = context.HttpContext.User.FindAll("Permission").Select(c => c.Value).ToList();
        
        if (!_permissions.All(permission => userPermissions.Contains(permission)))
        {
            context.Result = new RedirectToActionResult("AccessDenied", "Account", null);
        }
    }
}