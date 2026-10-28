using System.Runtime.CompilerServices;
using System.Text;
using FolderChat.Application.Abstractions;
using FolderChat.Application.Chat;
using FolderChat.Application.Common;
using FolderChat.Application.Conversations;
using FolderChat.Application.Providers;
using FolderChat.Core.Conversations;
using FolderChat.Core.Knowledge;
using FolderChat.Core.Providers;

namespace FolderChat.Application.Api;

/// <summary>
/// The REST API's use cases: what other apps can see and ask. Callers name providers, models and folders by name or
/// ID, and can only read and continue the conversations they started through the API.
/// </summary>
public sealed class ApiChatService(
    ChatService chat,
    IProviderStore providers,
    IConversationStore conversations,
    IKnowledgeStore folders,
    IKnowledgeIndex index,
    IFolderScanQueue scans,
    ApiDefaults defaults)
{
    public const int MaxUserLength = 200;

    /// <summary>Enabled providers with their enabled models, as the chat page's picker shows them.</summary>
    public async Task<IReadOnlyList<ApiProviderDto>> ListModelsAsync(CancellationToken ct = default)
    {
        var all = await providers.ListAsync(ct);
        return all
            .Where(p => p.IsEnabled)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new ApiProviderDto(
                p.Id,
                p.Name,
                p.Kind.ToString(),
                ProviderService.IsLocal(p),
                p.Models
                    .Where(m => m.IsEnabled)
                    .OrderBy(m => m.DisplayName ?? m.ModelId, StringComparer.OrdinalIgnoreCase)
                    .Select(m => new ApiModelDto(m.ModelId, m.DisplayName ?? m.ModelId, m.ContextWindow))
                    .ToList()))
            .Where(p => p.Models.Count > 0)
            .ToList();
    }

    public async Task<IReadOnlyList<ApiFolderDto>> ListFoldersAsync(CancellationToken ct = default)
    {
        var all = await folders.ListAsync(ct);
        var counts = await folders.CountFilesAsync(ct);
        return all
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(f => new ApiFolderDto(
                f.Id,
                f.Name,
                counts.TryGetValue(f.Id, out var count) ? count.Indexed : 0,
                scans.GetProgress(f.Id) is not null,
                f.LastScannedAt,
                f.ScanError))
            .ToList();
    }

    /// <summary>
    /// Turns an API request into a chat request, filling in what it leaves out from the conversation, then from the
    /// configured defaults.
    /// </summary>
    /// <exception cref="ValidationFailedException">The request can't be used as it is.</exception>
    /// <exception cref="NotFoundException">A conversation, provider, model or folder it names doesn't exist.</exception>
    public async Task<ApiPreparedChat> PrepareAsync(ApiChatRequest request, CancellationToken ct = default)
    {
        var message = request.Message?.Trim() ?? "";
        if (message.Length == 0)
        {
            throw new ValidationFailedException("Send a message: \"message\" is empty.");
        }

        var user = request.User?.Trim();
        if (user?.Length > MaxUserLength)
        {
            throw new ValidationFailedException($"\"user\" is limited to {MaxUserLength} characters.");
        }

        if (request.Effort is { } requestedEffort && !Enum.IsDefined(requestedEffort))
        {
            throw new ValidationFailedException("\"effort\" must be Quick, Standard or Thorough.");
        }

        Conversation? conversation = null;
        if (request.ConversationId is { } conversationId)
        {
            conversation = await FindApiConversationAsync(conversationId, includeMessages: false, ct);
        }

        var all = await providers.ListAsync(ct);
        var (provider, modelId) = ResolveModel(all, request, conversation);
        var folderIds = await ResolveFoldersAsync(request.Folders ?? (conversation is null ? defaults.Folders : null), conversation, ct);
        var effort = request.Effort ?? conversation?.Effort ?? defaults.Effort ?? SearchEffort.Standard;

        return new ApiPreparedChat(
            new ChatRequest(
                conversation?.Id,
                provider.Id,
                modelId,
                message,
                FolderIds: folderIds,
                Effort: effort,
                Instructions: request.Instructions ?? defaults.Instructions,
                Source: ConversationSource.Api,
                ExternalUser: string.IsNullOrEmpty(user) ? null : user),
            new ApiModelRefDto(provider.Id, provider.Name, modelId));
    }

    /// <summary>Asks the question and waits for the whole reply.</summary>
    /// <exception cref="ApiChatFailedException">The reply failed.</exception>
    public async Task<ApiChatReply> AskAsync(ApiPreparedChat prepared, CancellationToken ct = default)
    {
        Guid? conversationId = null;
        var text = new StringBuilder();
        IReadOnlyList<ApiSourceDto> sources = [];

        await foreach (var chatEvent in StreamAsync(prepared, ct))
        {
            switch (chatEvent)
            {
                case ApiStarted started:
                    conversationId = started.ConversationId;
                    break;
                case ApiDelta delta:
                    text.Append(delta.Text);
                    break;
                case ApiSources found:
                    sources = found.Sources;
                    break;
                case ApiCompleted completed:
                    return new ApiChatReply(
                        completed.ConversationId, completed.MessageId, text.ToString(), sources, completed.Model, completed.Usage, completed.FinishReason);
                case ApiError error:
                    throw new ApiChatFailedException(error.Error, error.ConversationId, error.MessageId is not null);
            }
        }

        // ChatService always ends with one of the two above; this is only reached if the caller cancelled.
        throw new OperationCanceledException(ct);
    }

    /// <summary>Asks the question and streams the reply as it's written.</summary>
    public async IAsyncEnumerable<ApiStreamEvent> StreamAsync(ApiPreparedChat prepared, [EnumeratorCancellation] CancellationToken ct = default)
    {
        Guid? conversationId = prepared.Request.ConversationId;
        await foreach (var chatEvent in chat.StreamAsync(prepared.Request, ct))
        {
            switch (chatEvent)
            {
                case ChatStarted started:
                    conversationId = started.ConversationId;
                    yield return new ApiStarted(started.ConversationId, started.Title, started.IsNewConversation);
                    break;
                case ChatDelta delta:
                    yield return new ApiDelta(delta.Text);
                    break;
                case ChatActivity activity:
                    yield return new ApiActivity(activity.Text);
                    break;
                case ChatSources found:
                    yield return new ApiSources(await ToApiSourcesAsync(found.Sources.Select(s => (s.Number, s.FolderId, s.Path, s.Page, s.PassageId)), ct));
                    break;
                case ChatCompleted completed:
                    yield return new ApiCompleted(
                        conversationId!.Value,
                        completed.MessageId,
                        prepared.Model,
                        new ApiUsageDto(completed.InputTokens, completed.OutputTokens, completed.CachedInputTokens, completed.ReasoningTokens),
                        completed.FinishReason);
                    break;
                case ChatFailed failed:
                    yield return new ApiError(failed.Error, conversationId, failed.MessageId);
                    break;
            }
        }
    }

    /// <summary>Conversations started through the API, most recently updated first, optionally for one user only.</summary>
    public async Task<IReadOnlyList<ApiConversationSummaryDto>> ListConversationsAsync(string? user, CancellationToken ct = default)
    {
        var all = await conversations.ListAsync(ct);
        return all
            .Where(c => c.Source == ConversationSource.Api && (string.IsNullOrEmpty(user) || c.ExternalUser == user))
            .Select(c => new ApiConversationSummaryDto(c.Id, c.Title, c.ExternalUser, c.CreatedAt, c.UpdatedAt))
            .ToList();
    }

    public async Task<ApiConversationDto> GetConversationAsync(Guid id, CancellationToken ct = default)
    {
        var conversation = await FindApiConversationAsync(id, includeMessages: true, ct);
        var messages = new List<ApiMessageDto>();
        foreach (var m in conversation.Messages.OrderBy(m => m.Sequence))
        {
            var isReply = m.Role == MessageRole.Assistant;
            messages.Add(new ApiMessageDto(
                m.Id,
                m.Role,
                m.Content,
                m.Status,
                m.Error,
                isReply && m.ModelId is not null ? new ApiModelRefDto(m.ProviderId ?? Guid.Empty, m.ProviderName ?? "", m.ModelId) : null,
                isReply ? new ApiUsageDto(m.InputTokens, m.OutputTokens, m.CachedInputTokens, m.ReasoningTokens) : null,
                await ToApiSourcesAsync(m.Sources.OrderBy(s => s.Number).Select(s => (s.Number, s.FolderId, s.Path, s.Page, s.PassageId)), ct),
                m.CreatedAt));
        }

        return new ApiConversationDto(conversation.Id, conversation.Title, conversation.ExternalUser, conversation.CreatedAt, conversation.UpdatedAt, messages);
    }

    public async Task DeleteConversationAsync(Guid id, CancellationToken ct = default)
    {
        var conversation = await FindApiConversationAsync(id, includeMessages: false, ct);
        conversations.Remove(conversation);
        await conversations.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Finds a conversation started through the API. Chats started in FolderChat itself are private to its owner, so
    /// to an API caller they don't exist.
    /// </summary>
    private async Task<Conversation> FindApiConversationAsync(Guid id, bool includeMessages, CancellationToken ct)
    {
        var conversation = await conversations.FindAsync(id, includeMessages, ct);
        return conversation is { Source: ConversationSource.Api }
            ? conversation
            : throw new NotFoundException("That conversation doesn't exist.");
    }

    private (Provider Provider, string ModelId) ResolveModel(List<Provider> all, ApiChatRequest request, Conversation? conversation)
    {
        var enabled = all.Where(p => p.IsEnabled).ToList();

        // A request that names a model or provider decides alone. Without one, a continued conversation keeps its
        // model, and a new one gets the default.
        string? providerName, modelName;
        if (request.Model is not null || request.Provider is not null)
        {
            (providerName, modelName) = (request.Provider, request.Model);
        }
        else if (conversation is { ProviderId: { } previousProvider, ModelId: { } previousModel }
                 && enabled.Any(p => p.Id == previousProvider))
        {
            return (enabled.First(p => p.Id == previousProvider), previousModel);
        }
        else
        {
            (providerName, modelName) = (defaults.Provider, defaults.Model);
        }

        if (string.IsNullOrWhiteSpace(modelName))
        {
            throw new ValidationFailedException(
                "Name a model in \"model\", or set a default in FolderChat:Api:Defaults:Model. GET /api/v1/models lists them.");
        }

        modelName = modelName.Trim();
        if (!string.IsNullOrWhiteSpace(providerName))
        {
            var provider = FindProvider(enabled, providerName.Trim());
            var listed = FindModels(provider, modelName).FirstOrDefault();

            // A model the provider has but FolderChat hasn't listed yet still works, as in the chat page.
            return (provider, listed?.ModelId ?? modelName);
        }

        var matches = enabled
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .SelectMany(p => FindModels(p, modelName).Where(m => m.IsEnabled).Select(m => (Provider: p, m.ModelId)))
            .ToList();
        return matches.Count switch
        {
            0 => throw new NotFoundException($"No provider has a model called \"{modelName}\". GET /api/v1/models lists them."),
            1 => matches[0],
            _ => throw new ValidationFailedException(
                $"More than one provider has a model called \"{modelName}\": {string.Join(", ", matches.Select(m => m.Provider.Name))}. Say which in \"provider\"."),
        };
    }

    private static Provider FindProvider(List<Provider> enabled, string nameOrId)
    {
        if (Guid.TryParse(nameOrId, out var id))
        {
            return enabled.FirstOrDefault(p => p.Id == id)
                ?? throw new NotFoundException($"There's no enabled provider with the ID {id}.");
        }

        var named = enabled.Where(p => string.Equals(p.Name, nameOrId, StringComparison.OrdinalIgnoreCase)).ToList();
        return named.Count switch
        {
            0 => throw new NotFoundException($"There's no enabled provider called \"{nameOrId}\"."),
            1 => named[0],
            _ => throw new ValidationFailedException($"More than one provider is called \"{nameOrId}\". Use its ID from GET /api/v1/models."),
        };
    }

    /// <summary>The provider's models whose ID or display name is <paramref name="name"/>, an exact ID first.</summary>
    private static IEnumerable<ProviderModel> FindModels(Provider provider, string name) =>
        provider.Models
            .Where(m => string.Equals(m.ModelId, name, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(m.DisplayName, name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.ModelId == name ? 0 : 1);

    private async Task<IReadOnlyList<Guid>> ResolveFoldersAsync(IReadOnlyList<string>? requested, Conversation? conversation, CancellationToken ct)
    {
        if (requested is null)
        {
            return conversation?.FolderIds ?? [];
        }

        var all = await folders.ListAsync(ct);
        var ids = new List<Guid>();
        foreach (var nameOrId in requested.Select(f => f?.Trim() ?? "").Where(f => f.Length > 0))
        {
            var match = Guid.TryParse(nameOrId, out var id)
                ? all.Where(f => f.Id == id).ToList()
                : all.Where(f => string.Equals(f.Name, nameOrId, StringComparison.OrdinalIgnoreCase)).ToList();
            ids.Add(match.Count switch
            {
                0 => throw new NotFoundException($"There's no knowledge folder called \"{nameOrId}\". GET /api/v1/folders lists them."),
                1 => match[0].Id,
                _ => throw new ValidationFailedException($"More than one folder is called \"{nameOrId}\". Use its ID from GET /api/v1/folders."),
            });
        }

        return ids.Distinct().ToList();
    }

    private async Task<IReadOnlyList<ApiSourceDto>> ToApiSourcesAsync(
        IEnumerable<(int Number, Guid FolderId, string Path, int? Page, long PassageId)> sources, CancellationToken ct)
    {
        var list = sources.ToList();
        if (list.Count == 0)
        {
            return [];
        }

        var names = (await folders.ListAsync(ct)).ToDictionary(f => f.Id, f => f.Name);
        var result = new List<ApiSourceDto>(list.Count);
        foreach (var source in list)
        {
            var passage = await index.GetPassageAsync(source.PassageId, ct);
            result.Add(new ApiSourceDto(
                source.Number,
                source.FolderId,
                names.GetValueOrDefault(source.FolderId, "Removed folder"),
                source.Path,
                source.Page,
                passage?.Text));
        }

        return result;
    }
}

/// <summary>A chat request ready to send, with the model it resolved to.</summary>
public sealed record ApiPreparedChat(ChatRequest Request, ApiModelRefDto Model);

/// <summary>
/// One step of a streamed API reply: <c>started</c>, then any number of <c>delta</c> and <c>activity</c>, then
/// <c>sources</c> when folders were used, then <c>completed</c> or <c>error</c>.
/// </summary>
public abstract record ApiStreamEvent
{
    /// <summary>The Server-Sent Events name. A method, so it isn't sent in the event's data.</summary>
    public abstract string GetEventName();
}

public sealed record ApiStarted(Guid ConversationId, string Title, bool IsNewConversation) : ApiStreamEvent
{
    public override string GetEventName() => "started";
}

public sealed record ApiDelta(string Text) : ApiStreamEvent
{
    public override string GetEventName() => "delta";
}

/// <summary>Something done on the way to the reply, such as searching a folder, in words for a person.</summary>
public sealed record ApiActivity(string Text) : ApiStreamEvent
{
    public override string GetEventName() => "activity";
}

public sealed record ApiSources(IReadOnlyList<ApiSourceDto> Sources) : ApiStreamEvent
{
    public override string GetEventName() => "sources";
}

public sealed record ApiCompleted(Guid ConversationId, Guid MessageId, ApiModelRefDto Model, ApiUsageDto Usage, string? FinishReason) : ApiStreamEvent
{
    public override string GetEventName() => "completed";
}

/// <param name="ConversationId">The conversation, when the question was saved in one.</param>
/// <param name="MessageId">The failed reply, when it was saved. Set when the provider failed.</param>
public sealed record ApiError(string Error, Guid? ConversationId, Guid? MessageId) : ApiStreamEvent
{
    public override string GetEventName() => "error";
}
