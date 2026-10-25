namespace FolderChat.Core.Knowledge;

/// <summary>
/// A piece of a file's text, a paragraph or a few, small enough to send to a model. Passages are what a folder
/// search finds and what replies cite.
/// </summary>
public sealed class Passage
{
    /// <summary>Numbered by the database, because the full-text index refers to passages by an integer row ID.</summary>
    public long Id { get; set; }

    public Guid FileId { get; set; }

    /// <summary>Position in the file, starting at 1.</summary>
    public int Ordinal { get; set; }

    /// <summary>The page the passage is on, for files with pages.</summary>
    public int? PageNumber { get; set; }

    /// <summary>Where the passage starts in the file's text.</summary>
    public int StartOffset { get; set; }

    public required string Text { get; set; }
}
