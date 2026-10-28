using FolderChat.Application.Abstractions;
using FolderChat.Core.Providers;
using FolderChat.Infrastructure.Persistence;
using FolderChat.Web.Infrastructure;
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

/// <summary>Records scan requests instead of scanning. Tests run the scanner themselves.</summary>
internal sealed class FakeScanQueue : IFolderScanQueue
{
    public List<Guid> Enqueued { get; } = [];

    public List<Guid> Cancelled { get; } = [];

    /// <summary>Progress to report for every folder, as if a scan were running.</summary>
    public ScanProgress? Progress { get; set; }

    public void Enqueue(Guid folderId) => Enqueued.Add(folderId);

    public ScanProgress? GetProgress(Guid folderId) => Progress;

    public void Cancel(Guid folderId) => Cancelled.Add(folderId);
}

/// <summary>Stands in for the computer's own folder window, which tests must never open.</summary>
internal sealed class FakeFolderPicker : IFolderPicker
{
    public bool IsAvailable { get; set; } = true;

    /// <summary>The folder the user chooses, or null when they cancel.</summary>
    public string? Choice { get; set; }

    public int Opened { get; private set; }

    public Task<string?> ChooseAsync(CancellationToken ct)
    {
        Opened++;
        return Task.FromResult(Choice);
    }
}

internal sealed class ManualClock(DateTimeOffset start, TimeZoneInfo? timeZone = null) : TimeProvider
{
    private DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => timeZone ?? base.LocalTimeZone;

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
