using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Configuration;

namespace Emby.Server.Implementations.Configuration;

/// <summary>
/// AI metadata named configuration registration.
/// </summary>
public class AiMetadataConfigurationFactory : IConfigurationFactory
{
    /// <summary>
    /// The named configuration key.
    /// </summary>
    public const string Key = AiMetadataConfiguration.ConfigurationKey;

    /// <inheritdoc />
    public IEnumerable<ConfigurationStore> GetConfigurations()
    {
        yield return new ConfigurationStore
        {
            Key = Key,
            ConfigurationType = typeof(AiMetadataConfiguration)
        };
    }
}
