namespace FolderChat.Core.Api;

/// <summary>
/// A key another app sends to use the REST API. Only a hash of the key is stored; the key itself is shown once,
/// when it's created.
/// </summary>
public sealed class ApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>What the key is for, such as the app that uses it.</summary>
    public required string Name { get; set; }

    /// <summary>The key's first characters, so it can be recognized in Settings without being stored.</summary>
    public required string Prefix { get; set; }

    /// <summary>The SHA-256 hash of the whole key, as lowercase hex.</summary>
    public required string Hash { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>When an API call last used the key, to the nearest minute or so.</summary>
    public DateTime? LastUsedAt { get; set; }
}
