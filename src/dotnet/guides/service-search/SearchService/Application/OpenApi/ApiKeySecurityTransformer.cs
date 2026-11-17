using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using SearchService.Application.Security;

namespace SearchService.Application.OpenApi;

/// <summary>
/// Describes the X-Api-Key header in the OpenAPI document: declares the scheme once and marks
/// every operation that requires authorization, so tools such as Swagger UI show an
/// "Authorize" button and send the key with those requests only.
/// </summary>
internal sealed class ApiKeySecurityTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        document.Info.Title = "Wayfinder API";
        document.Info.Description =
            "Search Wikivoyage travel guides and get cited answers. Search is open; "
            + $"/api/v1/ask needs an API key in the {ApiKeyAuthenticationHandler.HeaderName} header.";

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[ApiKeyAuthenticationHandler.SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiKeyAuthenticationHandler.HeaderName,
            Description = "An API key from the Auth:ApiKeys setting.",
        };
        return Task.CompletedTask;
    }

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
        {
            operation.Security ??= [];
            operation.Security.Add(
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(ApiKeyAuthenticationHandler.SchemeName, context.Document)] = [],
                }
            );
        }

        return Task.CompletedTask;
    }
}
