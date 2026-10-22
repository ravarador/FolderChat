using FolderChat.Application.Abstractions;
using FolderChat.Core.Providers;
using Microsoft.Extensions.AI;

namespace FolderChat.Infrastructure.AI;

/// <summary>
/// The four adapters. Each turns a provider's own SDK into a Microsoft.Extensions.AI <see cref="IChatClient"/>,
/// so the rest of the app never knows which provider it is talking to.
/// </summary>
internal sealed class ChatClientFactory(ProviderClients clients) : IChatClientFactory
{
    /// <summary>
    /// Claude needs a max_tokens value on every request. This is the ceiling when the model allows more;
    /// it limits the longest possible answer, not what each answer costs.
    /// </summary>
    internal const int AnthropicMaxOutputTokens = 64_000;

    public IChatClient Create(Provider provider, ProviderModel model) => provider.Kind switch
    {
        ProviderKind.Anthropic => clients.CreateAnthropic(provider).AsIChatClient(
            model.ModelId,
            model.MaxOutputTokens is { } max ? Math.Min(max, AnthropicMaxOutputTokens) : null),

        ProviderKind.OpenAI or ProviderKind.OpenAICompatible =>
            clients.CreateOpenAI(provider).GetChatClient(model.ModelId).AsIChatClient(),

        // OllamaApiClient is an IChatClient itself.
        ProviderKind.Ollama => clients.CreateOllama(provider, model.ModelId),

        _ => throw new NotSupportedException($"Provider type '{provider.Kind}' isn't supported."),
    };
}
