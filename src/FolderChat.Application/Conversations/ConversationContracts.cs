using FolderChat.Core.Conversations;

namespace FolderChat.Application.Conversations;

public sealed record ConversationSummaryDto(Guid Id, string Title, string? ModelId, DateTime UpdatedAt);

public sealed record ConversationDto(
    Guid Id,
    string Title,
    Guid? ProviderId,
    string? ModelId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<MessageDto> Messages);

public sealed record MessageDto(
    Guid Id,
    MessageRole Role,
    string Content,
    MessageStatus Status,
    string? Error,
    string? ProviderName,
    string? ModelId,
    long? InputTokens,
    long? OutputTokens,
    DateTime CreatedAt);
