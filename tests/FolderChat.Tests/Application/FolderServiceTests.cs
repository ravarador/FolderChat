using FolderChat.Application.Common;
using FolderChat.Application.Knowledge;
using FolderChat.Core.Providers;
using FolderChat.Infrastructure.Knowledge;
using FolderChat.Infrastructure.Persistence;
using FolderChat.Tests.Fakes;
using Microsoft.EntityFrameworkCore;

namespace FolderChat.Tests.Application;

public sealed class FolderServiceTests : IAsyncLifetime
{
    private readonly FakeScanQueue scans = new();
    private readonly TempFolder files = new();
    private TestDatabase database = null!;
    private FolderChatDbContext db = null!;

    public async ValueTask InitializeAsync()
    {
        database = await TestDatabase.CreateAsync();
        db = database.NewContext();
    }

    public async ValueTask DisposeAsync()
    {
        await db.DisposeAsync();
        await database.DisposeAsync();
        files.Dispose();
    }

    [Fact]
    public async Task Added_folders_are_saved_and_queued_for_reading()
    {
        // As Explorer's "Copy as path" gives it: in quotes. A trailing separator is dropped too.
        var added = await Service().AddAsync(new AddFolderRequest($"\"{files.Path}{Path.DirectorySeparatorChar}\""));

        Assert.Equal(files.Path, added.Path);
        Assert.Equal(Path.GetFileName(files.Path), added.Name);
        Assert.Equal([added.Id], scans.Enqueued);
        await using var check = database.NewContext();
        Assert.Equal(files.Path, (await check.KnowledgeFolders.SingleAsync()).Path);
    }

    [Fact]
    public async Task A_folder_cant_be_added_twice()
    {
        await Service().AddAsync(new AddFolderRequest(files.Path, "Docs"));

        var error = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Service().AddAsync(new AddFolderRequest(files.Path + Path.DirectorySeparatorChar)));

        Assert.Equal("That folder is already added.", error.Message);
    }

    [Fact]
    public async Task Choosing_a_folder_again_finds_the_one_already_added()
    {
        var chosen = await Service().AddChosenAsync(files.Path);

        // A Mac's folder window gives the path with a separator at the end.
        var again = await Service().AddChosenAsync(files.Path + Path.DirectorySeparatorChar);

        Assert.Equal((true, Path.GetFileName(files.Path)), (chosen.Added, chosen.Folder.Name));
        Assert.Equal((false, chosen.Folder.Id), (again.Added, again.Folder.Id));
        Assert.Equal([chosen.Folder.Id], scans.Enqueued); // read once, not again
    }

    [Fact]
    public async Task Paths_that_arent_a_folder_are_refused()
    {
        string[] paths =
        [
            "",
            "relative/folder",
            Path.Combine(files.Path, "missing"),
            Path.GetPathRoot(files.Path)!,
        ];

        foreach (var path in paths)
        {
            await Assert.ThrowsAsync<ValidationFailedException>(() => Service().AddAsync(new AddFolderRequest(path)));
        }

        Assert.Empty(scans.Enqueued);
    }

    [Fact]
    public async Task Summaries_need_a_model_and_start_a_scan()
    {
        var folder = await Service().AddAsync(new AddFolderRequest(files.Path, "Docs"));
        var provider = new Provider { Name = "Ollama", Kind = ProviderKind.Ollama };
        db.Providers.Add(provider);
        await db.SaveChangesAsync();
        scans.Enqueued.Clear();

        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Service().UpdateAsync(folder.Id, new UpdateFolderRequest("Docs", SummariesEnabled: true)));
        var updated = await Service().UpdateAsync(folder.Id, new UpdateFolderRequest("Contracts", true, provider.Id, " qwen3:4b "));

        Assert.Equal(("Contracts", true, provider.Id, "qwen3:4b"), (updated.Name, updated.SummariesEnabled, updated.SummaryProviderId, updated.SummaryModelId));
        Assert.Equal([folder.Id], scans.Enqueued);
    }

    [Fact]
    public async Task Removing_a_folder_stops_its_scan_and_forgets_it()
    {
        var folder = await Service().AddAsync(new AddFolderRequest(files.Path));

        await Service().DeleteAsync(folder.Id);

        Assert.Equal([folder.Id], scans.Cancelled);
        Assert.Empty(await Service().ListAsync());
        Assert.True(Directory.Exists(files.Path)); // the folder itself is left alone
    }

    private FolderService Service() => new(
        new EfKnowledgeStore(db),
        new KnowledgeIndex(db),
        new EfProviderStore(db),
        scans,
        TimeProvider.System);
}
