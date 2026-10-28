using System.Security.Cryptography;
using System.Text;
using FolderChat.Application.Abstractions;
using FolderChat.Application.Common;
using FolderChat.Core.Api;

namespace FolderChat.Application.Api;

/// <summary>Creates, lists and revokes the keys other apps use to call the REST API, and checks the keys they send.</summary>
public sealed class ApiKeyService(IApiKeyStore store, TimeProvider clock)
{
    /// <summary>Every key starts with this, so a leaked one is easy to recognize, for example by secret scanners.</summary>
    public const string KeyPrefix = "fck_";

    public const int MaxNameLength = 100;

    /// <summary>How many characters of a key are kept to show in Settings: the prefix and 8 more.</summary>
    private const int ShownLength = 12;

    /// <summary>Saving the last-used time on every call would mean a database write per request; once a minute is enough.</summary>
    private static readonly TimeSpan LastUsedPrecision = TimeSpan.FromMinutes(1);

    public async Task<IReadOnlyList<ApiKeyDto>> ListAsync(CancellationToken ct = default)
    {
        var keys = await store.ListAsync(ct);
        return keys.Select(ToDto).ToList();
    }

    /// <summary>Creates a key. The returned secret is the only copy: only its hash is saved.</summary>
    public async Task<CreatedApiKeyDto> CreateAsync(string? name, CancellationToken ct = default)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            throw new ValidationFailedException("Give the key a name, such as the app that will use it.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new ValidationFailedException($"Keep the name under {MaxNameLength} characters.");
        }

        var secret = KeyPrefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        var key = new ApiKey
        {
            Name = trimmed,
            Prefix = secret[..ShownLength],
            Hash = Hash(secret),
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        store.Add(key);
        await store.SaveChangesAsync(ct);
        return new CreatedApiKeyDto(ToDto(key), secret);
    }

    /// <summary>Revokes a key. Apps still using it get 401 from then on.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var key = await store.FindAsync(id, ct) ?? throw new NotFoundException("That API key doesn't exist.");
        store.Remove(key);
        await store.SaveChangesAsync(ct);
    }

    /// <summary>The key that <paramref name="secret"/> belongs to, or null when it isn't a current key.</summary>
    public async Task<ApiKeyDto?> AuthenticateAsync(string? secret, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(secret) || !secret.StartsWith(KeyPrefix, StringComparison.Ordinal) || secret.Length > 200)
        {
            return null;
        }

        // Looked up by hash, so the database never sees the key and a timing difference reveals nothing about it.
        var key = await store.FindByHashAsync(Hash(secret), ct);
        if (key is null)
        {
            return null;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (key.LastUsedAt is null || now - key.LastUsedAt.Value >= LastUsedPrecision)
        {
            key.LastUsedAt = now;
            await store.SaveChangesAsync(ct);
        }

        return ToDto(key);
    }

    internal static string Hash(string secret) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static ApiKeyDto ToDto(ApiKey key) => new(key.Id, key.Name, key.Prefix, key.CreatedAt, key.LastUsedAt);
}
