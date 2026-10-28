using FolderChat.Application.Conversations;
using FolderChat.Core.Conversations;
using FolderChat.Infrastructure.Persistence;
using FolderChat.Tests.Fakes;

namespace FolderChat.Tests.Application;

public sealed class UsageTests : IAsyncLifetime
{
    // 02:00 on November 1st in Tokyo is still October 31st in UTC, so the month starts in Tokyo's time, not UTC's.
    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
    private readonly ManualClock clock = new(new DateTimeOffset(2026, 11, 1, 2, 0, 0, TimeSpan.FromHours(9)), Tokyo);
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
    }

    [Fact]
    public async Task Usage_adds_up_this_months_replies_by_model()
    {
        var conversation = new Conversation { Title = "Chat", CreatedAt = Utc(2026, 10, 1), UpdatedAt = Utc(2026, 10, 1) };
        db.Conversations.Add(conversation);
        var sequence = 0;
        void Add(MessageRole role, string? model, long? input, long? output, DateTime createdAt, long? cached = null, long? reasoning = null) =>
            db.Messages.Add(new ConversationMessage
            {
                ConversationId = conversation.Id,
                Sequence = ++sequence,
                Role = role,
                Content = "…",
                ProviderName = model is null ? null : "Anthropic",
                ModelId = model,
                InputTokens = input,
                OutputTokens = output,
                CachedInputTokens = cached,
                ReasoningTokens = reasoning,
                CreatedAt = createdAt,
            });

        var startOfMonth = new DateTime(2026, 10, 31, 15, 0, 0, DateTimeKind.Utc); // midnight, November 1st, in Tokyo
        Add(MessageRole.Assistant, "claude-a", 1000, 100, startOfMonth.AddMinutes(-1)); // last month
        Add(MessageRole.User, null, null, null, startOfMonth);
        Add(MessageRole.Assistant, "claude-a", 200, 20, startOfMonth, cached: 50);
        Add(MessageRole.Assistant, "claude-a", 300, 30, startOfMonth.AddMinutes(30), reasoning: 7);
        Add(MessageRole.Assistant, "claude-b", 10, 1, startOfMonth.AddMinutes(40));
        Add(MessageRole.Assistant, "claude-b", null, null, startOfMonth.AddMinutes(50)); // reported nothing
        await db.SaveChangesAsync();

        var report = await new ConversationService(new EfConversationStore(db), clock).GetUsageAsync();

        Assert.Equal(startOfMonth, report.Since);
        Assert.Equal(
            [new UsageRowDto("Anthropic", "claude-a", 2, 500, 50, 50, 7), new UsageRowDto("Anthropic", "claude-b", 1, 10, 1, 0, 0)],
            report.Models);
    }

    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);
}
