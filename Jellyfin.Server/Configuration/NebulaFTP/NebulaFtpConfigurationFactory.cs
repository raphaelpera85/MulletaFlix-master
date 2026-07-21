using System.Collections.Generic;
using MediaBrowser.Common.Configuration;

namespace MulletaFlix.Server.Configuration.NebulaFTP;

public sealed class NebulaFtpConfigurationFactory : IConfigurationFactory
{
    public IEnumerable<ConfigurationStore> GetConfigurations()
    {
        return [new NebulaFtpConfigurationStore()];
    }
}
