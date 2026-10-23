namespace FolderChat.Application.Abstractions;

/// <summary>Reads the text out of a file: a PDF, a Word document or a plain text file.</summary>
public interface ITextExtractor
{
    /// <summary>Returns the file's text, cut off after <paramref name="maxLength"/> characters.</summary>
    /// <param name="fileName">Helps tell the file's type. The content decides first, so a renamed file still works.</param>
    /// <exception cref="Common.ValidationFailedException">The file's type isn't supported, or the file couldn't be read. The message says why.</exception>
    ExtractedText Extract(string fileName, ReadOnlyMemory<byte> content, int maxLength, CancellationToken ct = default);
}

/// <summary>The text read from a file. It can be empty, for example for a scanned PDF.</summary>
/// <param name="MediaType">How the file was read: one of <see cref="Attachments.MediaTypes"/>.</param>
/// <param name="IsTruncated">True when the file held more than the requested number of characters.</param>
/// <param name="PageCount">The number of pages, for formats that have pages.</param>
public sealed record ExtractedText(string Text, string MediaType, bool IsTruncated = false, int? PageCount = null);
