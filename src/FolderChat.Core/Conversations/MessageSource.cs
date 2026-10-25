namespace FolderChat.Core.Conversations;

/// <summary>A passage from a knowledge folder that a reply was given. Replies cite it by number, like [2].</summary>
public sealed class MessageSource
{
    public int Number { get; set; }

    public Guid FolderId { get; set; }

    public Guid FileId { get; set; }

    /// <summary>A copy of the file's path inside the folder, so the citation still reads well if the file goes.</summary>
    public required string Path { get; set; }

    public int? Page { get; set; }

    /// <summary>The cited passage. Re-reading a changed file replaces its passages, so this may no longer exist.</summary>
    public long PassageId { get; set; }
}
