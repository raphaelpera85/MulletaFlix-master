using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.Library
{
    /// <summary>
    /// Post-scan task to probe STRM files (external streams) after the main metadata scan completes.
    /// This keeps the main library scan extremely fast and error-free, as heavy remote network probes
    /// for subtitle/audio languages are deferred to this background worker with strict concurrency limits.
    /// </summary>
    public class StrmProbePostScanTask : ILibraryPostScanTask
    {
        private readonly IItemRepository _itemRepository;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger<StrmProbePostScanTask> _logger;

        public StrmProbePostScanTask(
            IItemRepository itemRepository,
            IFileSystem fileSystem,
            ILogger<StrmProbePostScanTask> logger)
        {
            _itemRepository = itemRepository;
            _fileSystem = fileSystem;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting post-scan STRM media info probing...");

            // Query all movies and episodes.
            var items = _itemRepository.GetItemList(new InternalItemsQuery
            {
                CollapseBoxSetItems = false,
                Recursive = true,
                DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions(false),
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode }
            });

            // Filter for remote STRM files that don't have media streams identified yet.
            var strmItems = items
                .Where(x => x.IsShortcut && x.GetMediaStreams().Count == 0)
                .ToList();

            if (strmItems.Count == 0)
            {
                _logger.LogInformation("No unprobed STRM items found.");
                progress.Report(100);
                return;
            }

            _logger.LogInformation("Found {Count} STRM items requiring media info probing.", strmItems.Count);

            // Control concurrency strictly during background probing to avoid CDN ban or rate-limiting.
            // 2 simultaneous requests is safe and will not trigger "streams and format are both null".
            using (var semaphore = new SemaphoreSlim(2, 2))
            {
                var processedCount = 0;
                var total = strmItems.Count;

                var tasks = strmItems.Select(async item =>
                {
                    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        _logger.LogDebug("Probing STRM media info for: {Path}", item.Path);

                        // Trigger remote probe by setting EnableRemoteContentProbe = true.
                        await item.RefreshMetadata(
                            new MetadataRefreshOptions(new DirectoryService(_fileSystem))
                            {
                                EnableRemoteContentProbe = true,
                                MetadataRefreshMode = MetadataRefreshMode.Default
                            },
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error probing STRM media info for {Path}", item.Path);
                    }
                    finally
                    {
                        semaphore.Release();
                        var current = Interlocked.Increment(ref processedCount);
                        progress.Report((double)current / total * 100);
                    }
                });

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            _logger.LogInformation("Finished post-scan STRM media info probing.");
            progress.Report(100);
        }
    }
}
