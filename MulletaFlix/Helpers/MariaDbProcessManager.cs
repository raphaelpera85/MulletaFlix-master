using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MySqlConnector;
using Microsoft.Extensions.Logging;

namespace MulletaFlix.Server.Helpers
{
    public static class MariaDbProcessManager
    {
        private static Process? _mariaDbProcess;

        public static async Task StartMariaDbAsync(IServerApplicationPaths appPaths, ILogger logger, CancellationToken cancellationToken = default)
        {
            try
            {
                var dataDir = Path.Combine(appPaths.DataPath, "mariadb_data");

                var appDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(appDir)) return;

                var binDir = Path.Combine(appDir, "mariadb", "bin");
                var exePath = Path.Combine(binDir, "mysqld.exe");
                var installDbPath = Path.Combine(binDir, "mysql_install_db.exe");
                var masterConnString = "Server=localhost;Port=3306;User ID=root;Password=;CharSet=utf8mb4;Connection Timeout=2;";

                if (!File.Exists(exePath))
                {
                    logger.LogWarning("MariaDB portable executable not found at {ExePath}. Assuming external database is configured.", exePath);
                    return;
                }

                if (await IsMariaDbAlreadyAvailableAsync(masterConnString, cancellationToken).ConfigureAwait(false))
                {
                    logger.LogInformation("MariaDB is already available on port 3306. Reusing the existing server instead of starting a second embedded instance.");
                    await InitializeDatabaseSchemasAsync(logger, masterConnString, cancellationToken).ConfigureAwait(false);
                    return;
                }

                // If data directory doesn't exist or is empty, we must initialize the database
                if (!Directory.Exists(dataDir) || Directory.GetFiles(dataDir).Length == 0)
                {
                    logger.LogInformation("Initializing new MariaDB data directory at {DataDir}", dataDir);
                    Directory.CreateDirectory(dataDir);

                    if (File.Exists(installDbPath))
                    {
                        var initProcess = Process.Start(new ProcessStartInfo
                        {
                            FileName = installDbPath,
                            Arguments = $"--datadir=\"{dataDir}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                        if (initProcess != null)
                        {
                            await initProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                        }
                    }
                }

                logger.LogInformation("Starting embedded MariaDB from {ExePath}...", exePath);

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"--datadir=\"{dataDir}\" --console --skip-log-bin",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _mariaDbProcess = new Process { StartInfo = startInfo };

                // Async readers prevent I/O deadlock when output buffer fills
                _mariaDbProcess.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        logger.LogDebug("MariaDB: {Data}", e.Data);
                };
                _mariaDbProcess.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        logger.LogWarning("MariaDB: {Data}", e.Data);
                };

                _mariaDbProcess.Start();
                _mariaDbProcess.BeginOutputReadLine();
                _mariaDbProcess.BeginErrorReadLine();

                // Wait for the database engine to come online via TCP port polling
                if (!await WaitForPortAsync(3306, TimeSpan.FromSeconds(30), logger, cancellationToken).ConfigureAwait(false))
                {
                    if (_mariaDbProcess.HasExited)
                    {
                        logger.LogError("Embedded MariaDB process exited prematurely with code {Code}", _mariaDbProcess.ExitCode);
                    }
                    else
                    {
                        logger.LogError("Embedded MariaDB started but did not become available on port 3306 within 30 seconds.");
                    }
                    return;
                }

                logger.LogInformation("MariaDB embedded process started with PID {PID} and is accepting connections on port 3306", _mariaDbProcess.Id);
                logger.LogWarning("Embedded MariaDB is running without a root password. This is acceptable for localhost-only access, but set a password if the port is exposed.");
                await InitializeDatabaseSchemasAsync(logger, masterConnString, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to start embedded MariaDB.");
            }
        }

        private static async Task<bool> IsMariaDbAlreadyAvailableAsync(string masterConnString, CancellationToken cancellationToken)
        {
            try
            {
                using var connection = new MySqlConnection(masterConnString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                return connection.State == System.Data.ConnectionState.Open;
            }
            catch
            {
                return false;
            }
        }

        private static async Task InitializeDatabaseSchemasAsync(ILogger logger, string masterConnString, CancellationToken cancellationToken)
        {
            var schemas = new[]
            {
                "mulletaflix_users",
                "mulletaflix_movies",
                "mulletaflix_series",
                "mulletaflix_channels",
                "mulletaflix_books"
            };

            for (int i = 0; i < 5; i++) // Tenta até 5 vezes estabelecer a conexão inicial
            {
                try
                {
                    logger.LogInformation("Ensuring sharding schemas exist in MariaDB...");
                    using var connection = new MySqlConnection(masterConnString);
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                    foreach (var schema in schemas)
                    {
                        using var command = connection.CreateCommand();
                        command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{schema}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
                        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        logger.LogInformation("Schema '{Schema}' verified/created.", schema);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Connection attempt {Attempt} failed: {Message}. Retrying...", i + 1, ex.Message);
                    await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
                }
            }
            logger.LogError("Could not connect to MariaDB to initialize schemas.");
        }

        /// <summary>
        /// Polls the given TCP port until it accepts a connection or the timeout expires.
        /// </summary>
        private static async Task<bool> WaitForPortAsync(int port, TimeSpan timeout, ILogger logger, CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            var delay = 250;

            while (sw.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var tcpClient = new TcpClient();
                    await tcpClient.ConnectAsync("127.0.0.1", port, cancellationToken).ConfigureAwait(false);
                    logger.LogInformation("MariaDB port {Port} is accepting connections after {Elapsed}ms", port, sw.ElapsedMilliseconds);
                    return true;
                }
                catch
                {
                    // Port not ready yet, continue polling
                }

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            return false;
        }
        public static void StopMariaDb(ILogger logger)
        {
            if (_mariaDbProcess != null && !_mariaDbProcess.HasExited)
            {
                try
                {
                    logger.LogInformation("Stopping embedded MariaDB process...");
                    _mariaDbProcess.Kill(true);
                    _mariaDbProcess.WaitForExit(5000);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error while stopping embedded MariaDB.");
                }
                finally
                {
                    _mariaDbProcess.Dispose();
                    _mariaDbProcess = null;
                }
            }
        }
    }
}

