using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.ScheduledTasks
{
    /// <summary>
    /// Scheduled task that cleans up inactive devices every hour.
    /// Devices that have no active session are deleted from the database.
    /// </summary>
    public class DeviceCleanupTask : IScheduledTask
    {
        private static readonly TimeSpan DeviceRetention = TimeSpan.FromDays(90);
        private readonly IDeviceManager _deviceManager;
        private readonly ISessionManager _sessionManager;
        private readonly ILogger<DeviceCleanupTask> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceCleanupTask"/> class.
        /// </summary>
        /// <param name="deviceManager">The device manager.</param>
        /// <param name="sessionManager">The session manager.</param>
        /// <param name="logger">The logger.</param>
        public DeviceCleanupTask(
            IDeviceManager deviceManager,
            ISessionManager sessionManager,
            ILogger<DeviceCleanupTask> logger)
        {
            _deviceManager = deviceManager;
            _sessionManager = sessionManager;
            _logger = logger;
        }

        /// <inheritdoc />
        public string Name => "Limpar dispositivos inativos";

        /// <inheritdoc />
        public string Key => "DeviceCleanup";

        /// <inheritdoc />
        public string Description => "Remove dispositivos que não possuem sessão ativa. Executa a cada 1 hora.";

        /// <inheritdoc />
        public string Category => "Manutenção";

        /// <inheritdoc />
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Iniciando limpeza de dispositivos inativos...");

            // Get all active session device IDs
            var activeSessions = _sessionManager.Sessions.ToList();
            var activeDeviceIds = new HashSet<string>(
                activeSessions
                    .Where(s => s.IsActive && !string.IsNullOrEmpty(s.DeviceId))
                    .Select(s => s.DeviceId!),
                StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation(
                "Sessões ativas encontradas: {ActiveSessionCount}. Dispositivos com sessão ativa: {ActiveDeviceCount}",
                activeSessions.Count(s => s.IsActive),
                activeDeviceIds.Count);

            // Get all devices
            var allDevices = _deviceManager.GetDevicesForUser(null);

            if (allDevices.Items.Count == 0)
            {
                _logger.LogInformation("Nenhum dispositivo encontrado para limpeza.");
                progress.Report(100);
                return;
            }

            int totalDevices = allDevices.Items.Count;
            int deletedCount = 0;
            int keptCount = 0;
            int currentIndex = 0;
            var cutoff = DateTime.UtcNow.Subtract(DeviceRetention);

            foreach (var deviceInfo in allDevices.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                currentIndex++;
                progress.Report((double)currentIndex / totalDevices * 100);

                var isActive = activeDeviceIds.Contains(deviceInfo.Id);
                if (!ShouldDeleteDevice(deviceInfo.DateLastActivity, isActive, cutoff))
                {
                    keptCount++;
                    continue;
                }

                // Get the device entity and delete it
                var device = _deviceManager.GetDevice(deviceInfo.Id);
                if (device is not null)
                {
                    // Get the full Device entity from the DeviceInfoDto
                    var expiredDevices = _deviceManager.GetDevices(
                            new MulletaFlix.Data.Queries.DeviceQuery { DeviceId = deviceInfo.Id })
                        .Items
                        .Where(item => item.DateLastActivity < cutoff)
                        .ToArray();

                    foreach (var fullDevice in expiredDevices)
                    {
                        await _deviceManager.DeleteDevice(fullDevice).ConfigureAwait(false);
                        deletedCount++;
                    }
                }
            }

            _logger.LogInformation(
                "Limpeza de dispositivos concluída. Total: {TotalDevices}, Mantidos (sessão ativa): {KeptCount}, Removidos (inativos): {DeletedCount}",
                totalDevices,
                keptCount,
                deletedCount);
        }

        public static bool ShouldDeleteDevice(DateTime? lastActivity, bool isActive, DateTime cutoff)
        {
            return !isActive && lastActivity.HasValue && lastActivity.Value < cutoff;
        }

        /// <inheritdoc />
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.IntervalTrigger,
                    IntervalTicks = TimeSpan.FromHours(1).Ticks
                }
            };
        }
    }
}
