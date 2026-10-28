using FolderChat.Application.Abstractions;
using FolderChat.Application.Conversations;
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

    public async Task<List<UsageRowDto>> ListUsageAsync(DateTime since, CancellationToken ct = default)
    {
        var rows = await db.Messages
            .AsNoTracking()
            .Where(m => m.Role == MessageRole.Assistant && m.CreatedAt >= since && (m.InputTokens != null || m.OutputTokens != null))
            .GroupBy(m => new { m.ProviderName, m.ModelId })
            .Select(g => new
            {
                g.Key.ProviderName,
                g.Key.ModelId,
                Replies = g.Count(),
                Input = g.Sum(m => m.InputTokens ?? 0),
                Output = g.Sum(m => m.OutputTokens ?? 0),
                Cached = g.Sum(m => m.CachedInputTokens ?? 0),
                Reasoning = g.Sum(m => m.ReasoningTokens ?? 0),
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new UsageRowDto(r.ProviderName ?? "Unknown provider", r.ModelId ?? "Unknown model", r.Replies, r.Input, r.Output, r.Cached, r.Reasoning))
            .ToList();
    }

    public void Add(Conversation conversation) => db.Conversations.Add(conversation);

    public void AddMessage(ConversationMessage message) => db.Messages.Add(message);

    public void Remove(Conversation conversation) => db.Conversations.Remove(conversation);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
