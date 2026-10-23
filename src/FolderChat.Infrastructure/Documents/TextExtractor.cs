using FolderChat.Application.Abstractions;
using FolderChat.Application.Common;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig.Exceptions;

namespace FolderChat.Infrastructure.Documents;

/// <summary>
/// Reads PDFs with PdfPig, Word documents with the Open XML SDK, and any other file that turns out to be text.
/// A file's first bytes decide how it is read, so a renamed file still works. The extension only helps explain
/// why a file can't be read.
/// </summary>
internal sealed class TextExtractor(ILogger<TextExtractor> logger) : ITextExtractor
{
    private const string WhatWorks = "FolderChat can read PDF, Word (.docx) and text files.";

    private static readonly string[] WordExtensions = [".docx", ".docm", ".dotx", ".dotm"];

    // The first bytes of each format. .docx, .xlsx and .pptx files are ZIP archives; .doc, .xls, .ppt and
    // password-protected Office files are compound files.
    private static ReadOnlySpan<byte> ZipSignature => [0x50, 0x4B, 0x03, 0x04];

    private static ReadOnlySpan<byte> CompoundFileSignature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47];

    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

    private enum Format
    {
        Pdf,
        Zip,
        OfficeLegacy,
        Image,
        Other,
    }

    public ExtractedText Extract(string fileName, ReadOnlyMemory<byte> content, int maxLength, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        switch (Identify(content.Span, extension))
        {
            case Format.Pdf:
                return Read(fileName, "PDF", () => PdfText.Extract(content, maxLength, ct));

            case Format.Zip when WordExtensions.Contains(extension):
                return Read(fileName, "Word document", () => WordText.Extract(content, maxLength, ct));

            case Format.Zip or Format.OfficeLegacy:
                throw new ValidationFailedException(ExplainOfficeFile(fileName, extension));

            case Format.Image:
                throw new ValidationFailedException($"{fileName} is an image. {WhatWorks} Images aren't supported yet.");
        }

        if (extension == ".pdf" || WordExtensions.Contains(extension))
        {
            throw new ValidationFailedException($"{fileName} isn't a real {(extension == ".pdf" ? "PDF" : "Word document")}. It may be damaged, or named with the wrong extension.");
        }

        return PlainText.Extract(content.Span, maxLength)
            ?? throw new ValidationFailedException($"{fileName} can't be attached. {WhatWorks}");
    }

    private static Format Identify(ReadOnlySpan<byte> bytes, string extension)
    {
        // PDFs may have a little junk before the header. Only trust that when the name says PDF, because a
        // text file about PDFs can contain "%PDF-" too.
        if (bytes.StartsWith("%PDF-"u8) || (extension == ".pdf" && bytes[..Math.Min(bytes.Length, 1024)].IndexOf("%PDF-"u8) > 0))
        {
            return Format.Pdf;
        }

        if (bytes.StartsWith(ZipSignature))
        {
            return Format.Zip;
        }

        if (bytes.StartsWith(CompoundFileSignature))
        {
            return Format.OfficeLegacy;
        }

        var isWebp = bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8);
        if (bytes.StartsWith(PngSignature) || bytes.StartsWith(JpegSignature) || bytes.StartsWith("GIF8"u8) || isWebp)
        {
            return Format.Image;
        }

        return Format.Other;
    }

    private static string ExplainOfficeFile(string fileName, string extension) => extension switch
    {
        ".doc" => $"{fileName} is an older Word file. Open it in Word, save it as .docx, and attach that.",
        _ when WordExtensions.Contains(extension) => $"{fileName} is password-protected. Remove the password in Word, then attach it again.",
        ".xls" or ".xlsx" or ".xlsm" or ".ods" => $"{fileName} is a spreadsheet. Save the sheet as CSV and attach that.",
        ".ppt" or ".pptx" or ".odp" => $"{fileName} is a presentation. Save it as a PDF and attach that.",
        _ => $"{fileName} can't be attached. {WhatWorks}",
    };

    private ExtractedText Read(string fileName, string kind, Func<ExtractedText> read)
    {
        try
        {
            return read();
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new ValidationFailedException($"{fileName} is password-protected. Remove the password, then attach it again.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A damaged file is expected, not a bug: one line at Warning, details at Debug. File names stay out of
            // the log, like message text.
            logger.LogWarning("Couldn't read an attached {Kind}: {Reason}", kind, ex.Message);
            logger.LogDebug(ex, "Details of the {Kind} read failure", kind);
            throw new ValidationFailedException($"Couldn't read {fileName}. The {kind} may be damaged.");
        }
    }
}
