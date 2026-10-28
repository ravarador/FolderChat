using FolderChat.Application.Api;

namespace FolderChat.Web.Infrastructure;

/// <summary>Settings under <c>FolderChat:Api</c> in appsettings.json, or <c>FolderChat__Api__*</c> environment variables.</summary>
public sealed class ApiOptions
{
    public const string SectionName = "FolderChat:Api";

    /// <summary>
    /// Extra addresses, separated by semicolons, that serve only the REST API at <c>/api/v1</c>, such as
    /// <c>http://0.0.0.0:5081</c> for apps on other computers. Empty by default: the API is then served only with the
    /// rest of FolderChat, on this computer.
    /// </summary>
    public string? Urls { get; set; }

    /// <summary>What a chat request uses for the values it leaves out.</summary>
    public ApiDefaults Defaults { get; set; } = new();

    /// <summary>The ports of <see cref="Urls"/>.</summary>
    public IReadOnlySet<int> GetPorts() =>
        SplitUrls(Urls)
            .Select(url => Uri.TryCreate(url.Replace("://*", "://localhost").Replace("://+", "://localhost"), UriKind.Absolute, out var uri)
                ? uri.Port
                : throw new InvalidOperationException($"FolderChat:Api:Urls has an address that isn't a URL: \"{url}\"."))
            .ToHashSet();

    public static string[] SplitUrls(string? urls) =>
        (urls ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
