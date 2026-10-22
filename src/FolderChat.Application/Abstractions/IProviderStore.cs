using FolderChat.Core.Providers;

namespace FolderChat.Application.Abstractions;

/// <summary>Loads and saves providers. Entities it returns are tracked, so edits persist on <see cref="SaveChangesAsync"/>.</summary>
public interface IProviderStore
{
    Task<List<Provider>> ListAsync(CancellationToken ct = default);

    Task<Provider?> FindAsync(Guid id, CancellationToken ct = default);

    void Add(Provider provider);

    void Remove(Provider provider);

    Task SaveChangesAsync(CancellationToken ct = default);
}
