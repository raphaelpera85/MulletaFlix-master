using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Emby.Server.Implementations.ScheduledTasks.Tasks;

namespace Emby.Server.Implementations.Library
{
    /// <summary>
    /// Post-scan task to trigger the StrmProbeScheduledTask automatically after the main metadata scan completes.
    /// This keeps the main library scan fast and offloads STRM probing to the scheduled tasks system.
    /// </summary>
    public class StrmProbePostScanTask : ILibraryPostScanTask
    {
        private readonly ITaskManager _taskManager;
        private readonly ILogger<StrmProbePostScanTask> _logger;

        public StrmProbePostScanTask(
            ITaskManager taskManager,
            ILogger<StrmProbePostScanTask> logger)
        {
            _taskManager = taskManager;
            _logger = logger;
        }

        /// <inheritdoc />
        public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Main scan complete. Queueing StrmProbeScheduledTask to identify STRM streams in background...");

            // Queue the scheduled task so it runs in the background.
            // If it is already running, this will do nothing (which is safe).
            _taskManager.QueueIfNotRunning<StrmProbeScheduledTask>();

            progress.Report(100);
            return Task.CompletedTask;
        }
    }
}
