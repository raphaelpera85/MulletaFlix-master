using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Providers.Plugins.MidiaStorageOnline.Configuration;

namespace Emby.Server.Implementations.Configuration
{
    public class MidiaStorageOnlineConfigurationFactory : IConfigurationFactory
    {
        public IEnumerable<ConfigurationStore> GetConfigurations()
        {
            return new[]
            {
                new ConfigurationStore
                {
                    ConfigurationType = typeof(PluginConfiguration),
                    Key = "midiastorageonline"
                }
            };
        }
    }
}
