using System.ComponentModel.DataAnnotations;
using FolderChat.Application.Conversations;
using FolderChat.Core.Knowledge;
using Microsoft.AspNetCore.Mvc;

namespace FolderChat.Web.Controllers.Api;

[Route("api/conversations")]
public sealed class ConversationsController(ConversationService conversations) : ApiControllerBase
{
    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct) =>
        Handle(async () => Ok(await conversations.ListAsync(ct)));

    [HttpGet("{id:guid}")]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        Handle(async () => Ok(await conversations.GetAsync(id, ct)));

    [HttpPatch("{id:guid}")]
    public Task<IActionResult> Rename(Guid id, RenameConversationRequest request, CancellationToken ct) =>
        Handle(async () => Ok(await conversations.RenameAsync(id, request.Title, ct)));

    [HttpDelete("{id:guid}")]
    public Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        Handle(async () =>
        {
            await conversations.DeleteAsync(id, ct);
            return NoContent();
        });

    /// <summary>Sets the knowledge folders this chat answers from, and how hard it searches them.</summary>
    [HttpPut("{id:guid}/folders")]
    public Task<IActionResult> SetFolders(Guid id, SetChatFoldersRequest request, CancellationToken ct) =>
        Handle(async () =>
        {
            await conversations.SetFoldersAsync(id, request.FolderIds, request.Effort, ct);
            return NoContent();
        });
}

public sealed record RenameConversationRequest([Required] string Title);

/// <param name="FolderIds">The folders, empty for none.</param>
/// <param name="Effort">Quick, Standard or Thorough.</param>
public sealed record SetChatFoldersRequest(IReadOnlyList<Guid>? FolderIds, SearchEffort Effort = SearchEffort.Standard);
