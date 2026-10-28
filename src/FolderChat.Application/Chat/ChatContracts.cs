using FolderChat.Application.Conversations;
using FolderChat.Core.Conversations;
using FolderChat.Core.Knowledge;

namespace FolderChat.Application.Chat;

/// <summary>Sends <paramref name="Message"/> to a model. Leave <paramref name="ConversationId"/> empty to start a new conversation.</summary>
/// <param name="AttachmentIds">Uploaded files to send with the message, in the order given. The message text can be empty when there are some.</param>
/// <param name="FolderIds">The knowledge folders this chat answers from. Saved with the conversation.</param>
/// <param name="Effort">How hard to search the folders. Ignored without any.</param>
/// <param name="Instructions">Extra instructions for the model, sent as a system message with this message only.</param>
/// <param name="Source">Who started the conversation. Only used when a new one is started.</param>
/// <param name="ExternalUser">The calling app's user ID, saved with a new API conversation.</param>
public sealed record ChatRequest(
    Guid? ConversationId,
    Guid ProviderId,
    string ModelId,
    string Message,
    IReadOnlyList<Guid>? AttachmentIds = null,
    IReadOnlyList<Guid>? FolderIds = null,
    SearchEffort Effort = SearchEffort.Standard,
    string? Instructions = null,
    ConversationSource Source = ConversationSource.App,
    string? ExternalUser = null);

/// <summary>One step of a streamed reply. The stream ends with either <see cref="ChatCompleted"/> or <see cref="ChatFailed"/>.</summary>
public abstract record ChatStreamEvent;

/// <summary>The user's message is saved. Sent first.</summary>
public sealed record ChatStarted(Guid ConversationId, string Title, Guid UserMessageId, bool IsNewConversation) : ChatStreamEvent;

/// <summary>The next piece of the reply.</summary>
public sealed record ChatDelta(string Text) : ChatStreamEvent;

/// <summary>Something done on the way to the reply, such as a search of the folder, in words for the user.</summary>
public sealed record ChatActivity(string Text) : ChatStreamEvent;

/// <summary>The folder passages the model was shown, numbered as it cites them. Sent just before the final event.</summary>
public sealed record ChatSources(IReadOnlyList<SourceDto> Sources) : ChatStreamEvent;

/// <summary>The reply is finished and saved.</summary>
/// <param name="FinishReason">Why the model stopped, such as <c>stop</c> or <c>length</c>, when the provider says.</param>
/// <param name="CachedInputTokens">The part of the input the provider read from its prompt cache, when it says.</param>
/// <param name="ReasoningTokens">The part of the output the model spent thinking, when the provider says.</param>
/// <param name="ContextTokens">How much of the model's context the last round used.</param>
public sealed record ChatCompleted(
    Guid MessageId,
    long? InputTokens,
    long? OutputTokens,
    string? FinishReason,
    long? CachedInputTokens = null,
    long? ReasoningTokens = null,
    long? ContextTokens = null) : ChatStreamEvent;

/// <summary>The reply failed. <paramref name="MessageId"/> is set when the failure was saved to the conversation.</summary>
public sealed record ChatFailed(string Error, Guid? MessageId) : ChatStreamEvent;
