using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Emby.Server.Implementations;
using Emby.Server.Implementations.Configuration;
using Emby.Server.Implementations.Serialization;
using MulletaFlix.Database.Implementations;
using MulletaFlix.Server.Extensions;
using MulletaFlix.Server.Helpers;
using MulletaFlix.Server.Implementations.DatabaseConfiguration;
using MulletaFlix.Server.Implementations.Extensions;
using MulletaFlix.Server.Implementations.StorageHelpers;
using MulletaFlix.Server.Implementations.SystemBackupService;
using MulletaFlix.Server.Configuration.NebulaFTP;
using MulletaFlix.Server.Migrations;
using MulletaFlix.Server.Migrations.Stages;
using MulletaFlix.Server.ServerSetupApp;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using static MediaBrowser.Controller.Extensions.ConfigurationExtensions;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace MulletaFlix.Server
{
    /// <summary>
    /// Class containing the entry point of the application.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// The name of logging configuration file containing application defaults.
        /// </summary>
        public const string LoggingConfigFileDefault = "logging.default.json";

        /// <summary>
        /// The name of the logging configuration file containing the system-specific override settings.
        /// </summary>
        public const string LoggingConfigFileSystem = "logging.json";

        private static readonly SerilogLoggerFactory _loggerFactory = new SerilogLoggerFactory();
        private static SetupServer? _setupServer;
        private static CoreAppHost? _appHost;
        private static IHost? _MulletaFlixHost = null;
        private static long _startTimestamp;
        private static ILogger _logger = NullLogger.Instance;
        private static bool _restartOnShutdown;
        private static IStartupLogger<MulletaFlixMigrationService>? _migrationLogger;
        private static string? _restoreFromBackup;
        private static int _configuredWorkerThreads;
        private static int _configuredCompletionPortThreads;

        /// <summary>
        /// The entry point of the application.
        /// </summary>
        /// <param name="args">The command line arguments passed.</param>
        /// <returns><see cref="Task" />.</returns>
        public static Task Main(string[] args)
        {
            static Task ErrorParsingArguments(IEnumerable<Error> errors)
            {
                Environment.ExitCode = 1;
                return Task.CompletedTask;
            }

            // Parse the command line arguments and either start the app or exit indicating error
            return Parser.Default.ParseArguments<StartupOptions>(args)
                .MapResult(StartApp, ErrorParsingArguments);
        }

        private static async Task StartApp(StartupOptions options)
        {
            ConfigureThreadPool();
            _restoreFromBackup = options.RestoreArchive;
            _startTimestamp = Stopwatch.GetTimestamp();
            ServerApplicationPaths appPaths = StartupHelpers.CreateApplicationPaths(options);
            appPaths.MakeSanityCheckOrThrow();

            // $MulletaFlix_LOG_DIR needs to be set for the logger configuration manager
            Environment.SetEnvironmentVariable("MulletaFlix_LOG_DIR", appPaths.LogDirectoryPath);

            // Enable cl-va P010 interop for tonemapping on Intel VAAPI
            Environment.SetEnvironmentVariable("NEOReadDebugKeys", "1");
            Environment.SetEnvironmentVariable("EnableExtendedVaFormats", "1");

            await StartupHelpers.InitLoggingConfigFile(appPaths).ConfigureAwait(false);

            // Create an instance of the application configuration to use for application startup
            IConfiguration startupConfig = CreateAppConfiguration(options, appPaths);
            StartupHelpers.InitializeLoggingFramework(startupConfig, appPaths);
            _setupServer = new SetupServer(static () => _MulletaFlixHost?.Services?.GetService<INetworkManager>(), appPaths, static () => _appHost, _loggerFactory, startupConfig);
            await _setupServer.RunAsync().ConfigureAwait(false);
            _logger = _loggerFactory.CreateLogger("Main");
            StartupLogger.Logger = new StartupLogger(_logger);

            // Use the logging framework for uncaught exceptions instead of std error
            AppDomain.CurrentDomain.UnhandledException += (_, e)
                => _logger.LogCritical((Exception)e.ExceptionObject, "Unhandled Exception");

            // Redirect plugin references from Jellyfin.* to MulletaFlix.* assemblies
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                var requestedName = new AssemblyName(args.Name);
                if (requestedName.Name!.StartsWith("Jellyfin.", StringComparison.Ordinal))
                {
                    // First try to load the compatibility shim (Jellyfin.*.dll)
                    var shimPath = Path.Combine(AppContext.BaseDirectory, requestedName.Name + ".dll");
                    if (File.Exists(shimPath))
                    {
                        try
                        {
                            return Assembly.LoadFrom(shimPath);
                        }
                        catch
                        {
                        }
                    }

                    // Fallback: redirect to MulletaFlix.* assembly
                    var mappedName = "MulletaFlix." + requestedName.Name["Jellyfin.".Length..];
                    var mappedAssemblyName = new AssemblyName(args.Name) { Name = mappedName };
                    try
                    {
                        return Assembly.Load(mappedAssemblyName);
                    }
                    catch
                    {
                        return null;
                    }
                }

                return null;
            };

            _logger.LogInformation(
                "MulletaFlix version: {Version}",
                Assembly.GetEntryAssembly()!.GetName().Version!.ToString(3));
            _logger.LogInformation(
                "ThreadPool minimum threads configured. WorkerThreads: {WorkerThreads}; CompletionPortThreads: {CompletionPortThreads}",
                _configuredWorkerThreads,
                _configuredCompletionPortThreads);

            StartupHelpers.LogEnvironmentInfo(_logger, appPaths);

            // If hosting the web client, validate the client content path
            if (startupConfig.HostWebClient())
            {
                var webContentPath = appPaths.WebPath;
                if (!Directory.Exists(webContentPath) || !Directory.EnumerateFiles(webContentPath).Any())
                {
                    _logger.LogError(
                        "The server is expected to host the web client, but the provided content directory is either " +
                        "invalid or empty: {WebContentPath}. If you do not want to host the web client with the " +
                        "server, you may set the '--nowebclient' command line flag, or set" +
                        "'{ConfigKey}=false' in your config settings",
                        webContentPath,
                        HostWebClientKey);
                    Environment.ExitCode = 1;
                    return;
                }
            }

            StorageHelper.TestCommonPathsForStorageCapacity(appPaths, StartupLogger.Logger.With(_loggerFactory.CreateLogger<Startup>()).BeginGroup($"Storage Check"));

            StartupHelpers.PerformStaticInitialization();

            // Iniciar o MariaDB Embutido
            await MariaDbProcessManager.StartMariaDbAsync(appPaths, _logger).ConfigureAwait(false);

            await ApplyStartupMigrationAsync(appPaths, startupConfig, options).ConfigureAwait(false);

            do
            {
                await StartServer(appPaths, options, startupConfig).ConfigureAwait(false);

                if (_restartOnShutdown)
                {
                    _startTimestamp = Stopwatch.GetTimestamp();
                    await _setupServer.StopAsync().ConfigureAwait(false);
                    await _setupServer.RunAsync().ConfigureAwait(false);
                }
            }
            while (_restartOnShutdown);

            _setupServer.Dispose();

            // Parar o MariaDB Embutido
            MariaDbProcessManager.StopMariaDb(_logger);
        }

        private static void ConfigureThreadPool()
        {
            ThreadPool.GetMinThreads(out var currentWorkerThreads, out var currentCompletionPortThreads);

            var desiredWorkerThreads = Math.Clamp(Environment.ProcessorCount * 4, 64, 512);
            var configuredValue = Environment.GetEnvironmentVariable("MulletaFlix_THREADPOOL_MIN_THREADS");
            if (int.TryParse(configuredValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var configuredThreads))
            {
                desiredWorkerThreads = Math.Clamp(configuredThreads, 16, 2048);
            }

            var desiredCompletionPortThreads = Math.Clamp(desiredWorkerThreads / 2, 32, 512);

            _configuredWorkerThreads = Math.Max(currentWorkerThreads, desiredWorkerThreads);
            _configuredCompletionPortThreads = Math.Max(currentCompletionPortThreads, desiredCompletionPortThreads);
            ThreadPool.SetMinThreads(_configuredWorkerThreads, _configuredCompletionPortThreads);
        }

        private static async Task StartServer(IServerApplicationPaths appPaths, StartupOptions options, IConfiguration startupConfig)
        {
            using CoreAppHost appHost = new CoreAppHost(
                            appPaths,
                            _loggerFactory,
                            options,
                            startupConfig);
            var configurationCompleted = false;
            try
            {
                _MulletaFlixHost = Host.CreateDefaultBuilder()
                    .UseConsoleLifetime()
                    .ConfigureServices(services => appHost.Init(services))
                    .ConfigureWebHostDefaults(webHostBuilder =>
                    {
                        webHostBuilder.ConfigureWebHostBuilder(appHost, startupConfig, appPaths, _logger);
                        if (bool.TryParse(Environment.GetEnvironmentVariable("MulletaFlix_ENABLE_IIS"), out var iisEnabled) && iisEnabled)
                        {
                            _logger.LogCritical("UNSUPPORTED HOSTING ENVIRONMENT Microsoft Internet Information Services. The option to run MulletaFlix on IIS is an unsupported and untested feature. Only use at your own discretion.");
                            webHostBuilder.UseIIS();
                        }
                    })
                    .ConfigureAppConfiguration(config => config.ConfigureAppConfiguration(options, appPaths, startupConfig))
                    .UseSerilog()
                    .ConfigureServices(e => e
                        .RegisterStartupLogger()
                        .AddSingleton<IServiceCollection>(e))
                    .Build();

                /*
                 * Initialize the transcode path marker so we avoid starting MulletaFlix in a broken state.
                 * This should really be a part of IApplicationPaths but this path is configured differently.
                 */
                _ = appHost.ConfigurationManager.GetTranscodePath();

                // Re-use the host service provider in the app host since ASP.NET doesn't allow a custom service collection.
                appHost.ServiceProvider = _MulletaFlixHost.Services;
                if (!string.IsNullOrWhiteSpace(_restoreFromBackup))
                {
                    await appHost.ServiceProvider.GetService<IBackupService>()!.RestoreBackupAsync(_restoreFromBackup).ConfigureAwait(false);
                    _restoreFromBackup = null;
                    _restartOnShutdown = true;
                    return;
                }

                var migrationService = ActivatorUtilities.CreateInstance<MulletaFlixMigrationService>(appHost.ServiceProvider);
                await migrationService.PrepareSystemForMigration(_logger).ConfigureAwait(false);
                await migrationService.MigrateStepAsync(MulletaFlixMigrationStageTypes.CoreInitialisation, appHost.ServiceProvider).ConfigureAwait(false);

                await appHost.InitializeServices(startupConfig).ConfigureAwait(false);
                _appHost = appHost;

                await migrationService.MigrateStepAsync(MulletaFlixMigrationStageTypes.AppInitialisation, appHost.ServiceProvider).ConfigureAwait(false);
                await migrationService.CleanupSystemAfterMigration(_logger).ConfigureAwait(false);
                try
                {
                    configurationCompleted = true;
                    await _setupServer!.StopAsync().ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

                    if (options.StartupMode is null or Configuration.StartupMode.MediaServer)
                    {
                        var bindPorts = new List<int> { appHost.HttpPort };
                        if (appHost.ListenWithHttps)
                        {
                            bindPorts.Add(appHost.HttpsPort);
                        }

                        await PortBindingRecovery.StartWithPortRecoveryAsync(
                            () => _MulletaFlixHost.StartAsync(),
                            bindPorts,
                            _logger,
                            "Main server").ConfigureAwait(false);

                        if (!OperatingSystem.IsWindows() && startupConfig.UseUnixSocket())
                        {
                            var socketPath = StartupHelpers.GetUnixSocketPath(startupConfig, appPaths);

                            StartupHelpers.SetUnixSocketPermissions(startupConfig, socketPath, _logger);
                        }
                    }
                }
                catch (Exception)
                {
                    _logger.LogError("Kestrel failed to start! This is most likely due to an invalid address or port bind - correct your bind configuration in network.xml and try again");
                    throw;
                }

                if (options.StartupMode is null or Configuration.StartupMode.MediaServer)
                {
                    await appHost.RunStartupTasksAsync().ConfigureAwait(false);
                    _logger.LogInformation("Startup complete {Time:g}", Stopwatch.GetElapsedTime(_startTimestamp));

                    await _MulletaFlixHost.WaitForShutdownAsync().ConfigureAwait(false);
                }

                _restartOnShutdown = appHost.ShouldRestart;
                _restoreFromBackup = appHost.RestoreBackupPath;
            }
            catch (Exception ex)
            {
                _restartOnShutdown = false;
                _logger.LogCritical(ex, "Error while starting server");
                if (_setupServer!.IsAlive && !configurationCompleted)
                {
                    _setupServer!.SoftStop();
                    if (options.StartupMode is null or Configuration.StartupMode.MediaServer)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(10)).ConfigureAwait(false);
                    }

                    await _setupServer!.StopAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                // Don't throw additional exception if startup failed.
                if (appHost.ServiceProvider is not null)
                {
                    _logger.LogInformation("Running query planner optimizations in the database... This might take a while");

                    var databaseProvider = appHost.ServiceProvider.GetRequiredService<IMulletaFlixDatabaseProvider>();
                    using var shutdownSource = new CancellationTokenSource();
                    shutdownSource.CancelAfter((int)TimeSpan.FromSeconds(60).TotalMilliseconds);
                    await databaseProvider.RunShutdownTask(shutdownSource.Token).ConfigureAwait(false);
                }

                _appHost = null;
                _MulletaFlixHost?.Dispose();
                _MulletaFlixHost = null;
            }
        }

        /// <summary>
        /// [Internal]Runs the startup Migrations.
        /// </summary>
        /// <remarks>
        /// Not intended to be used other then by MulletaFlix and its tests.
        /// </remarks>
        /// <param name="appPaths">Application Paths.</param>
        /// <param name="startupConfig">Startup Config.</param>
        /// <param name="startupOptions">The applications startup options.</param>
        /// <returns>A task.</returns>
        public static async Task ApplyStartupMigrationAsync(ServerApplicationPaths appPaths, IConfiguration startupConfig, StartupOptions startupOptions)
        {
            _migrationLogger = StartupLogger.Logger.BeginGroup<MulletaFlixMigrationService>($"Migration Service");
            var startupConfigurationManager = new ServerConfigurationManager(appPaths, _loggerFactory, new MyXmlSerializer());
            startupConfigurationManager.AddParts([new DatabaseConfigurationFactory(), new NebulaFtpConfigurationFactory()]);
            var migrationStartupServiceProvider = new ServiceCollection()
                .AddLogging(d => d.AddSerilog())
                .AddMulletaFlixDbContext(startupConfigurationManager, startupConfig)
                .AddSingleton<IApplicationPaths>(appPaths)
                .AddSingleton<ServerApplicationPaths>(appPaths)
                .RegisterStartupLogger();

            migrationStartupServiceProvider.AddSingleton(migrationStartupServiceProvider);
            var startupService = migrationStartupServiceProvider.BuildServiceProvider();

            var migrationService = ActivatorUtilities.CreateInstance<MulletaFlixMigrationService>(startupService);
            await migrationService.CheckFirstTimeRunOrMigration(appPaths, startupOptions).ConfigureAwait(false);
            await migrationService.MigrateStepAsync(Migrations.Stages.MulletaFlixMigrationStageTypes.PreInitialisation, startupService).ConfigureAwait(false);
        }

        /// <summary>
        /// [Internal]Runs the MulletaFlix migrator service with the Core stage.
        /// </summary>
        /// <remarks>
        /// Not intended to be used other then by MulletaFlix and its tests.
        /// </remarks>
        /// <param name="serviceProvider">The service provider.</param>
        /// <param name="migrationStage">The stage to run.</param>
        /// <returns>A task.</returns>
        public static async Task ApplyCoreMigrationsAsync(IServiceProvider serviceProvider, Migrations.Stages.MulletaFlixMigrationStageTypes migrationStage)
        {
            var migrationService = ActivatorUtilities.CreateInstance<MulletaFlixMigrationService>(serviceProvider, _migrationLogger!);
            await migrationService.MigrateStepAsync(migrationStage, serviceProvider).ConfigureAwait(false);
        }

        /// <summary>
        /// Create the application configuration.
        /// </summary>
        /// <param name="commandLineOpts">The command line options passed to the program.</param>
        /// <param name="appPaths">The application paths.</param>
        /// <returns>The application configuration.</returns>
        public static IConfiguration CreateAppConfiguration(StartupOptions commandLineOpts, IApplicationPaths appPaths)
        {
            return new ConfigurationBuilder()
                .ConfigureAppConfiguration(commandLineOpts, appPaths)
                .Build();
        }

        private static IConfigurationBuilder ConfigureAppConfiguration(
            this IConfigurationBuilder config,
            StartupOptions commandLineOpts,
            IApplicationPaths appPaths,
            IConfiguration? startupConfig = null)
        {
            // Use the swagger API page as the default redirect path if not hosting the web client
            var inMemoryDefaultConfig = ConfigurationOptions.DefaultConfiguration;
            if (startupConfig is not null && !startupConfig.HostWebClient())
            {
                inMemoryDefaultConfig[DefaultRedirectKey] = "api-docs/swagger";
            }

            return config
                .SetBasePath(appPaths.ConfigurationDirectoryPath)
                .AddInMemoryCollection(inMemoryDefaultConfig)
                .AddJsonFile(LoggingConfigFileDefault, optional: false, reloadOnChange: true)
                .AddJsonFile(LoggingConfigFileSystem, optional: true, reloadOnChange: true)
                .AddEnvironmentVariables("MulletaFlix_")
                .AddInMemoryCollection(commandLineOpts.ConvertToConfig());
        }
    }
}
