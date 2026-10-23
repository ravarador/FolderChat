using FolderChat.Application.Abstractions;
using FolderChat.Application.Common;
using FolderChat.Core.Conversations;

namespace FolderChat.Application.Attachments;

/// <summary>
/// Reads the files the user attaches and keeps their text until the message is sent. The chat links them to the
/// message; files that are never sent are deleted a day later.
/// </summary>
public sealed class AttachmentService(IAttachmentStore store, ITextExtractor extractor, TimeProvider clock)
{
    /// <summary>The largest file that can be attached, in bytes: 20 MB.</summary>
    public const int MaxFileSize = 20 * 1024 * 1024;

    /// <summary>
    /// The most text kept from one file: about 125,000 tokens of English. Longer files are cut off,
    /// and both the user and the model are told.
    /// </summary>
    public const int MaxTextLength = 500_000;

    private const int MaxFileNameLength = 255;

    /// <summary>How long an attached file waits to be sent before it is deleted.</summary>
    internal static readonly TimeSpan UnsentLifetime = TimeSpan.FromDays(1);

    /// <summary>Reads the file's text and saves it, ready to be sent with a message.</summary>
    public async Task<AttachmentSummaryDto> UploadAsync(string fileName, Stream content, CancellationToken ct = default)
    {
        var name = CleanFileName(fileName);
        var bytes = await ReadAllAsync(content, name, ct);
        if (bytes.Length == 0)
        {
            throw new ValidationFailedException($"{name} is empty.");
        }

        var extracted = extractor.Extract(name, bytes, MaxTextLength, ct);
        if (string.IsNullOrWhiteSpace(extracted.Text))
        {
            throw new ValidationFailedException(extracted.MediaType == MediaTypes.Pdf
                ? $"{name} has no text FolderChat can read. If it's a scan, its pages are pictures of text, which FolderChat can't read yet."
                : $"{name} has no text in it.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        await store.RemoveUnsentAsync(now - UnsentLifetime, ct);

        var attachment = new Attachment
        {
            FileName = ShortenFileName(name),
            MediaType = extracted.MediaType,
            Size = bytes.Length,
            Text = extracted.Text,
            IsTruncated = extracted.IsTruncated,
            PageCount = extracted.PageCount,
            CreatedAt = now,
        };
        store.Add(attachment);
        await store.SaveChangesAsync(ct);
        return ToSummary(attachment);
    }

    /// <summary>The attachment with its text, so the user can see exactly what the model is sent.</summary>
    public async Task<AttachmentDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var a = await FindAsync(id, ct);
        return new AttachmentDto(a.Id, a.FileName, a.MediaType, a.Size, a.Text.Length, a.IsTruncated, a.PageCount, a.MessageId is not null, a.Text);
    }

    /// <summary>Removes a file that hasn't been sent yet. Sent files go when their conversation is deleted.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var attachment = await FindAsync(id, ct);
        if (attachment.MessageId is not null)
        {
            throw new ValidationFailedException("That file was sent with a message. Delete the conversation to remove it.");
        }

        store.Remove(attachment);
        await store.SaveChangesAsync(ct);
    }

    internal static AttachmentSummaryDto ToSummary(Attachment a) =>
        new(a.Id, a.FileName, a.MediaType, a.Size, a.Text.Length, a.IsTruncated, a.PageCount);

    /// <summary>Just the file's name: browsers send no folder, but some older ones sent a full Windows path.</summary>
    internal static string CleanFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? "").Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return name.Length == 0 ? "Untitled" : name;
    }

    /// <summary>Fits a name in the database, keeping its extension.</summary>
    internal static string ShortenFileName(string name)
    {
        if (name.Length <= MaxFileNameLength)
        {
            return name;
        }

        var extension = Path.GetExtension(name);
        if (extension.Length > 20)
        {
            extension = "";
        }

        return name[..(MaxFileNameLength - extension.Length - 1)].TrimEnd() + "…" + extension;
    }

    private async Task<Attachment> FindAsync(Guid id, CancellationToken ct) =>
        await store.FindAsync(id, ct) ?? throw new NotFoundException("That file doesn't exist anymore.");

    /// <summary>Reads the whole file, but stops as soon as it is over the size limit.</summary>
    private static async Task<byte[]> ReadAllAsync(Stream content, string name, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxFileSize)
            {
                throw new ValidationFailedException($"{name} is larger than {MaxFileSize / (1024 * 1024)} MB.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
