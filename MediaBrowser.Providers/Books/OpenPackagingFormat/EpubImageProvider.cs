using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Providers.Books.OpenPackagingFormat
{
    /// <summary>
    /// Provides the primary image for EPUB items that have embedded covers.
    /// </summary>
    public class EpubImageProvider : IDynamicImageProvider
    {
        private readonly ILogger<EpubImageProvider> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="EpubImageProvider"/> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger{EpubImageProvider}"/> interface.</param>
        public EpubImageProvider(ILogger<EpubImageProvider> logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public string Name => "EPUB Metadata";

        /// <inheritdoc />
        public bool Supports(BaseItem item)
        {
            return item is Book;
        }

        /// <inheritdoc />
        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            yield return ImageType.Primary;
        }

        /// <inheritdoc />
        public Task<DynamicImageResponse> GetImage(BaseItem item, ImageType type, CancellationToken cancellationToken)
        {
            if (string.Equals(Path.GetExtension(item.Path), ".epub", StringComparison.OrdinalIgnoreCase))
            {
                return GetFromZip(item, cancellationToken);
            }

            return Task.FromResult(new DynamicImageResponse { HasImage = false });
        }

        private async Task<DynamicImageResponse> LoadCover(ZipArchive epub, XmlDocument opf, string opfRootDirectory, CancellationToken cancellationToken)
        {
            var utilities = new OpfReader<EpubImageProvider>(opf, _logger);
            var coverReference = utilities.ReadCoverPath(opfRootDirectory);
            if (coverReference == null)
            {
                coverReference = await TryReadFallbackCoverAsync(epub, opf, opfRootDirectory, cancellationToken).ConfigureAwait(false);
                if (coverReference == null)
                {
                    return new DynamicImageResponse { HasImage = false };
                }
            }

            var cover = coverReference.Value;
            var coverFile = epub.GetEntry(cover.Path);

            if (coverFile == null)
            {
                return new DynamicImageResponse { HasImage = false };
            }

            var memoryStream = new MemoryStream();

            var coverStream = await coverFile.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (coverStream.ConfigureAwait(false))
            {
                await coverStream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            }

            memoryStream.Position = 0;

            var response = new DynamicImageResponse { HasImage = true, Stream = memoryStream };
            response.SetFormatFromMimeType(cover.MimeType);

            return response;
        }

        private async Task<(string MimeType, string Path)?> TryReadFallbackCoverAsync(ZipArchive epub, XmlDocument opf, string opfRootDirectory, CancellationToken cancellationToken)
        {
            var namespaceManager = new XmlNamespaceManager(opf.NameTable);
            namespaceManager.AddNamespace("opf", "http://www.idpf.org/2007/opf");

            var manifestItems = opf
                .SelectNodes("//opf:manifest/opf:item", namespaceManager)?
                .Cast<XmlElement>()
                .ToArray() ?? [];

            var spineItems = opf
                .SelectNodes("//opf:spine/opf:itemref", namespaceManager)?
                .Cast<XmlElement>()
                .ToArray() ?? [];

            foreach (var spineItem in spineItems)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var idref = spineItem.GetAttribute("idref");
                if (string.IsNullOrWhiteSpace(idref))
                {
                    continue;
                }

                var manifestItem = manifestItems.FirstOrDefault(itemNode =>
                    string.Equals(itemNode.GetAttribute("id"), idref, StringComparison.OrdinalIgnoreCase));
                if (manifestItem is null)
                {
                    continue;
                }

                var href = manifestItem.GetAttribute("href");
                var mediaType = manifestItem.GetAttribute("media-type");
                if (string.IsNullOrWhiteSpace(href))
                {
                    continue;
                }

                var entryPath = Path.Combine(opfRootDirectory, Uri.UnescapeDataString(href));
                var entry = epub.GetEntry(entryPath);
                if (entry is null)
                {
                    continue;
                }

                if (IsImageMediaType(mediaType) || IsImageExtension(entry.FullName))
                {
                    return (string.IsNullOrWhiteSpace(mediaType) ? GetMimeTypeFromExtension(entry.FullName) : mediaType, entryPath);
                }

                var htmlImage = await TryReadFirstImageFromHtmlAsync(epub, entry, opfRootDirectory, cancellationToken).ConfigureAwait(false);
                if (htmlImage is not null)
                {
                    return htmlImage;
                }

                break;
            }

            foreach (var manifestItem in manifestItems)
            {
                var href = manifestItem.GetAttribute("href");
                var mediaType = manifestItem.GetAttribute("media-type");
                if (string.IsNullOrWhiteSpace(href) || !IsImageMediaType(mediaType))
                {
                    continue;
                }

                var entryPath = Path.Combine(opfRootDirectory, Uri.UnescapeDataString(href));
                if (epub.GetEntry(entryPath) is not null)
                {
                    return (mediaType, entryPath);
                }
            }

            return null;
        }

        private async Task<(string MimeType, string Path)?> TryReadFirstImageFromHtmlAsync(
            ZipArchive epub,
            ZipArchiveEntry htmlEntry,
            string opfRootDirectory,
            CancellationToken cancellationToken)
        {
            var extension = Path.GetExtension(htmlEntry.FullName);
            if (!string.Equals(extension, ".xhtml", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".htm", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            try
            {
                using var htmlStream = await htmlEntry.OpenAsync(cancellationToken).ConfigureAwait(false);
                var htmlDocument = new XmlDocument();
                htmlDocument.Load(htmlStream);

                var imageNode = htmlDocument.SelectSingleNode("//*[local-name()='img' and @src]")
                    ?? htmlDocument.SelectSingleNode("//*[local-name()='image' and (@src or @href)]");

                var imagePath = imageNode?.Attributes?["src"]?.Value
                    ?? imageNode?.Attributes?["href"]?.Value;
                if (string.IsNullOrWhiteSpace(imagePath))
                {
                    return null;
                }

                var resolvedPath = Path.Combine(Path.GetDirectoryName(htmlEntry.FullName) ?? opfRootDirectory, Uri.UnescapeDataString(imagePath));
                var imageEntry = epub.GetEntry(resolvedPath);
                if (imageEntry is null)
                {
                    return null;
                }

                return (GetMimeTypeFromExtension(imageEntry.FullName), imageEntry.FullName);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to read fallback EPUB cover from {Entry}", htmlEntry.FullName);
                return null;
            }
        }

        private static bool IsImageMediaType(string? mediaType)
        {
            return !string.IsNullOrWhiteSpace(mediaType) && mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsImageExtension(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetMimeTypeFromExtension(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                _ => "image/jpeg"
            };
        }

        private async Task<DynamicImageResponse> GetFromZip(BaseItem item, CancellationToken cancellationToken)
        {
            using var epub = await ZipFile.OpenReadAsync(item.Path, cancellationToken).ConfigureAwait(false);

            var opfFilePath = EpubUtils.ReadContentFilePath(epub);
            if (opfFilePath == null)
            {
                return new DynamicImageResponse { HasImage = false };
            }

            var opfRootDirectory = Path.GetDirectoryName(opfFilePath);
            if (opfRootDirectory == null)
            {
                return new DynamicImageResponse { HasImage = false };
            }

            var opfFile = epub.GetEntry(opfFilePath);
            if (opfFile == null)
            {
                return new DynamicImageResponse { HasImage = false };
            }

            using var opfStream = await opfFile.OpenAsync(cancellationToken).ConfigureAwait(false);

            var opfDocument = new XmlDocument();
            opfDocument.Load(opfStream);

            return await LoadCover(epub, opfDocument, opfRootDirectory, cancellationToken).ConfigureAwait(false);
        }
    }
}
