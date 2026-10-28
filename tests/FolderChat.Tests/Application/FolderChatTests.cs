using FolderChat.Application.Abstractions;
using FolderChat.Application.Chat;
using FolderChat.Application.Conversations;
using FolderChat.Application.Knowledge;
using FolderChat.Core.Conversations;
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

namespace FolderChat.Tests.Application;

/// <summary>Chats that answer from knowledge folders, at each effort level, against a real index of real files.</summary>
public sealed class FolderChatTests : IAsyncLifetime
{
    private readonly FakeChatClientFactory clients = new();
    private readonly FakeScanQueue scans = new();
    private readonly ManualClock clock = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
    private readonly TempFolder dataFolder = new();
    private readonly List<TempFolder> tempFolders = [];
    private TestDatabase database = null!;
    private FolderChatDbContext db = null!;
    private Provider provider = null!;
    private KnowledgeFolder folder = null!;

    public async ValueTask InitializeAsync()
    {
        database = await TestDatabase.CreateAsync();
        db = database.NewContext();
        provider = new Provider
        {
            Name = "Local test",
            Kind = ProviderKind.OpenAICompatible,
            BaseUrl = "http://localhost:1234/v1",
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            Models = [new ProviderModel { ModelId = "model-a" }],
        };
        db.Providers.Add(provider);
        await db.SaveChangesAsync();
        folder = await AddFolderAsync("Docs");
    }

    public async ValueTask DisposeAsync()
    {
        await db.DisposeAsync();
        await database.DisposeAsync();
        tempFolders.ForEach(f => f.Dispose());
        dataFolder.Dispose();
    }

    [Fact]
    public async Task Quick_searches_once_and_puts_the_passages_before_the_question()
    {
        await IndexAsync(folder, ("costs.md", "Hosting costs fell 8% after the move."), ("garden.md", "Tomatoes need sun."));
        var recorder = Reply(["Costs fell 8% [1]."]);

        var events = await CollectAsync(Ask("What happened to hosting costs?", SearchEffort.Quick));

        Assert.Equal("Searched Docs: 1 passage", Assert.Single(events.OfType<ChatActivity>()).Text);
        Assert.Equal("costs.md", Assert.Single(Assert.IsType<ChatSources>(events[^2]).Sources).Path);
        Assert.IsType<ChatCompleted>(events[^1]);

        var request = Assert.Single(recorder.Requests);
        Assert.Null(request.Options);
        Assert.Equal(ChatRole.System, request.Messages[0].Role);
        Assert.StartsWith("The user is chatting with a folder of their files called \"Docs\". It holds 2 readable files.", request.Messages[0].Text);
        Assert.StartsWith("<folder_passages>\n[1] Docs/costs.md\nHosting costs fell 8% after the move.", request.Messages[^1].Text);
        Assert.EndsWith("</folder_passages>\n\nWhat happened to hosting costs?", request.Messages[^1].Text);

        await using var check = database.NewContext();
        var conversation = await check.Conversations.Include(c => c.Messages).SingleAsync();
        Assert.Equal([folder.Id], conversation.FolderIds);
        Assert.Equal(SearchEffort.Quick, conversation.Effort);
        var reply = conversation.Messages.Single(m => m.Role == MessageRole.Assistant);
        Assert.Equal(("costs.md", 1, folder.Id), (Assert.Single(reply.Sources).Path, reply.Sources[0].Number, reply.Sources[0].FolderId));
        Assert.Empty(conversation.Messages.Single(m => m.Role == MessageRole.User).Sources);
    }

    [Fact]
    public async Task Standard_lets_the_model_search_for_itself()
    {
        await IndexAsync(folder, ("costs.md", "Hosting costs fell 8% after the move."));
        var recorder = Reply(
            ["Let me look.", Call("call-1", "search_folder", ("query", "hosting costs"))],
            ["Hosting costs fell 8% [1]."]);

        var events = await CollectAsync(Ask("What happened to hosting costs?", SearchEffort.Standard));

        Assert.Equal("Searched for “hosting costs”: 1 passage", Assert.Single(events.OfType<ChatActivity>()).Text);
        Assert.Equal("Let me look.\n\nHosting costs fell 8% [1].", string.Concat(events.OfType<ChatDelta>().Select(d => d.Text)));
        Assert.Equal("costs.md", Assert.Single(Assert.IsType<ChatSources>(events[^2]).Sources).Path);

        Assert.Equal(2, recorder.Requests.Count);
        Assert.Equal(["search_folder"], recorder.Requests[0].Options!.Tools!.Select(t => t.Name));
        var second = recorder.Requests[1].Messages;
        Assert.Contains(second[^2].Contents, c => c is FunctionCallContent { CallId: "call-1" });
        var result = Assert.IsType<FunctionResultContent>(Assert.Single(second[^1].Contents));
        Assert.Equal(ChatRole.Tool, second[^1].Role);
        Assert.StartsWith("[1] Docs/costs.md\nHosting costs fell 8% after the move.", (string)result.Result!);
        Assert.EndsWith("3 searches left.", (string)result.Result!);
    }

    [Fact]
    public async Task Thorough_can_also_list_files_and_read_them_whole()
    {
        await IndexAsync(folder, ("costs.md", "Hosting costs fell 8% after the move."));
        var recorder = Reply(
            [Call("call-1", "list_files")],
            [Call("call-2", "read_file", ("path", "costs.md"), ("part", 1))],
            ["The file says costs fell [1]."]);

        var events = await CollectAsync(Ask("Summarize the folder", SearchEffort.Thorough));

        Assert.Equal(["search_folder", "read_file", "list_files"], recorder.Requests[0].Options!.Tools!.Select(t => t.Name));

        // With one folder, a path without the folder's name is still understood.
        Assert.Equal(["Listed 1 file", "Read Docs/costs.md, part 1 of 1"], events.OfType<ChatActivity>().Select(a => a.Text));
        Assert.Equal("- Docs/costs.md\n", (string)Assert.IsType<FunctionResultContent>(recorder.Requests[1].Messages[^1].Contents[0]).Result!);
        Assert.IsType<ChatCompleted>(events[^1]);
    }

    [Fact]
    public async Task Several_folders_are_searched_together_and_told_apart()
    {
        await IndexAsync(folder, ("costs.md", "Hosting costs fell 8% after the move."));
        var reports = await AddFolderAsync("Reports", ("costs.md", "Hosting costs rose again in 2025."));
        var recorder = Reply(
            [Call("call-1", "search_folder", ("query", "hosting costs"))],
            [Call("call-2", "read_file", ("path", "Reports/costs.md"), ("part", 1))],
            ["They fell [1], then rose [2]."]);

        var events = await CollectAsync(Ask("What happened to hosting costs?", SearchEffort.Thorough, folder.Id, reports.Id));

        Assert.StartsWith(
            "The user is chatting with 2 folders of their files: \"Docs\" (1 readable file) and \"Reports\" (1 readable file). File paths start with the folder's name",
            recorder.Requests[0].Messages[0].Text);
        var searched = (string)Assert.IsType<FunctionResultContent>(recorder.Requests[1].Messages[^1].Contents[0]).Result!;
        Assert.Contains("Docs/costs.md\nHosting costs fell", searched);
        Assert.Contains("Reports/costs.md\nHosting costs rose", searched);
        Assert.Equal("Read Reports/costs.md, part 1 of 1", events.OfType<ChatActivity>().Last().Text);

        var sources = Assert.IsType<ChatSources>(events[^2]).Sources;
        Assert.Equal(new[] { folder.Id, reports.Id }.Order(), sources.Select(s => s.FolderId).Order());
        await using var check = database.NewContext();
        Assert.Equal([folder.Id, reports.Id], (await check.Conversations.SingleAsync()).FolderIds);
    }

    [Fact]
    public async Task Folders_with_the_same_name_get_different_labels()
    {
        await IndexAsync(folder, ("a.md", "Alpha notes."));
        var other = await AddFolderAsync("Docs", ("b.md", "Bravo notes."));
        var recorder = Reply([Call("call-1", "list_files")], ["Two files."]);

        await CollectAsync(Ask("What's in here?", SearchEffort.Thorough, folder.Id, other.Id));

        Assert.Equal("- Docs/a.md\n- Docs (2)/b.md\n", (string)Assert.IsType<FunctionResultContent>(recorder.Requests[1].Messages[^1].Contents[0]).Result!);
    }

    [Fact]
    public async Task Removed_folders_are_left_out()
    {
        await IndexAsync(folder, ("costs.md", "Hosting costs fell 8%."));
        Reply(["Fell [1]."]);

        var events = await CollectAsync(Ask("hosting costs", SearchEffort.Quick, folder.Id, Guid.NewGuid()));

        Assert.IsType<ChatCompleted>(events[^1]);
        await using var check = database.NewContext();
        Assert.Equal([folder.Id], (await check.Conversations.SingleAsync()).FolderIds);
    }

    [Fact]
    public async Task Standard_gives_no_tools_for_reading_whole_files()
    {
        await IndexAsync(folder, ("costs.md", "Hosting costs fell 8%."));
        var recorder = Reply([Call("call-1", "read_file", ("path", "costs.md"), ("part", 1))], ["Done."]);

        await CollectAsync(Ask("Read it", SearchEffort.Standard));

        var result = (string)Assert.IsType<FunctionResultContent>(recorder.Requests[1].Messages[^1].Contents[0]).Result!;
        Assert.StartsWith("There's no tool called read_file.", result);
    }

    [Fact]
    public async Task Models_that_cant_call_tools_get_one_search_instead()
    {
        await IndexAsync(folder, ("costs.md", "Hosting costs fell 8%."));
        var recorder = new FakeChatClient
        {
            FailFirstRequest = new InvalidOperationException("registry.ollama.ai/library/gemma3:1b does not support tools"),
            Chunks = ["Costs fell [1]."],
        };
        clients.Build = (_, _) => recorder;

        var events = await CollectAsync(Ask("hosting costs?", SearchEffort.Standard));

        Assert.Equal(
            ["This model can't search by itself, so FolderChat searched once for it.", "Searched Docs: 1 passage"],
            events.OfType<ChatActivity>().Select(a => a.Text));
        Assert.IsType<ChatCompleted>(events[^1]);
        Assert.Null(recorder.Requests[1].Options);
        Assert.StartsWith("<folder_passages>", recorder.Requests[1].Messages[^1].Text);
    }

    [Fact]
    public async Task A_model_that_keeps_searching_is_stopped()
    {
        await IndexAsync(folder, ("costs.md", "Hosting costs fell 8%."));
        var recorder = Reply([Call("call", "search_folder", ("query", "costs"))]);

        var events = await CollectAsync(Ask("hosting costs?", SearchEffort.Standard));

        var maxRounds = EffortLimits.For(SearchEffort.Standard).MaxRounds;
        Assert.Equal(maxRounds, recorder.Requests.Count);
        Assert.Equal(ChatToolMode.None, recorder.Requests[^1].Options!.ToolMode);
        Assert.Equal(ChatToolMode.Auto, recorder.Requests[^2].Options!.ToolMode);
        Assert.Equal("The model kept searching without answering. Try asking more specifically.", Assert.IsType<ChatFailed>(events[^1]).Error);
        var lastResult = (string)Assert.IsType<FunctionResultContent>(recorder.Requests[^1].Messages[^1].Contents[0]).Result!;
        Assert.StartsWith("That's all 4 searches for this question.", lastResult);
    }

    [Fact]
    public async Task Missing_or_unread_folders_fail_before_anything_is_saved()
    {
        var missing = await CollectAsync(new ChatRequest(null, provider.Id, "model-a", "Hi", FolderIds: [Guid.NewGuid()]));
        scans.Progress = new ScanProgress(ScanPhase.Reading, 0, 10);
        var reading = await CollectAsync(Ask("Hi", SearchEffort.Quick));

        Assert.Equal("That folder isn't available anymore. Choose another, or none.", Assert.IsType<ChatFailed>(Assert.Single(missing)).Error);
        Assert.Equal("FolderChat is still reading Docs. Try again in a moment.", Assert.IsType<ChatFailed>(Assert.Single(reading)).Error);
        await using var check = database.NewContext();
        Assert.Empty(await check.Conversations.ToListAsync());
    }

    [Fact]
    public async Task Each_chat_keeps_its_own_folders()
    {
        await IndexAsync(folder, ("costs.md", "Hosting costs fell 8%."));
        Reply(["Fell [1]."]);
        var withFolder = Assert.IsType<ChatStarted>((await CollectAsync(Ask("hosting costs", SearchEffort.Quick)))[0]);
        var without = Assert.IsType<ChatStarted>((await CollectAsync(new ChatRequest(null, provider.Id, "model-a", "Hello")))[0]);

        var conversations = new ConversationService(new EfConversationStore(database.NewContext()), clock);
        var first = await conversations.GetAsync(withFolder.ConversationId);
        var second = await conversations.GetAsync(without.ConversationId);

        Assert.Equal([folder.Id], first.FolderIds);
        Assert.Equal(SearchEffort.Quick, first.Effort);
        Assert.Equal((1, "costs.md", folder.Id), (first.Messages[1].Sources[0].Number, first.Messages[1].Sources[0].Path, first.Messages[1].Sources[0].FolderId));
        Assert.Empty(second.FolderIds);

        // Folders can change without sending a message, and the chat doesn't move up the list.
        await new ConversationService(new EfConversationStore(db), clock).SetFoldersAsync(without.ConversationId, [folder.Id], SearchEffort.Thorough);
        var changed = await new ConversationService(new EfConversationStore(database.NewContext()), clock).GetAsync(without.ConversationId);
        Assert.Equal([folder.Id], changed.FolderIds);
        Assert.Equal((SearchEffort.Thorough, second.UpdatedAt), (changed.Effort, changed.UpdatedAt));
    }

    private ChatRequest Ask(string message, SearchEffort effort, params Guid[] folderIds) =>
        new(null, provider.Id, "model-a", message, FolderIds: folderIds.Length > 0 ? folderIds : [folder.Id], Effort: effort);

    private FakeChatClient Reply(params IReadOnlyList<object>[] rounds)
    {
        var client = new FakeChatClient { Rounds = rounds };
        clients.Build = (_, _) => client;
        return client;
    }

    private static FunctionCallContent Call(string id, string name, params (string Name, object Value)[] arguments) =>
        new(id, name, arguments.ToDictionary(a => a.Name, a => (object?)a.Value));

    /// <summary>A knowledge folder on disk, saved and, when given files, scanned.</summary>
    [Fact]
    public async Task Token_counts_add_up_over_rounds_and_the_last_round_shows_how_full_the_context_is()
    {
        await IndexAsync(folder, ("costs.md", "Hosting costs fell 8% after the move."));
        Reply(
            [
                Call("call-1", "search_folder", ("query", "hosting costs")),
                new UsageContent(new UsageDetails { InputTokenCount = 100, OutputTokenCount = 10, CachedInputTokenCount = 40 }),
            ],
            [
                "Hosting costs fell 8% [1].",
                new UsageContent(new UsageDetails { InputTokenCount = 150, OutputTokenCount = 20, ReasoningTokenCount = 5 }),
            ]);

        var events = await CollectAsync(Ask("What happened to hosting costs?", SearchEffort.Standard));

        var completed = Assert.IsType<ChatCompleted>(events[^1]);
        Assert.Equal((250L, 30L, 40L, 5L, 170L), (completed.InputTokens, completed.OutputTokens, completed.CachedInputTokens, completed.ReasoningTokens, completed.ContextTokens));
        await using var check = database.NewContext();
        var saved = await check.Messages.SingleAsync(m => m.Role == MessageRole.Assistant);
        Assert.Equal((250L, 30L, 40L, 5L, 170L), (saved.InputTokens, saved.OutputTokens, saved.CachedInputTokens, saved.ReasoningTokens, saved.ContextTokens));
    }

    private async Task<KnowledgeFolder> AddFolderAsync(string name, params (string Path, string Text)[] contents)
    {
        var files = new TempFolder();
        tempFolders.Add(files);
        var added = new KnowledgeFolder { Name = name, Path = files.Path, CreatedAt = clock.GetUtcNow().UtcDateTime };
        db.KnowledgeFolders.Add(added);
        await db.SaveChangesAsync();
        if (contents.Length > 0)
        {
            await IndexAsync(added, contents);
        }

        return added;
    }

    private async Task IndexAsync(KnowledgeFolder target, params (string Path, string Text)[] contents)
    {
        var files = tempFolders.Single(f => f.Path == target.Path);
        foreach (var (path, text) in contents)
        {
            files.Write(path, text);
        }

        var scanner = new FolderScanner(
            db,
            new TextExtractor(NullLogger<TextExtractor>.Instance),
            new FileSummarizer(new EfProviderStore(db), clients, new MessageErrorDescriber()),
            new AppPaths(dataFolder.Path),
            clock,
            NullLogger<FolderScanner>.Instance);
        await scanner.ScanAsync(target.Id, _ => { }, CancellationToken.None);
    }

    private ChatService CreateService() => new(
        new EfProviderStore(db),
        new EfConversationStore(db),
        new EfAttachmentStore(db),
        new FolderAnswering(new EfKnowledgeStore(db), new KnowledgeIndex(db), scans),
        clients,
        new MessageErrorDescriber(),
        clock,
        NullLogger<ChatService>.Instance);

    private async Task<List<ChatStreamEvent>> CollectAsync(ChatRequest request)
    {
        clock.Advance(TimeSpan.FromSeconds(1));
        var events = new List<ChatStreamEvent>();
        await foreach (var chatEvent in CreateService().StreamAsync(request))
        {
            events.Add(chatEvent);
        }

        return events;
    }
}
