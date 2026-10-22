using FolderChat.Application.Abstractions;
using FolderChat.Application.Common;
using FolderChat.Application.Providers;
using FolderChat.Core.Providers;
using FolderChat.Infrastructure.Persistence;
using FolderChat.Tests.Fakes;
using Microsoft.EntityFrameworkCore;

namespace FolderChat.Tests.Application;

public sealed class ProviderServiceTests : IAsyncLifetime
{
    private readonly FakeModelCatalog catalog = new();
    private TestDatabase database = null!;
    private FolderChatDbContext db = null!;

    public async ValueTask InitializeAsync()
    {
        database = await TestDatabase.CreateAsync();
        db = database.NewContext();
    }

    public async ValueTask DisposeAsync()
    {
        await db.DisposeAsync();
        await database.DisposeAsync();
    }

    [Fact]
    public async Task Api_keys_are_stored_protected_and_never_returned()
    {
        var created = await Service().CreateAsync(new SaveProviderRequest("DeepSeek", ProviderKind.OpenAICompatible, "https://api.deepseek.com/", "sk-secret"));

        Assert.True(created.HasApiKey);
        Assert.Equal("https://api.deepseek.com", created.BaseUrl);
        await using var check = database.NewContext();
        Assert.Equal("protected(sk-secret)", (await check.Providers.SingleAsync()).ProtectedApiKey);
    }

    [Fact]
    public async Task Updating_with_no_key_keeps_it_and_an_empty_key_removes_it()
    {
        var service = Service();
        var created = await service.CreateAsync(new SaveProviderRequest("OpenAI", ProviderKind.OpenAI, null, "sk-1"));

        var kept = await service.UpdateAsync(created.Id, new SaveProviderRequest("OpenAI renamed", ProviderKind.OpenAI, null, null));
        Assert.True(kept.HasApiKey);
        Assert.Equal("OpenAI renamed", kept.Name);

        var removed = await service.UpdateAsync(created.Id, new SaveProviderRequest("OpenAI renamed", ProviderKind.OpenAI, null, ""));
        Assert.False(removed.HasApiKey);
    }

    [Theory]
    [InlineData("", ProviderKind.Ollama, null)]
    [InlineData("No URL", ProviderKind.OpenAICompatible, null)]
    [InlineData("Bad URL", ProviderKind.OpenAICompatible, "ftp://example.com")]
    [InlineData("Relative URL", ProviderKind.OpenAICompatible, "api.example.com/v1")]
    [InlineData("Unknown kind", (ProviderKind)99, null)]
    public async Task Invalid_providers_are_rejected(string name, ProviderKind kind, string? baseUrl) =>
        await Assert.ThrowsAsync<ValidationFailedException>(() => Service().CreateAsync(new SaveProviderRequest(name, kind, baseUrl, null)));

    [Fact]
    public async Task Refreshing_models_keeps_the_users_choices()
    {
        var service = Service();
        var provider = await service.CreateAsync(new SaveProviderRequest("Ollama", ProviderKind.Ollama, null, null));
        catalog.Models = [new("llama3"), new("qwen3"), new("old-model")];
        await service.RefreshModelsAsync(provider.Id);
        await service.SetModelEnabledAsync(provider.Id, "qwen3", enabled: false);
        await service.AddModelAsync(provider.Id, "typed-in");

        catalog.Models = [new("llama3", "Llama 3"), new("qwen3"), new("gemma4")];
        var refreshed = await service.RefreshModelsAsync(provider.Id);

        Assert.Equal(["gemma4", "llama3", "qwen3", "typed-in"], refreshed.Models.Select(m => m.ModelId));
        Assert.False(refreshed.Models.Single(m => m.ModelId == "qwen3").IsEnabled);
        Assert.Equal("Llama 3", refreshed.Models.Single(m => m.ModelId == "llama3").DisplayName);
        Assert.True(refreshed.Models.Single(m => m.ModelId == "typed-in").IsManual);
    }

    [Fact]
    public async Task Long_model_lists_start_hidden_from_the_picker()
    {
        var service = Service();
        var provider = await service.CreateAsync(new SaveProviderRequest("OpenRouter", ProviderKind.OpenAICompatible, "https://openrouter.ai/api/v1", "key"));
        catalog.Models = Enumerable.Range(1, ModelListMerger.AutoEnableLimit + 1).Select(i => new DiscoveredModel($"vendor/model-{i}")).ToList();

        var refreshed = await service.RefreshModelsAsync(provider.Id);

        Assert.All(refreshed.Models, m => Assert.False(m.IsEnabled));
        Assert.Empty(await service.GetPickerAsync());
    }

    [Fact]
    public async Task Failed_model_loads_surface_the_providers_message()
    {
        var service = Service();
        var provider = await service.CreateAsync(new SaveProviderRequest("Anthropic", ProviderKind.Anthropic, null, "bad"));
        catalog.Failure = new ProviderCallFailedException("Couldn't load models from Anthropic: the API key was rejected (401).");

        var error = await Assert.ThrowsAsync<ProviderCallFailedException>(() => service.RefreshModelsAsync(provider.Id));
        Assert.Contains("401", error.Message);
    }

    [Fact]
    public async Task The_picker_only_shows_enabled_providers_and_models()
    {
        var service = Service();
        var local = await service.CreateAsync(new SaveProviderRequest("Ollama", ProviderKind.Ollama, null, null));
        var off = await service.CreateAsync(new SaveProviderRequest("Off", ProviderKind.OpenAICompatible, "https://example.com/v1", null, IsEnabled: false));
        await service.RefreshModelsAsync(local.Id);
        await service.RefreshModelsAsync(off.Id);
        await service.SetModelEnabledAsync(local.Id, "model-b", enabled: false);

        var picker = Assert.Single(await service.GetPickerAsync());

        Assert.Equal("Ollama", picker.Name);
        Assert.True(picker.IsLocal);
        Assert.Equal(["Model A"], picker.Models.Select(m => m.Label));
    }

    [Theory]
    [InlineData(ProviderKind.Ollama, null, true)]
    [InlineData(ProviderKind.OpenAICompatible, "http://localhost:1234/v1", true)]
    [InlineData(ProviderKind.OpenAICompatible, "http://127.0.0.1:8080/v1", true)]
    [InlineData(ProviderKind.OpenAICompatible, "https://api.deepseek.com", false)]
    [InlineData(ProviderKind.Anthropic, null, false)]
    public void Local_providers_are_recognized(ProviderKind kind, string? baseUrl, bool expected) =>
        Assert.Equal(expected, ProviderService.IsLocal(new Provider { Name = "x", Kind = kind, BaseUrl = baseUrl }));

    [Theory]
    [InlineData("gpt-5", true)]
    [InlineData("deepseek-chat", true)]
    [InlineData("text-embedding-3-small", false)]
    [InlineData("whisper-1", false)]
    [InlineData("gpt-image-1", false)]
    [InlineData("tts-1-hd", false)]
    public void Non_chat_models_are_spotted(string modelId, bool expected) =>
        Assert.Equal(expected, ModelListMerger.LooksLikeChatModel(modelId));

    private ProviderService Service() => new(
        new EfProviderStore(db),
        catalog,
        new FakeSecretProtector(),
        TimeProvider.System);
}
