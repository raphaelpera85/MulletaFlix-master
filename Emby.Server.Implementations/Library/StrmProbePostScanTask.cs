using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.Data.Enums;
using MulletaFlix.Api.Jobs;
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
    /// This keeps the main library scan extremely fast and error-free by delegating the heavy remote network
    /// probes (which fetch subtitle/audio languages) to the MulletaFlix Background Job Queue (IJobQueue).
    /// </summary>
    public class StrmProbePostScanTask : ILibraryPostScanTask
    {
        private readonly IItemRepository _itemRepository;
        private readonly IFileSystem _fileSystem;
        private readonly IJobQueue _jobQueue;
        private readonly ILogger<StrmProbePostScanTask> _logger;

        public StrmProbePostScanTask(
            IItemRepository itemRepository,
            IFileSystem fileSystem,
            IJobQueue jobQueue,
            ILogger<StrmProbePostScanTask> logger)
        {
            _itemRepository = itemRepository;
            _fileSystem = fileSystem;
            _jobQueue = jobQueue;
            _logger = logger;
        }

        /// <inheritdoc />
        public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Checking for unprobed STRM files to queue in background...");

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
                return Task.CompletedTask;
            }

            _logger.LogInformation("Found {Count} STRM items requiring media info probing. Enqueueing to JobQueue...", strmItems.Count);

            var totalLibraryItems = items.Count;

            // Enqueue a background job in the official MulletaFlix Job Queue (visible in the Dashboard).
            // Uses the "MetadataRefresh" kind which natively handles throttling and progress updates.
            _jobQueue.Enqueue(
                "MetadataRefresh",
                $"Reconhecimento de Áudio/Legendas ({strmItems.Count} STRMs)",
                async (jobToken, jobProgress) =>
                {
                    jobProgress.Report(new JobQueueProgress(0, "Preparando", $"Biblioteca: {totalLibraryItems} mídias. Preparando varredura."));

                    // Control concurrency strictly during background probing to avoid CDN bans or rate-limiting.
                    // 2 simultaneous requests is safe and will not trigger "streams and format are both null".
                    using (var semaphore = new SemaphoreSlim(2, 2))
                    {
                        var processedCount = 0;
                        var total = strmItems.Count;

                        var tasks = strmItems.Select(async item =>
                        {
                            await semaphore.WaitAsync(jobToken).ConfigureAwait(false);
                            try
                            {
                                jobToken.ThrowIfCancellationRequested();

                                // Trigger remote probe by setting EnableRemoteContentProbe = true.
                                await item.RefreshMetadata(
                                    new MetadataRefreshOptions(new DirectoryService(_fileSystem))
                                    {
                                        EnableRemoteContentProbe = true,
                                        MetadataRefreshMode = MetadataRefreshMode.Default
                                    },
                                    jobToken).ConfigureAwait(false);
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
                                var percent = (int)((double)current / total * 100);
                                jobProgress.Report(new JobQueueProgress(
                                    percent,
                                    "Varrendo",
                                    $"Biblioteca: {totalLibraryItems} mídias. Probing STRMs: {current} de {total} finalizados."));
                            }
                        });

                        await Task.WhenAll(tasks).ConfigureAwait(false);
                    }

                    jobProgress.Report(new JobQueueProgress(100, "Concluido", $"Biblioteca: {totalLibraryItems} mídias. Todos os {strmItems.Count} STRMs foram identificados."));
                });


            progress.Report(100);
            return Task.CompletedTask;
        }
    }
}

