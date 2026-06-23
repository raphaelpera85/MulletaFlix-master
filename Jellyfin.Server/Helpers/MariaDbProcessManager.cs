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

        public static void StartMariaDb(IServerApplicationPaths appPaths, ILogger logger)
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

                if (IsMariaDbAlreadyAvailable(masterConnString))
                {
                    logger.LogInformation("MariaDB is already available on port 3306. Reusing the existing server instead of starting a second embedded instance.");
                    InitializeDatabaseSchemas(logger, masterConnString);
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
                        initProcess?.WaitForExit();
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
                if (!WaitForPort(3306, TimeSpan.FromSeconds(30), logger))
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
                InitializeDatabaseSchemas(logger, masterConnString);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to start embedded MariaDB.");
            }
        }

        private static bool IsMariaDbAlreadyAvailable(string masterConnString)
        {
            try
            {
                using var connection = new MySqlConnection(masterConnString);
                connection.Open();
                return connection.State == System.Data.ConnectionState.Open;
            }
            catch
            {
                return false;
            }
        }

        private static void InitializeDatabaseSchemas(ILogger logger, string masterConnString)
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
                    using var connection = new MySqlConnector.MySqlConnection(masterConnString);
                    connection.Open();

                    foreach (var schema in schemas)
                    {
                        using var command = connection.CreateCommand();
                        command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{schema}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
                        command.ExecuteNonQuery();
                        logger.LogInformation("Schema '{Schema}' verified/created.", schema);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Connection attempt {Attempt} failed: {Message}. Retrying...", i + 1, ex.Message);
                    Task.Delay(2000).GetAwaiter().GetResult();
                }
            }
            logger.LogError("Could not connect to MariaDB to initialize schemas.");
        }


        /// <summary>
        /// Polls the given TCP port until it accepts a connection or the timeout expires.
        /// </summary>
        private static bool WaitForPort(int port, TimeSpan timeout, ILogger logger)
        {
            var sw = Stopwatch.StartNew();
            var delay = 250;

            while (sw.Elapsed < timeout)
            {
                try
                {
                    using var tcpClient = new TcpClient();
                    tcpClient.Connect("127.0.0.1", port);
                    logger.LogInformation("MariaDB port {Port} is accepting connections after {Elapsed}ms", port, sw.ElapsedMilliseconds);
                    return true;
                }
                catch
                {
                    // Port not ready yet, continue polling
                }

                Task.Delay(delay).GetAwaiter().GetResult();
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

