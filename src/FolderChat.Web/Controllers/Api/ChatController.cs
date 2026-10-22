using System.ComponentModel.DataAnnotations;
using FolderChat.Application.Chat;
using FolderChat.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FolderChat.Web.Controllers.Api;

[Route("api/chat")]
public sealed class ChatController(ChatService chat, IOptions<JsonOptions> jsonOptions) : ApiControllerBase
{
    /// <summary>
    /// Sends a message and streams the reply as Server-Sent Events: <c>started</c>, then any number of
    /// <c>delta</c>, then <c>completed</c> or <c>error</c>. Closing the connection stops the model.
    /// </summary>
    [HttpPost("stream")]
    public async Task Stream(ChatStreamRequest request, CancellationToken ct)
    {
        ServerSentEvents.Start(Response);
        var json = jsonOptions.Value.JsonSerializerOptions;

        var events = chat.StreamAsync(new ChatRequest(request.ConversationId, request.ProviderId, request.ModelId, request.Message), ct);
        await foreach (var chatEvent in events)
        {
            var name = chatEvent switch
            {
                ChatStarted => "started",
                ChatDelta => "delta",
                ChatCompleted => "completed",
                ChatFailed => "error",
                _ => "unknown",
            };
            await ServerSentEvents.WriteAsync(Response, name, chatEvent, json, ct);
        }
    }
}

public sealed record ChatStreamRequest(
    Guid? ConversationId,
    Guid ProviderId,
    [Required] string ModelId,
    [Required, MaxLength(ChatService.MaxMessageLength)] string Message);
