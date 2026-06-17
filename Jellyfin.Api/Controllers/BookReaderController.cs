using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Books;
using MediaBrowser.Controller.Library;
using MulletaFlix.Api.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace MulletaFlix.Api.Controllers
{
    /// <summary>
    /// Book Reader Controller. Handles streaming EPUB content for in-browser reading.
    /// Does NOT require download permission — only requires access to the item.
    /// </summary>
    [Authorize]
    [Route("[controller]")]
    public class BookReaderController : BaseMulletaFlixApiController
    {
        private readonly IBookConversionService _bookConversionService;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<BookReaderController> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="BookReaderController"/> class.
        /// </summary>
        public BookReaderController(
            IBookConversionService bookConversionService,
            ILibraryManager libraryManager,
            ILogger<BookReaderController> logger)
        {
            _bookConversionService = bookConversionService;
            _libraryManager = libraryManager;
            _logger = logger;
        }

        /// <summary>
        /// Gets the EPUB content for reading a book.
        /// Returns the original EPUB or a converted EPUB.
        /// Does NOT require download permission — only item access.
        /// </summary>
        /// <param name="itemId">The item ID.</param>
        /// <param name="userId">The user ID (optional, extracted from token if not provided).</param>
        /// <response code="200">EPUB stream returned successfully.</response>
        /// <response code="404">Item not found.</response>
        /// <response code="415">Unsupported media type — format cannot be read or converted.</response>
        /// <response code="500">Conversion or streaming error.</response>
        /// <returns>A file stream with the EPUB content.</returns>
        [HttpGet("Items/{itemId}/BookReader/Epub")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult> GetEpub(
            [FromRoute, Required] Guid itemId,
            [FromQuery] Guid? userId = null)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
            {
                return NotFound("Item not found.");
            }

            if (!item.IsVisibleStandalone(null))
            {
                var requestUserId = RequestHelpers.GetUserId(User, userId);
                if (requestUserId == Guid.Empty)
                {
                    return Unauthorized();
                }
            }

            var result = await _bookConversionService.GetEpubStreamAsync(itemId, HttpContext.RequestAborted)
                .ConfigureAwait(false);

            if (result == null)
            {
                return StatusCode(
                    StatusCodes.Status415UnsupportedMediaType,
                    new BookReaderErrorResponse
                    {
                        ErrorCode = "UnsupportedFormat",
                        Message = "This book format is not supported for reading or conversion."
                    });
            }

            if (!System.IO.File.Exists(result.FilePath))
            {
                _logger.LogError("BookReader: File not found on disk: {Path}", result.FilePath);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new BookReaderErrorResponse
                    {
                        ErrorCode = "FileNotFound",
                        Message = "The book file could not be found on the server."
                    });
            }

            var stream = System.IO.File.OpenRead(result.FilePath);
            var contentType = result.ContentType;
            var fileName = item.Name + Path.GetExtension(result.FilePath);

            _logger.LogInformation(
                "BookReader: Streaming {Type} for item {ItemId} ({Name})",
                result.IsOriginal ? "original" : "converted",
                itemId,
                item.Name);

            return File(stream, contentType, fileName);
        }

        /// <summary>
        /// Gets the reading status for a book item.
        /// </summary>
        /// <param name="itemId">The item ID.</param>
        /// <param name="userId">The user ID (optional).</param>
        /// <response code="200">Status returned successfully.</response>
        /// <response code="404">Item not found.</response>
        /// <returns>A <see cref="BookReaderStatusResponse"/> with the current status.</returns>
        [HttpGet("Items/{itemId}/BookReader/Status")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<BookReaderStatusResponse>> GetStatus(
            [FromRoute, Required] Guid itemId,
            [FromQuery] Guid? userId = null)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
            {
                return NotFound("Item not found.");
            }

            if (!item.IsVisibleStandalone(null))
            {
                var requestUserId = RequestHelpers.GetUserId(User, userId);
                if (requestUserId == Guid.Empty)
                {
                    return Unauthorized();
                }
            }

            var status = await _bookConversionService.GetStatusAsync(itemId, HttpContext.RequestAborted)
                .ConfigureAwait(false);

            return new BookReaderStatusResponse
            {
                Status = status.ToString(),
                ItemId = itemId,
                ItemName = item.Name
            };
        }
    }

    /// <summary>
    /// Response model for book reader status.
    /// </summary>
    public class BookReaderStatusResponse
    {
        /// <summary>
        /// Gets or sets the current status: Direct, Converting, Ready, Unsupported, Failed.
        /// </summary>
        public string Status { get; set; } = "Unsupported";

        /// <summary>
        /// Gets or sets the item ID.
        /// </summary>
        public Guid ItemId { get; set; }

        /// <summary>
        /// Gets or sets the item name.
        /// </summary>
        public string? ItemName { get; set; }
    }

    /// <summary>
    /// Error response model for book reader errors.
    /// </summary>
    public class BookReaderErrorResponse
    {
        /// <summary>
        /// Gets or sets the machine-readable error code.
        /// </summary>
        public string ErrorCode { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the human-readable error message.
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }
}
