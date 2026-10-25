using FolderChat.Application.Knowledge;
using FolderChat.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace FolderChat.Web.Controllers.Api;

/// <summary>Knowledge folders: folders on this computer that FolderChat reads, so chats can answer from them.</summary>
[Route("api/folders")]
public sealed class FoldersController(FolderService folders, IFolderPicker picker) : ApiControllerBase
{
    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct) =>
        Handle(async () => Ok(await folders.ListAsync(ct)));

    [HttpGet("{id:guid}")]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        Handle(async () => Ok(await folders.GetAsync(id, ct)));

    /// <summary>Adds a folder by its full path and starts reading it in the background.</summary>
    [HttpPost]
    public Task<IActionResult> Add(AddFolderRequest request, CancellationToken ct) =>
        Handle(async () =>
        {
            var folder = await folders.AddAsync(request, ct);
            return CreatedAtAction(nameof(Get), new { id = folder.Id }, folder);
        });

    /// <summary>
    /// Opens the computer's own window for choosing a folder, File Explorer's on Windows, and adds the folder the user
    /// chooses, or finds it if it was added before. No content when they cancel. The window opens on the computer running
    /// FolderChat, so only a browser on that computer can ask for it.
    /// </summary>
    [HttpPost("choose")]
    public Task<IActionResult> Choose(CancellationToken ct) =>
        Handle(async () =>
        {
            if (!picker.CanOpenFor(HttpContext))
            {
                return Problem("FolderChat can't open a window for choosing a folder here. Paste the folder's path instead.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var path = await picker.ChooseAsync(ct);
            return path is null ? NoContent() : Ok(await folders.AddChosenAsync(path, ct));
        });

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, UpdateFolderRequest request, CancellationToken ct) =>
        Handle(async () => Ok(await folders.UpdateAsync(id, request, ct)));

    /// <summary>Forgets the folder and everything read from it. The files themselves aren't touched.</summary>
    [HttpDelete("{id:guid}")]
    public Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        Handle(async () =>
        {
            await folders.DeleteAsync(id, ct);
            return NoContent();
        });

    /// <summary>Checks the folder for new, changed and deleted files now.</summary>
    [HttpPost("{id:guid}/rescan")]
    public Task<IActionResult> Rescan(Guid id, CancellationToken ct) =>
        Handle(async () => Ok(await folders.RescanAsync(id, ct)));

    /// <summary>Every file found in the folder, with what FolderChat made of it.</summary>
    [HttpGet("{id:guid}/files")]
    public Task<IActionResult> Files(Guid id, CancellationToken ct) =>
        Handle(async () => Ok(await folders.ListFilesAsync(id, ct)));

    /// <summary>A passage's text, for a citation the user clicked.</summary>
    [HttpGet("passages/{passageId:long}")]
    public Task<IActionResult> Passage(long passageId, CancellationToken ct) =>
        Handle(async () => Ok(await folders.GetPassageAsync(passageId, ct)));

    /// <summary>Opens the computer's file manager with the file selected. It doesn't open or run the file.</summary>
    [HttpPost("files/{fileId:guid}/reveal")]
    public Task<IActionResult> Reveal(Guid fileId, CancellationToken ct) =>
        Handle(async () =>
        {
            var path = await folders.GetFullPathAsync(fileId, ct);
            return FileRevealer.Reveal(path)
                ? NoContent()
                : Problem("FolderChat couldn't open the file manager on this computer.", statusCode: StatusCodes.Status400BadRequest);
        });
}
