using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using FolderChat.Application.Abstractions;
using FolderChat.Core.Conversations;
using FolderChat.Core.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace FolderChat.Application.Chat;

/// <summary>Sends a message to the chosen model and streams the reply, saving both to the conversation.</summary>
public sealed class ChatService(
    IProviderStore providers,
    IConversationStore conversations,
    IAttachmentStore attachments,
    IChatClientFactory chatClients,
    IProviderErrorDescriber errors,
    TimeProvider clock,
    ILogger<ChatService> logger)
{
    public const int MaxMessageLength = 100_000;

    /// <summary>The most files one message can carry.</summary>
    public const int MaxAttachments = 10;

    private const int TitleLength = 60;

    /// <summary>
    /// Streams the reply to <paramref name="request"/>. If the caller stops early (the user pressed Stop
    /// or closed the page), whatever arrived so far is saved as a stopped message.
    /// </summary>
    public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var text = request.Message?.Trim() ?? "";
        var attachmentIds = request.AttachmentIds?.Distinct().ToList() ?? [];
        if (text.Length == 0 && attachmentIds.Count == 0)
        {
            yield return new ChatFailed("Type a message first.", null);
            yield break;
        }

        if (text.Length > MaxMessageLength)
        {
            yield return new ChatFailed($"Messages are limited to {MaxMessageLength:N0} characters.", null);
            yield break;
        }

        if (attachmentIds.Count > MaxAttachments)
        {
            yield return new ChatFailed($"Attach up to {MaxAttachments} files per message.", null);
            yield break;
        }

        var provider = await providers.FindAsync(request.ProviderId, ct);
        if (provider is null || !provider.IsEnabled)
        {
            yield return new ChatFailed("That provider isn't available. Pick another model.", null);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(request.ModelId))
        {
            yield return new ChatFailed("Pick a model first.", null);
            yield break;
        }

        var model = provider.Models.FirstOrDefault(m => m.ModelId == request.ModelId)
            ?? new ProviderModel { ProviderId = provider.Id, ModelId = request.ModelId };

        var files = await FindUnsentAsync(attachmentIds, ct);
        if (files is null)
        {
            yield return new ChatFailed("An attached file isn't available anymore. Remove it and attach it again.", null);
            yield break;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var isNew = request.ConversationId is null;
        Conversation conversation;
        if (isNew)
        {
            var title = MakeTitle(text.Length > 0 ? text : files[0].FileName);
            conversation = new Conversation { Title = title, CreatedAt = now, UpdatedAt = now };
            conversations.Add(conversation);
        }
        else
        {
            var existing = await conversations.FindAsync(request.ConversationId!.Value, includeMessages: true, ct);
            if (existing is null)
            {
                yield return new ChatFailed("That conversation doesn't exist anymore.", null);
                yield break;
            }

            conversation = existing;
        }

        var history = BuildHistory(conversation.Messages);
        var nextSequence = conversation.Messages.Count == 0 ? 1 : conversation.Messages.Max(m => m.Sequence) + 1;

        var userMessage = new ConversationMessage
        {
            ConversationId = conversation.Id,
            Sequence = nextSequence,
            Role = MessageRole.User,
            Content = text,
            CreatedAt = now,
        };
        for (var i = 0; i < files.Count; i++)
        {
            files[i].MessageId = userMessage.Id;
            files[i].Position = i + 1;
            userMessage.Attachments.Add(files[i]);
        }

        history.Add(new ChatMessage(ChatRole.User, ComposeUserText(userMessage)));

        // The stores share one unit of work, so this also saves the files' link to the message.
        conversations.AddMessage(userMessage);
        conversation.ProviderId = provider.Id;
        conversation.ModelId = model.ModelId;
        conversation.UpdatedAt = now;
        await conversations.SaveChangesAsync(ct);

        yield return new ChatStarted(conversation.Id, conversation.Title, userMessage.Id, isNew);

        var reply = new StringBuilder();
        var usage = new UsageTally();
        ChatFinishReason? finishReason = null;
        string? failure = null;
        var saved = false;
        var replySequence = nextSequence + 1;

        try
        {
            var (client, createError) = TryCreateClient(provider, model);
            failure = createError;

            if (client is not null)
            {
                using (client)
                {
                    await using var updates = client.GetStreamingResponseAsync(history, cancellationToken: ct).GetAsyncEnumerator(ct);
                    while (true)
                    {
                        bool hasNext;
                        try
                        {
                            hasNext = await updates.MoveNextAsync();
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                        {
                            logger.LogWarning(ex, "Chat with {Provider} model {Model} failed", provider.Name, model.ModelId);
                            failure = errors.Describe(ex, provider.Kind);
                            break;
                        }

                        if (!hasNext)
                        {
                            break;
                        }

                        var update = updates.Current;
                        finishReason = update.FinishReason ?? finishReason;
                        usage.Add(update);

                        var piece = update.Text;
                        if (!string.IsNullOrEmpty(piece))
                        {
                            reply.Append(piece);
                            yield return new ChatDelta(piece);
                        }
                    }
                }
            }

            if (failure is null && reply.Length == 0 && finishReason == ChatFinishReason.ContentFilter)
            {
                failure = "The model declined to answer this request.";
            }

            if (failure is not null)
            {
                var failed = await SaveReplyAsync(conversation, provider, model, replySequence, reply.ToString(), MessageStatus.Failed, failure, usage);
                saved = true;
                yield return new ChatFailed(failure, failed.Id);
            }
            else
            {
                var completed = await SaveReplyAsync(conversation, provider, model, replySequence, reply.ToString(), MessageStatus.Complete, null, usage);
                saved = true;
                yield return new ChatCompleted(completed.Id, usage.Input, usage.Output, finishReason?.Value);
            }
        }
        finally
        {
            // The caller stopped listening before the reply finished. Keep the partial reply.
            if (!saved && reply.Length > 0)
            {
                try
                {
                    await SaveReplyAsync(conversation, provider, model, replySequence, reply.ToString(), MessageStatus.Stopped, null, usage);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Couldn't save the stopped reply in conversation {ConversationId}", conversation.Id);
                }
            }
        }
    }

    internal static string MakeTitle(string message)
    {
        var firstLine = message.Split('\n', 2)[0];
        var collapsed = string.Join(' ', firstLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length == 0)
        {
            return "New chat";
        }

        return collapsed.Length <= TitleLength ? collapsed : collapsed[..(TitleLength - 1)].TrimEnd() + "…";
    }

    /// <summary>Earlier turns, oldest first, with their attached files. Failed replies and empty messages are left out.</summary>
    internal static List<ChatMessage> BuildHistory(IEnumerable<ConversationMessage> messages) =>
        messages
            .Where(m => m.Status != MessageStatus.Failed && (!string.IsNullOrWhiteSpace(m.Content) || m.Attachments.Count > 0))
            .OrderBy(m => m.Sequence)
            .Select(m => m.Role == MessageRole.User
                ? new ChatMessage(ChatRole.User, ComposeUserText(m))
                : new ChatMessage(ChatRole.Assistant, m.Content))
            .ToList();

    /// <summary>
    /// What the model sees for a user message: each attached file's text in a document tag, then what the user
    /// typed. Plain text works with every provider, local models included, so files need no special support.
    /// </summary>
    internal static string ComposeUserText(ConversationMessage message)
    {
        if (message.Attachments.Count == 0)
        {
            return message.Content;
        }

        var text = new StringBuilder();
        foreach (var file in message.Attachments.OrderBy(a => a.Position))
        {
            text.Append("<document name=\"").Append(file.FileName.Replace('"', '\'')).Append("\">\n")
                .Append(file.Text.TrimEnd()).Append('\n');
            if (file.IsTruncated)
            {
                // Invariant, so the number reads the same whatever language the computer uses.
                text.Append(CultureInfo.InvariantCulture, $"[The file is longer than this. Only its first {file.Text.Length:N0} characters are included.]\n");
            }

            text.Append("</document>\n\n");
        }

        return text.Append(message.Content).ToString().TrimEnd();
    }

    /// <summary>The attachments in the order asked for, or null if any is missing or was already sent.</summary>
    private async Task<List<Attachment>?> FindUnsentAsync(List<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var found = (await attachments.FindManyAsync(ids, ct)).ToDictionary(a => a.Id);
        var ordered = new List<Attachment>(ids.Count);
        foreach (var id in ids)
        {
            if (!found.TryGetValue(id, out var attachment) || attachment.MessageId is not null)
            {
                return null;
            }

            ordered.Add(attachment);
        }

        return ordered;
    }

    private (IChatClient? Client, string? Error) TryCreateClient(Provider provider, ProviderModel model)
    {
        try
        {
            return (chatClients.Create(provider, model), null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't create a client for {Provider} model {Model}", provider.Name, model.ModelId);
            return (null, errors.Describe(ex, provider.Kind));
        }
    }

    private async Task<ConversationMessage> SaveReplyAsync(
        Conversation conversation,
        Provider provider,
        ProviderModel model,
        int sequence,
        string content,
        MessageStatus status,
        string? error,
        UsageTally usage)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var message = new ConversationMessage
        {
            ConversationId = conversation.Id,
            Sequence = sequence,
            Role = MessageRole.Assistant,
            Content = content,
            Status = status,
            Error = error,
            ProviderId = provider.Id,
            ProviderName = provider.Name,
            ModelId = model.ModelId,
            InputTokens = usage.Input,
            OutputTokens = usage.Output,
            CreatedAt = now,
        };
        conversations.AddMessage(message);
        conversation.UpdatedAt = now;

        // Saved even when the request was cancelled, so a stopped reply isn't lost.
        await conversations.SaveChangesAsync(CancellationToken.None);
        return message;
    }

    /// <summary>
    /// Collects token counts from a stream. Providers report usage at different points (some once at the end,
    /// some split across the start and end), so the largest value seen for each count wins.
    /// </summary>
    private sealed class UsageTally
    {
        public long? Input { get; private set; }

        public long? Output { get; private set; }

        public void Add(ChatResponseUpdate update)
        {
            foreach (var usage in update.Contents.OfType<UsageContent>())
            {
                Input = Max(Input, usage.Details.InputTokenCount);
                Output = Max(Output, usage.Details.OutputTokenCount);
            }
        }

        private static long? Max(long? current, long? candidate) =>
            candidate is null ? current : current is null ? candidate : Math.Max(current.Value, candidate.Value);
    }
}
