using System.Runtime.CompilerServices;
using FolderChat.Application.Abstractions;
using FolderChat.Core.Providers;
using Microsoft.Extensions.AI;

namespace FolderChat.Tests.Fakes;

/// <summary>Replays scripted reply chunks and records the messages it was sent.</summary>
internal sealed class FakeChatClient : IChatClient
{
    public IReadOnlyList<string> Chunks { get; init; } = ["Hello", " world"];

    public UsageDetails? Usage { get; init; }

    public ChatFinishReason? FinishReason { get; init; } = ChatFinishReason.Stop;

    /// <summary>Thrown after the chunks are sent, to simulate a provider failing mid-reply.</summary>
    public Exception? FailAfterChunks { get; init; }

    /// <summary>After the chunks, waits until the caller cancels, like a slow model mid-reply.</summary>
    public bool HangAfterChunks { get; init; }

    public List<ChatMessage> ReceivedMessages { get; } = [];

    public bool Disposed { get; private set; }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("FolderChat only streams.");

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ReceivedMessages.AddRange(messages);

        foreach (var chunk in Chunks)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
        }

        if (FailAfterChunks is not null)
        {
            throw FailAfterChunks;
        }

        if (HangAfterChunks)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        var last = new ChatResponseUpdate { Role = ChatRole.Assistant, FinishReason = FinishReason };
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
