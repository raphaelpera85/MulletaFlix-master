using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Books;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace MulletaFlix.Server.Implementations.Books
{
    /// <summary>
    /// Service that manages book reading: detects format, converts to EPUB, caches results.
    /// </summary>
    public class BookConversionService : IBookConversionService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<BookConversionService> _logger;
        private readonly IEnumerable<IBookToEpubConverter> _converters;

        // Cache fingerprint -> result
        private static readonly ConcurrentDictionary<string, CachedEpub> _cache = new();

        // Lock per item to deduplicate simultaneous conversions
        private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _conversionLocks = new();

        private const string CacheFolderName = "book-reader-cache";

        // Formats that open directly
        private static readonly HashSet<string> DirectOpenFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".epub"
        };

        // Formats that are comics and should use comics reader
        private static readonly HashSet<string> ComicFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cbz", ".cbr", ".cb7", ".cbt"
        };

        // Formats that need conversion to EPUB
        private static readonly HashSet<string> ConvertibleFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".mobi", ".azw", ".azw3", ".txt", ".html", ".htm"
        };

        public BookConversionService(
            ILibraryManager libraryManager,
            ILogger<BookConversionService> logger,
            IEnumerable<IBookToEpubConverter> converters)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _converters = converters;
        }

        public async Task<BookReaderStatus> GetStatusAsync(Guid itemId, CancellationToken cancellationToken)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
                return BookReaderStatus.Failed;

            var ext = GetExtension(item);
            if (string.IsNullOrEmpty(ext))
                return BookReaderStatus.Unsupported;

            if (DirectOpenFormats.Contains(ext))
                return BookReaderStatus.Direct;

            if (ComicFormats.Contains(ext))
                return BookReaderStatus.Direct; // comics reader handles these

            if (!ConvertibleFormats.Contains(ext))
                return BookReaderStatus.Unsupported;

            // Check cache
            var fingerprint = ComputeFingerprint(item);
            var cacheKey = $"{itemId}_{fingerprint}";
            if (_cache.TryGetValue(cacheKey, out var cached) && cached.IsValid)
                return BookReaderStatus.Ready;

            // Check if a conversion is running
            if (_conversionLocks.ContainsKey(itemId))
                return BookReaderStatus.Converting;

            return BookReaderStatus.Ready; // We'll convert on demand
        }

        public async Task<BookStreamResult?> GetEpubStreamAsync(Guid itemId, CancellationToken cancellationToken)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
                return null;

            var ext = GetExtension(item);
            if (string.IsNullOrEmpty(ext))
                return null;

            // Direct open formats
            if (DirectOpenFormats.Contains(ext))
            {
                return new BookStreamResult
                {
                    FilePath = item.Path,
                    ContentType = "application/epub+zip",
                    Extension = ext,
                    IsOriginal = true
                };
            }

            // Comic formats: pass through for comics reader
            if (ComicFormats.Contains(ext))
            {
                return new BookStreamResult
                {
                    FilePath = item.Path,
                    ContentType = GetContentType(ext),
                    Extension = ext,
                    IsOriginal = true
                };
            }

            // Convertible formats
            if (!ConvertibleFormats.Contains(ext))
                return null;

            return await ConvertAndCacheAsync(item, ext, cancellationToken).ConfigureAwait(false);
        }

        public string GetContentType(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".epub" => "application/epub+zip",
                ".pdf" => "application/pdf",
                ".mobi" or ".azw" or ".azw3" => "application/x-mobipocket-ebook",
                ".txt" => "text/plain",
                ".html" or ".htm" => "text/html",
                ".cbz" => "application/x-cbz",
                ".cbr" => "application/x-cbr",
                ".cb7" => "application/x-cb7",
                ".cbt" => "application/x-cbt",
                _ => "application/octet-stream"
            };
        }

        private async Task<BookStreamResult?> ConvertAndCacheAsync(BaseItem item, string ext, CancellationToken cancellationToken)
        {
            var fingerprint = ComputeFingerprint(item);
            var cacheKey = $"{item.Id}_{fingerprint}";

            // Check cache first
            if (_cache.TryGetValue(cacheKey, out var cached) && cached.IsValid)
            {
                if (File.Exists(cached.EpubPath))
                {
                    _logger.LogInformation("BookReader: Using cached EPUB for item {ItemId} ({Fingerprint})", item.Id, fingerprint);
                    return new BookStreamResult
                    {
                        FilePath = cached.EpubPath,
                        ContentType = "application/epub+zip",
                        Extension = ".epub",
                        IsOriginal = false
                    };
                }

                // Cache file gone, remove entry
                _cache.TryRemove(cacheKey, out _);
            }

            // Deduplicate simultaneous conversions of the same item
            var semaphore = _conversionLocks.GetOrAdd(item.Id, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // Double-check cache after acquiring lock
                if (_cache.TryGetValue(cacheKey, out var recheck) && recheck.IsValid && File.Exists(recheck.EpubPath))
                {
                    return new BookStreamResult
                    {
                        FilePath = recheck.EpubPath,
                        ContentType = "application/epub+zip",
                        Extension = ".epub",
                        IsOriginal = false
                    };
                }

                // Find converter
                var converter = _converters.FirstOrDefault(c => c.CanConvert(ext));
                if (converter == null)
                {
                    _logger.LogWarning("BookReader: No converter found for extension {Ext} (item {ItemId})", ext, item.Id);
                    return null;
                }

                // Prepare cache directory
                var cacheDir = GetCacheDir(item, fingerprint);
                Directory.CreateDirectory(cacheDir);
                var outputPath = Path.Combine(cacheDir, "book.epub");

                var request = new BookConversionRequest
                {
                    ItemId = item.Id,
                    SourcePath = item.Path,
                    Extension = ext,
                    OutputDirectory = cacheDir,
                    OutputPath = outputPath
                };

                var result = await converter.ConvertToEpubAsync(request, cancellationToken).ConfigureAwait(false);

                if (result.Success && File.Exists(result.EpubPath))
                {
                    _cache[cacheKey] = new CachedEpub
                    {
                        EpubPath = result.EpubPath,
                        Fingerprint = fingerprint,
                        CreatedAt = DateTime.UtcNow,
                        IsValid = true
                    };

                    return new BookStreamResult
                    {
                        FilePath = result.EpubPath,
                        ContentType = "application/epub+zip",
                        Extension = ".epub",
                        IsOriginal = false
                    };
                }

                _logger.LogError("BookReader: Conversion failed for item {ItemId}: {Error}", item.Id, result.ErrorMessage);
                return null;
            }
            finally
            {
                semaphore.Release();
                // Clean up semaphore if no one is using it
                if (semaphore.CurrentCount == 1)
                {
                    _conversionLocks.TryRemove(item.Id, out _);
                }
            }
        }

        private static string GetExtension(BaseItem item)
        {
            if (string.IsNullOrEmpty(item.Path))
                return null;

            return Path.GetExtension(item.Path);
        }

        private static string ComputeFingerprint(BaseItem item)
        {
            if (string.IsNullOrEmpty(item.Path))
                return "unknown";

            var fileInfo = new FileInfo(item.Path);
            var fingerprint = $"{item.Path}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc:O}|1.0";
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint));
            return Convert.ToHexStringLower(bytes)[..16];
        }

        private string GetCacheDir(BaseItem item, string fingerprint)
        {
            var dataPath = _libraryManager.GetItemById(item.Id)?.Path;
            // Use application data path
            var appPaths = GetAppPaths();
            return Path.Combine(appPaths, CacheFolderName, item.Id.ToString("N"), fingerprint);
        }

        private string GetAppPaths()
        {
            // This is a simplified approach - in production, inject IApplicationPaths
            var basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MulletaFlix");
            return basePath;
        }

        private class CachedEpub
        {
            public string EpubPath { get; set; } = string.Empty;
            public string Fingerprint { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public bool IsValid { get; set; }
        }
    }
}
