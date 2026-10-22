using FolderChat.Core.Providers;
using Microsoft.Extensions.AI;

namespace FolderChat.Application.Abstractions;

/// <summary>Builds a chat client for one model. This is where each provider's adapter plugs in.</summary>
public interface IChatClientFactory
{
    /// <summary>Creates a client that talks to <paramref name="model"/>. The caller disposes it.</summary>
    IChatClient Create(Provider provider, ProviderModel model);
}
