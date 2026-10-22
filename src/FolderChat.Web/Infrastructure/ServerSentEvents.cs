using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

namespace FolderChat.Web.Infrastructure;

/// <summary>Writes a Server-Sent Events stream: one <c>event:</c> name and one JSON <c>data:</c> line per message.</summary>
internal static class ServerSentEvents
{
    public static void Start(HttpResponse response)
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        // Tells reverse proxies such as nginx not to hold the stream back.
        response.Headers["X-Accel-Buffering"] = "no";
        response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
    }

    public static async Task WriteAsync(HttpResponse response, string eventName, object payload, JsonSerializerOptions json, CancellationToken ct)
    {
        // The serializer escapes line breaks, so the JSON always fits on one data line.
        var data = JsonSerializer.Serialize(payload, payload.GetType(), json);
        await response.WriteAsync($"event: {eventName}\ndata: {data}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
