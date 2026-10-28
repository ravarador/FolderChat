using FolderChat.Core.Providers;

namespace FolderChat.Application.Providers;

/// <summary>A provider as the browser sees it. The API key itself is never included.</summary>
public sealed record ProviderDto(
    Guid Id,
    string Name,
    ProviderKind Kind,
    string? BaseUrl,
    bool HasApiKey,
    bool IsEnabled,
    IReadOnlyList<ProviderModelDto> Models);

public sealed record ProviderModelDto(
    string ModelId,
    string? DisplayName,
    bool IsEnabled,
    bool IsManual,
    int? MaxOutputTokens,
    int? ContextWindow);

/// <summary>Creates or updates a provider.</summary>
/// <param name="ApiKey">
/// On create, the key to store (optional). On update: <c>null</c> keeps the stored key,
/// an empty string removes it, and any other value replaces it.
/// </param>
public sealed record SaveProviderRequest(
    string Name,
    ProviderKind Kind,
    string? BaseUrl,
    string? ApiKey,
    bool IsEnabled = true);

/// <summary>A provider and its enabled models, for the chat's model picker.</summary>
public sealed record PickerProviderDto(
    Guid Id,
    string Name,
    ProviderKind Kind,
    bool IsLocal,
    IReadOnlyList<PickerModelDto> Models);

/// <param name="ContextWindow">The model's context size in tokens, when the provider reports it.</param>
public sealed record PickerModelDto(string ModelId, string Label, int? ContextWindow);
