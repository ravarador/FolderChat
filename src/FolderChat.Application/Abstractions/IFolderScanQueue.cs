namespace FolderChat.Application.Abstractions;

/// <summary>
/// Scans knowledge folders in the background, one at a time: reads new and changed files, forgets deleted ones,
/// and writes summaries when they're switched on.
/// </summary>
public interface IFolderScanQueue
{
    /// <summary>Asks for the folder to be scanned soon. A folder already waiting isn't queued twice.</summary>
    void Enqueue(Guid folderId);

    /// <summary>What the folder's scan is doing, or null when it isn't waiting or running.</summary>
    ScanProgress? GetProgress(Guid folderId);

    /// <summary>Stops the folder's scan, if one is running or waiting. Call before removing the folder.</summary>
    void Cancel(Guid folderId);
}

/// <param name="Done">Files handled so far in this phase.</param>
/// <param name="Total">Files to handle in this phase. Zero while the folder is being listed.</param>
public sealed record ScanProgress(ScanPhase Phase, int Done, int Total);

public enum ScanPhase
{
    /// <summary>Another folder is being scanned first.</summary>
    Waiting = 1,

    /// <summary>Reading new and changed files.</summary>
    Reading = 2,

    /// <summary>Writing summaries of files that don't have one.</summary>
    Summarizing = 3,
}
