using FolderChat.Application.Abstractions;
using FolderChat.Application.Attachments;
using FolderChat.Application.Common;
using FolderChat.Application.Knowledge;
using FolderChat.Core.Conversations;
using FolderChat.Core.Knowledge;

namespace FolderChat.Application.Conversations;

/// <summary>Lists, opens, renames and deletes conversations.</summary>
public sealed class ConversationService(IConversationStore store, TimeProvider clock)
{
    public const int MaxTitleLength = 200;

    public async Task<IReadOnlyList<ConversationSummaryDto>> ListAsync(CancellationToken ct = default)
    {
        var conversations = await store.ListAsync(ct);
        return conversations.Select(ToSummary).ToList();
    }

    public async Task<ConversationDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var conversation = await FindAsync(id, includeMessages: true, ct);
        return new ConversationDto(
            conversation.Id,
            conversation.Title,
            conversation.ProviderId,
            conversation.ModelId,
            conversation.FolderIds,
            conversation.Effort,
            conversation.CreatedAt,
            conversation.UpdatedAt,
            conversation.Messages.Select(ToDto).ToList());
    }

    public async Task<ConversationSummaryDto> RenameAsync(Guid id, string title, CancellationToken ct = default)
    {
        var trimmed = title?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            throw new ValidationFailedException("The title can't be empty.");
        }

        if (trimmed.Length > MaxTitleLength)
        {
            throw new ValidationFailedException($"Keep the title under {MaxTitleLength} characters.");
        }

        var conversation = await FindAsync(id, includeMessages: false, ct);
        conversation.Title = trimmed;
        conversation.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await store.SaveChangesAsync(ct);
        return ToSummary(conversation);
    }

    /// <summary>Sets which knowledge folders the chat answers from, and how hard it searches them.</summary>
    public async Task SetFoldersAsync(Guid id, IReadOnlyList<Guid>? folderIds, SearchEffort effort, CancellationToken ct = default)
    {
        var ids = folderIds?.Distinct().ToList() ?? [];
        if (ids.Count > FolderAnswering.MaxFolders)
        {
            throw new ValidationFailedException($"A chat can answer from up to {FolderAnswering.MaxFolders} folders.");
        }

        if (!Enum.IsDefined(effort))
        {
            throw new ValidationFailedException("Choose Quick, Standard or Thorough.");
        }

        var conversation = await FindAsync(id, includeMessages: false, ct);
        conversation.FolderIds = ids;
        conversation.Effort = effort;

        // A setting, not new activity, so UpdatedAt stays and the chat keeps its place in the list.
        await store.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var conversation = await FindAsync(id, includeMessages: false, ct);
        store.Remove(conversation);
        await store.SaveChangesAsync(ct);
    }

    internal static MessageDto ToDto(ConversationMessage m) => new(
        m.Id, m.Role, m.Content, m.Status, m.Error, m.ProviderName, m.ModelId, m.InputTokens, m.OutputTokens, m.CreatedAt,
        m.Attachments.OrderBy(a => a.Position).Select(AttachmentService.ToSummary).ToList(),
        m.Sources.OrderBy(s => s.Number).Select(ToSourceDto).ToList());

    internal static SourceDto ToSourceDto(MessageSource s) => new(s.Number, s.FolderId, s.FileId, s.Path, s.Page, s.PassageId);

    private async Task<Conversation> FindAsync(Guid id, bool includeMessages, CancellationToken ct) =>
        await store.FindAsync(id, includeMessages, ct) ?? throw new NotFoundException("That conversation doesn't exist.");

    private static ConversationSummaryDto ToSummary(Conversation c) => new(c.Id, c.Title, c.ModelId, c.UpdatedAt);
}
