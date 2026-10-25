using System.Globalization;
using System.Text;
using FolderChat.Application.Abstractions;
using FolderChat.Application.Attachments;
using FolderChat.Application.Common;
using FolderChat.Application.Knowledge;
using FolderChat.Core.Knowledge;
using FolderChat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FolderChat.Infrastructure.Knowledge;

/// <summary>
/// Brings one folder's index up to date: reads new and changed files with the same readers as attachments, splits
/// their text into passages, forgets files that are gone, and writes missing summaries when those are on. A file is
/// read again only when its size or modified time changes.
/// </summary>
internal sealed class FolderScanner(
    FolderChatDbContext db,
    ITextExtractor extractor,
    FileSummarizer summarizer,
    AppPaths paths,
    TimeProvider clock,
    ILogger<FolderScanner> logger)
{
    /// <summary>Larger files are skipped. PDFs that big are usually scans, with no text to read anyway.</summary>
    public const long MaxFileSize = 50L * 1024 * 1024;

    /// <summary>The most text indexed from one file: about half a million words.</summary>
    public const int MaxTextLength = 3_000_000;

    /// <summary>Only this many files are read, so a folder added by mistake, like a whole home folder, can't take hours.</summary>
    public const int MaxFiles = 20_000;

    /// <summary>Summaries stop after this many failures in a row, rather than failing the same way for every file.</summary>
    private const int MaxSummaryFailures = 3;

    // Windows marks online-only cloud files (OneDrive and the like) with these. Reading one would download it.
    private const FileAttributes RecallOnOpen = (FileAttributes)0x0004_0000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x0040_0000;

    private static readonly StringComparer PathComparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    // Package caches and build output: never documents, and often huge.
    private static readonly HashSet<string> IgnoredFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", "__pycache__", "venv",
    };

    // Types that are never text, skipped without reading them, which matters for big videos and archives.
    private static readonly Dictionary<string, string> UnreadableTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "Images", [".jpeg"] = "Images", [".png"] = "Images", [".gif"] = "Images", [".webp"] = "Images",
        [".heic"] = "Images", [".bmp"] = "Images", [".tif"] = "Images", [".tiff"] = "Images", [".raw"] = "Images",
        [".mp3"] = "Audio files", [".wav"] = "Audio files", [".m4a"] = "Audio files", [".flac"] = "Audio files", [".ogg"] = "Audio files",
        [".mp4"] = "Videos", [".mov"] = "Videos", [".avi"] = "Videos", [".mkv"] = "Videos", [".wmv"] = "Videos", [".webm"] = "Videos",
        [".zip"] = "Archives", [".7z"] = "Archives", [".rar"] = "Archives", [".gz"] = "Archives", [".tar"] = "Archives", [".iso"] = "Archives",
        [".exe"] = "Programs", [".dll"] = "Programs", [".msi"] = "Programs", [".so"] = "Programs", [".dylib"] = "Programs",
    };

    /// <param name="report">Called with progress as files are read and summarized.</param>
    public async Task ScanAsync(Guid folderId, Action<ScanProgress> report, CancellationToken ct)
    {
        var folder = await db.KnowledgeFolders.FirstOrDefaultAsync(f => f.Id == folderId, ct);
        if (folder is null)
        {
            return;
        }

        report(new ScanProgress(ScanPhase.Reading, 0, 0));
        if (!Directory.Exists(folder.Path))
        {
            folder.ScanError = "FolderChat can't find this folder. It may have been moved, renamed or deleted, or its drive isn't connected.";
            folder.LastScannedAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
            return;
        }

        var (found, tooMany) = ListFiles(folder.Path);
        var known = new Dictionary<string, KnowledgeFile>(PathComparer);
        foreach (var file in await db.KnowledgeFiles.Where(f => f.FolderId == folder.Id).ToListAsync(ct))
        {
            known.TryAdd(file.RelativePath, file);
        }

        // Forget files that are gone. The database deletes their passages, and the triggers update the index.
        var present = found.Select(f => f.RelativePath).ToHashSet(PathComparer);
        var removed = known.Values.Where(f => !present.Contains(f.RelativePath)).ToList();
        if (removed.Count > 0)
        {
            db.KnowledgeFiles.RemoveRange(removed);
            await db.SaveChangesAsync(ct);
        }

        var changed = found
            .Where(f => !known.TryGetValue(f.RelativePath, out var file)
                || file.Size != f.Info.Length
                || file.LastWriteTimeUtc != f.Info.LastWriteTimeUtc)
            .ToList();
        for (var i = 0; i < changed.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            report(new ScanProgress(ScanPhase.Reading, i, changed.Count));
            await IndexFileAsync(folder, changed[i], known.GetValueOrDefault(changed[i].RelativePath), ct);
        }

        var error = tooMany
            ? string.Create(CultureInfo.CurrentCulture, $"This folder has more than {MaxFiles:N0} files, so only the first {MaxFiles:N0} are read.")
            : null;
        if (folder.SummariesEnabled)
        {
            error = await SummarizeAsync(folder, report, ct) ?? error;
        }

        folder.ScanError = error;
        folder.LastScannedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        if (changed.Count > 0 || removed.Count > 0)
        {
            logger.LogInformation("Scanned folder {FolderId}: {Read} files read, {Removed} forgotten", folder.Id, changed.Count, removed.Count);
        }
    }

    /// <summary>The folder's files, in path order, leaving out hidden ones and folders that never hold documents.</summary>
    private (List<FoundFile> Files, bool TooMany) ListFiles(string root)
    {
        var files = new List<FoundFile>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 };

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            List<FileSystemInfo> entries;
            try
            {
                entries = directory.EnumerateFileSystemInfos("*", options).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (entry.Name.StartsWith('.') || (entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                {
                    continue;
                }

                if (entry is DirectoryInfo subfolder)
                {
                    // Links and junctions can loop back up the tree. FolderChat's own data folder holds the index itself.
                    if ((subfolder.Attributes & FileAttributes.ReparsePoint) == 0
                        && !IgnoredFolders.Contains(subfolder.Name)
                        && !PathComparer.Equals(Path.TrimEndingDirectorySeparator(subfolder.FullName), Path.TrimEndingDirectorySeparator(paths.DataDirectory)))
                    {
                        pending.Push(subfolder);
                    }
                }
                else if (entry is FileInfo file)
                {
                    files.Add(new FoundFile(Path.GetRelativePath(root, file.FullName).Replace('\\', '/'), file));
                    if (files.Count == MaxFiles)
                    {
                        return (Sorted(files), true);
                    }
                }
            }
        }

        return (Sorted(files), false);

        static List<FoundFile> Sorted(List<FoundFile> list) => list.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToList();
    }

    /// <summary>Reads a file and replaces its passages, all in one transaction, so searches never see it half done.</summary>
    private async Task IndexFileAsync(KnowledgeFolder folder, FoundFile found, KnowledgeFile? existing, CancellationToken ct)
    {
        // The file may have changed, or gone, since the folder was listed.
        found.Info.Refresh();
        if (!found.Info.Exists)
        {
            return;
        }

        var (extracted, note) = await ReadAsync(found.Info, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (existing is not null)
        {
            await db.Passages.Where(p => p.FileId == existing.Id).ExecuteDeleteAsync(ct);
        }

        var file = existing ?? new KnowledgeFile { FolderId = folder.Id, RelativePath = found.RelativePath };
        if (existing is null)
        {
            db.KnowledgeFiles.Add(file);
        }

        file.Size = found.Info.Length;
        file.LastWriteTimeUtc = found.Info.LastWriteTimeUtc;
        file.IndexedAt = clock.GetUtcNow().UtcDateTime;
        file.MediaType = extracted?.MediaType;
        file.PageCount = extracted?.PageCount;
        file.CharacterCount = extracted?.Text.Length ?? 0;
        file.IsTruncated = extracted?.IsTruncated ?? false;
        file.Summary = null; // the content changed, so any summary is out of date

        if (extracted is null || note is not null)
        {
            file.Status = KnowledgeFileStatus.Skipped;
            file.Note = note;
        }
        else
        {
            file.Status = KnowledgeFileStatus.Indexed;
            file.Note = extracted.IsTruncated
                ? string.Create(CultureInfo.CurrentCulture, $"Only the first {MaxTextLength:N0} characters can be searched.")
                : null;
            foreach (var passage in PassageSplitter.Split(extracted.Text, paged: extracted.MediaType == MediaTypes.Pdf))
            {
                db.Passages.Add(new Passage
                {
                    FileId = file.Id,
                    Ordinal = passage.Ordinal,
                    PageNumber = passage.Page,
                    StartOffset = passage.StartOffset,
                    Text = passage.Text,
                });
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        // The passages aren't needed again in this scan. Letting go of them keeps memory flat in big folders.
        foreach (var entry in db.ChangeTracker.Entries<Passage>().ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary>The file's text, or a note in words for the user saying why it can't be searched.</summary>
    private async Task<(ExtractedText? Text, string? Note)> ReadAsync(FileInfo info, CancellationToken ct)
    {
        if (UnreadableTypes.TryGetValue(info.Extension, out var kind))
        {
            return (null, $"{kind} aren't read. FolderChat reads PDF, Word (.docx) and text files.");
        }

        if (info.Length == 0)
        {
            return (null, "The file is empty.");
        }

        if (info.Length > MaxFileSize)
        {
            return (null, $"Larger than {MaxFileSize / (1024 * 1024)} MB.");
        }

        if ((info.Attributes & (RecallOnOpen | RecallOnDataAccess | FileAttributes.Offline)) != 0)
        {
            return (null, "Stored online only, and not downloaded to this computer.");
        }

        byte[] bytes;
        try
        {
            await using var stream = new FileStream(info.FullName, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite | FileShare.Delete,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            return (null, "FolderChat isn't allowed to read it.");
        }
        catch (IOException)
        {
            return (null, "Couldn't open it. Another program may be using it.");
        }

        try
        {
            var text = extractor.Extract(info.Name, bytes, MaxTextLength, ct);
            if (string.IsNullOrWhiteSpace(text.Text))
            {
                return (text, text.MediaType == MediaTypes.Pdf ? "No text to read. It may be a scan." : "No text in it.");
            }

            return (text, null);
        }
        catch (ValidationFailedException ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>Writes summaries for searchable files that don't have one yet. Returns why it stopped early, if it did.</summary>
    private async Task<string?> SummarizeAsync(KnowledgeFolder folder, Action<ScanProgress> report, CancellationToken ct)
    {
        var pending = await db.KnowledgeFiles
            .Where(f => f.FolderId == folder.Id && f.Status == KnowledgeFileStatus.Indexed && f.Summary == null)
            .OrderBy(f => f.RelativePath)
            .ToListAsync(ct);
        if (pending.Count == 0)
        {
            return null;
        }

        var (writer, error) = await summarizer.OpenAsync(folder, ct);
        if (writer is null)
        {
            return error;
        }

        using (writer)
        {
            var failures = 0;
            for (var i = 0; i < pending.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                report(new ScanProgress(ScanPhase.Summarizing, i, pending.Count));
                var file = pending[i];
                try
                {
                    file.Summary = await writer.SummarizeAsync(file.RelativePath, await ReadStartAsync(file.Id, ct), ct);
                    await db.SaveChangesAsync(ct);
                    failures = 0;
                }
                catch (ProviderCallFailedException ex)
                {
                    logger.LogWarning("Summarizing a file in folder {FolderId} failed: {Reason}", folder.Id, ex.Message);
                    if (++failures == MaxSummaryFailures)
                    {
                        return $"Summaries stopped after repeated errors: {ex.Message}";
                    }
                }
            }
        }

        return null;
    }

    /// <summary>The start of a file's text, rebuilt from its passages, up to what a summary reads.</summary>
    private async Task<string> ReadStartAsync(Guid fileId, CancellationToken ct)
    {
        var text = new StringBuilder();
        var passages = db.Passages.AsNoTracking().Where(p => p.FileId == fileId).OrderBy(p => p.Ordinal).Select(p => p.Text);
        await foreach (var passage in passages.AsAsyncEnumerable().WithCancellation(ct))
        {
            if (text.Length >= SummaryWriter.MaxInputLength)
            {
                break;
            }

            text.Append(passage).Append("\n\n");
        }

        return text.ToString();
    }

    private sealed record FoundFile(string RelativePath, FileInfo Info);
}
