namespace FolderChat.Core.Conversations;

/// <summary>
/// A file attached to a user's message. FolderChat keeps the text it read from the file, not the file itself,
/// and sends that text to the model with the message.
/// </summary>
public sealed class Attachment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The message the file was sent with. Null while the file waits to be sent.</summary>
    public Guid? MessageId { get; set; }

    /// <summary>Order within the message, starting at 1.</summary>
    public int Position { get; set; }

    public required string FileName { get; set; }

    /// <summary>How the file was read, such as <c>application/pdf</c> or <c>text/plain</c>.</summary>
    public required string MediaType { get; set; }

    /// <summary>The file's size in bytes.</summary>
    public long Size { get; set; }

    /// <summary>The text read from the file. This is what the model sees.</summary>
    public required string Text { get; set; }

    /// <summary>True when the file held more text than FolderChat keeps, so <see cref="Text"/> is only its start.</summary>
    public bool IsTruncated { get; set; }

    /// <summary>The number of pages, for PDFs.</summary>
    public int? PageCount { get; set; }

    public DateTime CreatedAt { get; set; }
}
