namespace FolderChat.Web.Infrastructure;

/// <summary>
/// On the addresses in <c>FolderChat:Api:Urls</c>, answers only <c>/api/v1</c>. Everything else, such as Settings and
/// your own chats, is 404 there, so opening the API to other computers doesn't open the rest of FolderChat.
/// </summary>
public sealed class ApiOnlyAddresses(RequestDelegate next, IReadOnlySet<int> apiPorts)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (apiPorts.Contains(context.Connection.LocalPort) && !context.Request.Path.StartsWithSegments("/api/v1"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        return next(context);
    }
}
