using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FolderChat.Application.Api;
using FolderChat.Infrastructure.Persistence;
using FolderChat.Tests.Fakes;
using FolderChat.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace FolderChat.Tests.Web;

/// <summary>The REST API at /api/v1 that other apps call with an API key.</summary>
public sealed class ApiV1Tests : IAsyncLifetime
{
    private readonly FolderChatAppFactory app = new();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => app.DisposeAsync();

    [Fact]
    public async Task Calls_without_a_key_are_refused_with_401()
    {
        var client = app.CreateClient();

        var response = await client.GetAsync("/api/v1/models");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("Send an API key", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Calls_with_a_wrong_key_are_refused_with_401()
    {
        await app.CreateApiClientAsync(); // a key exists, but not this one
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "fck_not-a-real-key");

        var response = await client.PostAsJsonAsync("/api/v1/chat", new { message = "Hi", model = "model-a" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Keys_are_shown_once_and_stop_working_when_revoked()
    {
        var settings = await app.CreateSignedInClientAsync();
        var created = await (await settings.PostAsJsonAsync("/api/api-keys", new { name = "Admin app" })).ReadJsonAsync();
        var secret = created.GetProperty("secret").GetString()!;
        var id = created.GetProperty("key").GetProperty("id").GetString();
        Assert.StartsWith("fck_", secret);
        Assert.StartsWith(created.GetProperty("key").GetProperty("prefix").GetString()!, secret);

        var client = app.CreateClient(new() { HandleCookies = false });
        client.DefaultRequestHeaders.Authorization = new("Bearer", secret);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/folders")).StatusCode);

        var listed = await settings.GetStringAsync("/api/api-keys");
        Assert.DoesNotContain(secret, listed);
        var key = Assert.Single(JsonDocument.Parse(listed).RootElement.EnumerateArray());
        Assert.Equal("Admin app", key.GetProperty("name").GetString());
        Assert.NotEqual(JsonValueKind.Null, key.GetProperty("lastUsedAt").ValueKind);

        Assert.Equal(HttpStatusCode.NoContent, (await settings.DeleteAsync($"/api/api-keys/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/folders")).StatusCode);
    }

    [Fact]
    public async Task Only_a_hash_of_each_key_is_stored()
    {
        var settings = await app.CreateSignedInClientAsync();
        var created = await (await settings.PostAsJsonAsync("/api/api-keys", new { name = "Admin app" })).ReadJsonAsync();
        var secret = created.GetProperty("secret").GetString()!;

        await using var scope = app.Services.CreateAsyncScope();
        var stored = Assert.Single(await scope.ServiceProvider.GetRequiredService<FolderChatDbContext>().ApiKeys.ToListAsync());

        Assert.Equal(ApiKeyService.Hash(secret), stored.Hash);
        Assert.Equal(secret[..12], stored.Prefix);
    }

    [Fact]
    public async Task Keys_can_only_be_made_from_the_app_itself()
    {
        // Another website can't make itself a key: creating one needs the page's anti-forgery token.
        var response = await app.CreateClient().PostAsJsonAsync("/api/api-keys", new { name = "Sneaky" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_key_opens_the_api_and_nothing_else()
    {
        var client = await app.CreateApiClientAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/providers", new { name = "x", kind = "Ollama" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/api-keys", new { name = "Another" })).StatusCode);
    }

    [Fact]
    public async Task Models_are_listed_by_provider()
    {
        await CreateProviderAsync("LM Studio");
        var client = await app.CreateApiClientAsync();

        var providers = await (await client.GetAsync("/api/v1/models")).ReadJsonAsync();

        var provider = Assert.Single(providers.EnumerateArray());
        Assert.Equal("LM Studio", provider.GetProperty("name").GetString());
        Assert.Equal(["model-a", "model-b"], provider.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("id").GetString()));
        Assert.Equal("Model A", provider.GetProperty("models")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Chat_returns_the_whole_reply_as_json_and_shows_in_the_chat_list()
    {
        var fake = new FakeChatClient
        {
            Chunks = ["Open Users, ", "then Reset password."],
            Usage = new UsageDetails { InputTokenCount = 12, OutputTokenCount = 5 },
        };
        app.ChatClients.Build = (_, _) => fake;
        await CreateProviderAsync("LM Studio");
        var client = await app.CreateApiClientAsync();

        var reply = await (await client.PostAsJsonAsync("/api/v1/chat", new
        {
            message = "How do I reset a password?",
            model = "Model A",
            instructions = "Answer only from the manuals.",
            user = "admin-42",
        })).ReadJsonAsync();

        Assert.Equal("Open Users, then Reset password.", reply.GetProperty("text").GetString());
        Assert.Equal("model-a", reply.GetProperty("model").GetProperty("id").GetString());
        Assert.Equal("LM Studio", reply.GetProperty("model").GetProperty("provider").GetString());
        Assert.Equal(12, reply.GetProperty("usage").GetProperty("inputTokens").GetInt64());
        Assert.Equal("stop", reply.GetProperty("finishReason").GetString());
        Assert.Equal(0, reply.GetProperty("sources").GetArrayLength());

        // The caller's instructions go to the model as a system message.
        Assert.Equal(ChatRole.System, fake.ReceivedMessages[0].Role);
        Assert.Equal("Answer only from the manuals.", fake.ReceivedMessages[0].Text);

        // The owner sees it in the chat list, marked as an API chat.
        var settings = await app.CreateSignedInClientAsync();
        var listed = Assert.Single((await (await settings.GetAsync("/api/conversations")).ReadJsonAsync()).EnumerateArray());
        Assert.Equal("Api", listed.GetProperty("source").GetString());

        var conversationId = reply.GetProperty("conversationId").GetString();
        var mine = await (await client.GetAsync("/api/v1/conversations?user=admin-42")).ReadJsonAsync();
        Assert.Equal([conversationId], mine.EnumerateArray().Select(c => c.GetProperty("id").GetString()));
        Assert.Empty((await (await client.GetAsync("/api/v1/conversations?user=someone-else")).ReadJsonAsync()).EnumerateArray());

        var conversation = await (await client.GetAsync($"/api/v1/conversations/{conversationId}")).ReadJsonAsync();
        Assert.Equal("admin-42", conversation.GetProperty("user").GetString());
        Assert.Equal(["User", "Assistant"], conversation.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("role").GetString()));
    }

    [Fact]
    public async Task Chat_streams_server_sent_events_when_asked()
    {
        app.ChatClients.Build = (_, _) => new FakeChatClient { Chunks = ["Hi ", "there"] };
        await CreateProviderAsync("LM Studio");
        var client = await app.CreateApiClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/chat", new { message = "Hello", model = "model-a", stream = true });

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        var events = HttpExtensions.ParseServerSentEvents(await response.Content.ReadAsStringAsync());
        Assert.Equal(["started", "delta", "delta", "completed"], events.Select(e => e.Event));
        Assert.Equal("""{"text":"Hi "}""", events[1].Data.GetRawText());
        Assert.Equal(events[0].Data.GetProperty("conversationId").GetString(), events[^1].Data.GetProperty("conversationId").GetString());
        Assert.Equal("model-a", events[^1].Data.GetProperty("model").GetProperty("id").GetString());
    }

    [Fact]
    public async Task A_continued_chat_keeps_its_model_and_history()
    {
        FakeChatClient? second = null;
        var calls = 0;
        app.ChatClients.Build = (_, _) => ++calls == 1 ? new FakeChatClient { Chunks = ["First answer"] } : second = new FakeChatClient { Chunks = ["Second answer"] };
        await CreateProviderAsync("LM Studio");
        var client = await app.CreateApiClientAsync();
        var first = await (await client.PostAsJsonAsync("/api/v1/chat", new { message = "First question", model = "model-b" })).ReadJsonAsync();

        var reply = await (await client.PostAsJsonAsync("/api/v1/chat", new
        {
            message = "Second question",
            conversationId = first.GetProperty("conversationId").GetString(),
        })).ReadJsonAsync();

        Assert.Equal("model-b", reply.GetProperty("model").GetProperty("id").GetString());
        Assert.Equal(first.GetProperty("conversationId").GetString(), reply.GetProperty("conversationId").GetString());
        Assert.Equal(["First question", "First answer", "Second question"], second!.ReceivedMessages.Select(m => m.Text));
    }

    [Fact]
    public async Task Defaults_from_settings_fill_in_what_a_request_leaves_out()
    {
        using var docs = new TempFolder();
        docs.Write("manual.md", "To reset a password, open Users and click Reset password.");
        app.Settings["FolderChat:Api:Defaults:Model"] = "model-b";
        app.Settings["FolderChat:Api:Defaults:Folders:0"] = "Admin manuals";
        app.Settings["FolderChat:Api:Defaults:Effort"] = "Quick";
        app.Settings["FolderChat:Api:Defaults:Instructions"] = "Be brief.";
        var fake = new FakeChatClient { Chunks = ["Click Reset password [1]."] };
        app.ChatClients.Build = (_, _) => fake;
        await CreateProviderAsync("LM Studio");
        await AddFolderAsync(docs.Path, "Admin manuals");
        var client = await app.CreateApiClientAsync();

        var reply = await (await client.PostAsJsonAsync("/api/v1/chat", new { message = "How do I reset a password?" })).ReadJsonAsync();

        Assert.Equal("model-b", reply.GetProperty("model").GetProperty("id").GetString());
        var source = Assert.Single(reply.GetProperty("sources").EnumerateArray());
        Assert.Equal("Admin manuals", source.GetProperty("folder").GetString());
        Assert.Equal("manual.md", source.GetProperty("file").GetString());
        Assert.Contains("open Users and click Reset password", source.GetProperty("text").GetString());
        Assert.EndsWith("Be brief.", fake.ReceivedMessages[0].Text);
        Assert.DoesNotContain(docs.Path, reply.GetRawText());
    }

    [Fact]
    public async Task Folders_are_listed_and_can_be_named_in_a_request()
    {
        using var docs = new TempFolder();
        docs.Write("guide.txt", "Backups run every night at 2 am.");
        app.ChatClients.Build = (_, _) => new FakeChatClient { Chunks = ["At 2 am [1]."] };
        await CreateProviderAsync("LM Studio");
        var folderId = await AddFolderAsync(docs.Path, "Ops guide");
        var client = await app.CreateApiClientAsync();

        var folder = Assert.Single((await (await client.GetAsync("/api/v1/folders")).ReadJsonAsync()).EnumerateArray());
        Assert.Equal((folderId, "Ops guide", 1), (folder.GetProperty("id").GetString(), folder.GetProperty("name").GetString(), folder.GetProperty("indexedFiles").GetInt32()));
        Assert.False(folder.TryGetProperty("path", out _));

        var reply = await (await client.PostAsJsonAsync("/api/v1/chat", new
        {
            message = "When do backups run?",
            model = "model-a",
            folders = new[] { "ops GUIDE" },
            effort = "Quick",
        })).ReadJsonAsync();

        Assert.Equal("guide.txt", reply.GetProperty("sources")[0].GetProperty("file").GetString());
    }

    [Fact]
    public async Task Unknown_names_get_a_readable_problem()
    {
        await CreateProviderAsync("LM Studio");
        var client = await app.CreateApiClientAsync();

        var noModel = await client.PostAsJsonAsync("/api/v1/chat", new { message = "Hi" });
        var unknownModel = await client.PostAsJsonAsync("/api/v1/chat", new { message = "Hi", model = "gpt-9" });
        var unknownFolder = await client.PostAsJsonAsync("/api/v1/chat", new { message = "Hi", model = "model-a", folders = new[] { "Nope" } });
        var empty = await client.PostAsJsonAsync("/api/v1/chat", new { message = " ", model = "model-a" });

        Assert.Equal(HttpStatusCode.BadRequest, noModel.StatusCode);
        Assert.StartsWith("Name a model", await DetailAsync(noModel));
        Assert.Equal(HttpStatusCode.NotFound, unknownModel.StatusCode);
        Assert.Equal("No provider has a model called \"gpt-9\". GET /api/v1/models lists them.", await DetailAsync(unknownModel));
        Assert.Equal(HttpStatusCode.NotFound, unknownFolder.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task A_model_two_providers_have_needs_the_provider_named()
    {
        app.ChatClients.Build = (provider, _) => new FakeChatClient { Chunks = [$"From {provider.Name}"] };
        await CreateProviderAsync("LM Studio");
        await CreateProviderAsync("Work server");
        var client = await app.CreateApiClientAsync();

        var ambiguous = await client.PostAsJsonAsync("/api/v1/chat", new { message = "Hi", model = "model-a" });
        var named = await (await client.PostAsJsonAsync("/api/v1/chat", new { message = "Hi", model = "model-a", provider = "work server" })).ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, ambiguous.StatusCode);
        Assert.Contains("LM Studio, Work server", await DetailAsync(ambiguous));
        Assert.Equal("From Work server", named.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Chats_started_in_the_app_stay_private()
    {
        app.ChatClients.Build = (_, _) => new FakeChatClient { Chunks = ["Private"] };
        var providerId = await CreateProviderAsync("LM Studio");
        var settings = await app.CreateSignedInClientAsync();
        var stream = await settings.PostAsJsonAsync("/api/chat/stream", new { providerId, modelId = "model-a", message = "My own question" });
        var conversationId = HttpExtensions.ParseServerSentEvents(await stream.Content.ReadAsStringAsync())[0].Data.GetProperty("conversationId").GetString();
        var client = await app.CreateApiClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/conversations/{conversationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/conversations/{conversationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/v1/chat", new { message = "Hi", conversationId })).StatusCode);
        Assert.Empty((await (await client.GetAsync("/api/v1/conversations")).ReadJsonAsync()).EnumerateArray());
    }

    [Fact]
    public async Task Provider_failures_come_back_as_502_with_the_conversation()
    {
        app.ChatClients.Build = (_, _) => new FakeChatClient { Chunks = [], FailAfterChunks = new InvalidOperationException("The API key was rejected (401).") };
        await CreateProviderAsync("LM Studio");
        var client = await app.CreateApiClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/chat", new { message = "Hi", model = "model-a" });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("detail").GetString()));
        var conversationId = problem.GetProperty("conversationId").GetString();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/conversations/{conversationId}")).StatusCode);
    }

    [Fact]
    public async Task Deleting_an_api_chat_removes_it()
    {
        app.ChatClients.Build = (_, _) => new FakeChatClient();
        await CreateProviderAsync("LM Studio");
        var client = await app.CreateApiClientAsync();
        var reply = await (await client.PostAsJsonAsync("/api/v1/chat", new { message = "Hi", model = "model-a" })).ReadJsonAsync();
        var conversationId = reply.GetProperty("conversationId").GetString();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/conversations/{conversationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/conversations/{conversationId}")).StatusCode);
    }

    [Fact]
    public async Task The_openapi_description_covers_only_the_v1_api()
    {
        var document = await (await app.CreateClient().GetAsync("/api/v1/openapi.json")).ReadJsonAsync();

        var paths = document.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("/api/v1/chat", paths);
        Assert.Contains("/api/v1/conversations/{id}", paths);
        Assert.All(paths, path => Assert.StartsWith("/api/v1/", path));
        Assert.Equal("FolderChat API", document.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("bearer", document.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey").GetProperty("scheme").GetString());
        Assert.Contains("Asks a question", document.GetProperty("paths").GetProperty("/api/v1/chat").GetProperty("post").GetProperty("summary").GetString());
    }

    [Theory]
    [InlineData(5081, "/api/v1/models", true)]
    [InlineData(5081, "/api/v1/openapi.json", true)]
    [InlineData(5081, "/", false)]
    [InlineData(5081, "/api/providers", false)]
    [InlineData(5081, "/Home/Settings", false)]
    [InlineData(5080, "/api/providers", true)]
    public async Task Api_only_addresses_serve_nothing_but_the_api(int port, string path, bool served)
    {
        var reached = false;
        var middleware = new ApiOnlyAddresses(_ => { reached = true; return Task.CompletedTask; }, new HashSet<int> { 5081 });
        var context = new DefaultHttpContext();
        context.Connection.LocalPort = port;
        context.Request.Path = path;

        await middleware.InvokeAsync(context);

        Assert.Equal(served, reached);
        Assert.Equal(served ? StatusCodes.Status200OK : StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public void Api_ports_are_read_from_the_urls()
    {
        var options = new ApiOptions { Urls = "http://0.0.0.0:5081; http://*:5082;https://+:5443" };

        Assert.Equal([5081, 5082, 5443], options.GetPorts().Order());
        Assert.Throws<InvalidOperationException>(() => new ApiOptions { Urls = "not a url" }.GetPorts());
    }

    private static async Task<string?> DetailAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString();

    private async Task<string> CreateProviderAsync(string name)
    {
        var settings = await app.CreateSignedInClientAsync();
        var created = await (await settings.PostAsJsonAsync("/api/providers", new
        {
            name,
            kind = "OpenAICompatible",
            baseUrl = "http://localhost:1234/v1",
        })).ReadJsonAsync();
        var id = created.GetProperty("id").GetString()!;
        (await settings.PostAsync($"/api/providers/{id}/models/refresh", null)).EnsureSuccessStatusCode();
        return id;
    }

    private async Task<string> AddFolderAsync(string path, string name)
    {
        var settings = await app.CreateSignedInClientAsync();
        var added = await (await settings.PostAsJsonAsync("/api/folders", new { path, name })).ReadJsonAsync();
        var id = added.GetProperty("id").GetString()!;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var folder = await (await settings.GetAsync($"/api/folders/{id}")).ReadJsonAsync();
            if (folder.GetProperty("scan").ValueKind == JsonValueKind.Null && folder.GetProperty("lastScannedAt").ValueKind != JsonValueKind.Null)
            {
                return id;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The folder wasn't scanned within 10 seconds.");
    }
}
