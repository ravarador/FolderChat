using FolderChat.Application.Abstractions;
using FolderChat.Core.Conversations;
using Microsoft.EntityFrameworkCore;

namespace FolderChat.Infrastructure.Persistence;

internal sealed class EfConversationStore(FolderChatDbContext db) : IConversationStore
{
    public Task<List<Conversation>> ListAsync(CancellationToken ct = default) =>
        db.Conversations.OrderByDescending(c => c.UpdatedAt).ToListAsync(ct);

    public Task<Conversation?> FindAsync(Guid id, bool includeMessages, CancellationToken ct = default)
    {
        IQueryable<Conversation> query = db.Conversations;
        if (includeMessages)
        {
            // Split, so a long message isn't repeated in the results once for each of its files.
            query = query
                .Include(c => c.Messages.OrderBy(m => m.Sequence))
                .ThenInclude(m => m.Attachments.OrderBy(a => a.Position))
                .AsSplitQuery();
        }

        return query.FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public void Add(Conversation conversation) => db.Conversations.Add(conversation);

    public void AddMessage(ConversationMessage message) => db.Messages.Add(message);

    public void Remove(Conversation conversation) => db.Conversations.Remove(conversation);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
