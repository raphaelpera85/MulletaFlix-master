using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller;

namespace Emby.Server.Implementations.ScheduledTasks.Tasks;

public sealed class ServerUpdateTask : IScheduledTask
{
    private const string UpdateManifestUrlEnv = "MulletaFlix_UPDATE_MANIFEST_URL";
    private const string UpdateServiceNameEnv = "MulletaFlix_UPDATE_SERVICE_NAME";
    private const string UpdateInstallRootEnv = "MulletaFlix_UPDATE_INSTALL_ROOT";
    private const string UpdateCheckHoursEnv = "MulletaFlix_UPDATE_CHECK_HOURS";

    private readonly ILogger<ServerUpdateTask> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServerApplicationHost _applicationHost;
    private readonly IApplicationPaths _applicationPaths;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;

    public ServerUpdateTask(
        ILogger<ServerUpdateTask> logger,
        IHttpClientFactory httpClientFactory,
        IServerApplicationHost applicationHost,
        IApplicationPaths applicationPaths,
        IHostApplicationLifetime hostApplicationLifetime)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _applicationHost = applicationHost;
        _applicationPaths = applicationPaths;
        _hostApplicationLifetime = hostApplicationLifetime;
    }

    public string Name => "Check Server Updates";

    public string Description => "Checks for MulletaFlix server updates, downloads them, and hands off installation to a hidden updater helper.";

    public string Category => "System";

    public string Key => "ServerUpdates";

    public bool IsHidden => false;

    public bool IsEnabled => true;

    public bool IsLogged => true;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.StartupTrigger
        };

        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(GetCheckIntervalHours()).Ticks
        };
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var manifestUrl = Environment.GetEnvironmentVariable(UpdateManifestUrlEnv);
        if (string.IsNullOrWhiteSpace(manifestUrl))
        {
            _logger.LogDebug("Server update manifest URL is not configured.");
            progress.Report(100);
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            _logger.LogDebug("Server update checks are currently implemented for Windows installs only.");
            progress.Report(100);
            return;
        }

        try
        {
            progress.Report(5);

            var client = _httpClientFactory.CreateClient();
            var manifest = await client.GetFromJsonAsync<ServerUpdateManifest>(manifestUrl, cancellationToken).ConfigureAwait(false);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.ArchiveUrl))
            {
                _logger.LogWarning("Server update manifest from {ManifestUrl} was empty or incomplete.", manifestUrl);
                progress.Report(100);
                return;
            }

            if (!Version.TryParse(manifest.Version, out var remoteVersion))
            {
                _logger.LogWarning("Server update manifest version {Version} is invalid.", manifest.Version);
                progress.Report(100);
                return;
            }

            var currentVersion = _applicationHost.ApplicationVersion;
            if (remoteVersion <= currentVersion)
            {
                _logger.LogDebug(
                    "Server update not required. Current version {CurrentVersion} is already at or above manifest version {RemoteVersion}.",
                    currentVersion,
                    remoteVersion);
                progress.Report(100);
                return;
            }

            progress.Report(20);

            var updateDirectory = Path.Combine(_applicationPaths.DataPath, "Updates");
            Directory.CreateDirectory(updateDirectory);

            var archivePath = Path.Combine(updateDirectory, $"server-update-{remoteVersion.ToString(3)}.zip");
            await DownloadArchiveAsync(client, manifest.ArchiveUrl, archivePath, cancellationToken).ConfigureAwait(false);

            progress.Report(60);

            if (!string.IsNullOrWhiteSpace(manifest.Checksum))
            {
                var actualChecksum = await ComputeSha256Async(archivePath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(manifest.Checksum, actualChecksum, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(archivePath);
                    throw new InvalidDataException(
                        $"Checksum mismatch for server update archive. Expected {manifest.Checksum}, got {actualChecksum}.");
                }
            }

            var installRoot = Environment.GetEnvironmentVariable(UpdateInstallRootEnv);
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                installRoot = AppContext.BaseDirectory;
            }

            var serviceName = Environment.GetEnvironmentVariable(UpdateServiceNameEnv);
            if (string.IsNullOrWhiteSpace(serviceName))
            {
                serviceName = "MulletaFlix";
            }

            var helperScript = Path.Combine(updateDirectory, "apply-server-update.ps1");
            await File.WriteAllTextAsync(helperScript, BuildUpdaterScript(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);

            StartHiddenUpdater(helperScript, archivePath, installRoot, serviceName);

            _logger.LogInformation(
                "Server update {RemoteVersion} downloaded. Update helper launched and server restart requested.",
                remoteVersion.ToString(3));

            _applicationHost.ShouldRestart = true;
            _hostApplicationLifetime.StopApplication();
            progress.Report(100);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Server update task failed.");
            progress.Report(100);
        }
    }

    private static int GetCheckIntervalHours()
    {
        var raw = Environment.GetEnvironmentVariable(UpdateCheckHoursEnv);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours) && hours > 0
            ? hours
            : 12;
    }

    private static async Task DownloadArchiveAsync(HttpClient client, string archiveUrl, string archivePath, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(archiveUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var fileStream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await responseStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static void StartHiddenUpdater(string helperScript, string archivePath, string installRoot, string serviceName)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = string.Join(
                ' ',
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                Quote(helperScript),
                Quote(Environment.ProcessId.ToString(CultureInfo.InvariantCulture)),
                Quote(archivePath),
                Quote(installRoot),
                Quote(serviceName))
        };

        Process.Start(psi);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static string BuildUpdaterScript()
    {
        return """
param(
    [string]$ProcessId,
    [string]$ArchivePath,
    [string]$InstallRoot,
    [string]$ServiceName
)

$ErrorActionPreference = 'Stop'

try {
    $pidValue = [int]$ProcessId
    try {
        Wait-Process -Id $pidValue -ErrorAction SilentlyContinue
    }
    catch {
        # ponytail: updater waits for the old process; if it is already gone, continue.
    }

    if (Test-Path -LiteralPath $ArchivePath) {
        Expand-Archive -LiteralPath $ArchivePath -DestinationPath $InstallRoot -Force
        Remove-Item -LiteralPath $ArchivePath -Force -ErrorAction SilentlyContinue
    }

    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service) {
        Restart-Service -Name $ServiceName -Force
        exit 0
    }

    $exe = Join-Path $InstallRoot 'MulletaFlix.exe'
    if (Test-Path -LiteralPath $exe) {
        Start-Process -FilePath $exe -WorkingDirectory $InstallRoot -WindowStyle Hidden
    }
}
catch {
    Write-Error $_
    exit 1
}
""";
    }

    private sealed class ServerUpdateManifest
    {
        public string Version { get; set; } = string.Empty;

        public string ArchiveUrl { get; set; } = string.Empty;

        public string? Checksum { get; set; }
    }
}
