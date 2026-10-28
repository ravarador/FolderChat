using FolderChat.Core.Conversations;
using FolderChat.Core.Knowledge;

namespace FolderChat.Application.Api;

// The shapes of the public REST API at /api/v1. Other apps are built against them, so they only gain fields:
// renaming or removing one needs a /api/v2.

/// <summary>An API key as Settings shows it. The key itself is never included.</summary>
/// <param name="Prefix">The key's first characters, such as <c>fck_3Jx9aQ2m</c>.</param>
public sealed record ApiKeyDto(Guid Id, string Name, string Prefix, DateTime CreatedAt, DateTime? LastUsedAt);

/// <summary>A new key, with the only copy of its secret.</summary>
public sealed record CreatedApiKeyDto(ApiKeyDto Key, string Secret);

/// <summary>
/// Values <c>POST /api/v1/chat</c> uses when a request leaves them out, from <c>FolderChat:Api:Defaults</c> in
/// appsettings.json. Providers, models and folders are given by name or ID.
/// </summary>
public sealed class ApiDefaults
{
    public string? Provider { get; set; }

    public string? Model { get; set; }

    public List<string>? Folders { get; set; }

    public SearchEffort? Effort { get; set; }

    public string? Instructions { get; set; }
}

/// <summary>A question for <c>POST /api/v1/chat</c>. Everything but <paramref name="Message"/> is optional.</summary>
/// <param name="ConversationId">Continues that conversation. Leave it out to start a new one.</param>
/// <param name="Provider">The provider's name or ID. Needed only when two providers have a model of the same name.</param>
/// <param name="Model">The model's ID or name. Defaults to the conversation's model, then to the configured default.</param>
/// <param name="Folders">Knowledge folders to answer from, by name or ID. An empty list answers without folders.</param>
/// <param name="Effort">How hard to search the folders: Quick, Standard or Thorough.</param>
/// <param name="Instructions">Extra instructions for the model, such as "Answer only from the manuals".</param>
/// <param name="User">The calling app's ID for its user, saved with a new conversation so it can list them later.</param>
/// <param name="Stream">True to get Server-Sent Events as the reply is written, instead of one JSON reply.</param>
public sealed record ApiChatRequest(
    string? Message,
    Guid? ConversationId = null,
    string? Provider = null,
    string? Model = null,
    IReadOnlyList<string>? Folders = null,
    SearchEffort? Effort = null,
    string? Instructions = null,
    string? User = null,
    bool Stream = false);

/// <summary>The finished reply to an <see cref="ApiChatRequest"/>.</summary>
/// <param name="Text">The reply in Markdown. Citations look like [1] and match <paramref name="Sources"/>.</param>
/// <param name="FinishReason">Why the model stopped, such as <c>stop</c> or <c>length</c>, when the provider says.</param>
public sealed record ApiChatReply(
    Guid ConversationId,
    Guid MessageId,
    string Text,
    IReadOnlyList<ApiSourceDto> Sources,
    ApiModelRefDto Model,
    ApiUsageDto Usage,
    string? FinishReason);

/// <summary>A passage the reply was given, cited as [<paramref name="Number"/>].</summary>
/// <param name="Folder">The knowledge folder's name.</param>
/// <param name="File">The file's path inside the folder.</param>
/// <param name="Page">The page, for PDF files.</param>
/// <param name="Text">The passage, or null when its file has changed since.</param>
public sealed record ApiSourceDto(int Number, Guid FolderId, string Folder, string File, int? Page, string? Text);

public sealed record ApiModelRefDto(Guid ProviderId, string Provider, string Id);

/// <summary>Tokens as the provider reported them. Null when it didn't say.</summary>
public sealed record ApiUsageDto(long? InputTokens, long? OutputTokens, long? CachedInputTokens, long? ReasoningTokens);

public sealed record ApiProviderDto(Guid Id, string Name, string Kind, bool IsLocal, IReadOnlyList<ApiModelDto> Models);

/// <param name="ContextWindow">The model's context size in tokens, when the provider reports it.</param>
public sealed record ApiModelDto(string Id, string Name, int? ContextWindow);

/// <param name="IndexedFiles">Files whose text can be searched.</param>
/// <param name="IsScanning">True while FolderChat is reading the folder's files. Answers may miss files until it's done.</param>
public sealed record ApiFolderDto(Guid Id, string Name, int IndexedFiles, bool IsScanning, DateTime? LastScannedAt, string? ScanError);

/// <param name="User">The calling app's user ID, as sent when the conversation started.</param>
public sealed record ApiConversationSummaryDto(Guid Id, string Title, string? User, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record ApiConversationDto(
    Guid Id,
    string Title,
    string? User,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<ApiMessageDto> Messages);

/// <param name="Model">The model that wrote an assistant message.</param>
/// <param name="Usage">Tokens an assistant message used.</param>
public sealed record ApiMessageDto(
    Guid Id,
    MessageRole Role,
    string Content,
    MessageStatus Status,
    string? Error,
    ApiModelRefDto? Model,
    ApiUsageDto? Usage,
    IReadOnlyList<ApiSourceDto> Sources,
    DateTime CreatedAt);

/// <summary>A reply failed after the request was accepted, such as when the provider refused it.</summary>
public sealed class ApiChatFailedException(string message, Guid? conversationId, bool isProviderError) : Exception(message)
{
    /// <summary>The conversation, when the question was saved in one.</summary>
    public Guid? ConversationId { get; } = conversationId;

    /// <summary>True when the provider failed, rather than the request being unusable.</summary>
    public bool IsProviderError { get; } = isProviderError;
}
