using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MulletaFlix.Api.Extensions;

namespace MulletaFlix.Api.Controllers;

[Authorize]
[Route("Videos")]
public sealed class StrmPrebufferController : BaseMulletaFlixApiController
{
    private readonly ILibraryManager _libraryManager;
    private readonly IStrmPrebufferManager _prebufferManager;

    public StrmPrebufferController(ILibraryManager libraryManager, IStrmPrebufferManager prebufferManager)
    {
        _libraryManager = libraryManager;
        _prebufferManager = prebufferManager;
    }

    [HttpGet("{itemId}/Prebuffer")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid itemId, CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById<BaseItem>(itemId, User.GetUserId());
        if (item is null)
        {
            return NotFound();
        }

        Response.ContentType = _prebufferManager.GetContentType(itemId) ?? "application/octet-stream";
        try
        {
            await _prebufferManager.CopyToAsync(itemId, Response.Body, cancellationToken).ConfigureAwait(false);
            return new EmptyResult();
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
    }
}
