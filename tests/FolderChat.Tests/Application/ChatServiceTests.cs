using FolderChat.Application.Chat;
using FolderChat.Core.Conversations;
using FolderChat.Core.Providers;
using FolderChat.Infrastructure.Persistence;
using FolderChat.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace FolderChat.Tests.Application;

public sealed class ChatServiceTests : IAsyncLifetime
{
    private readonly FakeChatClientFactory clients = new();
    private readonly ManualClock clock = new(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));
    private TestDatabase database = null!;
    private FolderChatDbContext db = null!;
    private Provider provider = null!;

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
            Models = [new ProviderModel { ModelId = "model-a", MaxOutputTokens = 4096 }],
        };
        db.Providers.Add(provider);
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await db.DisposeAsync();
        await database.DisposeAsync();
    }

    [Fact]
    public async Task New_conversation_streams_the_reply_and_saves_both_messages()
    {
        clients.Build = (_, _) => new FakeChatClient
        {
            Chunks = ["Hel", "lo!"],
            Usage = new UsageDetails { InputTokenCount = 12, OutputTokenCount = 3 },
        };

        var events = await CollectAsync(Request(null, "Say hello\nplease"));

        var started = Assert.IsType<ChatStarted>(events[0]);
        Assert.True(started.IsNewConversation);
        Assert.Equal("Say hello", started.Title);
        Assert.Equal(["Hel", "lo!"], events.OfType<ChatDelta>().Select(d => d.Text));
        var completed = Assert.IsType<ChatCompleted>(events[^1]);
        Assert.Equal(12, completed.InputTokens);
        Assert.Equal(3, completed.OutputTokens);
        Assert.Equal("stop", completed.FinishReason);

        await using var check = database.NewContext();
        var conversation = await check.Conversations.Include(c => c.Messages).SingleAsync();
        Assert.Equal(provider.Id, conversation.ProviderId);
        Assert.Equal("model-a", conversation.ModelId);
        var messages = conversation.Messages.OrderBy(m => m.Sequence).ToList();
        Assert.Equal([MessageRole.User, MessageRole.Assistant], messages.Select(m => m.Role));
        Assert.Equal("Say hello\nplease", messages[0].Content);
        Assert.Equal("Hello!", messages[1].Content);
        Assert.Equal(MessageStatus.Complete, messages[1].Status);
        Assert.Equal("Local test", messages[1].ProviderName);
        Assert.Equal(3, messages[1].OutputTokens);
    }

    [Fact]
    public async Task Continuing_a_conversation_sends_earlier_turns_in_order()
    {
        var first = await CollectAsync(Request(null, "First question"));
        var conversationId = Assert.IsType<ChatStarted>(first[0]).ConversationId;

        var recorder = new FakeChatClient { Chunks = ["Second answer"] };
        clients.Build = (_, _) => recorder;
        var second = await CollectAsync(Request(conversationId, "Second question"));

        Assert.False(Assert.IsType<ChatStarted>(second[0]).IsNewConversation);
        Assert.Equal(
            [(ChatRole.User, "First question"), (ChatRole.Assistant, "Hello world"), (ChatRole.User, "Second question")],
            recorder.ReceivedMessages.Select(m => (m.Role, m.Text)));
        Assert.True(recorder.Disposed);

        await using var check = database.NewContext();
        var sequences = await check.Messages.OrderBy(m => m.Sequence).Select(m => m.Sequence).ToListAsync();
        Assert.Equal([1, 2, 3, 4], sequences);
    }

    [Fact]
    public async Task Provider_errors_are_reported_and_saved_as_failed()
    {
        clients.Build = (_, _) => new FakeChatClient { Chunks = ["Part"], FailAfterChunks = new InvalidOperationException("quota exceeded") };

        var events = await CollectAsync(Request(null, "Hi"));

        var failed = Assert.IsType<ChatFailed>(events[^1]);
        Assert.Equal("quota exceeded", failed.Error);
        Assert.NotNull(failed.MessageId);

        await using var check = database.NewContext();
        var reply = await check.Messages.SingleAsync(m => m.Role == MessageRole.Assistant);
        Assert.Equal(MessageStatus.Failed, reply.Status);
        Assert.Equal("quota exceeded", reply.Error);
    }

    [Fact]
    public async Task Stopping_mid_reply_keeps_what_arrived_as_a_stopped_message()
    {
        clients.Build = (_, _) => new FakeChatClient { Chunks = ["Partial ", "answer"], HangAfterChunks = true };
        using var stop = new CancellationTokenSource();
        var received = new List<ChatStreamEvent>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var chatEvent in CreateService().StreamAsync(Request(null, "Tell me a story"), stop.Token))
            {
                received.Add(chatEvent);
                if (received.OfType<ChatDelta>().Count() == 2)
                {
                    await stop.CancelAsync();
                }
            }
        });

        await using var check = database.NewContext();
        var reply = await check.Messages.SingleAsync(m => m.Role == MessageRole.Assistant);
        Assert.Equal(MessageStatus.Stopped, reply.Status);
        Assert.Equal("Partial answer", reply.Content);
    }

    [Fact]
    public async Task Unknown_provider_fails_without_saving_anything()
    {
        var events = await CollectAsync(new ChatRequest(null, Guid.NewGuid(), "model-a", "Hi"));

        Assert.IsType<ChatFailed>(Assert.Single(events));
        await using var check = database.NewContext();
        Assert.Empty(await check.Conversations.ToListAsync());
    }

    [Fact]
    public async Task A_refusal_with_no_text_is_reported_as_declined()
    {
        clients.Build = (_, _) => new FakeChatClient { Chunks = [], FinishReason = ChatFinishReason.ContentFilter };

        var events = await CollectAsync(Request(null, "Something it won't answer"));

        Assert.Equal("The model declined to answer this request.", Assert.IsType<ChatFailed>(events[^1]).Error);
    }

    [Fact]
    public async Task Models_not_in_the_saved_list_still_work()
    {
        var events = await CollectAsync(new ChatRequest(null, provider.Id, "typed-by-hand", "Hi"));

        Assert.IsType<ChatCompleted>(events[^1]);
        Assert.Equal("typed-by-hand", clients.Calls.Single().Model.ModelId);
    }

    [Fact]
    public void History_leaves_out_failed_replies_and_follows_sequence()
    {
        var history = ChatService.BuildHistory(
        [
            new ConversationMessage { Sequence = 3, Role = MessageRole.User, Content = "Retry" },
            new ConversationMessage { Sequence = 1, Role = MessageRole.User, Content = "First" },
            new ConversationMessage { Sequence = 2, Role = MessageRole.Assistant, Content = "", Status = MessageStatus.Failed },
        ]);

        Assert.Equal(["First", "Retry"], history.Select(m => m.Text));
    }

    [Theory]
    [InlineData("Short question", "Short question")]
    [InlineData("  spaced    out\nsecond line", "spaced out")]
    [InlineData("\n\n", "New chat")]
    public void Titles_come_from_the_first_line(string message, string expected) =>
        Assert.Equal(expected, ChatService.MakeTitle(message));

    [Fact]
    public void Long_titles_are_shortened()
    {
        var title = ChatService.MakeTitle(new string('x', 200));

        Assert.Equal(60, title.Length);
        Assert.EndsWith("…", title);
    }

    private ChatRequest Request(Guid? conversationId, string message) =>
        new(conversationId, provider.Id, "model-a", message);

    private ChatService CreateService() => new(
        new EfProviderStore(db),
        new EfConversationStore(db),
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
