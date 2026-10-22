using System.ClientModel;
using System.Net;
using Anthropic.Exceptions;
using FolderChat.Application.Abstractions;
using FolderChat.Core.Providers;

namespace FolderChat.Infrastructure.AI;

/// <summary>Maps the different SDKs' exceptions to one set of plain messages.</summary>
internal sealed class ProviderErrorDescriber : IProviderErrorDescriber
{
    public string Describe(Exception exception, ProviderKind kind)
    {
        // Ollama-type providers never send a key, so a demand for one means the base URL points at a
        // cloud API that was saved with the wrong type.
        if (kind == ProviderKind.Ollama && StatusOf(exception) is 401 or 403)
        {
            return "the server asked for an API key, but Ollama-type providers don't send one. "
                + "If this is a cloud API such as DeepSeek, edit the provider and change its type to OpenAI-compatible.";
        }

        return exception switch
        {
            AnthropicUnauthorizedException => ForStatus(401),
            AnthropicForbiddenException => ForStatus(403),
            AnthropicNotFoundException => ForStatus(404),
            AnthropicRateLimitException => ForStatus(429),
            Anthropic5xxException => "the provider had a server error. Try again in a moment.",
            AnthropicIOException => "couldn't reach the provider. Check your internet connection.",
            AnthropicException anthropic => FirstLine(anthropic.Message),

            ClientResultException result => result.Status > 0 ? ForStatus(result.Status, ResponseDetail(result)) : FirstLine(result.Message),

            HttpRequestException { StatusCode: { } status } http => ForStatus((int)status, http.Message),
            HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError } =>
                "couldn't connect. Check the base URL, and that the server is running.",
            HttpRequestException http => "couldn't connect: " + FirstLine(http.Message),

            TimeoutException or TaskCanceledException => "the provider took too long to respond.",
            _ => FirstLine(exception.Message),
        };
    }

    private static int? StatusOf(Exception exception) => exception switch
    {
        AnthropicUnauthorizedException => 401,
        AnthropicForbiddenException => 403,
        ClientResultException { Status: > 0 } result => result.Status,
        HttpRequestException { StatusCode: { } status } => (int)status,
        _ => null,
    };

    /// <summary>
    /// Keeps the provider's own wording, which often shows the last characters of the key it received
    /// (DeepSeek: "Your api key: ****abcd is invalid"), so the user can tell whether the right key was sent.
    /// </summary>
    private static string ForStatus(int status, string? detail = null)
    {
        var info = Informative(detail);
        return status switch
        {
            400 => WithDetail("the provider rejected the request (400)", info),
            401 => WithDetail("the API key was rejected (401)", info, "Check it in Settings."),
            402 => WithDetail("the account needs credit or a payment method (402)", info),
            403 => WithDetail("this key isn't allowed to use that model or endpoint (403)", info),
            404 => WithDetail("not found (404)", info, "Check the base URL and the model ID."),
            429 => WithDetail("rate limited or out of credit (429)", info, "Wait a moment, or check your plan."),
            >= 500 when info is not null => $"the provider reported an error ({status}): {info}",
            >= 500 => $"the provider had a server error ({status}). Try again in a moment.",
            _ => WithDetail($"HTTP {status}", info),
        };
    }

    private static string WithDetail(string summary, string? info, string? advice = null)
    {
        var text = info is null ? summary + "." : $"{summary}: {info}";
        if (advice is null)
        {
            return text;
        }

        return (text.EndsWith('.') || text.EndsWith('!') || text.EndsWith('?') ? text : text + ".") + " " + advice;
    }

    /// <summary>The error message from the response body of a failed OpenAI-SDK call, when it was read.</summary>
    private static string? ResponseDetail(ClientResultException result)
    {
        try
        {
            return ErrorBody.Extract(result.GetRawResponse()?.Content?.ToString()) ?? result.Message;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return result.Message; // streamed responses can't be re-read
        }
    }

    /// <summary>Null when the text adds nothing to the status code.</summary>
    private static string? Informative(string? detail)
    {
        var line = FirstLine(detail);
        return line.Length == 0
            || line.StartsWith("Response status code does not indicate success", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Service request failed", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("HTTP ", StringComparison.OrdinalIgnoreCase)
            ? null
            : line;
    }

    private static string FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var line = text.Trim().Split('\n', 2)[0].Trim();
        return line.Length <= 300 ? line : line[..300] + "…";
    }
}
