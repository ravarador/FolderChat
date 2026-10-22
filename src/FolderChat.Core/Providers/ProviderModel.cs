namespace FolderChat.Core.Providers;

/// <summary>A model offered by a provider. Enabled models appear in the chat's model picker.</summary>
public sealed class ProviderModel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProviderId { get; set; }

    /// <summary>The ID the provider's API expects, such as <c>claude-opus-5-5</c> or <c>deepseek-chat</c>.</summary>
    public required string ModelId { get; set; }

    public string? DisplayName { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>True when the user typed the model ID instead of loading it from the provider.</summary>
    public bool IsManual { get; set; }

    /// <summary>The largest response the model allows, when the provider reports it.</summary>
    public int? MaxOutputTokens { get; set; }

    /// <summary>The context window size, when the provider reports it.</summary>
    public int? ContextWindow { get; set; }
}
