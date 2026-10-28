using FolderChat.Core.Api;

namespace FolderChat.Application.Abstractions;

/// <summary>Loads and saves API keys. Entities it returns are tracked, so edits persist on <see cref="SaveChangesAsync"/>.</summary>
public interface IApiKeyStore
{
    /// <summary>All keys, newest first.</summary>
    Task<List<ApiKey>> ListAsync(CancellationToken ct = default);

    Task<ApiKey?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>The key whose <see cref="ApiKey.Hash"/> is <paramref name="hash"/>.</summary>
    Task<ApiKey?> FindByHashAsync(string hash, CancellationToken ct = default);

    void Add(ApiKey key);

    void Remove(ApiKey key);

    Task SaveChangesAsync(CancellationToken ct = default);
}
