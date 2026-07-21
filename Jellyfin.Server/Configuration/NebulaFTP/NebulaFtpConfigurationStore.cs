using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Configuration;

namespace MulletaFlix.Server.Configuration.NebulaFTP;

public sealed class NebulaFtpConfigurationStore : ConfigurationStore
{
    public NebulaFtpConfigurationStore()
    {
        Key = "nebulaftp";
        ConfigurationType = typeof(NebulaFtpConfiguration);
    }
}
