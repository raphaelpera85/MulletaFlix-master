using System;
using System.Collections.Generic;
using System.Linq;
using Emby.Server.Implementations.Plugins;
using Xunit;

namespace MulletaFlix.Server.Implementations.Tests.Plugins;

public sealed class BootstrapPluginCatalogTests
{
    [Fact]
    public void BootstrapRepositories_DoNotIncludeBundledPlugins()
    {
        var blockedIds = new HashSet<Guid>
        {
            Guid.Parse("e9ca8b8e-ca6d-40e7-85dc-58e536df8eb3"),
            Guid.Parse("f5a34f7b-2e8a-4e6a-a722-3a216a81b374"),
            Guid.Parse("f69e946a-4b3c-4e9a-8f0a-8d7c1b2c4d9b"),
            Guid.Parse("dfee3828-01df-49df-85b1-5c2b75e5ea1a")
        };

        Assert.DoesNotContain(
            BootstrapPluginCatalog.BootstrapRepositories.SelectMany(repository => repository.PluginIds),
            pluginId => blockedIds.Contains(pluginId));
    }
}
