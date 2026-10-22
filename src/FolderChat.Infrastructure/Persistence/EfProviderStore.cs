using FolderChat.Application.Abstractions;
using FolderChat.Core.Providers;
using Microsoft.EntityFrameworkCore;

namespace FolderChat.Infrastructure.Persistence;

internal sealed class EfProviderStore(FolderChatDbContext db) : IProviderStore
{
    public Task<List<Provider>> ListAsync(CancellationToken ct = default) =>
        db.Providers.Include(p => p.Models).ToListAsync(ct);

    public Task<Provider?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.Providers.Include(p => p.Models).FirstOrDefaultAsync(p => p.Id == id, ct);

    public void Add(Provider provider) => db.Providers.Add(provider);

    public void Remove(Provider provider) => db.Providers.Remove(provider);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
