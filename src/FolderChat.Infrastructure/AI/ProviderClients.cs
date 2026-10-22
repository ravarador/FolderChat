using System.ClientModel;
using System.ClientModel.Primitives;
using Anthropic;
using FolderChat.Application.Abstractions;
using FolderChat.Application.Providers;
using FolderChat.Core.Providers;
using OllamaSharp;
using OpenAI;

namespace FolderChat.Infrastructure.AI;

internal static class HttpClientNames
{
    public const string Anthropic = "FolderChat.Anthropic";
    public const string OpenAI = "FolderChat.OpenAI";
    public const string Ollama = "FolderChat.Ollama";
}

/// <summary>Builds the official SDK client for each provider kind, with its stored API key and endpoint.</summary>
internal sealed class ProviderClients(ISecretProtector protector, IHttpClientFactory httpClients)
{
    /// <summary>Long enough for slow local models; the user can always press Stop sooner.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(10);

    public IAnthropicClient CreateAnthropic(Provider provider)
    {
        IAnthropicClient client = new AnthropicClient
        {
            HttpClient = httpClients.CreateClient(HttpClientNames.Anthropic),
            Timeout = RequestTimeout,
        };

        // Without a stored key the SDK falls back to ANTHROPIC_API_KEY (or an `ant auth login` profile).
        var apiKey = GetApiKey(provider);
        if (apiKey is not null)
        {
            client = client.WithOptions(options => options with { ApiKey = apiKey });
        }

        if (provider.BaseUrl is { } baseUrl)
        {
            client = client.WithOptions(options => options with { BaseUrl = baseUrl });
        }

        return client;
    }

    /// <summary>Used for OpenAI itself and for every OpenAI-compatible provider.</summary>
    public OpenAIClient CreateOpenAI(Provider provider)
    {
        var options = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(httpClients.CreateClient(HttpClientNames.OpenAI)),
            NetworkTimeout = RequestTimeout,
        };

        if (provider.BaseUrl is { } baseUrl)
        {
            options.Endpoint = new Uri(baseUrl);
        }

        var apiKey = GetApiKey(provider);
        if (apiKey is null && provider.Kind == ProviderKind.OpenAI)
        {
            apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        }

        // Local servers such as LM Studio ignore the key, but the SDK insists on one.
        return new OpenAIClient(new ApiKeyCredential(string.IsNullOrEmpty(apiKey) ? "not-needed" : apiKey), options);
    }

    public OllamaApiClient CreateOllama(Provider provider, string defaultModel = "")
    {
        var http = httpClients.CreateClient(HttpClientNames.Ollama);
        http.BaseAddress = new Uri((provider.BaseUrl ?? ProviderPresets.OllamaDefaultUrl).TrimEnd('/') + "/");
        return new OllamaApiClient(http, defaultModel);
    }

    private string? GetApiKey(Provider provider) =>
        string.IsNullOrEmpty(provider.ProtectedApiKey) ? null : protector.Unprotect(provider.ProtectedApiKey);
}
