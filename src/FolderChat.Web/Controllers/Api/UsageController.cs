using FolderChat.Application.Conversations;
using Microsoft.AspNetCore.Mvc;

namespace FolderChat.Web.Controllers.Api;

[Route("api/usage")]
public sealed class UsageController(ConversationService conversations) : ApiControllerBase
{
    /// <summary>Tokens used by chat replies this month, by model.</summary>
    [HttpGet]
    public Task<IActionResult> Get(CancellationToken ct) =>
        Handle(async () => Ok(await conversations.GetUsageAsync(ct)));
}
