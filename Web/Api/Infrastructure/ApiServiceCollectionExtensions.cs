using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;

namespace Web.Api.Infrastructure;

public static class ApiServiceCollectionExtensions
{
    /// <summary>
    /// Wires the API conventions into MVC: the global problem-details filter, the audit-friendly controller names,
    /// the <c>validation_failed</c> answer, and convention-based registration of the API's own services.
    /// </summary>
    public static IMvcBuilder AddNexPosApi(this IMvcBuilder mvc)
    {
        mvc.AddMvcOptions(options =>
        {
            // Order: after the controller-level [Produces], which resets the media type of every result to plain JSON.
            options.Filters.Add<ApiProblemResultFilter>(order: int.MaxValue);
            options.Conventions.Add(new ApiControllerNameConvention());
        });

        mvc.ConfigureApiBehaviorOptions(options =>
        {
            var legacy = options.InvalidModelStateResponseFactory;
            options.InvalidModelStateResponseFactory = context => ApiModelState.InvalidModelStateResponse(context, legacy);
        });

        mvc.Services.AddApiServicesByConvention();
        return mvc;
    }

    /// <summary>
    /// A class under <c>Web.Api.*</c> whose name matches an interface under <c>Web.Api.*</c> (<c>OrderQueryService</c> /
    /// <c>IOrderQueryService</c>) is registered as scoped - so a module adds its API-side services without touching
    /// <c>Program.cs</c>, which is frozen after Gate G0. Anything that needs another lifetime registers itself in
    /// <c>Program.cs</c> as before.
    /// </summary>
    public static IServiceCollection AddApiServicesByConvention(this IServiceCollection services)
    {
        var assembly = typeof(ApiControllerBase).Assembly;
        foreach (var implementation in assembly.GetTypes())
        {
            if (!implementation.IsClass || implementation.IsAbstract || implementation.IsGenericTypeDefinition) continue;
            if (implementation.Namespace is null || !implementation.Namespace.StartsWith("Web.Api", StringComparison.Ordinal)) continue;
            if (typeof(ControllerBase).IsAssignableFrom(implementation)) continue;

            var contract = implementation.GetInterfaces().FirstOrDefault(i =>
                i.Name == "I" + implementation.Name && i.Namespace is not null && i.Namespace.StartsWith("Web.Api", StringComparison.Ordinal));
            if (contract is not null) services.AddScoped(contract, implementation);
        }

        return services;
    }

    /// <summary>
    /// <c>/api/v1/*</c> requests that match no endpoint (404) or method (405) answer problem JSON rather than an empty body.
    /// Responses an action or the auth layer already wrote are left alone.
    /// </summary>
    public static IApplicationBuilder UseNexPosApiStatusPages(this IApplicationBuilder app)
        => app.UseWhen(
            context => ApiPaths.IsV1(context.Request),
            branch => branch.UseStatusCodePages(context =>
                ApiProblem.WriteAsync(context.HttpContext, context.HttpContext.Response.StatusCode)));
}
