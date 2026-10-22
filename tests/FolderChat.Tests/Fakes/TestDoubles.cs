using FolderChat.Application.Abstractions;
using FolderChat.Core.Providers;
using FolderChat.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FolderChat.Tests.Fakes;

internal sealed class FakeModelCatalog : IModelCatalog
{
    public IReadOnlyList<DiscoveredModel> Models { get; set; } = [new("model-a", "Model A"), new("model-b")];

    public Exception? Failure { get; set; }

    public Task<IReadOnlyList<DiscoveredModel>> ListModelsAsync(Provider provider, CancellationToken ct = default) =>
        Failure is null ? Task.FromResult(Models) : Task.FromException<IReadOnlyList<DiscoveredModel>>(Failure);
}

/// <summary>Marks values as "protected" in a way tests can read back.</summary>
internal sealed class FakeSecretProtector : ISecretProtector
{
    public string Protect(string plaintext) => $"protected({plaintext})";

    public string Unprotect(string protectedValue) => protectedValue["protected(".Length..^1];
}

internal sealed class MessageErrorDescriber : IProviderErrorDescriber
{
    public string Describe(Exception exception, ProviderKind kind) => exception.Message;
}

internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}

/// <summary>An in-memory SQLite database with the real migrations applied.</summary>
internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    private TestDatabase()
    {
    }

    public static async Task<TestDatabase> CreateAsync()
    {
        var database = new TestDatabase();
        await database.connection.OpenAsync();
        await using var db = database.NewContext();
        await db.Database.MigrateAsync();
        return database;
    }

    /// <summary>A fresh context, so assertions read what was saved rather than what is cached.</summary>
    public FolderChatDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FolderChatDbContext>().UseSqlite(connection).Options);

    public ValueTask DisposeAsync() => connection.DisposeAsync();
}
