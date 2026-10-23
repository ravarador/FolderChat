using FolderChat.Core.Conversations;

namespace FolderChat.Application.Abstractions;

/// <summary>Loads and saves attachments. Entities it returns are tracked, so edits persist on <see cref="SaveChangesAsync"/>.</summary>
public interface IAttachmentStore
{
    Task<Attachment?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>The attachments with these IDs, in no particular order. IDs that don't exist are skipped.</summary>
    Task<List<Attachment>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    void Add(Attachment attachment);

    void Remove(Attachment attachment);

    /// <summary>Deletes attachments created before <paramref name="cutoff"/> that were never sent. Runs straight away.</summary>
    Task RemoveUnsentAsync(DateTime cutoff, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
