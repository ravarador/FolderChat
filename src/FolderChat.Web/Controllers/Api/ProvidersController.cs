using System.ComponentModel.DataAnnotations;
using FolderChat.Application.Providers;
using Microsoft.AspNetCore.Mvc;

namespace FolderChat.Web.Controllers.Api;

[Route("api/providers")]
public sealed class ProvidersController(ProviderService providers) : ApiControllerBase
{
    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct) =>
        Handle(async () => Ok(await providers.ListAsync(ct)));

    [HttpGet("presets")]
    public IActionResult Presets() => Ok(ProviderPresets.All);

    /// <summary>Enabled providers and models, grouped for the chat's model picker.</summary>
    [HttpGet("picker")]
    public Task<IActionResult> Picker(CancellationToken ct) =>
        Handle(async () => Ok(await providers.GetPickerAsync(ct)));

    [HttpGet("{id:guid}")]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        Handle(async () => Ok(await providers.GetAsync(id, ct)));

    [HttpPost]
    public Task<IActionResult> Create(SaveProviderRequest request, CancellationToken ct) =>
        Handle(async () =>
        {
            var provider = await providers.CreateAsync(request, ct);
            return CreatedAtAction(nameof(Get), new { id = provider.Id }, provider);
        });

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, SaveProviderRequest request, CancellationToken ct) =>
        Handle(async () => Ok(await providers.UpdateAsync(id, request, ct)));

    [HttpDelete("{id:guid}")]
    public Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        Handle(async () =>
        {
            await providers.DeleteAsync(id, ct);
            return NoContent();
        });

    /// <summary>Reloads the model list from the provider's API.</summary>
    [HttpPost("{id:guid}/models/refresh")]
    public Task<IActionResult> RefreshModels(Guid id, CancellationToken ct) =>
        Handle(async () => Ok(await providers.RefreshModelsAsync(id, ct)));

    /// <summary>Adds a model by ID. Model IDs can contain slashes (OpenRouter's do), so they travel in the body.</summary>
    [HttpPost("{id:guid}/models")]
    public Task<IActionResult> AddModel(Guid id, ModelIdRequest request, CancellationToken ct) =>
        Handle(async () => Ok(await providers.AddModelAsync(id, request.ModelId, ct)));

    [HttpPut("{id:guid}/models")]
    public Task<IActionResult> SetModelEnabled(Guid id, SetModelEnabledRequest request, CancellationToken ct) =>
        Handle(async () => Ok(await providers.SetModelEnabledAsync(id, request.ModelId, request.IsEnabled, ct)));

    [HttpPut("{id:guid}/models/all")]
    public Task<IActionResult> SetAllModelsEnabled(Guid id, SetAllModelsEnabledRequest request, CancellationToken ct) =>
        Handle(async () => Ok(await providers.SetAllModelsEnabledAsync(id, request.IsEnabled, ct)));

    [HttpPost("{id:guid}/models/remove")]
    public Task<IActionResult> RemoveModel(Guid id, ModelIdRequest request, CancellationToken ct) =>
        Handle(async () => Ok(await providers.RemoveModelAsync(id, request.ModelId, ct)));
}

public sealed record ModelIdRequest([Required] string ModelId);

public sealed record SetModelEnabledRequest([Required] string ModelId, bool IsEnabled);

public sealed record SetAllModelsEnabledRequest(bool IsEnabled);
