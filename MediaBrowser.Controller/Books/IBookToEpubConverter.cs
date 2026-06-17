using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Controller.Books
{
    /// <summary>
    /// Result of a book-to-EPUB conversion.
    /// </summary>
    public class BookConversionResult
    {
        /// <summary>
        /// Gets or sets the path to the converted EPUB file.
        /// </summary>
        public string EpubPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether the conversion succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Gets or sets an error message if conversion failed.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Gets or sets a machine-readable error code for the API.
        /// </summary>
        public string? ErrorCode { get; set; }
    }

    /// <summary>
    /// Request for converting a book to EPUB.
    /// </summary>
    public class BookConversionRequest
    {
        /// <summary>
        /// Gets or sets the item ID (GUID).
        /// </summary>
        public System.Guid ItemId { get; set; }

        /// <summary>
        /// Gets or sets the full path to the original file.
        /// </summary>
        public string SourcePath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the original file extension (e.g. ".mobi").
        /// </summary>
        public string Extension { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the output directory for the converted EPUB.
        /// </summary>
        public string OutputDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the output file path for the converted EPUB.
        /// </summary>
        public string OutputPath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Interface for converting book formats to EPUB.
    /// </summary>
    public interface IBookToEpubConverter
    {
        /// <summary>
        /// Determines whether this converter can handle the given file extension.
        /// </summary>
        bool CanConvert(string extension);

        /// <summary>
        /// Converts a book to EPUB asynchronously.
        /// </summary>
        Task<BookConversionResult> ConvertToEpubAsync(BookConversionRequest request, CancellationToken cancellationToken);
    }
}
