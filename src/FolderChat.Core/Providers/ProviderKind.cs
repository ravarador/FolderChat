namespace FolderChat.Core.Providers;

/// <summary>How FolderChat talks to a provider. Each kind maps to one adapter.</summary>
public enum ProviderKind
{
    /// <summary>Claude models through the official Anthropic SDK.</summary>
    Anthropic = 1,

    /// <summary>OpenAI's own API through the official OpenAI SDK.</summary>
    OpenAI = 2,

    /// <summary>
    /// Any API that speaks OpenAI's chat-completions protocol: DeepSeek, Kimi, GLM, Gemini,
    /// OpenRouter, Groq, LM Studio and others. Needs a base URL.
    /// </summary>
    OpenAICompatible = 3,

    /// <summary>Local models served by Ollama.</summary>
    Ollama = 4,
}
