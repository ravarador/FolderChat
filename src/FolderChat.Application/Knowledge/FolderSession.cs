using System.Globalization;
using System.Text;
using System.Text.Json;
using FolderChat.Application.Abstractions;
using FolderChat.Core.Conversations;
using FolderChat.Core.Knowledge;
using Microsoft.Extensions.AI;

namespace FolderChat.Application.Knowledge;

/// <summary>
/// A chat's knowledge folders in use for one reply: the instructions for the model, the tools it may call at the
/// chosen effort, and the numbered passages it has been shown, which become the reply's sources. The model sees each
/// file's path with its folder's name in front, like "Contracts/2026/acme.pdf", so files in different folders never
/// get mixed up.
/// </summary>
public sealed class FolderSession
{
    /// <summary>Files without pages are read in slices of about this many characters.</summary>
    internal const int SliceLength = 6_000;

    private const int MaxListedFiles = 150;

    private static readonly AITool SearchTool = AIFunctionFactory.CreateDeclaration(
        "search_folder",
        "Searches the files in the user's folders for passages that contain these words, best matches first. It matches words, not meaning.",
        Schema("""
            {
              "type": "object",
              "properties": {
                "query": { "type": "string", "description": "Words to look for, as they would appear in the documents." }
              },
              "required": ["query"]
            }
            """));

    private static readonly AITool ReadTool = AIFunctionFactory.CreateDeclaration(
        "read_file",
        "Reads one page, or one part, of a file in the user's folders, to see the text around a passage.",
        Schema("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "The file's path, starting with its folder's name, exactly as search results or the file list show it." },
                "part": { "type": "integer", "description": "For files with pages, the page number. Other files come in parts of about 6,000 characters, numbered from 1." }
              },
              "required": ["path", "part"]
            }
            """));

    private static readonly AITool ListTool = AIFunctionFactory.CreateDeclaration(
        "list_files",
        "Lists the readable files in the user's folders, with a short summary of each when summaries are switched on.",
        Schema("""
            {
              "type": "object",
              "properties": {
                "filter": { "type": "string", "description": "Only list files whose path contains this text. Leave it out to list them all." }
              }
            }
            """));

    private readonly IKnowledgeStore store;
    private readonly IKnowledgeIndex index;
    private readonly IReadOnlyDictionary<Guid, int> fileCounts;
    private readonly Dictionary<Guid, string> labels;
    private readonly SourceList sources = new();
    private EffortLimits limits;
    private int searches;
    private int reads;
    private int lists;
    private int textSent;

    /// <param name="fileCounts">How many readable files each folder holds.</param>
    internal FolderSession(
        IReadOnlyList<KnowledgeFolder> folders,
        IReadOnlyDictionary<Guid, int> fileCounts,
        SearchEffort effort,
        IKnowledgeStore store,
        IKnowledgeIndex index)
    {
        Folders = folders;
        Effort = effort;
        this.fileCounts = fileCounts;
        this.store = store;
        this.index = index;
        limits = EffortLimits.For(effort);
        labels = MakeLabels(folders);
    }

    public IReadOnlyList<KnowledgeFolder> Folders { get; }

    public SearchEffort Effort { get; private set; }

    /// <summary>The tools the model may call. None for Quick, where FolderChat searches for the model.</summary>
    public IList<AITool> Tools => Effort switch
    {
        SearchEffort.Quick => [],
        SearchEffort.Thorough => [SearchTool, ReadTool, ListTool],
        _ => [SearchTool],
    };

    /// <summary>The most model round trips one reply may take.</summary>
    public int MaxRounds => limits.MaxRounds;

    /// <summary>Every passage the model was shown, numbered as it was told.</summary>
    public IReadOnlyList<MessageSource> Sources => sources.All;

    /// <summary>The system message that tells the model about the folders, its tools and how to cite.</summary>
    public string Instructions
    {
        get
        {
            var text = new StringBuilder();
            if (Folders.Count == 1)
            {
                text.Append(CultureInfo.InvariantCulture, $"The user is chatting with a folder of their files called \"{labels[Folders[0].Id]}\". It holds {Files(Folders[0])}.");
            }
            else
            {
                var list = Folders.Select(f => $"\"{labels[f.Id]}\" ({Files(f)})").ToList();
                text.Append(CultureInfo.InvariantCulture, $"The user is chatting with {Folders.Count} folders of their files: {JoinNames(list)}.");
            }

            text.Append(CultureInfo.InvariantCulture, $" File paths start with the folder's name, as in \"{labels[Folders[0].Id]}/notes.md\".\n\n");
            text.Append(Effort switch
            {
                SearchEffort.Quick =>
                    "FolderChat searched the folders for the words in the user's latest message, and put the passages it found in a <folder_passages> block at the start of that message.",
                SearchEffort.Thorough => string.Create(CultureInfo.InvariantCulture,
                    $"""
                    Before answering, search the folders with search_folder, read around useful passages with read_file, and use list_files to see what the folders hold. Be thorough: when the question calls for it, check several files and compare them.
                    Search matches words, not meaning. If a search doesn't find what you need, try other words: synonyms, names, numbers, or the terms the documents would use.
                    For each question you can search {limits.Searches} times, read {limits.Reads} pages or parts, and list files {limits.Lists} times.
                    """),
                _ => string.Create(CultureInfo.InvariantCulture,
                    $"""
                    Before answering, search the folders with search_folder.
                    Search matches words, not meaning. If a search doesn't find what you need, try other words: synonyms, names, numbers, or the terms the documents would use.
                    You can search {limits.Searches} times per question.
                    """),
            });
            text.Append("\n\n");
            text.Append("""
                Passages are numbered like [3]. Cite the ones you use by number in square brackets, right after what they support, and cite only numbers you were given.
                Base your answer on the passages. If they don't answer the question, say that the folders don't seem to cover it. You may then answer from general knowledge, saying clearly that it doesn't come from the folders.
                """);
            return text.ToString();
        }
    }

    /// <summary>Switches to Quick, for a model that turned out not to support tools.</summary>
    public void UseQuickSearch()
    {
        Effort = SearchEffort.Quick;
        limits = EffortLimits.For(SearchEffort.Quick);
    }

    /// <summary>
    /// Quick: searches once with the user's message, and returns the passages to put before it, plus a line for
    /// the user saying what was found.
    /// </summary>
    public async Task<(string Passages, string Activity)> FindPassagesAsync(string question, CancellationToken ct = default)
    {
        searches++;
        var found = string.IsNullOrWhiteSpace(question)
            ? []
            : await index.SearchAsync(FolderIds, question, limits.PassagesPerSearch, ct);

        var block = new StringBuilder("<folder_passages>\n");
        block.Append(found.Count == 0 ? "No passages in the folders contain the words of this message.\n" : FormatPassages(found));
        block.Append("</folder_passages>");
        return (block.ToString(), $"Searched {SearchedNames()}: {Count(found.Count, "passage")}");
    }

    /// <summary>Runs a tool call. Returns the result for the model, and a line for the user saying what was done.</summary>
    public async Task<(string Result, string Activity)> InvokeAsync(FunctionCallContent call, CancellationToken ct = default)
    {
        var allowed = Tools.Any(t => t.Name == call.Name);
        return call.Name switch
        {
            "search_folder" when allowed => await SearchAsync(GetString(call, "query"), ct),
            "read_file" when allowed => await ReadAsync(GetString(call, "path"), GetInt(call, "part") ?? 1, ct),
            "list_files" when allowed => await ListAsync(GetString(call, "filter"), ct),
            _ => ($"There's no tool called {call.Name}. Use the tools you were given.", $"Tried a tool that doesn't exist ({call.Name})"),
        };
    }

    private IReadOnlyCollection<Guid> FolderIds => labels.Keys;

    private async Task<(string Result, string Activity)> SearchAsync(string? query, CancellationToken ct)
    {
        query = query?.Trim() ?? "";
        if (query.Length == 0)
        {
            return ("Give some words to search for.", "Tried to search without words");
        }

        if (searches >= limits.Searches)
        {
            return ($"That's all {limits.Searches} searches for this question. Answer now with the passages you have.", "Search limit reached");
        }

        if (textSent >= limits.TextBudget)
        {
            return ("You've read as much as this question allows. Answer now with the passages you have.", "Reading limit reached");
        }

        searches++;
        var found = await index.SearchAsync(FolderIds, query, limits.PassagesPerSearch, ct);
        var left = limits.Searches - searches;
        var remaining = left == 0 ? "That was the last search." : $"{Count(left, "search", "searches")} left.";
        var activity = $"Searched for “{Shorten(query, 80)}”: {Count(found.Count, "passage")}";
        if (found.Count == 0)
        {
            return ($"No passages contain those words. Try other words: synonyms, names, or the terms the documents would use. {remaining}", activity);
        }

        return (FormatPassages(found) + remaining, activity);
    }

    private async Task<(string Result, string Activity)> ReadAsync(string? path, int part, CancellationToken ct)
    {
        if (reads >= limits.Reads)
        {
            return ($"That's all {limits.Reads} reads for this question. Answer now with what you have.", "Read limit reached");
        }

        var room = limits.TextBudget - textSent;
        if (room <= 0)
        {
            return ("You've read as much as this question allows. Answer now with what you have.", "Reading limit reached");
        }

        var target = Resolve(path);
        var filePart = target is { } file ? await index.ReadAsync(file.Folder.Id, file.Path, Math.Max(part, 1), SliceLength, ct) : null;
        if (filePart is null)
        {
            return (
                $"There's no file \"{path}\" with a part {part}. Use a path exactly as a search result or the file list shows it, starting with the folder's name.",
                $"Couldn't find {Shorten(path ?? "", 80)}, part {part}");
        }

        reads++;
        var label = $"{labels[filePart.FolderId]}/{filePart.Path}";
        var where = $"{(filePart.IsPage ? "page" : "part")} {filePart.Part} of {filePart.PartCount}";
        if (filePart.FirstPassageId == 0)
        {
            return ($"{label}, {where}, has no text: it may be a picture, or blank.", $"Read {Shorten(label, 80)}, {where}: no text");
        }

        var number = sources.Add(new FoundPassage(
            filePart.FirstPassageId, filePart.FolderId, filePart.FileId, filePart.Path, filePart.IsPage ? filePart.Part : null, filePart.Text));
        var text = filePart.Text.Length <= room ? filePart.Text : filePart.Text[..room] + "\n(Cut off: you've read as much as this question allows.)";
        textSent += text.Length;
        return ($"[{number}] {label}, {where}\n{text}", $"Read {Shorten(label, 80)}, {where}");
    }

    private async Task<(string Result, string Activity)> ListAsync(string? filter, CancellationToken ct)
    {
        if (lists >= limits.Lists)
        {
            return ($"That's all {limits.Lists} file lists for this question. Answer now with what you have.", "File list limit reached");
        }

        lists++;
        filter = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();
        var files = new List<(KnowledgeFolder Folder, KnowledgeFile File, string Path)>();
        foreach (var folder in Folders)
        {
            foreach (var file in await store.ListFilesAsync(folder.Id, ct))
            {
                var path = $"{labels[folder.Id]}/{file.RelativePath}";
                if (file.Status == KnowledgeFileStatus.Indexed && (filter is null || path.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                {
                    files.Add((folder, file, path));
                }
            }
        }

        var text = new StringBuilder();
        foreach (var (folder, file, path) in files.Take(MaxListedFiles))
        {
            text.Append("- ").Append(path);
            if (file.PageCount is { } pages)
            {
                text.Append(" (").Append(Count(pages, "page")).Append(')');
            }

            if (folder.SummariesEnabled && !string.IsNullOrWhiteSpace(file.Summary))
            {
                text.Append(": ").Append(Shorten(string.Join(' ', file.Summary.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)), 400));
            }

            text.Append('\n');
        }

        if (files.Count > MaxListedFiles)
        {
            text.Append(CultureInfo.InvariantCulture, $"…and {files.Count - MaxListedFiles:N0} more. Use a filter to narrow the list.\n");
        }
        else if (files.Count == 0)
        {
            text.Append(filter is null ? "The folders have no readable files." : $"No file paths contain \"{filter}\".");
        }

        textSent += text.Length;
        return (text.ToString(), $"Listed {Count(files.Count, "file")}" + (filter is null ? "" : $" matching “{Shorten(filter, 40)}”"));
    }

    /// <summary>Numbers each passage and adds its text, until the question's reading budget runs out.</summary>
    private string FormatPassages(IReadOnlyList<FoundPassage> found)
    {
        var text = new StringBuilder();
        foreach (var passage in found)
        {
            if (sources.TryGetNumber(passage.Id, out var shown))
            {
                text.Append(CultureInfo.InvariantCulture, $"[{shown}] {Label(passage)}: already shown above.\n\n");
                continue;
            }

            if (textSent + passage.Text.Length > limits.TextBudget)
            {
                text.Append("(More passages matched, but you've read as much as this question allows.)\n\n");
                break;
            }

            var number = sources.Add(passage);
            textSent += passage.Text.Length;
            text.Append(CultureInfo.InvariantCulture, $"[{number}] {Label(passage)}\n{passage.Text}\n\n");
        }

        return text.ToString();
    }

    private string Label(FoundPassage passage)
    {
        var path = $"{labels[passage.FolderId]}/{passage.Path}";
        return passage.Page is { } page ? $"{path}, page {page}" : path;
    }

    /// <summary>The folder, and the path inside it, of a path as the model gives it: the folder's name, then the path.</summary>
    private (KnowledgeFolder Folder, string Path)? Resolve(string? modelPath)
    {
        var path = (modelPath ?? "").Trim().Trim('"').Replace('\\', '/').TrimStart('/');
        if (path.StartsWith("./", StringComparison.Ordinal))
        {
            path = path[2..];
        }

        // Longest name first, so a folder called "Docs" doesn't claim the files of "Docs (2)".
        foreach (var folder in Folders.OrderByDescending(f => labels[f.Id].Length))
        {
            var prefix = labels[folder.Id] + "/";
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return (folder, path[prefix.Length..]);
            }
        }

        // Models sometimes leave the folder's name off. With one folder, there's no doubt which it means.
        return Folders.Count == 1 ? (Folders[0], path) : null;
    }

    private string SearchedNames() =>
        Folders.Count <= 3 ? JoinNames(Folders.Select(f => labels[f.Id]).ToList()) : $"{Folders.Count} folders";

    private string Files(KnowledgeFolder folder) => Count(fileCounts.GetValueOrDefault(folder.Id), "readable file");

    /// <summary>
    /// A name for each folder that is unique in this chat and can start a path: slashes become dashes, and a name
    /// used twice gets a number, as in "Docs (2)".
    /// </summary>
    private static Dictionary<Guid, string> MakeLabels(IReadOnlyList<KnowledgeFolder> folders)
    {
        var labels = new Dictionary<Guid, string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            var name = folder.Name.Replace('/', '-').Replace('\\', '-').Trim();
            name = name.Length == 0 ? "Folder" : name;
            var label = name;
            for (var n = 2; !used.Add(label); n++)
            {
                label = $"{name} ({n})";
            }

            labels[folder.Id] = label;
        }

        return labels;
    }

    private static string JoinNames(IReadOnlyList<string> names) =>
        names.Count <= 1 ? string.Concat(names) : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";

    private static string Count(int count, string noun, string? plural = null) =>
        count == 1 ? $"1 {noun}" : string.Create(CultureInfo.InvariantCulture, $"{(count == 0 ? "no" : count.ToString("N0", CultureInfo.InvariantCulture))} {plural ?? noun + "s"}");

    private static string Shorten(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    /// <summary>Arguments arrive as JSON values or as plain .NET values, depending on the provider's SDK.</summary>
    private static string? GetString(FunctionCallContent call, string name) =>
        call.Arguments is not null && call.Arguments.TryGetValue(name, out var value)
            ? value switch
            {
                null => null,
                string text => text,
                JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
                JsonElement { ValueKind: JsonValueKind.Null } => null,
                _ => Convert.ToString(value, CultureInfo.InvariantCulture),
            }
            : null;

    private static int? GetInt(FunctionCallContent call, string name) =>
        int.TryParse(GetString(call, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
