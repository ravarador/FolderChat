using System.Text.Json;
using FolderChat.Application.Abstractions;
using FolderChat.Core.Knowledge;
using FolderChat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FolderChat.Infrastructure.Knowledge;

/// <summary>Searches passages with SQLite's FTS5 full-text index, ranked by BM25, and reads files back from their passages.</summary>
internal sealed class KnowledgeIndex(FolderChatDbContext db) : IKnowledgeIndex
{
    /// <summary>No file takes more than this many places in one search, so a long file can't crowd out the rest.</summary>
    private const int MaxPerFile = 3;

    public async Task<IReadOnlyList<FoundPassage>> SearchAsync(IReadOnlyCollection<Guid> folderIds, string words, int limit, CancellationToken ct = default)
    {
        var query = FtsQuery.Build(words);
        if (query is null || limit <= 0 || folderIds.Count == 0)
        {
            return [];
        }

        // The folders go in as one JSON list. EF stores Guids as uppercase text, so they're written the same way.
        var folders = JsonSerializer.Serialize(folderIds.Select(id => id.ToString("D").ToUpperInvariant()));

        // bm25() is lower for better matches. Fetching extra rows leaves room to skip a file's surplus passages.
        var rows = await db.Database.SqlQuery<SearchRow>($"""
            SELECT p.Id, f.FolderId, p.FileId, f.RelativePath AS Path, p.PageNumber AS Page, p.Text
            FROM PassageSearch
            JOIN Passages p ON p.Id = PassageSearch.rowid
            JOIN KnowledgeFiles f ON f.Id = p.FileId
            WHERE PassageSearch MATCH {query} AND f.FolderId IN (SELECT value FROM json_each({folders}))
            ORDER BY bm25(PassageSearch)
            LIMIT {limit * MaxPerFile}
            """).ToListAsync(ct);

        var perFile = new Dictionary<Guid, int>();
        var results = new List<FoundPassage>(limit);
        foreach (var row in rows)
        {
            var count = perFile.GetValueOrDefault(row.FileId);
            if (count == MaxPerFile)
            {
                continue;
            }

            perFile[row.FileId] = count + 1;
            results.Add(new FoundPassage(row.Id, row.FolderId, row.FileId, row.Path, row.Page, row.Text));
            if (results.Count == limit)
            {
                break;
            }
        }

        return results;
    }

    public Task<FoundPassage?> GetPassageAsync(long passageId, CancellationToken ct = default) =>
        db.Passages
            .Where(p => p.Id == passageId)
            .Join(db.KnowledgeFiles, p => p.FileId, f => f.Id, (p, f) => new FoundPassage(p.Id, f.FolderId, p.FileId, f.RelativePath, p.PageNumber, p.Text))
            .FirstOrDefaultAsync(ct);

    public async Task<FilePart?> ReadAsync(Guid folderId, string path, int part, int sliceLength, CancellationToken ct = default)
    {
        var files = db.KnowledgeFiles.AsNoTracking().Where(f => f.FolderId == folderId && f.Status == KnowledgeFileStatus.Indexed);

        // Models sometimes change the case of a path; fall back to a case-insensitive match.
        var file = await files.FirstOrDefaultAsync(f => f.RelativePath == path, ct)
            ?? await files.FirstOrDefaultAsync(f => f.RelativePath.ToLower() == path.ToLower(), ct);
        if (file is null || part < 1)
        {
            return null;
        }

        List<Passage> passages;
        int partCount;
        bool isPage;
        if (file.PageCount is { } pageCount)
        {
            if (part > pageCount)
            {
                return null;
            }

            partCount = pageCount;
            isPage = true;
            passages = await db.Passages.AsNoTracking()
                .Where(p => p.FileId == file.Id && p.PageNumber == part)
                .OrderBy(p => p.Ordinal)
                .ToListAsync(ct);
        }
        else
        {
            partCount = Math.Max(1, (file.CharacterCount + sliceLength - 1) / sliceLength);
            if (part > partCount)
            {
                return null;
            }

            isPage = false;
            var start = (part - 1) * sliceLength;
            var end = part * sliceLength;
            passages = await db.Passages.AsNoTracking()
                .Where(p => p.FileId == file.Id && p.StartOffset >= start && p.StartOffset < end)
                .OrderBy(p => p.Ordinal)
                .ToListAsync(ct);
        }

        // A page with no text, such as a picture, has no passages: FirstPassageId 0 says so.
        return new FilePart(
            file.FolderId,
            file.Id,
            file.RelativePath,
            part,
            partCount,
            isPage,
            passages.Count == 0 ? 0 : passages[0].Id,
            string.Join("\n\n", passages.Select(p => p.Text)));
    }

    private sealed class SearchRow
    {
        public long Id { get; set; }

        public Guid FolderId { get; set; }

        public Guid FileId { get; set; }

        public string Path { get; set; } = "";

        public int? Page { get; set; }

        public string Text { get; set; } = "";
    }
}
