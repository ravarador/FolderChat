using FolderChat.Application.Abstractions;
using FolderChat.Application.Common;
using FolderChat.Core.Knowledge;

namespace FolderChat.Application.Knowledge;

/// <summary>Adds, changes and removes knowledge folders, and shows what was found in them.</summary>
public sealed class FolderService(
    IKnowledgeStore store,
    IKnowledgeIndex index,
    IProviderStore providers,
    IFolderScanQueue scans,
    TimeProvider clock)
{
    public const int MaxNameLength = 100;
    private const int MaxPathLength = 1024;

    public async Task<IReadOnlyList<FolderDto>> ListAsync(CancellationToken ct = default)
    {
        var folders = await store.ListAsync(ct);
        var counts = await store.CountFilesAsync(ct);
        return folders
            .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(f => ToDto(f, counts.GetValueOrDefault(f.Id)))
            .ToList();
    }

    public async Task<FolderDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var folder = await FindAsync(id, ct);
        var counts = await store.CountFilesAsync(ct);
        return ToDto(folder, counts.GetValueOrDefault(folder.Id));
    }

    /// <summary>Adds the folder and starts reading it in the background.</summary>
    public async Task<FolderDto> AddAsync(AddFolderRequest request, CancellationToken ct = default)
    {
        var path = NormalizePath(request.Path);
        if (await FindByPathAsync(path, ct) is not null)
        {
            throw new ValidationFailedException("That folder is already added.");
        }

        var name = string.IsNullOrWhiteSpace(request.Name) ? DefaultName(path) : ValidateName(request.Name);
        return await SaveNewAsync(path, name, ct);
    }

    /// <summary>
    /// The folder the user chose in the computer's folder window: added and read if it's new, or the one already added,
    /// since choosing a folder again just means wanting to use it.
    /// </summary>
    public async Task<ChosenFolderDto> AddChosenAsync(string path, CancellationToken ct = default)
    {
        path = NormalizePath(path);
        return await FindByPathAsync(path, ct) is { } existing
            ? new ChosenFolderDto(await GetAsync(existing.Id, ct), Added: false)
            : new ChosenFolderDto(await SaveNewAsync(path, DefaultName(path), ct), Added: true);
    }

    /// <summary>Renames the folder and changes its summary settings. Turning summaries on writes the missing ones.</summary>
    public async Task<FolderDto> UpdateAsync(Guid id, UpdateFolderRequest request, CancellationToken ct = default)
    {
        var folder = await FindAsync(id, ct);
        folder.Name = ValidateName(request.Name);

        if (request.SummariesEnabled)
        {
            var modelId = request.SummaryModelId?.Trim();
            var provider = request.SummaryProviderId is { } providerId ? await providers.FindAsync(providerId, ct) : null;
            if (provider is null || string.IsNullOrEmpty(modelId))
            {
                throw new ValidationFailedException("Pick the model that writes the summaries.");
            }

            folder.SummariesEnabled = true;
            folder.SummaryProviderId = provider.Id;
            folder.SummaryModelId = modelId;
            folder.ScanError = null;
        }
        else
        {
            folder.SummariesEnabled = false;
        }

        await store.SaveChangesAsync(ct);
        if (folder.SummariesEnabled)
        {
            scans.Enqueue(folder.Id);
        }

        return await GetAsync(id, ct);
    }

    /// <summary>Forgets the folder and everything read from it. The folder's files themselves aren't touched.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var folder = await FindAsync(id, ct);
        scans.Cancel(folder.Id);
        await store.RemoveAsync(folder.Id, ct);
    }

    /// <summary>Checks the folder for new, changed and deleted files now, rather than at the next scheduled scan.</summary>
    public async Task<FolderDto> RescanAsync(Guid id, CancellationToken ct = default)
    {
        var folder = await FindAsync(id, ct);
        scans.Enqueue(folder.Id);
        return await GetAsync(folder.Id, ct);
    }

    public async Task<IReadOnlyList<FolderFileDto>> ListFilesAsync(Guid id, CancellationToken ct = default)
    {
        var folder = await FindAsync(id, ct);
        var files = await store.ListFilesAsync(folder.Id, ct);
        return files
            .Select(f => new FolderFileDto(
                f.Id, f.RelativePath, f.Size, f.Status, f.Note, f.MediaType, f.PageCount, f.CharacterCount, f.IsTruncated, f.Summary))
            .ToList();
    }

    public async Task<PassageDto> GetPassageAsync(long passageId, CancellationToken ct = default)
    {
        var passage = await index.GetPassageAsync(passageId, ct)
            ?? throw new NotFoundException("That passage isn't there anymore: its file changed or was removed. Ask again to search the current files.");
        return new PassageDto(passage.Id, passage.FileId, passage.Path, passage.Page, passage.Text);
    }

    /// <summary>The file's full path on this computer, for showing it in the file manager.</summary>
    public async Task<string> GetFullPathAsync(Guid fileId, CancellationToken ct = default)
    {
        var file = await store.FindFileAsync(fileId, ct) ?? throw new NotFoundException("That file isn't in any folder anymore.");
        var folder = await FindAsync(file.FolderId, ct);
        var fullPath = Path.GetFullPath(Path.Join(folder.Path, file.RelativePath));
        var folderPath = Path.TrimEndingDirectorySeparator(folder.Path) + Path.DirectorySeparatorChar;

        // The path came from scanning the folder, but never let it point anywhere else.
        if (!fullPath.StartsWith(folderPath, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)
            || !File.Exists(fullPath))
        {
            throw new NotFoundException("That file isn't there anymore.");
        }

        return fullPath;
    }

    /// <summary>The folder's full path, without quotes or a trailing separator, checked to exist.</summary>
    internal static string NormalizePath(string? input)
    {
        var path = input?.Trim().Trim('"', '\'').Trim() ?? "";
        if (path.Length == 0)
        {
            throw new ValidationFailedException("Enter the folder's path.");
        }

        if (path.Length > MaxPathLength)
        {
            throw new ValidationFailedException("That path is too long.");
        }

        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[1..]);
        }

        if (!Path.IsPathFullyQualified(path))
        {
            var example = OperatingSystem.IsWindows() ? @"C:\Users\you\Documents\Contracts" : "/Users/you/Documents/Contracts";
            throw new ValidationFailedException($"Enter the folder's full path, such as {example}.");
        }

        string fullPath;
        try
        {
            fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ValidationFailedException("That isn't a valid folder path.");
        }

        if (!Directory.Exists(fullPath))
        {
            throw new ValidationFailedException("FolderChat can't find that folder. Check the path, and that its drive is connected.");
        }

        if (Path.TrimEndingDirectorySeparator(Path.GetPathRoot(fullPath) ?? "") == fullPath)
        {
            throw new ValidationFailedException("Pick a folder rather than a whole drive.");
        }

        return fullPath;
    }

    private static string DefaultName(string path)
    {
        var name = Path.GetFileName(path);
        return name.Length == 0 ? path : name.Length <= MaxNameLength ? name : name[..MaxNameLength];
    }

    private static string ValidateName(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            throw new ValidationFailedException("Give the folder a name.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new ValidationFailedException($"Keep the name under {MaxNameLength} characters.");
        }

        return trimmed;
    }

    private async Task<FolderDto> SaveNewAsync(string path, string name, CancellationToken ct)
    {
        var folder = new KnowledgeFolder { Name = name, Path = path, CreatedAt = clock.GetUtcNow().UtcDateTime };
        store.Add(folder);
        await store.SaveChangesAsync(ct);
        scans.Enqueue(folder.Id);
        return ToDto(folder, null);
    }

    private async Task<KnowledgeFolder?> FindByPathAsync(string path, CancellationToken ct)
    {
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return (await store.ListAsync(ct)).FirstOrDefault(f => string.Equals(f.Path, path, comparison));
    }

    private async Task<KnowledgeFolder> FindAsync(Guid id, CancellationToken ct) =>
        await store.FindAsync(id, ct) ?? throw new NotFoundException("That folder doesn't exist.");

    private FolderDto ToDto(KnowledgeFolder folder, FolderCounts? counts) => new(
        folder.Id,
        folder.Name,
        folder.Path,
        folder.CreatedAt,
        folder.LastScannedAt,
        folder.ScanError,
        counts?.Indexed ?? 0,
        counts?.Skipped ?? 0,
        counts?.Summarized ?? 0,
        folder.SummariesEnabled,
        folder.SummaryProviderId,
        folder.SummaryModelId,
        folder.SummaryInputTokens,
        folder.SummaryOutputTokens,
        scans.GetProgress(folder.Id));
}
