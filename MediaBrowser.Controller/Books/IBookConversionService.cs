using System;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Controller.Books
{
    /// <summary>
    /// Service for managing book reading including format detection,
    /// conversion to EPUB, and caching.
    /// </summary>
    public interface IBookConversionService
    {
        /// <summary>
        /// Gets the reader status for a given item ID.
        /// </summary>
        Task<BookReaderStatus> GetStatusAsync(Guid itemId, CancellationToken cancellationToken);

        /// <summary>
        /// Gets the EPUB stream for reading a book.
        /// Returns the original EPUB if the format is EPUB, or converts
        /// to EPUB if needed. Returns null if the format is unsupported.
        /// </summary>
        Task<BookStreamResult?> GetEpubStreamAsync(Guid itemId, CancellationToken cancellationToken);

        /// <summary>
        /// Gets the content type for the book stream response.
        /// </summary>
        string GetContentType(string extension);
    }

    /// <summary>
    /// Result containing a stream for book reading.
    /// </summary>
    public class BookStreamResult
    {
        /// <summary>
        /// Gets or sets the file path to stream.
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the content type.
        /// </summary>
        public string ContentType { get; set; } = "application/epub+zip";

        /// <summary>
        /// Gets or sets the original file extension.
        /// </summary>
        public string Extension { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether the result is the original file or converted.
        /// </summary>
        public bool IsOriginal { get; set; } = true;
    }
}
