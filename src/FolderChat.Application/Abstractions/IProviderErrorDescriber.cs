using FolderChat.Core.Providers;

namespace FolderChat.Application.Abstractions;

/// <summary>Turns an exception from a provider SDK into a short message that is safe to show to the user.</summary>
public interface IProviderErrorDescriber
{
    /// <param name="kind">The provider's type, so the message can point at a misconfigured type.</param>
    string Describe(Exception exception, ProviderKind kind);
}
