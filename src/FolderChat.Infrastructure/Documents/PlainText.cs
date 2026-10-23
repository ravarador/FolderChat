using System.Text;
using FolderChat.Application.Abstractions;
using FolderChat.Application.Attachments;

namespace FolderChat.Infrastructure.Documents;

/// <summary>
/// Reads text files of any kind: notes, Markdown, CSV, JSON, source code. Understands UTF-8, UTF-16 and UTF-32
/// with a byte order mark, UTF-8 without one, and Windows-1252, which older Windows programs still write.
/// </summary>
internal static class PlainText
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly Encoding Windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    // Byte order marks.
    private static ReadOnlySpan<byte> Utf8Mark => [0xEF, 0xBB, 0xBF];

    private static ReadOnlySpan<byte> Utf32LittleEndianMark => [0xFF, 0xFE, 0x00, 0x00];

    private static ReadOnlySpan<byte> Utf16LittleEndianMark => [0xFF, 0xFE];

    private static ReadOnlySpan<byte> Utf16BigEndianMark => [0xFE, 0xFF];

    /// <summary>Returns null when the bytes don't look like text.</summary>
    public static ExtractedText? Extract(ReadOnlySpan<byte> bytes, int maxLength)
    {
        var decoded = Decode(bytes);
        if (decoded is null || !LooksLikeText(decoded))
        {
            return null;
        }

        var text = new BoundedText(maxLength);
        text.Append(decoded.ReplaceLineEndings("\n"));
        return new ExtractedText(text.ToString(), MediaTypes.PlainText, text.IsTruncated);
    }

    internal static string? Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(Utf8Mark))
        {
            return Encoding.UTF8.GetString(bytes[Utf8Mark.Length..]);
        }

        if (bytes.StartsWith(Utf32LittleEndianMark))
        {
            return Encoding.UTF32.GetString(bytes[Utf32LittleEndianMark.Length..]);
        }

        if (bytes.StartsWith(Utf16LittleEndianMark))
        {
            return Encoding.Unicode.GetString(bytes[Utf16LittleEndianMark.Length..]);
        }

        if (bytes.StartsWith(Utf16BigEndianMark))
        {
            return Encoding.BigEndianUnicode.GetString(bytes[Utf16BigEndianMark.Length..]);
        }

        // Text without a byte order mark has no zero bytes. Binary files nearly always do.
        if (bytes.Contains((byte)0))
        {
            return null;
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Windows1252.GetString(bytes);
        }
    }

    /// <summary>False when the start of the text is more than a few percent control characters, as in a binary file.</summary>
    private static bool LooksLikeText(string text)
    {
        var sample = text.AsSpan(0, Math.Min(text.Length, 8_000));
        var control = 0;
        foreach (var c in sample)
        {
            if (c == '\0')
            {
                return false;
            }

            // Tabs, line breaks, form feeds and the escape that starts terminal colors are normal in text files.
            if (char.IsControl(c) && c is not ('\t' or '\n' or '\r' or '\f' or '\v' or '\u001b') && c < '\u007f')
            {
                control++;
            }
        }

        return control <= sample.Length / 20;
    }
}
