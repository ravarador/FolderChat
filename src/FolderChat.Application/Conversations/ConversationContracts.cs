using FolderChat.Application.Attachments;
using FolderChat.Core.Conversations;
using FolderChat.Core.Knowledge;

namespace FolderChat.Application.Conversations;

public sealed record ConversationSummaryDto(Guid Id, string Title, string? ModelId, DateTime UpdatedAt);

/// <param name="FolderIds">The knowledge folders this chat answers from. May include folders since removed.</param>
/// <param name="Effort">How hard this chat searches its folders.</param>
public sealed record ConversationDto(
    Guid Id,
    string Title,
    Guid? ProviderId,
    string? ModelId,
    IReadOnlyList<Guid> FolderIds,
    SearchEffort Effort,
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
    long? CachedInputTokens,
    long? ReasoningTokens,
    long? ContextTokens,
    DateTime CreatedAt,
    IReadOnlyList<AttachmentSummaryDto> Attachments,
    IReadOnlyList<SourceDto> Sources);

/// <summary>Tokens used by chat replies since <paramref name="Since"/>, by model.</summary>
/// <param name="Since">The start of the period, in UTC: the first of this month, in this computer's time zone.</param>
public sealed record UsageReportDto(DateTime Since, IReadOnlyList<UsageRowDto> Models);

/// <param name="Replies">Replies that reported token counts.</param>
public sealed record UsageRowDto(
    string ProviderName,
    string ModelId,
    int Replies,
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens,
    long ReasoningTokens);

/// <summary>A folder passage a reply was given. The reply cites it as [<paramref name="Number"/>].</summary>
/// <param name="Path">The file's path inside the folder.</param>
public sealed record SourceDto(int Number, Guid FolderId, Guid FileId, string Path, int? Page, long PassageId);
