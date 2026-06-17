using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Configuration;

namespace MulletaFlix.Server.Migrations.Routines;

/// <summary>
/// Migration to disable legacy authorization in the system config.
/// </summary>
[MulletaFlixMigration("2026-05-31T16:00:00", nameof(DisableLegacyAuthorization), Stage = Stages.MulletaFlixMigrationStageTypes.CoreInitialisation)]
public class DisableLegacyAuthorization : IAsyncMigrationRoutine
{
    private readonly IServerConfigurationManager _serverConfigurationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="DisableLegacyAuthorization"/> class.
    /// </summary>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    public DisableLegacyAuthorization(IServerConfigurationManager serverConfigurationManager)
    {
        _serverConfigurationManager = serverConfigurationManager;
    }

    /// <inheritdoc />
    public Task PerformAsync(CancellationToken cancellationToken)
    {
        _serverConfigurationManager.Configuration.EnableLegacyAuthorization = false;
        _serverConfigurationManager.SaveConfiguration();

        return Task.CompletedTask;
    }
}

