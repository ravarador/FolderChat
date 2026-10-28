using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace FolderChat.Web.Infrastructure;

/// <summary>Names the REST API's OpenAPI description and says how to authenticate, so tools like Swagger UI and Postman can call it.</summary>
internal sealed class ApiDocument : IOpenApiDocumentTransformer
{
    private const string SchemeName = "ApiKey";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "FolderChat API",
            Version = "v1",
            Description = "Ask questions answered from your FolderChat knowledge folders, from your own app's server. "
                + "Send an API key from Settings > API keys as \"Authorization: Bearer fck_…\".",
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            Description = "An API key created in FolderChat's Settings, starting with fck_.",
        };
        document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(SchemeName, document)] = [] }];
        return Task.CompletedTask;
    }
}
