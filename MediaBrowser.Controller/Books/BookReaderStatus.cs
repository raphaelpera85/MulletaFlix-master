namespace MediaBrowser.Controller.Books
{
    /// <summary>
    /// Possible statuses for a book reader endpoint.
    /// </summary>
    public enum BookReaderStatus
    {
        /// <summary>
        /// The file opens directly (EPUB, CBZ, etc.)
        /// </summary>
        Direct,

        /// <summary>
        /// Conversion is in progress.
        /// </summary>
        Converting,

        /// <summary>
        /// Converted EPUB is ready in cache.
        /// </summary>
        Ready,

        /// <summary>
        /// Format is not supported for reading.
        /// </summary>
        Unsupported,

        /// <summary>
        /// Conversion or access failed.
        /// </summary>
        Failed
    }
}
