using System.ComponentModel.DataAnnotations;
using FolderChat.Application.Conversations;
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
}

public sealed record RenameConversationRequest([Required] string Title);
