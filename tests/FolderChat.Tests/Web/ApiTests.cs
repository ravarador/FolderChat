using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FolderChat.Tests.Fakes;
using Microsoft.Extensions.AI;

namespace FolderChat.Tests.Web;

public sealed class ApiTests : IAsyncLifetime
{
    private readonly FolderChatAppFactory app = new();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => app.DisposeAsync();

    [Fact]
    public async Task The_chat_page_loads_with_an_anti_forgery_token()
    {
        var html = await app.CreateClient().GetStringAsync("/");

        Assert.Contains("name=\"csrf-token\"", html);
        Assert.Contains("id=\"model-picker\"", html);
    }

    [Fact]
    public async Task Requests_for_other_host_names_are_refused()
    {
        // Stops a malicious website from reaching this app through DNS rebinding.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/providers");
        request.Headers.Host = "evil.example";

        var response = await app.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Changes_without_the_anti_forgery_token_are_refused()
    {
        var response = await app.CreateClient().PostAsJsonAsync("/api/providers", NewProvider());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Providers_are_saved_without_ever_returning_the_key()
    {
        var client = await app.CreateSignedInClientAsync();

        var createResponse = await client.PostAsJsonAsync("/api/providers", NewProvider());
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.ReadJsonAsync();
        var listBody = await client.GetStringAsync("/api/providers");

        Assert.True(created.GetProperty("hasApiKey").GetBoolean());
        Assert.Equal("OpenAICompatible", created.GetProperty("kind").GetString());
        Assert.DoesNotContain("super-secret-key", created.GetRawText());
        Assert.DoesNotContain("super-secret-key", listBody);
    }

    [Fact]
    public async Task Invalid_providers_get_a_readable_problem()
    {
        var client = await app.CreateSignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/providers", new { name = "No URL", kind = "OpenAICompatible" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("OpenAI-compatible providers need a base URL.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Provider_failures_come_back_as_502_with_the_reason()
    {
        var client = await app.CreateSignedInClientAsync();
        var created = await (await client.PostAsJsonAsync("/api/providers", NewProvider())).ReadJsonAsync();
        app.ModelCatalog.Failure = new FolderChat.Application.Common.ProviderCallFailedException(
            "Couldn't load models from LM Studio: the API key was rejected (401). Check it in Settings.");

        var response = await client.PostAsync($"/api/providers/{created.GetProperty("id").GetString()}/models/refresh", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Couldn't load models from LM Studio: the API key was rejected (401). Check it in Settings.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Loaded_models_appear_in_the_picker()
    {
        var client = await app.CreateSignedInClientAsync();
        var providerId = await CreateProviderWithModelsAsync(client);

        var picker = await (await client.GetAsync("/api/providers/picker")).ReadJsonAsync();

        var provider = Assert.Single(picker.EnumerateArray());
        Assert.Equal(providerId, provider.GetProperty("id").GetString());
        Assert.True(provider.GetProperty("isLocal").GetBoolean());
        Assert.Equal(["Model A", "model-b"], provider.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("label").GetString()));
    }

    [Fact]
    public async Task Chat_streams_the_reply_and_saves_the_conversation()
    {
        app.ChatClients.Build = (_, _) => new FakeChatClient
        {
            Chunks = ["Hi ", "there"],
            Usage = new UsageDetails { InputTokenCount = 8, OutputTokenCount = 2 },
        };
        var client = await app.CreateSignedInClientAsync();
        var providerId = await CreateProviderWithModelsAsync(client);

        var response = await client.PostAsJsonAsync("/api/chat/stream", new { providerId, modelId = "model-a", message = "Hello there\nhow are you?" });

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        var events = HttpExtensions.ParseServerSentEvents(await response.Content.ReadAsStringAsync());
        Assert.Equal(["started", "delta", "delta", "completed"], events.Select(e => e.Event));
        Assert.Equal("Hello there", events[0].Data.GetProperty("title").GetString());
        Assert.Equal(2, events[^1].Data.GetProperty("outputTokens").GetInt64());

        var conversationId = events[0].Data.GetProperty("conversationId").GetString();
        var conversation = await (await client.GetAsync($"/api/conversations/{conversationId}")).ReadJsonAsync();
        var messages = conversation.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(["User", "Assistant"], messages.Select(m => m.GetProperty("role").GetString()));
        Assert.Equal("Hi there", messages[1].GetProperty("content").GetString());
        Assert.Equal("model-a", conversation.GetProperty("modelId").GetString());
    }

    [Fact]
    public async Task Conversations_can_be_renamed_and_deleted()
    {
        var client = await app.CreateSignedInClientAsync();
        var providerId = await CreateProviderWithModelsAsync(client);
        var stream = await client.PostAsJsonAsync("/api/chat/stream", new { providerId, modelId = "model-a", message = "Plan a trip" });
        var id = HttpExtensions.ParseServerSentEvents(await stream.Content.ReadAsStringAsync())[0].Data.GetProperty("conversationId").GetString();

        var renamed = await (await client.PatchAsJsonAsync($"/api/conversations/{id}", new { title = "Japan trip" })).ReadJsonAsync();
        Assert.Equal("Japan trip", renamed.GetProperty("title").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/conversations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/conversations/{id}")).StatusCode);
        Assert.Empty((await (await client.GetAsync("/api/conversations")).ReadJsonAsync()).EnumerateArray());
    }

    private static object NewProvider() => new
    {
        name = "LM Studio",
        kind = "OpenAICompatible",
        baseUrl = "http://localhost:1234/v1",
        apiKey = "super-secret-key",
    };

    private static async Task<string> CreateProviderWithModelsAsync(HttpClient client)
    {
        var created = await (await client.PostAsJsonAsync("/api/providers", NewProvider())).ReadJsonAsync();
        var id = created.GetProperty("id").GetString()!;
        (await client.PostAsync($"/api/providers/{id}/models/refresh", null)).EnsureSuccessStatusCode();
        return id;
    }
}
