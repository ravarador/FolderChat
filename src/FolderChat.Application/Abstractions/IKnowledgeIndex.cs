namespace FolderChat.Application.Abstractions;

/// <summary>Searches and reads the indexed text of knowledge folders.</summary>
public interface IKnowledgeIndex
{
    /// <summary>
    /// Passages in any of the folders that contain the words in <paramref name="words"/>, best match first. The text
    /// can be a question as typed: punctuation and search syntax are ignored, and common words like "the" don't count.
    /// No single file takes more than a few of the places.
    /// </summary>
    Task<IReadOnlyList<FoundPassage>> SearchAsync(IReadOnlyCollection<Guid> folderIds, string words, int limit, CancellationToken ct = default);

    Task<FoundPassage?> GetPassageAsync(long passageId, CancellationToken ct = default);

    /// <summary>
    /// One part of a file's text: a page for files with pages, otherwise a slice of about <paramref name="sliceLength"/>
    /// characters. Null when the folder has no such file, or the file has no such part.
    /// </summary>
    Task<FilePart?> ReadAsync(Guid folderId, string path, int part, int sliceLength, CancellationToken ct = default);
}

/// <param name="Path">The file's path inside its folder.</param>
public sealed record FoundPassage(long Id, Guid FolderId, Guid FileId, string Path, int? Page, string Text);

/// <param name="Part">The page number for files with pages, otherwise the slice number. Starts at 1.</param>
/// <param name="PartCount">How many pages or slices the file has.</param>
/// <param name="IsPage">True when <paramref name="Part"/> is a page number.</param>
/// <param name="FirstPassageId">The first passage in the part, so a reply can cite the part. Zero when the part has no text.</param>
public sealed record FilePart(Guid FolderId, Guid FileId, string Path, int Part, int PartCount, bool IsPage, long FirstPassageId, string Text);
