using System.Text;
using FolderChat.Application.Abstractions;
using FolderChat.Application.Common;
using FolderChat.Core.Knowledge;
using FolderChat.Core.Providers;
using Microsoft.Extensions.AI;

namespace FolderChat.Application.Knowledge;

/// <summary>Writes the short per-file summaries that Thorough answers use to pick files, with the model the folder's settings name.</summary>
public sealed class FileSummarizer(IProviderStore providers, IChatClientFactory chatClients, IProviderErrorDescriber errors)
{
    /// <summary>Connects to the folder's summary model. Returns an error message instead when it can't be used.</summary>
    public async Task<(SummaryWriter? Writer, string? Error)> OpenAsync(KnowledgeFolder folder, CancellationToken ct = default)
    {
        var provider = folder.SummaryProviderId is { } id ? await providers.FindAsync(id, ct) : null;
        if (provider is null || !provider.IsEnabled || string.IsNullOrEmpty(folder.SummaryModelId))
        {
            return (null, "Summaries are on, but their model isn't available anymore. Pick another one in Settings.");
        }

        var model = provider.Models.FirstOrDefault(m => m.ModelId == folder.SummaryModelId)
            ?? new ProviderModel { ProviderId = provider.Id, ModelId = folder.SummaryModelId };
        try
        {
            return (new SummaryWriter(chatClients.Create(provider, model), provider, errors), null);
        }
        catch (Exception ex)
        {
            return (null, "Summaries stopped: " + errors.Describe(ex, provider.Kind));
        }
    }
}

/// <summary>Writes summaries with one model. Dispose it when done.</summary>
public sealed class SummaryWriter(IChatClient client, Provider provider, IProviderErrorDescriber errors) : IDisposable
{
    /// <summary>How much of each file the model reads: enough for the gist, and cheap even for very long files.</summary>
    public const int MaxInputLength = 24_000;

    private const int MaxSummaryLength = 2_000;

    private const string Instructions = """
        You summarize documents so someone can tell, without opening one, whether it answers their question.
        Reply with Markdown only: one sentence saying what the document is, then up to five bullet points with its key facts, including important names, dates and numbers.
        Keep it under 120 words, and write in the document's language.
        """;

    /// <exception cref="ProviderCallFailedException">The model couldn't be reached, or refused. The message says why.</exception>
    public async Task<FileSummary> SummarizeAsync(string path, string text, CancellationToken ct = default)
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.System, Instructions),
            new(ChatRole.User, $"<document name=\"{path.Replace('"', '\'')}\">\n{Truncate(text, MaxInputLength)}\n</document>"),
        ];

        var summary = new StringBuilder();
        long? input = null, output = null;
        try
        {
            await foreach (var update in client.GetStreamingResponseAsync(messages, cancellationToken: ct))
            {
                summary.Append(update.Text);

                // Providers report usage at different points, so the largest value seen wins, as in chats.
                foreach (var usage in update.Contents.OfType<UsageContent>())
                {
                    input = Math.Max(input ?? 0, usage.Details.InputTokenCount ?? 0);
                    output = Math.Max(output ?? 0, usage.Details.OutputTokenCount ?? 0);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw new ProviderCallFailedException(errors.Describe(ex, provider.Kind), ex);
        }

        var result = summary.ToString().Trim();
        if (result.Length == 0)
        {
            throw new ProviderCallFailedException("the model returned an empty summary.");
        }

        return new FileSummary(
            result.Length <= MaxSummaryLength ? result : Truncate(result, MaxSummaryLength).TrimEnd() + "…",
            input,
            output);
    }

    public void Dispose() => client.Dispose();

    /// <summary>Cuts text to a length without splitting a character that takes two UTF-16 code units.</summary>
    private static string Truncate(string text, int length)
    {
        if (text.Length <= length)
        {
            return text;
        }

        return text[..(char.IsHighSurrogate(text[length - 1]) ? length - 1 : length)];
    }
}

/// <summary>A file's summary, and the tokens it took when the provider reports them.</summary>
public sealed record FileSummary(string Text, long? InputTokens, long? OutputTokens);
