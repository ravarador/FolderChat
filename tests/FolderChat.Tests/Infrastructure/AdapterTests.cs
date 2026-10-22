using System.Text;
using FolderChat.Application.Common;
using FolderChat.Core.Providers;
using FolderChat.Infrastructure.AI;
using FolderChat.Tests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace FolderChat.Tests.Infrastructure;

/// <summary>
/// Runs each provider's real SDK against a scripted HTTP server: checks the URL and key it sends,
/// and that its streamed reply comes back as plain text.
/// </summary>
public sealed class AdapterTests
{
    [Theory]
    [InlineData("https://api.z.ai/api/paas/v4", "https://api.z.ai/api/paas/v4/chat/completions")]
    [InlineData("https://api.deepseek.com", "https://api.deepseek.com/chat/completions")]
    [InlineData("http://localhost:1234/v1", "http://localhost:1234/v1/chat/completions")]
    public async Task OpenAI_compatible_providers_stream_from_their_base_url(string baseUrl, string expectedUrl)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(OpenAIStream("Hel", "lo"), "text/event-stream"));
        var provider = Provider(ProviderKind.OpenAICompatible, baseUrl, "test-key");

        var text = await StreamTextAsync(handler, provider, "glm-4.6");

        Assert.Equal("Hello", text);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(expectedUrl, request.Uri.ToString());
        Assert.Equal("Bearer test-key", request.Headers["Authorization"]);
        Assert.Contains("\"model\":\"glm-4.6\"", request.Body);
    }

    [Fact]
    public async Task OpenAI_compatible_model_lists_come_from_the_models_endpoint()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(
            """{"object":"list","data":[{"id":"deepseek-reasoner","object":"model","created":0,"owned_by":"deepseek"},{"id":"deepseek-chat","object":"model","created":0,"owned_by":"deepseek"}]}""",
            "application/json"));

        var models = await Catalog(handler).ListModelsAsync(Provider(ProviderKind.OpenAICompatible, "https://api.deepseek.com", "key"));

        Assert.Equal(["deepseek-chat", "deepseek-reasoner"], models.Select(m => m.ModelId));
        Assert.Equal("https://api.deepseek.com/models", handler.Requests.Single().Uri.ToString());
    }

    [Fact]
    public async Task Ollama_streams_from_the_local_server()
    {
        var ndjson = string.Join('\n',
            """{"model":"llama3","created_at":"2026-10-21T12:00:00Z","message":{"role":"assistant","content":"Hel"},"done":false}""",
            """{"model":"llama3","created_at":"2026-10-21T12:00:00Z","message":{"role":"assistant","content":"lo"},"done":false}""",
            """{"model":"llama3","created_at":"2026-10-21T12:00:00Z","message":{"role":"assistant","content":""},"done":true,"done_reason":"stop","prompt_eval_count":5,"eval_count":2}""") + "\n";
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(ndjson, "application/x-ndjson"));

        var text = await StreamTextAsync(handler, Provider(ProviderKind.Ollama, null, null), "llama3");

        Assert.Equal("Hello", text);
        Assert.Equal("http://localhost:11434/api/chat", handler.Requests.Single().Uri.ToString());
    }

    [Fact]
    public async Task Ollama_model_lists_come_from_the_tags_endpoint()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(
            """{"models":[{"name":"gemma4:31b","model":"gemma4:31b","modified_at":"2026-05-01T00:00:00Z","size":19000000000,"digest":"abc"}]}""",
            "application/json"));

        var models = await Catalog(handler).ListModelsAsync(Provider(ProviderKind.Ollama, "http://192.168.1.20:11434", null));

        Assert.Equal("gemma4:31b", Assert.Single(models).ModelId);
        Assert.Equal("http://192.168.1.20:11434/api/tags", handler.Requests.Single().Uri.ToString());
    }

    [Fact]
    public async Task Anthropic_streams_with_the_api_key_and_a_capped_max_tokens()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(AnthropicStream("Hello", " there"), "text/event-stream"));
        var provider = Provider(ProviderKind.Anthropic, null, "sk-ant-test");
        var model = new ProviderModel { ModelId = "claude-opus-5-5", MaxOutputTokens = 128_000 };

        var text = await StreamTextAsync(handler, provider, model);

        Assert.Equal("Hello there", text);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.Uri.ToString());
        Assert.Contains("sk-ant-test", string.Join(" ", request.Headers.Values));
        Assert.Contains($"\"max_tokens\":{ChatClientFactory.AnthropicMaxOutputTokens}", request.Body);
        Assert.Contains("\"model\":\"claude-opus-5-5\"", request.Body);
    }

    [Fact]
    public async Task Anthropic_model_lists_include_names_and_limits()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(
            """
            {"data":[{"type":"model","id":"claude-opus-5-5","display_name":"Claude Opus 5.5","created_at":"2026-01-01T00:00:00Z","max_input_tokens":1000000,"max_tokens":128000}],
             "has_more":false,"first_id":"claude-opus-5-5","last_id":"claude-opus-5-5"}
            """,
            "application/json"));

        var models = await Catalog(handler).ListModelsAsync(Provider(ProviderKind.Anthropic, null, "sk-ant-test"));

        var model = Assert.Single(models);
        Assert.Equal(("claude-opus-5-5", "Claude Opus 5.5", 128_000, 1_000_000), (model.ModelId, model.DisplayName, model.MaxOutputTokens, model.ContextWindow));
        Assert.StartsWith("https://api.anthropic.com/v1/models", handler.Requests.Single().Uri.ToString());
    }

    [Fact]
    public async Task A_rejected_key_becomes_a_readable_error()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Text(
            """{"error":{"message":"Incorrect API key provided","type":"invalid_request_error","code":"invalid_api_key"}}""",
            "application/json",
            System.Net.HttpStatusCode.Unauthorized));
        var catalog = new ModelCatalog(Clients(handler), new ProviderErrorDescriber(), NullLogger<ModelCatalog>.Instance);

        var error = await Assert.ThrowsAsync<ProviderCallFailedException>(() =>
            catalog.ListModelsAsync(Provider(ProviderKind.OpenAICompatible, "https://api.moonshot.ai/v1", "wrong")));

        Assert.Equal("Couldn't load models from Test: the API key was rejected (401): Incorrect API key provided. Check it in Settings.", error.Message);
    }

    [Fact]
    public async Task A_cloud_api_saved_as_Ollama_gets_a_hint_to_change_the_type()
    {
        // Seen for real: DeepSeek's URL saved with the Ollama type, which never sends the key.
        var stub = new StubHttpHandler(_ => StubHttpHandler.Text(
            """{"error":{"message":"Authentication Fails (governor)","type":"authentication_error"}}""",
            "application/json",
            System.Net.HttpStatusCode.Unauthorized));
        var clients = new ProviderClients(new FakeSecretProtector(), new StubHttpClientFactory(new ErrorBodyHandler { InnerHandler = stub }));
        var catalog = new ModelCatalog(clients, new ProviderErrorDescriber(), NullLogger<ModelCatalog>.Instance);

        var error = await Assert.ThrowsAsync<ProviderCallFailedException>(() =>
            catalog.ListModelsAsync(Provider(ProviderKind.Ollama, "https://api.deepseek.com", null)));

        Assert.Contains("Ollama-type providers don't send one", error.Message);
        Assert.Contains("change its type to OpenAI-compatible", error.Message);
        Assert.Equal("https://api.deepseek.com/api/tags", stub.Requests.Single().Uri.ToString());
    }

    [Fact]
    public async Task Ollama_errors_keep_the_servers_explanation()
    {
        // Seen for real: a model too big for the machine's free memory.
        var stub = new StubHttpHandler(_ => StubHttpHandler.Text(
            """{"error":"model requires more system memory (28.8 GiB) than is available (11.7 GiB)"}""",
            "application/json",
            System.Net.HttpStatusCode.InternalServerError));
        var clients = new ProviderClients(new FakeSecretProtector(), new StubHttpClientFactory(new ErrorBodyHandler { InnerHandler = stub }));
        using var client = new ChatClientFactory(clients).Create(Provider(ProviderKind.Ollama, null, null), new ProviderModel { ModelId = "gemma4:31b" });

        var error = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Hi")]))
            {
            }
        });

        Assert.Equal(
            "the provider reported an error (500): model requires more system memory (28.8 GiB) than is available (11.7 GiB)",
            new ProviderErrorDescriber().Describe(error, ProviderKind.Ollama));
    }

    [Theory]
    [InlineData("""{"error":"model not found"}""", "model not found")]
    [InlineData("""{"error":{"message":"Insufficient balance","type":"payment"}}""", "Insufficient balance")]
    [InlineData("""{"message":"Bad gateway"}""", "Bad gateway")]
    [InlineData("""{"error":{"message":"Authentication Fails, Your api key: ****abcd is invalid (request_id: 7bfd4749-2f57)"}}""", "Authentication Fails, Your api key: ****abcd is invalid")]
    [InlineData("<html><body>502</body></html>", null)]
    [InlineData("plain text failure\nmore detail", "plain text failure")]
    [InlineData("", null)]
    public void Error_bodies_are_reduced_to_their_message(string body, string? expected) =>
        Assert.Equal(expected, ErrorBody.Extract(body));

    private static Provider Provider(ProviderKind kind, string? baseUrl, string? apiKey) => new()
    {
        Name = "Test",
        Kind = kind,
        BaseUrl = baseUrl,
        ProtectedApiKey = apiKey is null ? null : new FakeSecretProtector().Protect(apiKey),
    };

    private static ProviderClients Clients(StubHttpHandler handler) =>
        new(new FakeSecretProtector(), new StubHttpClientFactory(handler));

    private static ModelCatalog Catalog(StubHttpHandler handler) =>
        new(Clients(handler), new MessageErrorDescriber(), NullLogger<ModelCatalog>.Instance);

    private static Task<string> StreamTextAsync(StubHttpHandler handler, Provider provider, string modelId) =>
        StreamTextAsync(handler, provider, new ProviderModel { ModelId = modelId });

    private static async Task<string> StreamTextAsync(StubHttpHandler handler, Provider provider, ProviderModel model)
    {
        using var client = new ChatClientFactory(Clients(handler)).Create(provider, model);
        var text = new StringBuilder();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Hi")]))
        {
            text.Append(update.Text);
        }

        return text.ToString();
    }

    private static string OpenAIStream(params string[] chunks)
    {
        var events = new StringBuilder();
        foreach (var chunk in chunks)
        {
            events.Append($$"""data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"m","choices":[{"index":0,"delta":{"role":"assistant","content":"{{chunk}}"},"finish_reason":null}]}""").Append("\n\n");
        }

        events.Append("""data: {"id":"c1","object":"chat.completion.chunk","created":1,"model":"m","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}""").Append("\n\n");
        events.Append("data: [DONE]\n\n");
        return events.ToString();
    }

    private static string AnthropicStream(params string[] chunks)
    {
        var events = new StringBuilder();
        void Add(string type, string json) => events.Append($"event: {type}\ndata: {json}\n\n");

        Add("message_start", """{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":1}}}""");
        Add("content_block_start", """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""");
        foreach (var chunk in chunks)
        {
            Add("content_block_delta", $$$"""{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"{{{chunk}}}"}}""");
        }

        Add("content_block_stop", """{"type":"content_block_stop","index":0}""");
        Add("message_delta", """{"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":4}}""");
        Add("message_stop", """{"type":"message_stop"}""");
        return events.ToString();
    }
}
