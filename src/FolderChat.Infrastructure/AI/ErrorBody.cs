using System.Text.Json;
using System.Text.RegularExpressions;

namespace FolderChat.Infrastructure.AI;

/// <summary>Pulls the human-readable message out of a provider's error response.</summary>
internal static partial class ErrorBody
{
    private const int MaxLength = 300;

    /// <summary>
    /// Understands the common shapes: <c>{"error":"..."}</c> (Ollama), <c>{"error":{"message":"..."}}</c>
    /// (OpenAI-style), and <c>{"message":"..."}</c>. Returns null when there is nothing useful.
    /// </summary>
    public static string? Extract(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var trimmed = body.Trim();
        if (trimmed.StartsWith('<'))
        {
            return null; // an HTML error page says nothing useful in one line
        }

        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var json = JsonDocument.Parse(trimmed);
                return Shorten(FindMessage(json.RootElement));
            }
            catch (JsonException)
            {
                // Not JSON after all; fall back to the raw text.
            }
        }

        return Shorten(trimmed.Split('\n', 2)[0]);
    }

    private static string? FindMessage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (root.TryGetProperty("error", out var error))
        {
            if (error.ValueKind == JsonValueKind.String)
            {
                return error.GetString();
            }

            if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var nested) && nested.ValueKind == JsonValueKind.String)
            {
                return nested.GetString();
            }
        }

        foreach (var name in new[] { "message", "detail" })
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static string? Shorten(string? text)
    {
        // A trailing "(request_id: …)" means nothing to the user.
        var line = text is null ? null : RequestIdSuffix().Replace(text, "").Trim();
        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        return line.Length <= MaxLength ? line : line[..MaxLength] + "…";
    }

    [GeneratedRegex(@"\s*\(request[_ ]?id:?[^)]*\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RequestIdSuffix();
}

/// <summary>
/// OllamaSharp discards the body of a failed response, which is where Ollama explains the problem
/// ("model requires more system memory…"). This handler keeps that explanation in the exception.
/// </summary>
internal sealed class ErrorBodyHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        string? detail;
        try
        {
            detail = ErrorBody.Extract(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            detail = null;
        }

        if (detail is null)
        {
            return response;
        }

        var status = response.StatusCode;
        response.Dispose();
        throw new HttpRequestException(detail, inner: null, status);
    }
}
