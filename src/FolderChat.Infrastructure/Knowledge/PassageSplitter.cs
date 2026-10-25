using System.Text;
using System.Text.RegularExpressions;

namespace FolderChat.Infrastructure.Knowledge;

/// <summary>A piece of a file's text, ready to be stored and indexed.</summary>
internal sealed record SplitPassage(int Ordinal, int? Page, int StartOffset, string Text);

/// <summary>
/// Splits a file's text into passages of a few paragraphs each, so a search can return just the relevant part of a
/// long file. Paragraphs stay whole where they can, and passages never cross a page.
/// </summary>
internal static partial class PassageSplitter
{
    /// <summary>A passage closes once it reaches this length.</summary>
    public const int TargetLength = 1_200;

    /// <summary>No passage is longer. Longer paragraphs are cut at a sentence end, or at least between words.</summary>
    public const int MaxLength = 2_000;

    /// <param name="paged">True for text from a PDF, whose pages start with "[Page n]" lines.</param>
    public static IEnumerable<SplitPassage> Split(string text, bool paged)
    {
        var ordinal = 0;
        foreach (var (page, pageText, pageOffset) in Pages(text, paged))
        {
            foreach (var (passage, offset) in Pack(pageText))
            {
                yield return new SplitPassage(++ordinal, page, pageOffset + offset, passage);
            }
        }
    }

    private static IEnumerable<(int? Page, string Text, int Offset)> Pages(string text, bool paged)
    {
        var markers = paged ? PageMarker().Matches(text) : null;
        if (markers is null || markers.Count == 0)
        {
            yield return (null, text, 0);
            yield break;
        }

        for (var i = 0; i < markers.Count; i++)
        {
            var start = markers[i].Index + markers[i].Length;
            var end = i + 1 < markers.Count ? markers[i + 1].Index : text.Length;
            yield return (int.Parse(markers[i].Groups[1].ValueSpan), text[start..end], start);
        }
    }

    /// <summary>Gathers paragraphs into passages of about <see cref="TargetLength"/> characters.</summary>
    private static IEnumerable<(string Text, int Offset)> Pack(string text)
    {
        var current = new StringBuilder();
        var start = 0;
        foreach (Match paragraph in Paragraph().Matches(text))
        {
            foreach (var (piece, offset) in Cut(paragraph.Value.TrimEnd(), paragraph.Index))
            {
                if (current.Length > 0 && current.Length + 2 + piece.Length > MaxLength)
                {
                    yield return (current.ToString(), start);
                    current.Clear();
                }

                if (current.Length == 0)
                {
                    start = offset;
                }
                else
                {
                    current.Append("\n\n");
                }

                current.Append(piece);
                if (current.Length >= TargetLength)
                {
                    yield return (current.ToString(), start);
                    current.Clear();
                }
            }
        }

        if (current.Length > 0)
        {
            yield return (current.ToString(), start);
        }
    }

    /// <summary>Cuts a paragraph longer than <see cref="MaxLength"/> into pieces, at sentence ends where it can.</summary>
    private static IEnumerable<(string Text, int Offset)> Cut(string paragraph, int offset)
    {
        var position = 0;
        while (paragraph.Length - position > MaxLength)
        {
            var window = paragraph.AsSpan(position, MaxLength);
            var end = LastSentenceEnd(window);
            if (end < MaxLength / 2)
            {
                var space = window.LastIndexOfAny(' ', '\n');
                end = space > MaxLength / 2 ? space + 1 : MaxLength;
            }

            // Don't split a character that takes two UTF-16 code units.
            if (char.IsHighSurrogate(paragraph[position + end - 1]))
            {
                end--;
            }

            yield return (paragraph.Substring(position, end).TrimEnd(), offset + position);
            position += end;
            while (position < paragraph.Length && char.IsWhiteSpace(paragraph[position]))
            {
                position++;
            }
        }

        if (position < paragraph.Length)
        {
            yield return (paragraph[position..], offset + position);
        }
    }

    /// <summary>The position just after the last ". ", "! ", "? " or "。" in the text, or 0 if there is none.</summary>
    private static int LastSentenceEnd(ReadOnlySpan<char> text)
    {
        for (var i = text.Length - 1; i > 0; i--)
        {
            if (text[i] == '。' || (char.IsWhiteSpace(text[i]) && text[i - 1] is '.' or '!' or '?'))
            {
                return i + 1;
            }
        }

        return 0;
    }

    [GeneratedRegex(@"^\[Page (\d+)\]$", RegexOptions.Multiline)]
    private static partial Regex PageMarker();

    // A run of text that starts with a non-space character and continues across single line breaks,
    // ending at a blank line.
    [GeneratedRegex(@"\S(?:[^\n]|\n(?![ \t]*(?:\n|$)))*")]
    private static partial Regex Paragraph();
}
