using FolderChat.Application.Abstractions;
using FolderChat.Core.Conversations;
using Microsoft.EntityFrameworkCore;

namespace FolderChat.Infrastructure.Persistence;

internal sealed class EfAttachmentStore(FolderChatDbContext db) : IAttachmentStore
{
    public Task<Attachment?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.Attachments.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<List<Attachment>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        db.Attachments.Where(a => ids.Contains(a.Id)).ToListAsync(ct);

    public void Add(Attachment attachment) => db.Attachments.Add(attachment);

    public void Remove(Attachment attachment) => db.Attachments.Remove(attachment);

    public Task RemoveUnsentAsync(DateTime cutoff, CancellationToken ct = default) =>
        db.Attachments.Where(a => a.MessageId == null && a.CreatedAt < cutoff).ExecuteDeleteAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
