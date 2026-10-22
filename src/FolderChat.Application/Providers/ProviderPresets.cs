using FolderChat.Core.Providers;

namespace FolderChat.Application.Providers;

/// <summary>A ready-made provider setup the Settings page can fill in.</summary>
public sealed record ProviderPreset(
    string Key,
    string Name,
    ProviderKind Kind,
    string? BaseUrl,
    bool NeedsApiKey,
    string? ApiKeyUrl,
    string Notes);

public static class ProviderPresets
{
    public const string OllamaDefaultUrl = "http://localhost:11434";

    public static IReadOnlyList<ProviderPreset> All { get; } =
    [
        new("anthropic", "Anthropic (Claude)", ProviderKind.Anthropic, null, true,
            "https://platform.claude.com/settings/keys",
            "Leave the key blank to use the ANTHROPIC_API_KEY environment variable."),
        new("openai", "OpenAI", ProviderKind.OpenAI, null, true,
            "https://platform.openai.com/api-keys",
            "Leave the key blank to use the OPENAI_API_KEY environment variable."),
        new("deepseek", "DeepSeek", ProviderKind.OpenAICompatible, "https://api.deepseek.com", true,
            "https://platform.deepseek.com/api_keys",
            "Hosted by DeepSeek in China."),
        new("kimi", "Kimi (Moonshot AI)", ProviderKind.OpenAICompatible, "https://api.moonshot.ai/v1", true,
            "https://platform.kimi.ai/console/api-keys",
            "Hosted by Moonshot AI in China."),
        new("glm", "GLM (Z.ai)", ProviderKind.OpenAICompatible, "https://api.z.ai/api/paas/v4", true,
            "https://z.ai/manage-apikey/apikey-list",
            "Hosted by Zhipu AI. In mainland China use https://open.bigmodel.cn/api/paas/v4 instead."),
        new("gemini", "Google Gemini", ProviderKind.OpenAICompatible,
            "https://generativelanguage.googleapis.com/v1beta/openai", true,
            "https://aistudio.google.com/apikey",
            "Uses Google's OpenAI-compatible endpoint."),
        new("openrouter", "OpenRouter", ProviderKind.OpenAICompatible, "https://openrouter.ai/api/v1", true,
            "https://openrouter.ai/settings/keys",
            "One key for hundreds of models, including Kimi, GLM, DeepSeek and Qwen."),
        new("groq", "Groq", ProviderKind.OpenAICompatible, "https://api.groq.com/openai/v1", true,
            "https://console.groq.com/keys",
            "Very fast hosting for open models."),
        new("xai", "xAI (Grok)", ProviderKind.OpenAICompatible, "https://api.x.ai/v1", true,
            "https://console.x.ai",
            ""),
        new("mistral", "Mistral", ProviderKind.OpenAICompatible, "https://api.mistral.ai/v1", true,
            "https://console.mistral.ai/api-keys",
            ""),
        new("ollama", "Ollama (local)", ProviderKind.Ollama, OllamaDefaultUrl, false,
            null,
            "Runs models on this computer. Install Ollama, then run 'ollama pull <model>'."),
        new("lmstudio", "LM Studio (local)", ProviderKind.OpenAICompatible, "http://localhost:1234/v1", false,
            null,
            "Start LM Studio's local server first."),
    ];
}
