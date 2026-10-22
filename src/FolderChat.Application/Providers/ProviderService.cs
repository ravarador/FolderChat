using FolderChat.Application.Abstractions;
using FolderChat.Application.Common;
using FolderChat.Core.Providers;

namespace FolderChat.Application.Providers;

/// <summary>Adds, edits and removes providers, and manages which of their models show in the picker.</summary>
public sealed class ProviderService(
    IProviderStore store,
    IModelCatalog catalog,
    ISecretProtector protector,
    TimeProvider clock)
{
    private const int MaxNameLength = 100;
    private const int MaxModelIdLength = 200;
    private const int MaxApiKeyLength = 1000;

    public async Task<IReadOnlyList<ProviderDto>> ListAsync(CancellationToken ct = default)
    {
        var providers = await store.ListAsync(ct);
        return providers
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToDto)
            .ToList();
    }

    public async Task<ProviderDto> GetAsync(Guid id, CancellationToken ct = default) =>
        ToDto(await FindAsync(id, ct));

    public async Task<ProviderDto> CreateAsync(SaveProviderRequest request, CancellationToken ct = default)
    {
        var (name, baseUrl) = Validate(request);
        var provider = new Provider
        {
            Name = name,
            Kind = request.Kind,
            BaseUrl = baseUrl,
            IsEnabled = request.IsEnabled,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        SetApiKey(provider, request.ApiKey);

        store.Add(provider);
        await store.SaveChangesAsync(ct);
        return ToDto(provider);
    }

    public async Task<ProviderDto> UpdateAsync(Guid id, SaveProviderRequest request, CancellationToken ct = default)
    {
        var provider = await FindAsync(id, ct);
        var (name, baseUrl) = Validate(request);

        provider.Name = name;
        provider.Kind = request.Kind;
        provider.BaseUrl = baseUrl;
        provider.IsEnabled = request.IsEnabled;
        if (request.ApiKey is not null)
        {
            SetApiKey(provider, request.ApiKey);
        }

        await store.SaveChangesAsync(ct);
        return ToDto(provider);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var provider = await FindAsync(id, ct);
        store.Remove(provider);
        await store.SaveChangesAsync(ct);
    }

    /// <summary>Reloads the provider's model list from its API.</summary>
    public async Task<ProviderDto> RefreshModelsAsync(Guid id, CancellationToken ct = default)
    {
        var provider = await FindAsync(id, ct);
        var discovered = await catalog.ListModelsAsync(provider, ct);
        ModelListMerger.Merge(provider, discovered);
        await store.SaveChangesAsync(ct);
        return ToDto(provider);
    }

    /// <summary>Adds a model by ID, for providers that don't list their models.</summary>
    public async Task<ProviderDto> AddModelAsync(Guid id, string modelId, CancellationToken ct = default)
    {
        var provider = await FindAsync(id, ct);
        modelId = ValidateModelId(modelId);

        var existing = provider.Models.FirstOrDefault(m => m.ModelId == modelId);
        if (existing is not null)
        {
            existing.IsEnabled = true;
        }
        else
        {
            provider.Models.Add(new ProviderModel { ProviderId = provider.Id, ModelId = modelId, IsManual = true });
        }

        await store.SaveChangesAsync(ct);
        return ToDto(provider);
    }

    public async Task<ProviderDto> SetModelEnabledAsync(Guid id, string modelId, bool enabled, CancellationToken ct = default)
    {
        var provider = await FindAsync(id, ct);
        var model = provider.Models.FirstOrDefault(m => m.ModelId == modelId)
            ?? throw new NotFoundException($"Model '{modelId}' is not saved for this provider.");

        model.IsEnabled = enabled;
        await store.SaveChangesAsync(ct);
        return ToDto(provider);
    }

    public async Task<ProviderDto> SetAllModelsEnabledAsync(Guid id, bool enabled, CancellationToken ct = default)
    {
        var provider = await FindAsync(id, ct);
        foreach (var model in provider.Models)
        {
            model.IsEnabled = enabled;
        }

        await store.SaveChangesAsync(ct);
        return ToDto(provider);
    }

    public async Task<ProviderDto> RemoveModelAsync(Guid id, string modelId, CancellationToken ct = default)
    {
        var provider = await FindAsync(id, ct);
        provider.Models.RemoveAll(m => m.ModelId == modelId);
        await store.SaveChangesAsync(ct);
        return ToDto(provider);
    }

    /// <summary>Enabled providers that have at least one enabled model, for the chat's model picker.</summary>
    public async Task<IReadOnlyList<PickerProviderDto>> GetPickerAsync(CancellationToken ct = default)
    {
        var providers = await store.ListAsync(ct);
        return providers
            .Where(p => p.IsEnabled)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new PickerProviderDto(
                p.Id,
                p.Name,
                p.Kind,
                IsLocal(p),
                p.Models
                    .Where(m => m.IsEnabled)
                    .OrderBy(m => m.DisplayName ?? m.ModelId, StringComparer.OrdinalIgnoreCase)
                    .Select(m => new PickerModelDto(m.ModelId, m.DisplayName ?? m.ModelId))
                    .ToList()))
            .Where(p => p.Models.Count > 0)
            .ToList();
    }

    /// <summary>True when the provider runs on this computer, so messages never leave it.</summary>
    public static bool IsLocal(Provider provider)
    {
        if (provider.Kind == ProviderKind.Ollama && string.IsNullOrEmpty(provider.BaseUrl))
        {
            return true;
        }

        return Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var uri) && uri.IsLoopback;
    }

    private async Task<Provider> FindAsync(Guid id, CancellationToken ct) =>
        await store.FindAsync(id, ct) ?? throw new NotFoundException("That provider doesn't exist.");

    private void SetApiKey(Provider provider, string? apiKey)
    {
        var trimmed = apiKey?.Trim();
        if (trimmed?.Length > MaxApiKeyLength)
        {
            throw new ValidationFailedException("That API key is too long.");
        }

        provider.ProtectedApiKey = string.IsNullOrEmpty(trimmed) ? null : protector.Protect(trimmed);
    }

    private static (string Name, string? BaseUrl) Validate(SaveProviderRequest request)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length == 0)
        {
            throw new ValidationFailedException("Give the provider a name.");
        }

        if (name.Length > MaxNameLength)
        {
            throw new ValidationFailedException($"Keep the name under {MaxNameLength} characters.");
        }

        if (!Enum.IsDefined(request.Kind))
        {
            throw new ValidationFailedException("Choose a provider type.");
        }

        var baseUrl = NormalizeBaseUrl(request.BaseUrl);
        if (baseUrl is null && request.Kind == ProviderKind.OpenAICompatible)
        {
            throw new ValidationFailedException("OpenAI-compatible providers need a base URL.");
        }

        return (name, baseUrl);
    }

    /// <summary>Trims the URL and drops any trailing slash. Returns null for a blank value.</summary>
    internal static string? NormalizeBaseUrl(string? baseUrl)
    {
        var trimmed = baseUrl?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ValidationFailedException("The base URL must start with http:// or https://.");
        }

        return trimmed.TrimEnd('/');
    }

    private static string ValidateModelId(string? modelId)
    {
        var trimmed = modelId?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            throw new ValidationFailedException("Enter a model ID.");
        }

        if (trimmed.Length > MaxModelIdLength)
        {
            throw new ValidationFailedException($"Keep the model ID under {MaxModelIdLength} characters.");
        }

        return trimmed;
    }

    private static ProviderDto ToDto(Provider provider) => new(
        provider.Id,
        provider.Name,
        provider.Kind,
        provider.BaseUrl,
        provider.ProtectedApiKey is not null,
        provider.IsEnabled,
        provider.Models
            .OrderBy(m => m.ModelId, StringComparer.OrdinalIgnoreCase)
            .Select(m => new ProviderModelDto(m.ModelId, m.DisplayName, m.IsEnabled, m.IsManual, m.MaxOutputTokens, m.ContextWindow))
            .ToList());
}
