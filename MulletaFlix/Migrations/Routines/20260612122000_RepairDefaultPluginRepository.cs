using System;
using System.Linq;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Updates;

namespace MulletaFlix.Server.Migrations.Routines;

/// <summary>
/// Repairs the default plugin repository when branding rewrites or disabled entries hide the public catalog.
/// </summary>
#pragma warning disable CS0618 // Type or member is obsolete
[MulletaFlixMigration("2026-06-12T12:20:00", nameof(RepairDefaultPluginRepository), "74B87B08-2A36-4E41-A138-61E83F90F884", RunMigrationOnSetup = true)]
public class RepairDefaultPluginRepository : IMigrationRoutine
#pragma warning restore CS0618 // Type or member is obsolete
{
    private const string DefaultRepositoryName = "Jellyfin Stable";
    private const string DefaultRepositoryUrl = "https://repo.jellyfin.org/files/plugin/manifest.json";
    private const string OldRepositoryUrl = "https://repo.jellyfin.org/releases/plugin/manifest-stable.json";

    private readonly IServerConfigurationManager _serverConfigurationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="RepairDefaultPluginRepository"/> class.
    /// </summary>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    public RepairDefaultPluginRepository(IServerConfigurationManager serverConfigurationManager)
    {
        _serverConfigurationManager = serverConfigurationManager;
    }

    /// <inheritdoc />
    public void Perform()
    {
        var repositories = _serverConfigurationManager.Configuration.PluginRepositories;
        if (repositories.Length == 0)
        {
            _serverConfigurationManager.Configuration.PluginRepositories =
            [
                new RepositoryInfo
                {
                    Name = DefaultRepositoryName,
                    Url = DefaultRepositoryUrl,
                    Enabled = true
                }
            ];
            _serverConfigurationManager.SaveConfiguration();
            return;
        }

        var updated = false;
        var defaultRepository = repositories.FirstOrDefault(IsDefaultRepository);
        if (defaultRepository is null)
        {
            _serverConfigurationManager.Configuration.PluginRepositories = repositories
                .Append(new RepositoryInfo
                {
                    Name = DefaultRepositoryName,
                    Url = DefaultRepositoryUrl,
                    Enabled = true
                })
                .ToArray();
            updated = true;
        }
        else
        {
            if (!string.Equals(defaultRepository.Name, DefaultRepositoryName, StringComparison.Ordinal))
            {
                defaultRepository.Name = DefaultRepositoryName;
                updated = true;
            }

            if (!string.Equals(defaultRepository.Url, DefaultRepositoryUrl, StringComparison.OrdinalIgnoreCase))
            {
                defaultRepository.Url = DefaultRepositoryUrl;
                updated = true;
            }

            if (!defaultRepository.Enabled)
            {
                defaultRepository.Enabled = true;
                updated = true;
            }
        }

        if (updated)
        {
            _serverConfigurationManager.SaveConfiguration();
        }
    }

    private static bool IsDefaultRepository(RepositoryInfo repository)
    {
        return string.Equals(repository.Url, DefaultRepositoryUrl, StringComparison.OrdinalIgnoreCase)
            || string.Equals(repository.Url, OldRepositoryUrl, StringComparison.OrdinalIgnoreCase)
            || (repository.Url?.Contains("repo.MulletaFlix.org", StringComparison.OrdinalIgnoreCase) ?? false)
            || (repository.Name?.Contains("MulletaFlix Stable", StringComparison.OrdinalIgnoreCase) ?? false)
            || (repository.Name?.Contains("Jellyfin Stable", StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
