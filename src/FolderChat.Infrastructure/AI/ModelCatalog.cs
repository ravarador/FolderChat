using Anthropic.Models.Models;
using FolderChat.Application.Abstractions;
using FolderChat.Application.Common;
using FolderChat.Core.Providers;
using Microsoft.Extensions.Logging;

namespace FolderChat.Infrastructure.AI;

/// <summary>Reads each provider's model list from its API.</summary>
internal sealed class ModelCatalog(
    ProviderClients clients,
    IProviderErrorDescriber errors,
    ILogger<ModelCatalog> logger) : IModelCatalog
{
    public async Task<IReadOnlyList<DiscoveredModel>> ListModelsAsync(Provider provider, CancellationToken ct = default)
    {
        try
        {
            var models = provider.Kind switch
            {
                ProviderKind.Anthropic => await ListAnthropicAsync(provider, ct),
                ProviderKind.OpenAI or ProviderKind.OpenAICompatible => await ListOpenAIAsync(provider, ct),
                ProviderKind.Ollama => await ListOllamaAsync(provider, ct),
                _ => throw new NotSupportedException($"Provider type '{provider.Kind}' isn't supported."),
            };

            return models
                .Where(m => !string.IsNullOrWhiteSpace(m.ModelId))
                .OrderBy(m => m.ModelId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A rejected key or a stopped server is an expected failure: one line at Warning, details at Debug.
            var reason = errors.Describe(ex, provider.Kind);
            logger.LogWarning("Loading models from {Provider} failed: {Reason}", provider.Name, reason);
            logger.LogDebug(ex, "Model list error details for {Provider}", provider.Name);
            throw new ProviderCallFailedException($"Couldn't load models from {provider.Name}: {reason}", ex);
        }
    }

    private async Task<List<DiscoveredModel>> ListAnthropicAsync(Provider provider, CancellationToken ct)
    {
        var client = clients.CreateAnthropic(provider);
        var models = new List<DiscoveredModel>();
        var page = await client.Models.List(new ModelListParams { Limit = 100 }, ct);
        while (true)
        {
            models.AddRange(page.Items.Select(m => new DiscoveredModel(
                m.ID,
                m.DisplayName,
                ToInt(m.MaxTokens),
                ToInt(m.MaxInputTokens))));

            if (!page.HasNext())
            {
                return models;
            }

            page = await page.Next(ct);
        }
    }

    private async Task<List<DiscoveredModel>> ListOpenAIAsync(Provider provider, CancellationToken ct)
    {
        var client = clients.CreateOpenAI(provider);
        var result = await client.GetOpenAIModelClient().GetModelsAsync(ct);
        return result.Value.Select(m => new DiscoveredModel(m.Id)).ToList();
    }

    private async Task<List<DiscoveredModel>> ListOllamaAsync(Provider provider, CancellationToken ct)
    {
        using var client = clients.CreateOllama(provider);
        var models = await client.ListLocalModelsAsync(ct);
        return models.Select(m => new DiscoveredModel(m.Name)).ToList();
    }

    private static int? ToInt(long? value) => value is null ? null : (int)Math.Min(value.Value, int.MaxValue);
}
