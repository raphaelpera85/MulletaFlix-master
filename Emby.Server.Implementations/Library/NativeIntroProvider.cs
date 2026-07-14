#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Branding;
using MulletaFlix.Database.Implementations.Entities;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.Library;

/// <summary>
/// Provides a configured local intro without requiring an external plugin.
/// </summary>
public sealed class NativeIntroProvider : IIntroProvider
{
    private static readonly string[] SupportedExtensions = [".mkv", ".mp4", ".webm", ".mov", ".avi"];
    private readonly IServerConfigurationManager _configurationManager;
    private readonly IStrmPrebufferManager _prebufferManager;
    private readonly ILogger<NativeIntroProvider> _logger;

    public NativeIntroProvider(
        IServerConfigurationManager configurationManager,
        IStrmPrebufferManager prebufferManager,
        ILogger<NativeIntroProvider> logger)
    {
        _configurationManager = configurationManager;
        _prebufferManager = prebufferManager;
        _logger = logger;
    }

    public string Name => "MulletaFlix Native Intro";

    public async Task<IEnumerable<IntroInfo>> GetIntros(BaseItem item, User user)
    {
        var options = _configurationManager.GetConfiguration<BrandingOptions>("branding");
        if (!options.IntroEnabled || string.IsNullOrWhiteSpace(options.IntroPath))
        {
            return [];
        }

        var introPath = ResolveIntroPath(options.IntroPath);
        if (introPath is null)
        {
            _logger.LogWarning("Configured native intro path was not found or is not a supported video: {Path}", options.IntroPath);
            return [];
        }

        await _prebufferManager.PrepareAsync(item).ConfigureAwait(false);

        return [new IntroInfo { Path = introPath }];
    }

    private static string ResolveIntroPath(string path)
    {
        if (File.Exists(path))
        {
            return SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) ? path : null;
        }

        if (!Directory.Exists(path))
        {
            return null;
        }

        return Directory.EnumerateFiles(path)
            .Where(file => SupportedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
