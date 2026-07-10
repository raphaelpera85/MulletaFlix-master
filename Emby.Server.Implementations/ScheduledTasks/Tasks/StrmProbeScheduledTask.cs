using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.Api.Jobs;
using MulletaFlix.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.ScheduledTasks.Tasks
{
    /// <summary>
    /// Scheduled Task for manually or automatically triggering STRM media info probing.
    /// This task delegates the actual work to the MulletaFlix Job Queue (IJobQueue) so it appears
    /// in the "Fila de Trabalhos" dashboard with real-time logs and progress, while keeping the execution tracked.
    /// </summary>
    public class StrmProbeScheduledTask : IScheduledTask
    {
        private readonly IItemRepository _itemRepository;
        private readonly IFileSystem _fileSystem;
        private readonly IJobQueue _jobQueue;
        private readonly ILogger<StrmProbeScheduledTask> _logger;

        public StrmProbeScheduledTask(
            IItemRepository itemRepository,
            IFileSystem fileSystem,
            IJobQueue jobQueue,
            ILogger<StrmProbeScheduledTask> logger)
        {
            _itemRepository = itemRepository;
            _fileSystem = fileSystem;
            _jobQueue = jobQueue;
            _logger = logger;
        }

        /// <inheritdoc />
        public string Name => "Identificar Áudio e Legendas de STRMs";

        /// <inheritdoc />
        public string Description => "Identifica idiomas de áudio, codecs e faixas de legenda para arquivos .strm com links externos.";

        /// <inheritdoc />
        public string Category => "Biblioteca";

        /// <inheritdoc />
        public string Key => "ProbeStrmMediaInfo";

        /// <inheritdoc />
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // Default: run once a day in the background
            yield return new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromDays(1).Ticks
            };
        }

        /// <inheritdoc />
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

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

            var totalLibraryItems = items.Count;
            var correlationId = $"strm-probe-scheduled-task";

            // If a probe job is already running in the queue, cancel it or wait.
            // For safety, we cancel any existing job with this correlation ID.
            _jobQueue.CancelByCorrelationId(correlationId);

            _logger.LogInformation("Enqueueing STRM probe job in MulletaFlix Job Queue...");
            var job = _jobQueue.Enqueue(
                "MetadataRefresh",
                $"Reconhecimento de Áudio/Legendas ({strmItems.Count} STRMs)",
                async (jobToken, jobProgress) =>
                {
                    jobProgress.Report(new JobQueueProgress(0, "Preparando", $"Biblioteca: {totalLibraryItems} mídias. Preparando varredura."));

                    var activeItems = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
                    using (var semaphore = new SemaphoreSlim(2, 2))
                    {
                        var processedCount = 0;
                        var total = strmItems.Count;

                        var tasks = strmItems.Select(async item =>
                        {
                            await semaphore.WaitAsync(jobToken).ConfigureAwait(false);

                            var itemDisplayName = System.IO.Path.GetFileNameWithoutExtension(item.Path);
                            if (string.IsNullOrEmpty(itemDisplayName))
                            {
                                if (item is MediaBrowser.Controller.Entities.TV.Episode episode)
                                {
                                    var seriesName = episode.FindSeriesName();
                                    itemDisplayName = !string.IsNullOrEmpty(seriesName) ? $"{seriesName} - {item.Name}" : item.Name;
                                }
                                else
                                {
                                    itemDisplayName = item.Name;
                                }
                            }

                            activeItems.TryAdd(item.Id.ToString(), itemDisplayName);
                            var activeList = string.Join(", ", activeItems.Values);
                            jobProgress.Report(new JobQueueProgress(
                                (int)((double)processedCount / total * 100),
                                "Varrendo",
                                $"Biblioteca: {totalLibraryItems} mídias. Probing STRMs: {processedCount} de {total} finalizados. Processando: {activeList}"));

                            try
                            {
                                jobToken.ThrowIfCancellationRequested();

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
                                activeItems.TryRemove(item.Id.ToString(), out _);
                                semaphore.Release();
                                var current = Interlocked.Increment(ref processedCount);
                                var percent = (int)((double)current / total * 100);
                                var remainingList = string.Join(", ", activeItems.Values);
                                var progressMsg = $"Biblioteca: {totalLibraryItems} mídias. Probing STRMs: {current} de {total} finalizados.";
                                if (!string.IsNullOrEmpty(remainingList))
                                {
                                    progressMsg += $" Processando: {remainingList}";
                                }
                                jobProgress.Report(new JobQueueProgress(
                                    percent,
                                    "Varrendo",
                                    progressMsg));
                            }
                        });

                        await Task.WhenAll(tasks).ConfigureAwait(false);
                    }

                    jobProgress.Report(new JobQueueProgress(100, "Concluido", $"Biblioteca: {totalLibraryItems} mídias. Todos os {strmItems.Count} STRMs foram identificados."));
                },
                correlationId);

            // Wait for the enqueued job to complete and report its progress to the Scheduled Task UI.
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                var currentJob = _jobQueue.GetJob(job.Id);
                if (currentJob == null)
                {
                    break;
                }

                progress.Report(currentJob.Progress);

                if (string.Equals(currentJob.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (string.Equals(currentJob.Status, "Failed", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(currentJob.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"O trabalho na fila falhou ou foi cancelado: {currentJob.ErrorMessage}");
                }
            }

            progress.Report(100);
        }
    }
}
