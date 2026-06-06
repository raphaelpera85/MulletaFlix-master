using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.ScheduledTasks.Tasks;

/// <summary>
/// Scheduled task that checks for expired user licenses and disables the associated users.
/// Runs every 15 minutes by default.
/// </summary>
public class ExpireLicensesTask : IScheduledTask
{
    private readonly IUserLicenseManager _licenseManager;
    private readonly ILocalizationManager _localization;
    private readonly ILogger<ExpireLicensesTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExpireLicensesTask"/> class.
    /// </summary>
    /// <param name="licenseManager">The license manager.</param>
    /// <param name="localization">The localization provider.</param>
    /// <param name="logger">The logger.</param>
    public ExpireLicensesTask(
        IUserLicenseManager licenseManager,
        ILocalizationManager localization,
        ILogger<ExpireLicensesTask> logger)
    {
        _licenseManager = licenseManager;
        _localization = localization;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Expire User Licenses";

    /// <inheritdoc />
    public string Description => "Checks for expired user licenses and disables affected user accounts.";

    /// <inheritdoc />
    public string Category => _localization.GetLocalizedString("TasksMaintenanceCategory");

    /// <inheritdoc />
    public string Key => nameof(ExpireLicensesTask);

    /// <inheritdoc/>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting license expiration check...");
        progress.Report(0);

        var disabledCount = await _licenseManager.ExpireOutdatedLicensesAsync(cancellationToken)
            .ConfigureAwait(false);

        if (disabledCount > 0)
        {
            _logger.LogInformation("{Count} user(s) disabled due to expired licenses.", disabledCount);
        }
        else
        {
            _logger.LogDebug("No expired licenses found.");
        }

        progress.Report(100);
    }

    /// <inheritdoc/>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Run every 15 minutes
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromMinutes(15).Ticks
        };
    }
}
