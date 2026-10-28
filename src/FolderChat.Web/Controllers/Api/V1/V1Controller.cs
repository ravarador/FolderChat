using FolderChat.Application.Api;
using FolderChat.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FolderChat.Web.Controllers.Api.V1;

/// <summary>
/// The REST API other apps call, from their server, with an API key. It's versioned: fields are only ever added, so
/// apps built against v1 keep working.
/// </summary>
[Route("api/v1")]
[ApiKeyAuth]
[IgnoreAntiforgeryToken] // Callers prove themselves with a key, not a browser cookie, so there's nothing to forge.
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
public sealed class V1Controller(ApiChatService api, IOptions<JsonOptions> jsonOptions) : ApiControllerBase
{
    /// <summary>Lists the enabled providers and their models.</summary>
    [HttpGet("models")]
    [ProducesResponseType<IReadOnlyList<ApiProviderDto>>(StatusCodes.Status200OK)]
    public Task<IActionResult> Models(CancellationToken ct) =>
        Handle(async () => Ok(await api.ListModelsAsync(ct)));

    /// <summary>Lists the knowledge folders chats can answer from.</summary>
    [HttpGet("folders")]
    [ProducesResponseType<IReadOnlyList<ApiFolderDto>>(StatusCodes.Status200OK)]
    public Task<IActionResult> Folders(CancellationToken ct) =>
        Handle(async () => Ok(await api.ListFoldersAsync(ct)));

    /// <summary>
    /// Asks a question. Returns the whole reply as JSON, or with <c>"stream": true</c>, Server-Sent Events as it's
    /// written: <c>started</c>, <c>delta</c>, <c>activity</c>, <c>sources</c>, then <c>completed</c> or <c>error</c>.
    /// </summary>
    [HttpPost("chat")]
    [Produces("application/json", "text/event-stream")]
    [ProducesResponseType<ApiChatReply>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    public async Task<IActionResult> Chat(ApiChatRequest request, CancellationToken ct)
    {
        ApiPreparedChat? prepared = null;
        var invalid = await Handle(async () =>
        {
            prepared = await api.PrepareAsync(request, ct);
            return Ok();
        });
        if (prepared is null)
        {
            return invalid;
        }

        if (request.Stream)
        {
            ServerSentEvents.Start(Response);
            var json = jsonOptions.Value.JsonSerializerOptions;
            await foreach (var chatEvent in api.StreamAsync(prepared, ct))
            {
                await ServerSentEvents.WriteAsync(Response, chatEvent.GetEventName(), chatEvent, json, ct);
            }

            return new EmptyResult();
        }

        try
        {
            return Ok(await api.AskAsync(prepared, ct));
        }
        catch (ApiChatFailedException ex)
        {
            var problem = Problem(
                ex.Message,
                statusCode: ex.IsProviderError ? StatusCodes.Status502BadGateway : StatusCodes.Status400BadRequest,
                title: ex.IsProviderError ? "Provider error" : "Invalid request");
            if (ex.ConversationId is { } conversationId && problem.Value is ProblemDetails details)
            {
                details.Extensions["conversationId"] = conversationId;
            }

            return problem;
        }
    }

    /// <summary>Lists conversations started through the API, most recent first. Pass <c>user</c> for one user's only.</summary>
    [HttpGet("conversations")]
    [ProducesResponseType<IReadOnlyList<ApiConversationSummaryDto>>(StatusCodes.Status200OK)]
    public Task<IActionResult> Conversations(string? user, CancellationToken ct) =>
        Handle(async () => Ok(await api.ListConversationsAsync(user, ct)));

    /// <summary>Returns a conversation started through the API, with its messages and their sources.</summary>
    [HttpGet("conversations/{id:guid}")]
    [ProducesResponseType<ApiConversationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<IActionResult> Conversation(Guid id, CancellationToken ct) =>
        Handle(async () => Ok(await api.GetConversationAsync(id, ct)));

    /// <summary>Deletes a conversation started through the API.</summary>
    [HttpDelete("conversations/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<IActionResult> DeleteConversation(Guid id, CancellationToken ct) =>
        Handle(async () =>
        {
            await api.DeleteConversationAsync(id, ct);
            return NoContent();
        });
}
