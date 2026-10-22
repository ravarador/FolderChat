namespace FolderChat.Core.Providers;

/// <summary>A configured connection to a model provider, such as an Anthropic API key or a local Ollama server.</summary>
public sealed class Provider
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Name { get; set; }

    public ProviderKind Kind { get; set; }

    /// <summary>Overrides the provider's default endpoint. Required for <see cref="ProviderKind.OpenAICompatible"/>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The API key, encrypted at rest. Never send this value to the browser.</summary>
    public string? ProtectedApiKey { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public List<ProviderModel> Models { get; set; } = [];
}
