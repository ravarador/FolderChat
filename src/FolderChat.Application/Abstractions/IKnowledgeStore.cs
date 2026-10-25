using FolderChat.Core.Knowledge;

namespace FolderChat.Application.Abstractions;

/// <summary>Loads and saves knowledge folders and their files. Entities it returns are tracked, so edits persist on <see cref="SaveChangesAsync"/>.</summary>
public interface IKnowledgeStore
{
    /// <summary>All folders, without their files.</summary>
    Task<List<KnowledgeFolder>> ListAsync(CancellationToken ct = default);

    /// <summary>Finds a folder, without its files.</summary>
    Task<KnowledgeFolder?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>How many files each folder has, by folder ID. Folders with no files are left out.</summary>
    Task<Dictionary<Guid, FolderCounts>> CountFilesAsync(CancellationToken ct = default);

    /// <summary>The folder's files, ordered by path. They aren't tracked: changes to them aren't saved.</summary>
    Task<List<KnowledgeFile>> ListFilesAsync(Guid folderId, CancellationToken ct = default);

    Task<KnowledgeFile?> FindFileAsync(Guid fileId, CancellationToken ct = default);

    void Add(KnowledgeFolder folder);

    /// <summary>Deletes the folder with its files and their passages. Runs straight away.</summary>
    Task RemoveAsync(Guid folderId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <param name="Indexed">Files whose text can be searched.</param>
/// <param name="Skipped">Files that couldn't be read.</param>
/// <param name="Summarized">Searchable files that have a summary.</param>
public sealed record FolderCounts(int Indexed, int Skipped, int Summarized);
