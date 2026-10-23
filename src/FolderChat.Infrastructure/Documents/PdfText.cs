using FolderChat.Application.Abstractions;
using FolderChat.Application.Attachments;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace FolderChat.Infrastructure.Documents;

/// <summary>Reads a PDF's text page by page with PdfPig. Each page starts with a [Page n] line, so the model can cite pages.</summary>
internal static class PdfText
{
    private static readonly ContentOrderTextExtractor.Options Layout = new()
    {
        SeparateParagraphsWithDoubleNewline = true,
        ReplaceWhitespaceWithSpace = true,
    };

    public static ExtractedText Extract(ReadOnlyMemory<byte> content, int maxLength, CancellationToken ct)
    {
        // Skipping a missing font loses a few letters instead of the whole file.
        using var document = PdfDocument.Open(content, new ParsingOptions { SkipMissingFonts = true });
        var text = new BoundedText(maxLength);
        foreach (var page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();

            // PdfPig ends lines with Environment.NewLine, which is \r\n on Windows.
            var pageText = ContentOrderTextExtractor.GetText(page, Layout).ReplaceLineEndings("\n").Trim();
            if (pageText.Length > 0)
            {
                text.AppendBlock($"[Page {page.Number}]\n{pageText}");
            }

            if (text.IsTruncated)
            {
                break;
            }
        }

        return new ExtractedText(text.ToString(), MediaTypes.Pdf, text.IsTruncated, document.NumberOfPages);
    }
}
