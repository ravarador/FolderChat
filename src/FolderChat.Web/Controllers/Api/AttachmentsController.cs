using FolderChat.Application.Attachments;
using Microsoft.AspNetCore.Mvc;

namespace FolderChat.Web.Controllers.Api;

/// <summary>
/// Files are uploaded as soon as they are attached, so problems show before the message is sent.
/// The chat request then lists the IDs of the files to send with the message.
/// </summary>
[Route("api/attachments")]
public sealed class AttachmentsController(AttachmentService attachments) : ApiControllerBase
{
    /// <summary>Room for the multipart headers around the largest allowed file.</summary>
    private const long MaxRequestSize = AttachmentService.MaxFileSize + 1024 * 1024;

    [HttpPost]
    [RequestSizeLimit(MaxRequestSize)]
    public Task<IActionResult> Upload(IFormFile file, CancellationToken ct) =>
        Handle(async () =>
        {
            await using var content = file.OpenReadStream();
            var attachment = await attachments.UploadAsync(file.FileName, content, ct);
            return CreatedAtAction(nameof(Get), new { id = attachment.Id }, attachment);
        });

    /// <summary>The attachment with the text read from it, which is what the model is sent.</summary>
    [HttpGet("{id:guid}")]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        Handle(async () => Ok(await attachments.GetAsync(id, ct)));

    /// <summary>Removes a file that was attached but not sent.</summary>
    [HttpDelete("{id:guid}")]
    public Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        Handle(async () =>
        {
            await attachments.DeleteAsync(id, ct);
            return NoContent();
        });
}
