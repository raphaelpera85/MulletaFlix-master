using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Open.Nat;

namespace MulletaFlix.Networking;

/// <summary>
/// <see cref="IHostedService"/> responsible for automatically mapping server ports on compatible routers.
/// </summary>
public sealed class PortMappingHost : IHostedService
{
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(8);

    private readonly ILogger<PortMappingHost> _logger;
    private readonly IConfigurationManager _configurationManager;
    private readonly INetworkManager _networkManager;
    private readonly List<Mapping> _activeMappings = new();

    private dynamic? _natDevice;

    /// <summary>
    /// Initializes a new instance of the <see cref="PortMappingHost"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="configurationManager">The configuration manager.</param>
    /// <param name="networkManager">The network manager.</param>
    public PortMappingHost(
        ILogger<PortMappingHost> logger,
        IConfigurationManager configurationManager,
        INetworkManager networkManager)
    {
        _logger = logger;
        _configurationManager = configurationManager;
        _networkManager = networkManager;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var config = _configurationManager.GetNetworkConfiguration();
#pragma warning disable CS0618 // Temporary compatibility with existing network configuration schema.
        if (!config.EnableRemoteAccess)
        {
            _logger.LogInformation("Automatic port mapping skipped because remote access is disabled.");
            return;
        }

        if (!config.EnableUPnP)
        {
            _logger.LogInformation("Automatic port mapping is disabled in network settings.");
            return;
        }
#pragma warning restore CS0618

        var localAddress = GetLocalAddress();
        if (localAddress is null)
        {
            _logger.LogWarning("Automatic port mapping skipped because no local LAN address could be determined.");
            return;
        }

        foreach (var portMapper in new[] { PortMapper.Upnp, PortMapper.Pmp })
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (await TryCreateMappingsAsync(portMapper, localAddress, cancellationToken).ConfigureAwait(false))
            {
                return;
            }
        }

        _logger.LogWarning(
            "Automatic port mapping failed for HTTP {HttpPort} and HTTPS {HttpsPort}. " +
            "If the internet connection uses CGNAT or the router blocks UPnP/PMP, the server cannot open the ports automatically.",
            config.PublicHttpPort,
            config.EnableHttps ? config.PublicHttpsPort : 0);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_natDevice is null || _activeMappings.Count == 0)
        {
            return;
        }

        foreach (var mapping in _activeMappings)
        {
            try
            {
                await _natDevice.DeletePortMapAsync(mapping).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove port mapping {Protocol} {PublicPort}.", mapping.Protocol, mapping.PublicPort);
            }
        }

        _activeMappings.Clear();
    }

    private async Task<bool> TryCreateMappingsAsync(PortMapper portMapper, IPAddress localAddress, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(DiscoveryTimeout);

            var discoverer = new NatDiscoverer();
            _logger.LogInformation("Searching for a NAT device with {PortMapper} support...", portMapper);
            var device = await discoverer.DiscoverDeviceAsync(portMapper, timeout).ConfigureAwait(false);

            _natDevice = device;
            _activeMappings.Clear();

            var config = _configurationManager.GetNetworkConfiguration();
            var mappings = BuildMappings(config);
            foreach (var mapping in mappings)
            {
                await device.CreatePortMapAsync(mapping).ConfigureAwait(false);
                _activeMappings.Add(mapping);
                _logger.LogInformation(
                    "Mapped {Protocol} port {PublicPort} -> {LocalAddress}:{PrivatePort} using {PortMapper}.",
                    mapping.Protocol,
                    mapping.PublicPort,
                    localAddress,
                    mapping.PrivatePort,
                    portMapper);
            }

            return _activeMappings.Count > 0;
        }
        catch (NatDeviceNotFoundException ex)
        {
            _logger.LogDebug(ex, "No NAT device supporting {PortMapper} was found.", portMapper);
            return false;
        }
        catch (MappingException ex)
        {
            _logger.LogWarning(ex, "The router rejected an automatic port mapping request.");
            _activeMappings.Clear();
            _natDevice = null;
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error while creating automatic port mappings with {PortMapper}.", portMapper);
            _activeMappings.Clear();
            _natDevice = null;
            return false;
        }
    }

    private static IReadOnlyList<Mapping> BuildMappings(NetworkConfiguration config)
    {
        var mappings = new List<Mapping>();

        if (config.PublicHttpPort > 0 && config.InternalHttpPort > 0)
        {
            mappings.Add(new Mapping(Protocol.Tcp, config.InternalHttpPort, config.PublicHttpPort, "MulletaFlix HTTP"));
        }

        if (config.EnableHttps && config.PublicHttpsPort > 0 && config.InternalHttpsPort > 0)
        {
            mappings.Add(new Mapping(Protocol.Tcp, config.InternalHttpsPort, config.PublicHttpsPort, "MulletaFlix HTTPS"));
        }

        return mappings;
    }

    private IPAddress? GetLocalAddress()
    {
        return _networkManager.GetInternalBindAddresses()
            .Select(x => x.Address)
            .FirstOrDefault(address => address is not null && !IPAddress.IsLoopback(address));
    }
}
