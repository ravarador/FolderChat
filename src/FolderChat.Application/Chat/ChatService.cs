using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using FolderChat.Application.Abstractions;
using FolderChat.Application.Conversations;
using FolderChat.Application.Knowledge;
using FolderChat.Core.Conversations;
using FolderChat.Core.Knowledge;
using FolderChat.Core.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace FolderChat.Application.Chat;

/// <summary>Sends a message to the chosen model and streams the reply, saving both to the conversation.</summary>
public sealed class ChatService(
    IProviderStore providers,
    IConversationStore conversations,
    IAttachmentStore attachments,
    FolderAnswering folders,
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

        var folderIds = request.FolderIds?.Distinct().ToList() ?? [];
        if (folderIds.Count > FolderAnswering.MaxFolders)
        {
            yield return new ChatFailed($"A chat can answer from up to {FolderAnswering.MaxFolders} folders.", null);
            yield break;
        }

        FolderSession? folderSession = null;
        if (folderIds.Count > 0)
        {
            var (session, folderError) = await folders.StartAsync(folderIds, request.Effort, ct);
            if (session is null)
            {
                yield return new ChatFailed(folderError ?? "Those folders can't be used right now.", null);
                yield break;
            }

            folderSession = session;
        }

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
        conversation.FolderIds = folderSession?.Folders.Select(f => f.Id).ToList() ?? [];
        conversation.Effort = Enum.IsDefined(request.Effort) ? request.Effort : conversation.Effort;
        conversation.UpdatedAt = now;
        await conversations.SaveChangesAsync(ct);

        yield return new ChatStarted(conversation.Id, conversation.Title, userMessage.Id, isNew);

        var reply = new Reply();
        var saved = false;
        var replySequence = nextSequence + 1;

        try
        {
            var (client, createError) = TryCreateClient(provider, model);
            reply.Failure = createError;

            if (client is not null)
            {
                using (client)
                {
                    if (folderSession is not null)
                    {
                        history.Insert(0, new ChatMessage(ChatRole.System, folderSession.Instructions));
                        if (folderSession.Effort == SearchEffort.Quick)
                        {
                            yield return await SearchForModelAsync(folderSession, history, text, ct);
                        }
                    }

                    await foreach (var chatEvent in RunModelAsync(client, history, folderSession, reply, provider, model, ct))
                    {
                        yield return chatEvent;
                    }

                    // A model that can't call tools fails straight away. Search once for it instead.
                    if (folderSession is not null && folderSession.Effort != SearchEffort.Quick && reply.CouldNotUseTools)
                    {
                        logger.LogInformation("{Provider} model {Model} can't call tools; searching for it instead", provider.Name, model.ModelId);
                        folderSession.UseQuickSearch();
                        reply.Failure = null;
                        history[0] = new ChatMessage(ChatRole.System, folderSession.Instructions);
                        yield return new ChatActivity("This model can't search by itself, so FolderChat searched once for it.");
                        yield return await SearchForModelAsync(folderSession, history, text, ct);
                        await foreach (var chatEvent in RunModelAsync(client, history, folderSession, reply, provider, model, ct))
                        {
                            yield return chatEvent;
                        }
                    }
                }
            }

            if (reply.Failure is null && reply.Text.Length == 0)
            {
                if (reply.FinishReason == ChatFinishReason.ContentFilter)
                {
                    reply.Failure = "The model declined to answer this request.";
                }
                else if (reply.RanOutOfRounds)
                {
                    reply.Failure = "The model kept searching without answering. Try asking more specifically.";
                }
            }

            if (folderSession is { Sources.Count: > 0 })
            {
                yield return new ChatSources(folderSession.Sources.Select(ConversationService.ToSourceDto).ToList());
            }

            if (reply.Failure is not null)
            {
                var failed = await SaveReplyAsync(conversation, provider, model, replySequence, reply, MessageStatus.Failed, folderSession);
                saved = true;
                yield return new ChatFailed(reply.Failure, failed.Id);
            }
            else
            {
                var completed = await SaveReplyAsync(conversation, provider, model, replySequence, reply, MessageStatus.Complete, folderSession);
                saved = true;
                yield return new ChatCompleted(
                    completed.Id,
                    reply.InputTokens,
                    reply.OutputTokens,
                    reply.FinishReason?.Value,
                    reply.CachedInputTokens,
                    reply.ReasoningTokens,
                    reply.ContextTokens);
            }
        }
        finally
        {
            // The caller stopped listening before the reply finished. Keep the partial reply.
            if (!saved && reply.Text.Length > 0)
            {
                try
                {
                    await SaveReplyAsync(conversation, provider, model, replySequence, reply, MessageStatus.Stopped, folderSession);
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

    /// <summary>
    /// Streams the model's reply. When the folders give the model tools, each round's tool calls are run and their
    /// results sent back, until the model answers or runs out of rounds. Without tools there is just one round.
    /// </summary>
    private async IAsyncEnumerable<ChatStreamEvent> RunModelAsync(
        IChatClient client,
        List<ChatMessage> messages,
        FolderSession? folderSession,
        Reply reply,
        Provider provider,
        ProviderModel model,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var tools = folderSession?.Tools ?? [];
        var options = tools.Count == 0 ? null : new ChatOptions { Tools = tools, ToolMode = ChatToolMode.Auto };
        var maxRounds = tools.Count == 0 ? 1 : folderSession!.MaxRounds;

        for (var round = 1; round <= maxRounds; round++)
        {
            var updates = new List<ChatResponseUpdate>();
            var calls = new List<FunctionCallContent>();
            var usage = new UsageTally();

            await using (var stream = client.GetStreamingResponseAsync(messages, options, ct).GetAsyncEnumerator(ct))
            {
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = await stream.MoveNextAsync();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        logger.LogWarning(ex, "Chat with {Provider} model {Model} failed", provider.Name, model.ModelId);
                        reply.Failure = errors.Describe(ex, provider.Kind);
                        break;
                    }

                    if (!hasNext)
                    {
                        break;
                    }

                    var update = stream.Current;
                    updates.Add(update);
                    reply.FinishReason = update.FinishReason ?? reply.FinishReason;
                    usage.Add(update);

                    var piece = update.Text;
                    if (!string.IsNullOrEmpty(piece))
                    {
                        // Text after a search starts a new paragraph, so it doesn't run on from what came before.
                        if (reply.StartParagraph && reply.Text.Length > 0)
                        {
                            piece = "\n\n" + piece;
                        }

                        reply.StartParagraph = false;
                        reply.Text.Append(piece);
                        yield return new ChatDelta(piece);
                    }

                    calls.AddRange(update.Contents.OfType<FunctionCallContent>());
                }
            }

            reply.AddUsage(usage);
            if (reply.Failure is not null || calls.Count == 0 || folderSession is null)
            {
                yield break;
            }

            if (round == maxRounds)
            {
                reply.RanOutOfRounds = true;
                yield break;
            }

            // The model's turn goes back into the conversation with its calls, followed by their results.
            foreach (var message in updates.ToChatResponse().Messages)
            {
                message.Contents = message.Contents.Where(c => c is not UsageContent).ToList();
                messages.Add(message);
            }

            var results = new List<AIContent>();
            foreach (var call in calls)
            {
                reply.ToolCalls++;
                var (result, activity) = await RunToolAsync(folderSession, call, ct);
                results.Add(new FunctionResultContent(call.CallId, result));
                yield return new ChatActivity(activity);
            }

            messages.Add(new ChatMessage(ChatRole.Tool, results));
            reply.StartParagraph = true;

            // The next round is the last: ask for an answer rather than more searching.
            if (round == maxRounds - 1)
            {
                options!.ToolMode = ChatToolMode.None;
            }
        }
    }

    /// <summary>Quick: searches the folders with the user's message and puts the passages it finds before it.</summary>
    private static async Task<ChatActivity> SearchForModelAsync(FolderSession folderSession, List<ChatMessage> history, string question, CancellationToken ct)
    {
        var (passages, activity) = await folderSession.FindPassagesAsync(question, ct);
        history[^1] = new ChatMessage(ChatRole.User, passages + "\n\n" + history[^1].Text);
        return new ChatActivity(activity);
    }

    private async Task<(string Result, string Activity)> RunToolAsync(FolderSession folderSession, FunctionCallContent call, CancellationToken ct)
    {
        try
        {
            return await folderSession.InvokeAsync(call, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "The {Tool} folder tool failed", call.Name);
            return ("That didn't work because of an error in FolderChat. Answer with what you have.", $"Couldn't run {call.Name}");
        }
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
        Reply reply,
        MessageStatus status,
        FolderSession? folderSession)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var message = new ConversationMessage
        {
            ConversationId = conversation.Id,
            Sequence = sequence,
            Role = MessageRole.Assistant,
            Content = reply.Text.ToString(),
            Status = status,
            Error = status == MessageStatus.Failed ? reply.Failure : null,
            ProviderId = provider.Id,
            ProviderName = provider.Name,
            ModelId = model.ModelId,
            InputTokens = reply.InputTokens,
            OutputTokens = reply.OutputTokens,
            CachedInputTokens = reply.CachedInputTokens,
            ReasoningTokens = reply.ReasoningTokens,
            ContextTokens = reply.ContextTokens,
            Sources = folderSession?.Sources.ToList() ?? [],
            CreatedAt = now,
        };
        conversations.AddMessage(message);
        conversation.UpdatedAt = now;

        // Saved even when the request was cancelled, so a stopped reply isn't lost.
        await conversations.SaveChangesAsync(CancellationToken.None);
        return message;
    }

    /// <summary>The reply as it streams in, over one or more rounds with the model.</summary>
    private sealed class Reply
    {
        public StringBuilder Text { get; } = new();

        public ChatFinishReason? FinishReason { get; set; }

        public string? Failure { get; set; }

        public long? InputTokens { get; private set; }

        public long? OutputTokens { get; private set; }

        public long? CachedInputTokens { get; private set; }

        public long? ReasoningTokens { get; private set; }

        /// <summary>The last round's input and output: how full the model's context ended up.</summary>
        public long? ContextTokens { get; private set; }

        /// <summary>Tool calls run so far.</summary>
        public int ToolCalls { get; set; }

        /// <summary>True after a round of tool calls, so the next text begins a new paragraph.</summary>
        public bool StartParagraph { get; set; }

        /// <summary>True when the model still wanted to call tools in its last allowed round.</summary>
        public bool RanOutOfRounds { get; set; }

        /// <summary>
        /// True when the model failed before writing or calling anything, with an error about tools or functions:
        /// the sign of a model, or provider, that can't call tools.
        /// </summary>
        public bool CouldNotUseTools =>
            Failure is not null
            && Text.Length == 0
            && ToolCalls == 0
            && (Failure.Contains("tool", StringComparison.OrdinalIgnoreCase) || Failure.Contains("function", StringComparison.OrdinalIgnoreCase));

        /// <summary>Each round is billed separately, so the rounds' counts add up.</summary>
        public void AddUsage(UsageTally round)
        {
            InputTokens = Sum(InputTokens, round.Input);
            OutputTokens = Sum(OutputTokens, round.Output);
            CachedInputTokens = Sum(CachedInputTokens, round.CachedInput);
            ReasoningTokens = Sum(ReasoningTokens, round.Reasoning);

            // Each round re-sends the whole conversation so far, so the latest round is the fullest.
            if (round.Input is not null || round.Output is not null)
            {
                ContextTokens = (round.Input ?? 0) + (round.Output ?? 0);
            }
        }

        private static long? Sum(long? total, long? add) => add is null ? total : (total ?? 0) + add;
    }

    /// <summary>
    /// Collects token counts from one streamed response. Providers report usage at different points (some once at
    /// the end, some split across the start and end), so the largest value seen for each count wins.
    /// </summary>
    private sealed class UsageTally
    {
        public long? Input { get; private set; }

        public long? Output { get; private set; }

        public long? CachedInput { get; private set; }

        public long? Reasoning { get; private set; }

        public void Add(ChatResponseUpdate update)
        {
            foreach (var usage in update.Contents.OfType<UsageContent>())
            {
                Input = Max(Input, usage.Details.InputTokenCount);
                Output = Max(Output, usage.Details.OutputTokenCount);
                CachedInput = Max(CachedInput, usage.Details.CachedInputTokenCount);
                Reasoning = Max(Reasoning, usage.Details.ReasoningTokenCount);
            }
        }

        private static long? Max(long? current, long? candidate) =>
            candidate is null ? current : current is null ? candidate : Math.Max(current.Value, candidate.Value);
    }
}
