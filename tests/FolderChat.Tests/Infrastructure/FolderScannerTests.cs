using FolderChat.Application.Abstractions;
using FolderChat.Application.Knowledge;
using FolderChat.Core.Knowledge;
using FolderChat.Core.Providers;
using FolderChat.Infrastructure;
using FolderChat.Infrastructure.Documents;
using FolderChat.Infrastructure.Knowledge;
using FolderChat.Infrastructure.Persistence;
using FolderChat.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace FolderChat.Tests.Infrastructure;

/// <summary>Scans real folders on disk into a real SQLite database, then searches and reads them back.</summary>
public sealed class FolderScannerTests : IAsyncLifetime
{
    private readonly ManualClock clock = new(new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.Zero));
    private readonly TempFolder files = new();
    private readonly TempFolder dataFolder = new();
    private readonly FakeChatClientFactory chatClients = new();
    private TestDatabase database = null!;
    private FolderChatDbContext db = null!;
    private KnowledgeFolder folder = null!;

    public async ValueTask InitializeAsync()
    {
        database = await TestDatabase.CreateAsync();
        db = database.NewContext();
        folder = new KnowledgeFolder { Name = "Docs", Path = files.Path, CreatedAt = clock.GetUtcNow().UtcDateTime };
        db.KnowledgeFolders.Add(folder);
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await db.DisposeAsync();
        await database.DisposeAsync();
        files.Dispose();
        dataFolder.Dispose();
    }

    [Fact]
    public async Task Readable_files_are_indexed_and_the_rest_skipped_with_a_reason()
    {
        files.Write("notes/meeting.md", "# Meeting\n\nHosting costs fell 8% after the move.");
        files.Write("report.pdf", TestFiles.Pdf(["Quarterly report"], ["Revenue grew 12%"]));
        files.Write("plan.docx", TestFiles.Docx(TestFiles.Paragraph("Launch plan", style: "Heading1")));
        files.Write("photo.jpg", [0xFF, 0xD8, 0xFF, 0xE0]);
        files.Write("empty.txt", "");
        files.Write("node_modules/lib/readme.md", "Package docs");
        files.Write(".git/config", "Hidden");

        var reports = await ScanAsync();

        var found = await ListFilesAsync();
        Assert.Equal(["empty.txt", "notes/meeting.md", "photo.jpg", "plan.docx", "report.pdf"], found.Select(f => f.RelativePath));
        Assert.Equal(
            [KnowledgeFileStatus.Skipped, KnowledgeFileStatus.Indexed, KnowledgeFileStatus.Skipped, KnowledgeFileStatus.Indexed, KnowledgeFileStatus.Indexed],
            found.Select(f => f.Status));
        Assert.Equal("The file is empty.", found[0].Note);
        Assert.StartsWith("Images aren't read.", found[2].Note);
        Assert.Equal(2, found[4].PageCount);
        Assert.Equal(new ScanProgress(ScanPhase.Reading, 4, 5), reports[^1]);

        var meeting = Assert.Single(await Index().SearchAsync([folder.Id], "How much did hosting cost?", 5));
        Assert.Equal("notes/meeting.md", meeting.Path);
        var revenue = Assert.Single(await Index().SearchAsync([folder.Id], "revenue", 5));
        Assert.Equal(("report.pdf", (int?)2, "Revenue grew 12%"), (revenue.Path, revenue.Page, revenue.Text));
    }

    [Fact]
    public async Task Rescans_read_changed_files_again_and_forget_deleted_ones()
    {
        files.Write("a.txt", "alpha original text");
        files.Write("b.txt", "bravo");
        files.Write("c.txt", "charlie");
        await ScanAsync();
        var firstRead = (await ListFilesAsync()).ToDictionary(f => f.RelativePath, f => f.IndexedAt);

        clock.Advance(TimeSpan.FromMinutes(1));
        files.Write("a.txt", "alpha rewritten");
        files.Delete("b.txt");
        await ScanAsync();

        var found = await ListFilesAsync();
        Assert.Equal(["a.txt", "c.txt"], found.Select(f => f.RelativePath));
        Assert.NotEqual(firstRead["a.txt"], found[0].IndexedAt);
        Assert.Equal(firstRead["c.txt"], found[1].IndexedAt); // unchanged, so not read again
        Assert.Empty(await Index().SearchAsync([folder.Id], "original", 5));
        Assert.Single(await Index().SearchAsync([folder.Id], "rewritten", 5));
        Assert.Empty(await Index().SearchAsync([folder.Id], "bravo", 5));
        await AssertIndexMatchesPassagesAsync();
    }

    [Fact]
    public async Task Removing_a_folder_removes_its_passages_from_the_index()
    {
        files.Write("a.txt", "alpha");
        await ScanAsync();

        await new EfKnowledgeStore(db).RemoveAsync(folder.Id);

        Assert.Empty(await db.KnowledgeFiles.ToListAsync());
        Assert.Empty(await db.Passages.ToListAsync());
        Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT count(*) AS Value FROM PassageSearch WHERE PassageSearch MATCH 'alpha'").SingleAsync());
        await AssertIndexMatchesPassagesAsync();
    }

    [Fact]
    public async Task Searches_spread_their_places_across_files()
    {
        files.Write("long.txt", string.Join("\n\n", Enumerable.Range(1, 10).Select(i => $"Budget section {i}. " + new string('x', 1_300))));
        files.Write("short.txt", "The budget is approved.");
        await ScanAsync();

        var found = await Index().SearchAsync([folder.Id], "budget", 5);

        Assert.Equal(3, found.Count(p => p.Path == "long.txt"));
        Assert.Contains(found, p => p.Path == "short.txt");
    }

    [Fact]
    public async Task Files_can_be_read_back_by_page_or_by_part()
    {
        files.Write("report.pdf", TestFiles.Pdf(["Page one text"], [], ["Page three text"]));
        files.Write("long.txt", string.Join("\n\n", Enumerable.Range(1, 40).Select(i => $"Paragraph {i}. " + new string('y', 300))));
        await ScanAsync();

        var page = await Index().ReadAsync(folder.Id, "report.pdf", 3, FolderSession.SliceLength);
        Assert.Equal(("Page three text", 3, true), (page!.Text, page.PartCount, page.IsPage));
        Assert.Equal(0, (await Index().ReadAsync(folder.Id, "report.pdf", 2, FolderSession.SliceLength))!.FirstPassageId);
        Assert.Null(await Index().ReadAsync(folder.Id, "report.pdf", 4, FolderSession.SliceLength));

        // Paths are matched without regard to case when there's no exact match.
        var part = await Index().ReadAsync(folder.Id, "LONG.TXT", 2, FolderSession.SliceLength);
        Assert.Equal(("long.txt", false, 3), (part!.Path, part.IsPage, part.PartCount));
        Assert.Contains("Paragraph 25.", part.Text);
        Assert.DoesNotContain("Paragraph 1.", part.Text);
    }

    [Fact]
    public async Task Summaries_are_written_when_switched_on()
    {
        var recorder = new FakeChatClient
        {
            Chunks = ["A short ", "summary."],
            Usage = new UsageDetails { InputTokenCount = 30, OutputTokenCount = 6 },
        };
        chatClients.Build = (_, _) => recorder;
        await UseSummariesAsync();
        files.Write("a.txt", "alpha");
        files.Write("b.txt", "bravo");

        var reports = await ScanAsync();

        Assert.All(await ListFilesAsync(), f => Assert.Equal("A short summary.", f.Summary));
        Assert.Contains("<document name=\"a.txt\">\nalpha", recorder.Requests[0].Messages[^1].Text);
        Assert.Contains(new ScanProgress(ScanPhase.Summarizing, 1, 2), reports);
        var scanned = await db.KnowledgeFolders.AsNoTracking().SingleAsync();
        Assert.Null(scanned.ScanError);
        Assert.Equal((60, 12), (scanned.SummaryInputTokens, scanned.SummaryOutputTokens));
    }

    [Fact]
    public async Task Summaries_stop_after_repeated_failures()
    {
        var recorder = new FakeChatClient { Chunks = [], FailAfterChunks = new InvalidOperationException("rate limited") };
        chatClients.Build = (_, _) => recorder;
        await UseSummariesAsync();
        for (var i = 1; i <= 5; i++)
        {
            files.Write($"file{i}.txt", $"text {i}");
        }

        await ScanAsync();

        Assert.Equal(3, recorder.Requests.Count);
        Assert.Equal("Summaries stopped after repeated errors: rate limited", (await db.KnowledgeFolders.AsNoTracking().SingleAsync()).ScanError);
    }

    [Fact]
    public async Task A_folder_that_is_gone_says_so()
    {
        files.Dispose();

        await ScanAsync();

        Assert.StartsWith("FolderChat can't find this folder.", (await db.KnowledgeFolders.AsNoTracking().SingleAsync()).ScanError);
    }

    private async Task<List<ScanProgress>> ScanAsync()
    {
        var reports = new List<ScanProgress>();
        var scanner = new FolderScanner(
            db,
            new TextExtractor(NullLogger<TextExtractor>.Instance),
            new FileSummarizer(new EfProviderStore(db), chatClients, new MessageErrorDescriber()),
            new AppPaths(dataFolder.Path),
            clock,
            NullLogger<FolderScanner>.Instance);
        await scanner.ScanAsync(folder.Id, reports.Add, CancellationToken.None);
        return reports;
    }

    private async Task UseSummariesAsync()
    {
        var provider = new Provider
        {
            Name = "Local",
            Kind = ProviderKind.OpenAICompatible,
            BaseUrl = "http://localhost:1234/v1",
            Models = [new ProviderModel { ModelId = "summarizer" }],
        };
        db.Providers.Add(provider);
        folder.SummariesEnabled = true;
        folder.SummaryProviderId = provider.Id;
        folder.SummaryModelId = "summarizer";
        await db.SaveChangesAsync();
    }

    private Task<List<KnowledgeFile>> ListFilesAsync() =>
        db.KnowledgeFiles.AsNoTracking().OrderBy(f => f.RelativePath).ToListAsync();

    private KnowledgeIndex Index() => new(db);

    /// <summary>FTS5's own check that the index holds exactly the text of the Passages table.</summary>
    private Task AssertIndexMatchesPassagesAsync() =>
        db.Database.ExecuteSqlRawAsync("INSERT INTO PassageSearch (PassageSearch, rank) VALUES ('integrity-check', 1);");
}
