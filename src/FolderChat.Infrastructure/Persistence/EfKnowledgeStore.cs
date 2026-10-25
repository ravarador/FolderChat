using FolderChat.Application.Abstractions;
using FolderChat.Core.Knowledge;
using Microsoft.EntityFrameworkCore;

namespace FolderChat.Infrastructure.Persistence;

internal sealed class EfKnowledgeStore(FolderChatDbContext db) : IKnowledgeStore
{
    public Task<List<KnowledgeFolder>> ListAsync(CancellationToken ct = default) =>
        db.KnowledgeFolders.ToListAsync(ct);

    public Task<KnowledgeFolder?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.KnowledgeFolders.FirstOrDefaultAsync(f => f.Id == id, ct);

    public async Task<Dictionary<Guid, FolderCounts>> CountFilesAsync(CancellationToken ct = default)
    {
        var rows = await db.KnowledgeFiles
            .GroupBy(f => f.FolderId)
            .Select(g => new
            {
                FolderId = g.Key,
                Indexed = g.Count(f => f.Status == KnowledgeFileStatus.Indexed),
                Skipped = g.Count(f => f.Status == KnowledgeFileStatus.Skipped),
                Summarized = g.Count(f => f.Status == KnowledgeFileStatus.Indexed && f.Summary != null),
            })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.FolderId, r => new FolderCounts(r.Indexed, r.Skipped, r.Summarized));
    }

    // Read-only, and possibly thousands of rows, so not tracked.
    public Task<List<KnowledgeFile>> ListFilesAsync(Guid folderId, CancellationToken ct = default) =>
        db.KnowledgeFiles.AsNoTracking().Where(f => f.FolderId == folderId).OrderBy(f => f.RelativePath).ToListAsync(ct);

    public Task<KnowledgeFile?> FindFileAsync(Guid fileId, CancellationToken ct = default) =>
        db.KnowledgeFiles.FirstOrDefaultAsync(f => f.Id == fileId, ct);

    public void Add(KnowledgeFolder folder) => db.KnowledgeFolders.Add(folder);

    // The database cascades to the folder's files and passages, and the passages' triggers take them out of the
    // search index.
    public Task RemoveAsync(Guid folderId, CancellationToken ct = default) =>
        db.KnowledgeFolders.Where(f => f.Id == folderId).ExecuteDeleteAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
