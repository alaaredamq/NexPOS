using System.Globalization;
using System.Reflection;
using System.Text;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;

namespace Web.Api.Infrastructure;

/// <summary>
/// OpenAPI for the versioned API only (the legacy controllers are not part of the contract). The document is what
/// <c>npm run api:types</c> turns into <c>client/src/api/schema.d.ts</c>, so the UI and API halves of a module can be
/// built in parallel from the DTOs alone.
/// </summary>
public static class ApiSwagger
{
    public const string DocumentName = "v1";

    public static IServiceCollection AddNexPosSwagger(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc(DocumentName, new OpenApiInfo
            {
                Title = "NexPOS API",
                Version = "v1",
                Description =
                    "JSON API of the NexPOS single-page app. Sign in with POST /api/v1/auth/login; the session lives in an HttpOnly cookie. " +
                    "Every non-GET request must send the header 'X-NexPOS-Client: web'. Send 'Accept-Language: en|ar' for localized messages. " +
                    "Errors are RFC 7807 problem+json with a stable 'code'; lists return { items, total, page, pageSize }.",
            });

            options.DocInclusionPredicate((_, api) => IsVersioned(api.RelativePath));
            options.TagActionsBy(api => new[] { ModuleOf(api.RelativePath) });
            options.CustomSchemaIds(SchemaId);
            options.EnableAnnotations();
            options.SupportNonNullableReferenceTypes(); // `string` is required, `string?` is nullable - the TypeScript types depend on it
            options.OperationFilter<TidyOperationFilter>();

            // The XML comments of the DTOs and controllers become the descriptions in the generated types.
            var comments = Path.Combine(AppContext.BaseDirectory, "Web.xml");
            if (File.Exists(comments)) options.IncludeXmlComments(comments, includeControllerXmlComments: true);
        });

        return services;
    }

    /// <summary>Serves <c>/swagger/v1/swagger.json</c> and the browsable UI. Development only: an installed shop PC has no use for it.</summary>
    public static IApplicationBuilder UseNexPosSwagger(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return app;

        app.UseSwagger();
        app.UseSwaggerUI(options => options.SwaggerEndpoint($"/swagger/{DocumentName}/swagger.json", "NexPOS API v1"));
        return app;
    }

    /// <summary>The same document the endpoint serves, as text - for <see cref="ApiContractDump"/>, with no server running.</summary>
    public static string RenderDocument(IServiceProvider services)
    {
        var document = services.GetRequiredService<ISwaggerProvider>().GetSwagger(DocumentName);

        // XML comments carry the line endings of the checkout (CRLF on Windows); the contract must not depend on them.
        return document.SerializeAsJson(Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0).Replace("\\r\\n", "\\n");
    }

    private static bool IsVersioned(string? relativePath)
        => relativePath is not null
           && (relativePath.Equals(ApiPaths.V1Prefix, StringComparison.OrdinalIgnoreCase)
               || relativePath.StartsWith(ApiPaths.V1Prefix + "/", StringComparison.OrdinalIgnoreCase));

    /// <summary>"api/v1/cash-drawer/sessions/{id}" -> "cash-drawer": one tag per module.</summary>
    private static string ModuleOf(string? relativePath)
    {
        var parts = (relativePath ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 2 ? parts[2] : "api";
    }

    /// <summary>
    /// <c>PagedResult&lt;ProductDto&gt;</c> becomes <c>ProductDtoPagedResult</c> (the default id, <c>PagedResult`1</c>, would collide
    /// for every list). Two different DTOs sharing a name make Swagger fail loudly - name them uniquely.
    /// </summary>
    private static string SchemaId(Type type)
    {
        if (!type.IsGenericType) return type.Name;

        var name = new StringBuilder();
        foreach (var argument in type.GetGenericArguments()) name.Append(SchemaId(argument));
        name.Append(type.Name[..type.Name.IndexOf('`')]);
        return name.ToString();
    }
}

/// <summary>
/// Two clean-ups the generated TypeScript would otherwise inherit: a public (<c>[AllowAnonymous]</c>) action does not answer
/// 401 / 403 (the base class declares them for every action), and JSON is JSON - the extra <c>text/json</c> and
/// <c>application/*+json</c> media types the input formatter advertises are dropped.
/// </summary>
internal sealed class TidyOperationFilter : Swashbuckle.AspNetCore.SwaggerGen.IOperationFilter
{
    public void Apply(OpenApiOperation operation, Swashbuckle.AspNetCore.SwaggerGen.OperationFilterContext context)
    {
        var anonymous = context.MethodInfo.GetCustomAttributes(inherit: true).OfType<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>().Any();
        if (anonymous)
        {
            // An action that declares its own 401 (a failed sign-in) keeps it; the base class's blanket 401 / 403 go.
            var own401 = context.MethodInfo.GetCustomAttributes<Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute>(inherit: false)
                .Any(a => a.StatusCode == StatusCodes.Status401Unauthorized);
            if (!own401) operation.Responses.Remove("401");
            operation.Responses.Remove("403");
        }

        if (operation.RequestBody?.Content is { } content)
        {
            content.Remove("text/json");
            content.Remove("application/*+json");
        }
    }
}

/// <summary>
/// <c>Web.exe --dump-api-contract &lt;folder&gt;</c> writes <c>swagger.json</c> and <c>permissions.ts</c> and exits, before the
/// database is touched or a port is opened. <c>client/scripts/api-types.mjs</c> runs it, so <c>npm run api:types</c> is
/// deterministic and needs no running server.
/// </summary>
public static class ApiContractDump
{
    public const string Flag = "--dump-api-contract";

    public static bool TryGetTarget(string[] args, out string folder)
    {
        var index = Array.IndexOf(args, Flag);
        folder = index >= 0 && index + 1 < args.Length ? args[index + 1] : string.Empty;
        return folder.Length > 0;
    }

    public static async Task WriteAsync(IServiceProvider services, string folder)
    {
        Directory.CreateDirectory(folder);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await File.WriteAllTextAsync(Path.Combine(folder, "swagger.json"), ApiSwagger.RenderDocument(services) + "\n", utf8);
        await File.WriteAllTextAsync(Path.Combine(folder, "permissions.ts"), PermissionsTypeScript.Render(), utf8);
    }
}

/// <summary>The permission keys of <c>PermissionHelper.Permissions</c> as a generated TypeScript module.</summary>
public static class PermissionsTypeScript
{
    public static string Render()
    {
        var fields = typeof(Web.Helpers.PermissionHelper.Permissions)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (Name: f.Name, Value: (string)f.GetRawConstantValue()!))
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ToList();

        var text = new StringBuilder();
        text.Append("// GENERATED by `npm run api:types` from Web/Helpers/PermissionHelper.cs. Do not edit; do not hand-merge.\n");
        text.Append("// On a merge conflict take either side and run `npm run api:types` again.\n\n");
        text.Append("export const Permissions = {\n");
        foreach (var (name, value) in fields)
            text.Append(CultureInfo.InvariantCulture, $"  {name}: '{value}',\n");
        text.Append("} as const;\n\n");
        text.Append("export type Permission = (typeof Permissions)[keyof typeof Permissions];\n\n");
        text.Append("export const ALL_PERMISSIONS: readonly Permission[] = Object.values(Permissions);\n");
        return text.ToString();
    }
}
