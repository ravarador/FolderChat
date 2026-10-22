using FolderChat.Application.Abstractions;
using FolderChat.Core.Providers;

namespace FolderChat.Application.Providers;

/// <summary>Updates a provider's saved models from a fresh list, keeping the user's choices.</summary>
internal static class ModelListMerger
{
    /// <summary>
    /// Lists longer than this start with every model switched off, so a catalog like OpenRouter's
    /// doesn't flood the model picker. The user switches on the ones they want.
    /// </summary>
    public const int AutoEnableLimit = 30;

    // Parts of OpenAI-style model IDs that mean "not a chat model".
    private static readonly string[] NonChatMarkers =
    [
        "embed", "tts", "whisper", "dall-e", "moderation", "transcribe", "audio",
        "realtime", "image", "babbage", "davinci", "sora", "rerank",
    ];

    public static bool LooksLikeChatModel(string modelId) =>
        !NonChatMarkers.Any(marker => modelId.Contains(marker, StringComparison.OrdinalIgnoreCase));

    public static void Merge(Provider provider, IReadOnlyList<DiscoveredModel> discovered)
    {
        var saved = provider.Models.ToDictionary(m => m.ModelId, StringComparer.Ordinal);
        var listed = new HashSet<string>(StringComparer.Ordinal);
        var enableNewModels = discovered.Count <= AutoEnableLimit;

        foreach (var model in discovered)
        {
            if (!listed.Add(model.ModelId))
            {
                continue;
            }

            if (saved.TryGetValue(model.ModelId, out var existing))
            {
                existing.DisplayName = model.DisplayName ?? existing.DisplayName;
                existing.MaxOutputTokens = model.MaxOutputTokens ?? existing.MaxOutputTokens;
                existing.ContextWindow = model.ContextWindow ?? existing.ContextWindow;
                existing.IsManual = false;
                continue;
            }

            provider.Models.Add(new ProviderModel
            {
                ProviderId = provider.Id,
                ModelId = model.ModelId,
                DisplayName = model.DisplayName,
                MaxOutputTokens = model.MaxOutputTokens,
                ContextWindow = model.ContextWindow,
                IsEnabled = enableNewModels && LooksLikeChatModel(model.ModelId),
            });
        }

        // Drop models the provider no longer offers, but keep the ones the user typed in.
        provider.Models.RemoveAll(m => !m.IsManual && !listed.Contains(m.ModelId));
    }
}
