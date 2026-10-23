namespace FolderChat.Application.Attachments;

/// <summary>An attached file as the browser sees it in lists. <paramref name="CharacterCount"/> is the length of the text kept.</summary>
public sealed record AttachmentSummaryDto(
    Guid Id,
    string FileName,
    string MediaType,
    long Size,
    int CharacterCount,
    bool IsTruncated,
    int? PageCount);

/// <summary>An attached file with the text the model sees.</summary>
public sealed record AttachmentDto(
    Guid Id,
    string FileName,
    string MediaType,
    long Size,
    int CharacterCount,
    bool IsTruncated,
    int? PageCount,
    bool IsSent,
    string Text);

/// <summary>The ways FolderChat reads a file.</summary>
public static class MediaTypes
{
    public const string Pdf = "application/pdf";

    public const string Word = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>Any file read as text: notes, Markdown, CSV, JSON, source code and so on.</summary>
    public const string PlainText = "text/plain";
}
