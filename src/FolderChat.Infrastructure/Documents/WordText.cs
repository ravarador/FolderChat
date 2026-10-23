using System.Runtime.InteropServices;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FolderChat.Application.Abstractions;
using FolderChat.Application.Attachments;

namespace FolderChat.Infrastructure.Documents;

/// <summary>
/// Reads the body of a Word document with the Open XML SDK. Headings start with #, list items with -, and tables
/// become Markdown tables, which helps the model follow the structure. Text deleted in tracked changes is left out.
/// </summary>
internal static class WordText
{
    /// <summary>
    /// The largest document XML read, about 33 million characters. That holds far more text than is kept, and it
    /// stops a small, highly compressed file from expanding into gigabytes of memory.
    /// </summary>
    private const long MaxPartLength = 32 * 1024 * 1024;

    private const string ListItemPrefix = "- ";

    public static ExtractedText Extract(ReadOnlyMemory<byte> content, int maxLength, CancellationToken ct)
    {
        using var stream = AsStream(content);
        using var document = WordprocessingDocument.Open(stream, isEditable: false, new OpenSettings { MaxCharactersInPart = MaxPartLength });
        var text = new BoundedText(maxLength);
        if (document.MainDocumentPart?.Document?.Body is { } body)
        {
            AppendBlocks(body, text, ct);
        }

        return new ExtractedText(text.ToString(), MediaTypes.Word, text.IsTruncated);
    }

    private static void AppendBlocks(OpenXmlElement container, BoundedText text, CancellationToken ct)
    {
        var inList = false;
        foreach (var element in container.ChildElements)
        {
            if (text.IsTruncated)
            {
                return;
            }

            ct.ThrowIfCancellationRequested();
            if (element is Paragraph paragraph)
            {
                var line = new StringBuilder();
                AppendRuns(paragraph, line);
                var paragraphText = line.ToString().Trim();
                if (paragraphText.Length == 0)
                {
                    continue;
                }

                // List items follow each other line by line, like a Markdown list.
                var prefix = Prefix(paragraph);
                var isListItem = prefix == ListItemPrefix;
                if (isListItem && inList)
                {
                    text.AppendLine(prefix + paragraphText);
                }
                else
                {
                    text.AppendBlock(prefix + paragraphText);
                }

                inList = isListItem;
            }
            else if (element is Table table)
            {
                AppendTable(table, text);
                inList = false;
            }
            else if (element is not AlternateContentFallback)
            {
                // Content controls and similar wrappers hold paragraphs and tables of their own.
                AppendBlocks(element, text, ct);
            }
        }
    }

    /// <summary>The text inside a paragraph or table cell, keeping its tabs and line breaks.</summary>
    private static void AppendRuns(OpenXmlElement element, StringBuilder line)
    {
        foreach (var child in element.ChildElements)
        {
            switch (child)
            {
                case Text text:
                    line.Append(text.Text);
                    break;
                case TabChar or PositionalTab:
                    line.Append('\t');
                    break;
                case Break or CarriageReturn:
                    line.Append('\n');
                    break;
                case NoBreakHyphen:
                    line.Append('-');
                    break;
                case Paragraph nested:
                    // A paragraph inside a text box, or one of several in a table cell.
                    if (line.Length > 0)
                    {
                        line.Append('\n');
                    }

                    AppendRuns(nested, line);
                    break;
                case AlternateContentFallback:
                    // A copy of the content just before it, kept for older versions of Word.
                    break;
                default:
                    // Runs, hyperlinks, fields and inserted text hold the text elements above. Deleted text and
                    // field codes are separate element types, so they are skipped.
                    AppendRuns(child, line);
                    break;
            }
        }
    }

    /// <summary>Markdown-style marks for headings and list items.</summary>
    private static string Prefix(Paragraph paragraph)
    {
        var properties = paragraph.ParagraphProperties;
        var style = properties?.ParagraphStyleId?.Val?.Value ?? "";
        if (style.Equals("Title", StringComparison.OrdinalIgnoreCase))
        {
            return "# ";
        }

        if (style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(style.AsSpan("Heading".Length), out var level)
            && level is >= 1 and <= 6)
        {
            return new string('#', level) + " ";
        }

        return properties?.NumberingProperties is null ? "" : ListItemPrefix;
    }

    /// <summary>Adds the table as Markdown, one row per line.</summary>
    private static void AppendTable(Table table, BoundedText text)
    {
        var rows = new List<string>();
        foreach (var row in table.Elements<TableRow>())
        {
            var cells = row.Elements<TableCell>().Select(CellText).ToList();
            if (cells.Count == 0)
            {
                continue;
            }

            rows.Add("| " + string.Join(" | ", cells) + " |");
            if (rows.Count == 1)
            {
                rows.Add("|" + string.Concat(Enumerable.Repeat(" --- |", cells.Count)));
            }
        }

        if (rows.Count > 0)
        {
            text.AppendBlock(string.Join('\n', rows));
        }
    }

    /// <summary>A cell's text on one line, with any | escaped so it doesn't end the cell.</summary>
    private static string CellText(TableCell cell)
    {
        var line = new StringBuilder();
        AppendRuns(cell, line);
        var words = line.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words).Replace("|", "\\|");
    }

    private static MemoryStream AsStream(ReadOnlyMemory<byte> content) =>
        MemoryMarshal.TryGetArray(content, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(content.ToArray(), writable: false);
}
