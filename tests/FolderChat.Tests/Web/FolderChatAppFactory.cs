using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FolderChat.Application.Abstractions;
using FolderChat.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FolderChat.Tests.Web;

/// <summary>Runs the real app on a throwaway data folder, with fake model providers.</summary>
public sealed partial class FolderChatAppFactory : WebApplicationFactory<Program>
{
    private readonly string dataDirectory = Path.Combine(Path.GetTempPath(), "folderchat-tests", Guid.NewGuid().ToString("N"));

    internal FakeChatClientFactory ChatClients { get; } = new();

    internal FakeModelCatalog ModelCatalog { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("FolderChat:DataDirectory", dataDirectory);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IChatClientFactory>();
            services.AddSingleton<IChatClientFactory>(ChatClients);
            services.RemoveAll<IModelCatalog>();
            services.AddSingleton<IModelCatalog>(ModelCatalog);
        });
    }

    /// <summary>A client that has loaded the page once, so it holds the anti-forgery cookie and token.</summary>
    public async Task<HttpClient> CreateSignedInClientAsync()
    {
        var client = CreateClient();
        var html = await client.GetStringAsync("/");
        var token = CsrfMeta().Match(html).Groups["token"].Value;
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
        return client;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A log file may still be closing; the temp folder is cleaned up by the OS eventually.
        }
    }

    [GeneratedRegex("""<meta name="csrf-token" content="(?<token>[^"]+)" />""")]
    private static partial Regex CsrfMeta();
}

internal static class HttpExtensions
{
    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Parses a Server-Sent Events body into (event name, JSON data) pairs.</summary>
    public static List<(string Event, JsonElement Data)> ParseServerSentEvents(string body) =>
        body.Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(block =>
            {
                var lines = block.Split('\n');
                var name = lines.First(l => l.StartsWith("event:")).Substring(6).Trim();
                var data = lines.First(l => l.StartsWith("data:")).Substring(5).Trim();
                return (name, JsonDocument.Parse(data).RootElement.Clone());
            })
            .ToList();
}
