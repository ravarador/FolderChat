using FolderChat.Core.Providers;

namespace FolderChat.Application.Abstractions;

/// <summary>Asks a provider which models it offers.</summary>
public interface IModelCatalog
{
    /// <exception cref="Common.ProviderCallFailedException">The provider rejected the request or could not be reached.</exception>
    Task<IReadOnlyList<DiscoveredModel>> ListModelsAsync(Provider provider, CancellationToken ct = default);
}

/// <summary>A model reported by a provider. Token limits are null when the provider doesn't say.</summary>
public sealed record DiscoveredModel(
    string ModelId,
    string? DisplayName = null,
    int? MaxOutputTokens = null,
    int? ContextWindow = null);
