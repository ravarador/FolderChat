using FolderChat.Application.Abstractions;
using FolderChat.Core.Api;
using Microsoft.EntityFrameworkCore;

namespace FolderChat.Infrastructure.Persistence;

internal sealed class EfApiKeyStore(FolderChatDbContext db) : IApiKeyStore
{
    public Task<List<ApiKey>> ListAsync(CancellationToken ct = default) =>
        db.ApiKeys.OrderByDescending(k => k.CreatedAt).ToListAsync(ct);

    public Task<ApiKey?> FindAsync(Guid id, CancellationToken ct = default) =>
        db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);

    public Task<ApiKey?> FindByHashAsync(string hash, CancellationToken ct = default) =>
        db.ApiKeys.FirstOrDefaultAsync(k => k.Hash == hash, ct);

    public void Add(ApiKey key) => db.ApiKeys.Add(key);

    public void Remove(ApiKey key) => db.ApiKeys.Remove(key);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
