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

    [Fact]
    public async Task Files_can_be_attached_sent_and_read_back()
    {
        var recorder = new FakeChatClient { Chunks = ["Noted."] };
        app.ChatClients.Build = (_, _) => recorder;
        var client = await app.CreateSignedInClientAsync();
        var providerId = await CreateProviderWithModelsAsync(client);

        var upload = await UploadAsync(client, "todo.txt", "Buy milk.\r\nCall Sam."u8.ToArray());
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var attachment = await upload.ReadJsonAsync();
        var attachmentId = attachment.GetProperty("id").GetString();
        Assert.Equal(("todo.txt", 19), (attachment.GetProperty("fileName").GetString(), attachment.GetProperty("characterCount").GetInt32()));

        var stream = await client.PostAsJsonAsync("/api/chat/stream", new { providerId, modelId = "model-a", message = "", attachmentIds = new[] { attachmentId } });
        var events = HttpExtensions.ParseServerSentEvents(await stream.Content.ReadAsStringAsync());
        Assert.Equal("completed", events[^1].Event);
        Assert.Equal("todo.txt", events[0].Data.GetProperty("title").GetString());
        Assert.Contains("Buy milk.\nCall Sam.", Assert.Single(recorder.ReceivedMessages).Text);

        var conversationId = events[0].Data.GetProperty("conversationId").GetString();
        var conversation = await (await client.GetAsync($"/api/conversations/{conversationId}")).ReadJsonAsync();
        var sentFile = Assert.Single(conversation.GetProperty("messages")[0].GetProperty("attachments").EnumerateArray());
        Assert.Equal("todo.txt", sentFile.GetProperty("fileName").GetString());
        Assert.False(sentFile.TryGetProperty("text", out _));

        var full = await (await client.GetAsync($"/api/attachments/{attachmentId}")).ReadJsonAsync();
        Assert.Equal("Buy milk.\nCall Sam.", full.GetProperty("text").GetString());
        Assert.True(full.GetProperty("isSent").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"/api/attachments/{attachmentId}")).StatusCode);

        // Deleting the conversation deletes its files.
        await client.DeleteAsync($"/api/conversations/{conversationId}");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/attachments/{attachmentId}")).StatusCode);
    }

    [Fact]
    public async Task Unsent_files_can_be_removed()
    {
        var client = await app.CreateSignedInClientAsync();
        var attachment = await (await UploadAsync(client, "draft.md", "# Draft"u8.ToArray())).ReadJsonAsync();
        var url = $"/api/attachments/{attachment.GetProperty("id").GetString()}";

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Files_that_cant_be_read_get_a_readable_problem()
    {
        var client = await app.CreateSignedInClientAsync();

        var response = await UploadAsync(client, "photo.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("photo.png is an image.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Uploads_without_the_anti_forgery_token_are_refused()
    {
        var response = await UploadAsync(app.CreateClient(), "notes.txt", "Hi"u8.ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string fileName, byte[] content)
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent(content), "file", fileName } };
        return await client.PostAsync("/api/attachments", form);
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
