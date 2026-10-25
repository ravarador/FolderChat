using System.Runtime.CompilerServices;
using FolderChat.Application.Abstractions;
using FolderChat.Core.Providers;
using Microsoft.Extensions.AI;

namespace FolderChat.Tests.Fakes;

/// <summary>Replays scripted replies and records the requests it was sent.</summary>
internal sealed class FakeChatClient : IChatClient
{
    public IReadOnlyList<string> Chunks { get; init; } = ["Hello", " world"];

    /// <summary>
    /// One scripted reply per request, for conversations with tool calls: each item is either a string of text or
    /// a <see cref="FunctionCallContent"/>. Requests after the last script repeat it. Overrides <see cref="Chunks"/>.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<object>>? Rounds { get; init; }

    public UsageDetails? Usage { get; init; }

    public ChatFinishReason? FinishReason { get; init; } = ChatFinishReason.Stop;

    /// <summary>Thrown after the chunks are sent, to simulate a provider failing mid-reply.</summary>
    public Exception? FailAfterChunks { get; init; }

    /// <summary>After the chunks, waits until the caller cancels, like a slow model mid-reply.</summary>
    public bool HangAfterChunks { get; init; }

    /// <summary>Thrown by the first request before anything is sent, like a model refusing tools. Later requests work.</summary>
    public Exception? FailFirstRequest { get; init; }

    /// <summary>Each request, with copies of its messages and options as they were when it was sent.</summary>
    public List<(List<ChatMessage> Messages, ChatOptions? Options)> Requests { get; } = [];

    /// <summary>The messages of the first request.</summary>
    public List<ChatMessage> ReceivedMessages => Requests.Count > 0 ? Requests[0].Messages : [];

    public bool Disposed { get; private set; }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("FolderChat only streams.");

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Requests.Add((messages.ToList(), options?.Clone()));
        if (FailFirstRequest is not null && Requests.Count == 1)
        {
            throw FailFirstRequest;
        }

        var script = Rounds is null ? Chunks.Cast<object>().ToList() : Rounds[Math.Min(Requests.Count, Rounds.Count) - 1];

        foreach (var item in script)
        {
            await Task.Yield();
            yield return item is AIContent content
                ? new ChatResponseUpdate(ChatRole.Assistant, [content])
                : new ChatResponseUpdate(ChatRole.Assistant, (string)item);
        }

        if (FailAfterChunks is not null)
        {
            throw FailAfterChunks;
        }

        if (HangAfterChunks)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        var callsTools = script.Any(item => item is FunctionCallContent);
        var last = new ChatResponseUpdate { Role = ChatRole.Assistant, FinishReason = callsTools ? ChatFinishReason.ToolCalls : FinishReason };
        if (Usage is not null)
        {
            last.Contents.Add(new UsageContent(Usage));
        }

        yield return last;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() => Disposed = true;
}

internal sealed class FakeChatClientFactory : IChatClientFactory
{
    /// <summary>Builds the client for each request. Replace it to script a test's reply.</summary>
    public Func<Provider, ProviderModel, IChatClient> Build { get; set; } = (_, _) => new FakeChatClient();

    public List<(Provider Provider, ProviderModel Model)> Calls { get; } = [];

    public IChatClient Create(Provider provider, ProviderModel model)
    {
        Calls.Add((provider, model));
        return Build(provider, model);
    }
}
