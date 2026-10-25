namespace FolderChat.Core.Knowledge;

/// <summary>A file found in a knowledge folder, and what FolderChat made of it.</summary>
public sealed class KnowledgeFile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FolderId { get; set; }

    /// <summary>The path inside the folder, with forward slashes, such as <c>2026/contract.pdf</c>.</summary>
    public required string RelativePath { get; set; }

    public long Size { get; set; }

    /// <summary>The file's last-modified time when it was read. A different time on the next scan means it changed.</summary>
    public DateTime LastWriteTimeUtc { get; set; }

    public KnowledgeFileStatus Status { get; set; }

    /// <summary>Why the file was skipped, in words for the user.</summary>
    public string? Note { get; set; }

    /// <summary>How the file was read, such as <c>application/pdf</c>. Null for skipped files.</summary>
    public string? MediaType { get; set; }

    public int? PageCount { get; set; }

    /// <summary>The length of the text read from the file.</summary>
    public int CharacterCount { get; set; }

    /// <summary>True when the file held more text than FolderChat indexes, so only its start can be found.</summary>
    public bool IsTruncated { get; set; }

    /// <summary>A short Markdown summary, when the folder has summaries switched on.</summary>
    public string? Summary { get; set; }

    public DateTime IndexedAt { get; set; }
}

public enum KnowledgeFileStatus
{
    /// <summary>The file's text is searchable.</summary>
    Indexed = 1,

    /// <summary>The file couldn't be read, or wasn't a type FolderChat reads. <see cref="KnowledgeFile.Note"/> says why.</summary>
    Skipped = 2,
}
