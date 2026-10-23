using System.Text;
using DocumentFormat.OpenXml.Wordprocessing;
using FolderChat.Application.Attachments;
using FolderChat.Application.Common;
using FolderChat.Infrastructure.Documents;
using FolderChat.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace FolderChat.Tests.Infrastructure;

/// <summary>Reads real PDFs and Word documents, built in memory by <see cref="TestFiles"/>.</summary>
public sealed class TextExtractorTests
{
    private readonly TextExtractor extractor = new(NullLogger<TextExtractor>.Instance);

    [Fact]
    public void Pdfs_are_read_page_by_page()
    {
        var pdf = TestFiles.Pdf(["Quarterly report"], ["Revenue grew 12%", "Costs fell"]);

        var result = extractor.Extract("report.pdf", pdf, 10_000);

        Assert.Equal(MediaTypes.Pdf, result.MediaType);
        Assert.Equal(2, result.PageCount);
        Assert.False(result.IsTruncated);
        Assert.Equal("[Page 1]\nQuarterly report\n\n[Page 2]\nRevenue grew 12%\nCosts fell", result.Text);
    }

    [Fact]
    public void Long_pdfs_stop_at_the_limit_but_count_every_page()
    {
        var pages = Enumerable.Range(1, 20).Select(i => new[] { $"Page number {i} has some text on it" }).ToArray();

        var result = extractor.Extract("book.pdf", TestFiles.Pdf(pages), 100);

        Assert.True(result.IsTruncated);
        Assert.Equal(100, result.Text.Length);
        Assert.StartsWith("[Page 1]\nPage number 1", result.Text);
        Assert.Equal(20, result.PageCount);
    }

    [Fact]
    public void Pdfs_without_text_come_back_empty()
    {
        var result = extractor.Extract("scan.pdf", TestFiles.Pdf([], []), 10_000);

        Assert.Equal(("", MediaTypes.Pdf, 2), (result.Text, result.MediaType, result.PageCount));
    }

    [Fact]
    public void A_renamed_pdf_is_still_read_as_a_pdf()
    {
        var result = extractor.Extract("notes.txt", TestFiles.Pdf(["Hello"]), 10_000);

        Assert.Equal(MediaTypes.Pdf, result.MediaType);
        Assert.Equal("[Page 1]\nHello", result.Text);
    }

    [Fact]
    public void Word_documents_keep_their_structure()
    {
        var docx = TestFiles.Docx(
            TestFiles.Paragraph("Project plan", style: "Heading1"),
            new Paragraph(TestFiles.Run("Owner:"), new Run(new TabChar()), TestFiles.Run("Sam"), new Run(new Break()), TestFiles.Run("Due Friday")),
            TestFiles.Paragraph("Write tests", listItem: true),
            TestFiles.Paragraph("Update the docs", listItem: true),
            new Paragraph(TestFiles.Run("Keep this"), new DeletedRun(new Run(new DeletedText(" but not this")))),
            TestFiles.Paragraph("   "),
            TestFiles.Table(["Name", "Role"], ["Ana", "Lead | owner"]));

        var result = extractor.Extract("plan.docx", docx, 10_000);

        Assert.Equal(MediaTypes.Word, result.MediaType);
        Assert.Null(result.PageCount);
        Assert.Equal(
            "# Project plan\n\nOwner:\tSam\nDue Friday\n\n- Write tests\n- Update the docs\n\nKeep this\n\n| Name | Role |\n| --- | --- |\n| Ana | Lead \\| owner |",
            result.Text);
    }

    [Fact]
    public void Long_word_documents_stop_at_the_limit()
    {
        var paragraphs = Enumerable.Range(1, 50).Select(i => TestFiles.Paragraph($"Paragraph {i} of a long document.")).ToArray();

        var result = extractor.Extract("long.docx", TestFiles.Docx(paragraphs), 200);

        Assert.True(result.IsTruncated);
        Assert.Equal(200, result.Text.Length);
        Assert.StartsWith("Paragraph 1 of a long document.\n\nParagraph 2", result.Text);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-8 with mark")]
    [InlineData("utf-16")]
    [InlineData("windows-1252")]
    public void Text_files_are_decoded_whatever_their_encoding(string encoding)
    {
        const string text = "Café “menu”\r\n- Crème brûlée\r\n";
        var bytes = encoding switch
        {
            "utf-8" => new UTF8Encoding(false).GetBytes(text),
            "utf-8 with mark" => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)],
            "utf-16" => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)],
            _ => CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetBytes(text),
        };

        var result = extractor.Extract("menu.txt", bytes, 10_000);

        Assert.Equal(MediaTypes.PlainText, result.MediaType);
        Assert.Equal("Café “menu”\n- Crème brûlée\n", result.Text);
    }

    [Theory]
    [InlineData("Program.cs")]
    [InlineData("data.csv")]
    [InlineData("README")]
    public void Any_text_file_can_be_read(string fileName)
    {
        var result = extractor.Extract(fileName, "line one\nline two"u8.ToArray(), 10_000);

        Assert.Equal("line one\nline two", result.Text);
    }

    [Fact]
    public void Text_is_cut_at_the_limit_without_splitting_a_character()
    {
        // The emoji takes two UTF-16 code units, and the limit falls between them.
        var result = extractor.Extract("chat.txt", Encoding.UTF8.GetBytes("abc😀def"), 4);

        Assert.True(result.IsTruncated);
        Assert.Equal("abc", result.Text);
    }

    [Theory]
    [InlineData("photo.png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0 }, "photo.png is an image.")]
    [InlineData("old.doc", new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0 }, "Open it in Word, save it as .docx")]
    [InlineData("secret.docx", new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0 }, "secret.docx is password-protected.")]
    [InlineData("budget.xlsx", new byte[] { 0x50, 0x4B, 0x03, 0x04, 0, 0 }, "Save the sheet as CSV")]
    [InlineData("slides.pptx", new byte[] { 0x50, 0x4B, 0x03, 0x04, 0, 0 }, "Save it as a PDF")]
    [InlineData("tool.exe", new byte[] { 0x4D, 0x5A, 0x90, 0, 3, 0, 0, 0 }, "tool.exe can't be attached. FolderChat can read PDF, Word (.docx) and text files.")]
    [InlineData("fake.pdf", new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C, 0x3E }, "fake.pdf isn't a real PDF.")]
    [InlineData("broken.pdf", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37, 0x0A, 0x67, 0x61, 0x72, 0x62 }, "Couldn't read broken.pdf. The PDF may be damaged.")]
    [InlineData("broken.docx", new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0, 0, 0, 0x67, 0x61, 0x72, 0x62 }, "Couldn't read broken.docx. The Word document may be damaged.")]
    public void Files_that_cant_be_read_say_why(string fileName, byte[] content, string expected)
    {
        var error = Assert.Throws<ValidationFailedException>(() => extractor.Extract(fileName, content, 10_000));

        Assert.Contains(expected, error.Message);
    }
}
