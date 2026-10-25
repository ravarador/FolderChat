using System.ComponentModel.DataAnnotations;
using FolderChat.Application.Chat;
using FolderChat.Application.Knowledge;
using FolderChat.Core.Knowledge;
using FolderChat.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FolderChat.Web.Controllers.Api;

[Route("api/chat")]
public sealed class ChatController(ChatService chat, IOptions<JsonOptions> jsonOptions) : ApiControllerBase
{
    /// <summary>
    /// Sends a message and streams the reply as Server-Sent Events: <c>started</c>, then any number of
    /// <c>delta</c> and <c>activity</c> (searches of a folder), then <c>sources</c> when a folder was used, then
    /// <c>completed</c> or <c>error</c>. Closing the connection stops the model.
    /// </summary>
    [HttpPost("stream")]
    public async Task Stream(ChatStreamRequest request, CancellationToken ct)
    {
        ServerSentEvents.Start(Response);
        var json = jsonOptions.Value.JsonSerializerOptions;

        var events = chat.StreamAsync(
            new ChatRequest(
                request.ConversationId,
                request.ProviderId,
                request.ModelId,
                request.Message ?? "",
                request.AttachmentIds,
                request.FolderIds,
                request.Effort),
            ct);
        await foreach (var chatEvent in events)
        {
            var name = chatEvent switch
            {
                ChatStarted => "started",
                ChatDelta => "delta",
                ChatActivity => "activity",
                ChatSources => "sources",
                ChatCompleted => "completed",
                ChatFailed => "error",
                _ => "unknown",
            };
            await ServerSentEvents.WriteAsync(Response, name, chatEvent, json, ct);
        }
    }
}

/// <param name="Message">Can be empty when files are attached.</param>
/// <param name="AttachmentIds">Files uploaded through <c>/api/attachments</c> to send with the message.</param>
/// <param name="FolderIds">The knowledge folders this chat answers from, saved with the conversation.</param>
/// <param name="Effort">How hard to search the folders: Quick, Standard or Thorough.</param>
public sealed record ChatStreamRequest(
    Guid? ConversationId,
    Guid ProviderId,
    [Required] string ModelId,
    [MaxLength(ChatService.MaxMessageLength)] string? Message,
    [MaxLength(ChatService.MaxAttachments)] IReadOnlyList<Guid>? AttachmentIds = null,
    [MaxLength(FolderAnswering.MaxFolders)] IReadOnlyList<Guid>? FolderIds = null,
    SearchEffort Effort = SearchEffort.Standard);
