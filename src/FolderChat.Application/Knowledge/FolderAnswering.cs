using FolderChat.Application.Abstractions;
using FolderChat.Core.Knowledge;

namespace FolderChat.Application.Knowledge;

/// <summary>Lets chats answer from knowledge folders.</summary>
public sealed class FolderAnswering(IKnowledgeStore store, IKnowledgeIndex index, IFolderScanQueue scans)
{
    /// <summary>The most folders one chat can answer from.</summary>
    public const int MaxFolders = 10;

    /// <summary>
    /// Gets a chat's folders ready for one reply. Folders that were removed are left out. Returns a message saying why
    /// instead, when none of the folders can be used yet.
    /// </summary>
    public async Task<(FolderSession? Session, string? Error)> StartAsync(
        IReadOnlyList<Guid> folderIds,
        SearchEffort effort,
        CancellationToken ct = default)
    {
        var all = await store.ListAsync(ct);
        var folders = folderIds.Distinct().Select(id => all.FirstOrDefault(f => f.Id == id)).OfType<KnowledgeFolder>().ToList();
        if (folders.Count == 0)
        {
            return (null, folderIds.Count == 1
                ? "That folder isn't available anymore. Choose another, or none."
                : "Those folders aren't available anymore. Choose others, or none.");
        }

        var counts = await store.CountFilesAsync(ct);
        var fileCounts = folders.ToDictionary(f => f.Id, f => counts.GetValueOrDefault(f.Id)?.Indexed ?? 0);
        if (fileCounts.Values.Sum() == 0)
        {
            var names = Names(folders);
            return (null, folders.Any(f => scans.GetProgress(f.Id) is not null)
                ? $"FolderChat is still reading {names}. Try again in a moment."
                : $"FolderChat hasn't found any files it can read in {names}. Check {(folders.Count == 1 ? "it" : "them")} in Settings.");
        }

        var level = Enum.IsDefined(effort) ? effort : SearchEffort.Standard;
        return (new FolderSession(folders, fileCounts, level, store, index), null);
    }

    private static string Names(List<KnowledgeFolder> folders) =>
        folders.Count == 1 ? folders[0].Name
        : folders.Count <= 3 ? $"{string.Join(", ", folders.Take(folders.Count - 1).Select(f => f.Name))} and {folders[^1].Name}"
        : $"these {folders.Count} folders";
}
