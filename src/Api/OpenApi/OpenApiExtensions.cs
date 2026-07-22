using Microsoft.AspNetCore.OpenApi;
// OpenAPI.NET v2 collapsed Microsoft.OpenApi.Models into the root namespace.
using Microsoft.OpenApi;

namespace Keepr.Api.OpenApi;

/// <summary>
/// OpenAPI document generation and the Swagger UI that renders it. The document itself comes
/// from Microsoft.AspNetCore.OpenApi (first-party, .NET 10), which also lifts the XML doc
/// comments off controllers and DTOs — so the summaries in the feature folders are the docs.
/// </summary>
public static class OpenApiExtensions
{
    private const string DocumentName = "v1";
    private const string BearerScheme = "Bearer";

    public static IServiceCollection AddKeeprOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer<ApiInfoTransformer>();
            options.AddDocumentTransformer<BearerSecurityTransformer>();
            options.AddOperationTransformer<AuthResponsesTransformer>();
        });

    /// <summary>
    /// Serves the spec at <c>/openapi/v1.json</c> and Swagger UI at <c>/swagger</c>.
    /// Development only: the document describes every endpoint including their request shapes,
    /// which is not something to publish alongside a production deployment by default.
    /// </summary>
    public static WebApplication UseKeeprOpenApi(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return app;

        app.MapOpenApi();
        app.UseSwaggerUI(o =>
        {
            o.SwaggerEndpoint($"/openapi/{DocumentName}.json", "Keepr API v1");
            o.RoutePrefix = "swagger";
            o.DocumentTitle = "Keepr API";
            // Collapsed by default; the API is small enough to scan by tag.
            o.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
            o.EnablePersistAuthorization();
        });

        return app;
    }
}

/// <summary>Title, version, and the orientation a newcomer needs before poking at endpoints.</summary>
internal sealed class ApiInfoTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Keepr API",
            Version = "v1",
            Description = """
                Personal media store: presigned direct-to-R2 uploads, a folder hierarchy, and a
                10-day trash.

                **Three behaviours that surprise clients:**

                1. **The server may rename what you send.** A name colliding inside its
                   destination folder is auto-suffixed (`report.pdf` → `report (2).pdf`). This
                   applies to folder create/rename/move, file rename/move, *and upload* — always
                   display the name from the response, never the one you sent.
                2. **`DELETE` means "move to trash".** Files and folders are recoverable for 10
                   days and still count against quota until purged. `GET /api/me/usage` reports
                   `trashedBytes` so a UI can explain why deleting didn't free space.
                3. **`folderId: null` means the user's root**, not a missing value.

                Everything except `/health` and `/api/auth/*` needs a bearer token: call
                `POST /api/auth/login`, then paste the `accessToken` into **Authorize** above.
                """
        };
        return Task.CompletedTask;
    }
}

/// <summary>
/// Declares the JWT bearer scheme and applies it as the document-wide default, so Swagger UI
/// shows an Authorize button and sends the token on every call.
/// </summary>
internal sealed class BearerSecurityTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        var scheme = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "JWT from POST /api/auth/login. Paste the raw token; \"Bearer \" is added for you."
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = scheme;

        document.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            }
        ];

        return Task.CompletedTask;
    }
}

/// <summary>
/// Documents the 401 that every <c>[Authorize]</c> endpoint can return. Without this the spec
/// implies authenticated endpoints only ever succeed, which is the first thing a client hits.
/// </summary>
internal sealed class AuthResponsesTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        var allowsAnonymous = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any();
        var requiresAuth = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any();

        if (!requiresAuth || allowsAnonymous) return Task.CompletedTask;

        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Missing or expired bearer token." });

        return Task.CompletedTask;
    }
}
