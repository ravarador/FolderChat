using FolderChat.Application.Abstractions;
using FolderChat.Core.Conversations;

namespace FolderChat.Application.Knowledge;

/// <summary>The passages a reply was shown, numbered in the order the model first saw them. Replies cite them by number.</summary>
internal sealed class SourceList
{
    private readonly List<MessageSource> sources = [];
    private readonly Dictionary<long, int> numbers = [];

    public IReadOnlyList<MessageSource> All => sources;

    public bool TryGetNumber(long passageId, out int number) => numbers.TryGetValue(passageId, out number);

    /// <summary>The passage's number, given the first time it is added.</summary>
    public int Add(FoundPassage passage)
    {
        if (numbers.TryGetValue(passage.Id, out var number))
        {
            return number;
        }

        number = sources.Count + 1;
        numbers[passage.Id] = number;
        sources.Add(new MessageSource
        {
            Number = number,
            FolderId = passage.FolderId,
            FileId = passage.FileId,
            Path = passage.Path,
            Page = passage.Page,
            PassageId = passage.Id,
        });
        return number;
    }
}
