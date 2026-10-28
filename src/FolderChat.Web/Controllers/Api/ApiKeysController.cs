using FolderChat.Application.Api;
using Microsoft.AspNetCore.Mvc;

namespace FolderChat.Web.Controllers.Api;

/// <summary>The API keys other apps use for <c>/api/v1</c>, managed from Settings.</summary>
[Route("api/api-keys")]
public sealed class ApiKeysController(ApiKeyService keys) : ApiControllerBase
{
    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct) =>
        Handle(async () => Ok(await keys.ListAsync(ct)));

    /// <summary>Creates a key. The response holds its secret, which is never shown again.</summary>
    [HttpPost]
    public Task<IActionResult> Create(CreateApiKeyRequest request, CancellationToken ct) =>
        Handle(async () =>
        {
            var created = await keys.CreateAsync(request.Name, ct);
            return StatusCode(StatusCodes.Status201Created, created);
        });

    [HttpDelete("{id:guid}")]
    public Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        Handle(async () =>
        {
            await keys.DeleteAsync(id, ct);
            return NoContent();
        });
}

public sealed record CreateApiKeyRequest(string? Name);
