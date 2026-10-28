using FolderChat.Application.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FolderChat.Web.Infrastructure;

/// <summary>
/// Lets a request through only with a current API key in <c>Authorization: Bearer fck_…</c>. Keys are made in
/// Settings, so with none made the API is closed.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ApiKeyAuthAttribute : Attribute, IAsyncAuthorizationFilter
{
    /// <summary>Where the authenticated key is kept for the rest of the request, as an <see cref="ApiKeyDto"/>.</summary>
    public const string ItemKey = "FolderChat.ApiKey";

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var header = http.Request.Headers.Authorization.ToString();
        var secret = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : null;

        var keys = http.RequestServices.GetRequiredService<ApiKeyService>();
        var key = await keys.AuthenticateAsync(secret, http.RequestAborted);
        if (key is not null)
        {
            http.Items[ItemKey] = key;
            return;
        }

        http.Response.Headers.WWWAuthenticate = "Bearer";
        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "API key needed",
            Detail = secret is null
                ? "Send an API key in the Authorization header: \"Bearer fck_…\". Create one in FolderChat's Settings, under API keys."
                : "That API key isn't valid. It may have been revoked; create a new one in FolderChat's Settings.",
        })
        {
            StatusCode = StatusCodes.Status401Unauthorized,
            ContentTypes = { "application/problem+json" },
        };
    }
}
