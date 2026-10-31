namespace Web.Api.Infrastructure;

/// <summary>
/// Marks an action that is deliberately open to every signed-in user - no permission needed (the session bootstrap,
/// "change my own PIN"). It does nothing at run time: <see cref="ApiControllerBase"/> already requires a signed-in user.
/// It exists so that every action of the API has to say who may call it - <c>[RequirePermission]</c>,
/// <c>[AllowAnonymous]</c> or this - and a test fails for an action that says nothing. The old pages' permission checks
/// must be copied onto the API action by action; a forgotten attribute is a hole, not a default.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AnySignedInUserAttribute : Attribute
{
}
