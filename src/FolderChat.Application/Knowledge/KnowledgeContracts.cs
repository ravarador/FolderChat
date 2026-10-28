using FolderChat.Application.Abstractions;
using FolderChat.Core.Knowledge;

namespace FolderChat.Application.Knowledge;

/// <summary>A knowledge folder as the browser sees it.</summary>
/// <param name="Scan">What the background scan is doing, or null when it's idle.</param>
public sealed record FolderDto(
    Guid Id,
    string Name,
    string Path,
    DateTime CreatedAt,
    DateTime? LastScannedAt,
    string? ScanError,
    int IndexedFiles,
    int SkippedFiles,
    int SummarizedFiles,
    bool SummariesEnabled,
    Guid? SummaryProviderId,
    string? SummaryModelId,
    long SummaryInputTokens,
    long SummaryOutputTokens,
    ScanProgress? Scan);

public sealed record FolderFileDto(
    Guid Id,
    string Path,
    long Size,
    KnowledgeFileStatus Status,
    string? Note,
    string? MediaType,
    int? PageCount,
    int CharacterCount,
    bool IsTruncated,
    string? Summary);

/// <summary>A passage, as shown when a citation is clicked.</summary>
public sealed record PassageDto(long Id, Guid FileId, string Path, int? Page, string Text);

/// <summary>A folder the user chose in the computer's folder window.</summary>
/// <param name="Added">True when it was new; false when it had been added before.</param>
public sealed record ChosenFolderDto(FolderDto Folder, bool Added);

/// <param name="Path">The folder's full path. Quotes around it, as Explorer's "Copy as path" adds, are fine.</param>
/// <param name="Name">How the folder is shown. Defaults to the folder's own name.</param>
public sealed record AddFolderRequest(string Path, string? Name = null);

/// <param name="SummariesEnabled">Turns on per-file summaries, written by the model the other two values name.</param>
public sealed record UpdateFolderRequest(
    string Name,
    bool SummariesEnabled = false,
    Guid? SummaryProviderId = null,
    string? SummaryModelId = null);
