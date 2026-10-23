using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace FolderChat.Tests.Fakes;

/// <summary>Builds real PDFs and Word documents in memory, so tests need no sample files.</summary>
internal static class TestFiles
{
    /// <summary>A PDF with one page per array, each holding the given lines. An empty array makes a page with no text, like a scan.</summary>
    public static byte[] Pdf(params string[][] pages)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var lines in pages)
        {
            var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
            var y = 750.0;
            foreach (var line in lines)
            {
                page.AddText(line, 12, new PdfPoint(50, y), font);
                y -= 20;
            }
        }

        return builder.Build();
    }

    public static byte[] Docx(params OpenXmlElement[] body)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(body));
            main.Document.Save();
        }

        return stream.ToArray();
    }

    /// <summary>A Word paragraph, optionally with a style such as <c>Heading1</c>, or as a list item.</summary>
    public static Paragraph Paragraph(string text, string? style = null, bool listItem = false)
    {
        var properties = new ParagraphProperties();
        if (style is not null)
        {
            properties.ParagraphStyleId = new ParagraphStyleId { Val = style };
        }

        if (listItem)
        {
            properties.NumberingProperties = new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 });
        }

        return new Paragraph(properties, Run(text));
    }

    public static Run Run(string text) => new(new Text(text) { Space = SpaceProcessingModeValues.Preserve });

    public static Table Table(params string[][] rows) =>
        new(rows.Select(cells => new TableRow(cells.Select(cell => new TableCell(Paragraph(cell))))));
}
