using FolderChat.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace FolderChat.Web.Controllers.Api;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// Runs an action and turns the app's expected failures (bad input, a missing item, a provider that
    /// rejects a key) into problem responses. Catching them here, inside our own code, keeps the Visual
    /// Studio debugger from stopping on each one as a "user-unhandled" exception. Anything else still goes
    /// to the global exception handler.
    /// </summary>
    protected async Task<IActionResult> Handle(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ValidationFailedException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Invalid request");
        }
        catch (NotFoundException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound, title: "Not found");
        }
        catch (ProviderCallFailedException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway, title: "Provider error");
        }
    }
}
