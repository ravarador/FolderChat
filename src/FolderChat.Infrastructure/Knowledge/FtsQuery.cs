using System.Text.RegularExpressions;

namespace FolderChat.Infrastructure.Knowledge;

/// <summary>
/// Turns text as the user or model typed it into a SQLite FTS5 query: its meaningful words, each quoted so no
/// character can act as search syntax, joined with OR. The index ranks passages that match more of the words,
/// and rarer ones, higher.
/// </summary>
internal static partial class FtsQuery
{
    private const int MaxWords = 24;

    // Words so common that matching them says nothing about a passage, plus the ones people use to ask.
    private static readonly HashSet<string> CommonWords = new(StringComparer.Ordinal)
    {
        "a", "about", "after", "all", "also", "am", "an", "and", "any", "are", "as", "at", "be", "been", "before",
        "but", "by", "can", "could", "did", "do", "does", "for", "from", "had", "has", "have", "he", "her", "here",
        "his", "how", "i", "if", "in", "into", "is", "it", "its", "me", "my", "no", "not", "of", "on", "or", "our", "please",
        "she", "should", "show", "so", "some", "tell", "than", "that", "the", "their", "them", "then", "there",
        "these", "they", "this", "those", "to", "us", "was", "we", "were", "what", "when", "where", "which", "who",
        "whom", "why", "will", "with", "would", "you", "your",
    };

    /// <summary>The query, or null when the text has no words at all.</summary>
    public static string? Build(string text)
    {
        var words = Word().Matches(text.ToLowerInvariant()).Select(m => m.Value).Distinct().ToList();

        // Keep numbers of any length, but drop single letters and common words. If that leaves nothing,
        // as for "who are we?", search for the common words after all.
        var meaningful = words.Where(w => !CommonWords.Contains(w) && (w.Length > 1 || char.IsDigit(w[0]))).ToList();
        var chosen = meaningful.Count > 0 ? meaningful : words;
        return chosen.Count == 0 ? null : string.Join(" OR ", chosen.Take(MaxWords).Select(w => $"\"{w}\""));
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();
}
