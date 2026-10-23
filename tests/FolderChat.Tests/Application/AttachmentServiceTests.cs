using System.Text;
using FolderChat.Application.Attachments;
using FolderChat.Application.Common;
using FolderChat.Core.Conversations;
using FolderChat.Infrastructure.Documents;
using FolderChat.Infrastructure.Persistence;
using FolderChat.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FolderChat.Tests.Application;

public sealed class AttachmentServiceTests : IAsyncLifetime
{
    private readonly ManualClock clock = new(new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.Zero));
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
    public async Task Uploads_are_read_and_kept_until_sent()
    {
        var summary = await UploadAsync("notes.md", "# Notes\r\nCall Sam.");

        Assert.Equal(("notes.md", MediaTypes.PlainText, 18L, 17, false), (summary.FileName, summary.MediaType, summary.Size, summary.CharacterCount, summary.IsTruncated));
        await using var check = database.NewContext();
        var saved = await check.Attachments.SingleAsync();
        Assert.Null(saved.MessageId);
        Assert.Equal("# Notes\nCall Sam.", saved.Text);
    }

    [Fact]
    public async Task The_text_can_be_read_back()
    {
        var summary = await UploadAsync("notes.txt", "Call Sam.");

        var attachment = await Service().GetAsync(summary.Id);

        Assert.Equal("Call Sam.", attachment.Text);
        Assert.False(attachment.IsSent);
    }

    [Fact]
    public async Task Empty_files_are_refused()
    {
        var error = await Assert.ThrowsAsync<ValidationFailedException>(() => UploadAsync("empty.txt", ""));

        Assert.Equal("empty.txt is empty.", error.Message);
    }

    [Fact]
    public async Task Files_with_only_blank_lines_are_refused()
    {
        var error = await Assert.ThrowsAsync<ValidationFailedException>(() => UploadAsync("blank.txt", "\n \n"));

        Assert.Equal("blank.txt has no text in it.", error.Message);
    }

    [Fact]
    public async Task Files_over_the_size_limit_are_refused()
    {
        using var content = new MemoryStream(new byte[AttachmentService.MaxFileSize + 1]);

        var error = await Assert.ThrowsAsync<ValidationFailedException>(() => Service().UploadAsync("huge.log", content));

        Assert.Equal("huge.log is larger than 20 MB.", error.Message);
    }

    [Fact]
    public async Task Pdfs_without_text_get_a_hint_about_scans()
    {
        using var content = new MemoryStream(TestFiles.Pdf([]));

        var error = await Assert.ThrowsAsync<ValidationFailedException>(() => Service().UploadAsync("scan.pdf", content));

        Assert.Contains("If it's a scan", error.Message);
    }

    [Fact]
    public async Task Unsent_files_can_be_removed_but_sent_ones_cannot()
    {
        var service = Service();
        var unsent = await UploadAsync("draft.txt", "Draft");
        var sent = await UploadAsync("final.txt", "Final");
        await MarkSentAsync(sent.Id);

        await service.DeleteAsync(unsent.Id);
        var error = await Assert.ThrowsAsync<ValidationFailedException>(() => service.DeleteAsync(sent.Id));

        Assert.Contains("Delete the conversation", error.Message);
        await using var check = database.NewContext();
        Assert.Equal([sent.Id], await check.Attachments.Select(a => a.Id).ToListAsync());
    }

    [Fact]
    public async Task Files_never_sent_are_deleted_a_day_later()
    {
        var forgotten = await UploadAsync("forgotten.txt", "Old");
        var sent = await UploadAsync("sent.txt", "Kept");
        await MarkSentAsync(sent.Id);

        clock.Advance(TimeSpan.FromHours(25));
        var fresh = await UploadAsync("fresh.txt", "New");

        await using var check = database.NewContext();
        var left = await check.Attachments.Select(a => a.Id).ToListAsync();
        Assert.Equal(new[] { sent.Id, fresh.Id }.Order(), left.Order());
        Assert.DoesNotContain(forgotten.Id, left);
    }

    [Theory]
    [InlineData(@"C:\Users\sam\Documents\report.pdf", "report.pdf")]
    [InlineData("../../notes.txt", "notes.txt")]
    [InlineData("  spaced.txt  ", "spaced.txt")]
    [InlineData("", "Untitled")]
    public void File_names_lose_any_folder(string fileName, string expected) =>
        Assert.Equal(expected, AttachmentService.CleanFileName(fileName));

    [Fact]
    public void Long_file_names_keep_their_extension()
    {
        var name = AttachmentService.ShortenFileName(new string('a', 300) + ".pdf");

        Assert.Equal(255, name.Length);
        Assert.EndsWith("aaa….pdf", name);
    }

    private AttachmentService Service() =>
        new(new EfAttachmentStore(db), new TextExtractor(NullLogger<TextExtractor>.Instance), clock);

    private async Task<AttachmentSummaryDto> UploadAsync(string fileName, string text)
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return await Service().UploadAsync(fileName, content);
    }

    /// <summary>Links the attachment to a message, as sending it does.</summary>
    private async Task MarkSentAsync(Guid attachmentId)
    {
        var conversation = new Conversation { Title = "Chat", CreatedAt = clock.GetUtcNow().UtcDateTime };
        var message = new ConversationMessage
        {
            ConversationId = conversation.Id,
            Sequence = 1,
            Content = "Here",
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.Conversations.Add(conversation);
        db.Messages.Add(message);
        (await db.Attachments.SingleAsync(a => a.Id == attachmentId)).MessageId = message.Id;
        await db.SaveChangesAsync();
    }
}
