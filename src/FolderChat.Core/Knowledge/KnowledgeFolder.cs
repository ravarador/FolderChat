namespace FolderChat.Core.Knowledge;

/// <summary>A folder on this computer that FolderChat reads and indexes, so chats can answer from the files in it.</summary>
public sealed class KnowledgeFolder
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Name { get; set; }

    /// <summary>The folder's full path on this computer.</summary>
    public required string Path { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>When the last scan finished.</summary>
    public DateTime? LastScannedAt { get; set; }

    /// <summary>Why the last scan, or its summaries, stopped early. Null when it went fine.</summary>
    public string? ScanError { get; set; }

    /// <summary>When true, a model writes a short summary of each file. Thorough answers use them to pick files.</summary>
    public bool SummariesEnabled { get; set; }

    /// <summary>The provider of the model that writes summaries.</summary>
    public Guid? SummaryProviderId { get; set; }

    /// <summary>The model that writes summaries.</summary>
    public string? SummaryModelId { get; set; }

    /// <summary>Tokens the summary model has read for this folder, over all its summaries so far.</summary>
    public long SummaryInputTokens { get; set; }

    /// <summary>Tokens the summary model has written for this folder.</summary>
    public long SummaryOutputTokens { get; set; }

    public List<KnowledgeFile> Files { get; set; } = [];
}
