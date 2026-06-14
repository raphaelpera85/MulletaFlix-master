using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MulletaFlix.Api.Jobs;

/// <summary>
/// Channel based internal job queue with cancellation, per-kind throttling and persistent status.
/// </summary>
public sealed class MulletaFlixJobQueue : BackgroundService, IJobQueue
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Channel<JobQueueWorkItem> _channel;
    private readonly ConcurrentDictionary<string, JobQueueWorkItem> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastStartByKind = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyDictionary<string, SemaphoreSlim> _kindLimits;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<MulletaFlixJobQueue> _logger;
    private readonly string _databasePath;
    private int _activeWorkers;

    public MulletaFlixJobQueue(
        IServerApplicationPaths applicationPaths,
        IMemoryCache memoryCache,
        ILogger<MulletaFlixJobQueue> logger)
    {
        _memoryCache = memoryCache;
        _logger = logger;
        _databasePath = Path.Combine(applicationPaths.DataPath, "mulletaflix-jobqueue.db");
        _channel = Channel.CreateUnbounded<JobQueueWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _kindLimits = new Dictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase)
        {
            ["AiMetadata"] = new(1, 1),
            ["MovieMetadata"] = new(6, 6),
            ["SeriesMetadata"] = new(6, 6),
            ["BookMetadata"] = new(2, 2),
            ["ChannelMetadata"] = new(4, 4),
            ["ImagePrewarm"] = new(4, 4),
            ["Maintenance"] = new(1, 1)
        };
    }

    public JobQueueItemDto Enqueue(
        string kind,
        string title,
        Func<CancellationToken, IProgress<JobQueueProgress>, Task> handler,
        string? correlationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(handler);

        var now = DateTimeOffset.UtcNow;
        var workItem = new JobQueueWorkItem
        {
            Id = Guid.NewGuid().ToString("N"),
            CorrelationId = correlationId,
            Kind = kind,
            Title = title,
            Status = "Queued",
            Progress = 0,
            Phase = "Fila",
            Summary = "Aguardando processamento.",
            CreatedAt = now,
            Handler = handler
        };
        workItem.Logs.Enqueue($"[{now:yyyy-MM-dd HH:mm:ss}] Job enfileirado: {title}.");

        _jobs[workItem.Id] = workItem;
        PersistJobSafe(workItem);

        if (!_channel.Writer.TryWrite(workItem))
        {
            workItem.Status = "Failed";
            workItem.ErrorMessage = "Nao foi possivel enfileirar o trabalho.";
            workItem.FinishedAt = DateTimeOffset.UtcNow;
            PersistJobSafe(workItem);
        }

        return ToDto(workItem);
    }

    public JobQueueStatusDto GetStatus()
    {
        var jobs = _jobs.Values
            .OrderByDescending(job => job.CreatedAt)
            .Take(100)
            .Select(ToDto)
            .ToArray();

        return new JobQueueStatusDto
        {
            Queued = _jobs.Values.Count(job => string.Equals(job.Status, "Queued", StringComparison.OrdinalIgnoreCase)),
            Running = _jobs.Values.Count(job => string.Equals(job.Status, "Running", StringComparison.OrdinalIgnoreCase)),
            Completed = _jobs.Values.Count(job => string.Equals(job.Status, "Completed", StringComparison.OrdinalIgnoreCase)),
            Failed = _jobs.Values.Count(job => string.Equals(job.Status, "Failed", StringComparison.OrdinalIgnoreCase)),
            Cancelled = _jobs.Values.Count(job => string.Equals(job.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)),
            ActiveWorkers = Volatile.Read(ref _activeWorkers),
            MaxWorkers = GetWorkerCount(),
            Jobs = jobs
        };
    }

    public JobQueueItemDto? GetJob(string id)
    {
        return _jobs.TryGetValue(id, out var job) ? ToDto(job) : null;
    }

    public bool Cancel(string id)
    {
        if (!_jobs.TryGetValue(id, out var job))
        {
            return false;
        }

        job.Cancellation.Cancel();
        if (string.Equals(job.Status, "Queued", StringComparison.OrdinalIgnoreCase))
        {
            MarkCancelled(job, "Cancelado antes do inicio.");
        }

        return true;
    }

    public bool CancelByCorrelationId(string correlationId)
    {
        var found = false;
        foreach (var job in _jobs.Values.Where(job => string.Equals(job.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase)))
        {
            found |= Cancel(job.Id);
        }

        return found;
    }

    public int CancelAll()
    {
        var count = 0;
        foreach (var job in _jobs.Values.Where(job => job.Cancellable && IsActiveStatus(job.Status)))
        {
            if (Cancel(job.Id))
            {
                count++;
            }
        }

        return count;
    }

    public async Task SetCacheAsync(string cacheKey, string value, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(ttl);
        _memoryCache.Set(cacheKey, value, expiresAt);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureDatabaseAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO JobCache (CacheKey, Value, ExpiresAt)
            VALUES ($cacheKey, $value, $expiresAt)
            ON CONFLICT(CacheKey) DO UPDATE SET
                Value = excluded.Value,
                ExpiresAt = excluded.ExpiresAt;
            """;
        command.Parameters.AddWithValue("$cacheKey", cacheKey);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$expiresAt", expiresAt.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetCacheAsync(string cacheKey, CancellationToken cancellationToken)
    {
        if (_memoryCache.TryGetValue<string>(cacheKey, out var cached))
        {
            return cached;
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureDatabaseAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value, ExpiresAt FROM JobCache WHERE CacheKey = $cacheKey LIMIT 1;";
        command.Parameters.AddWithValue("$cacheKey", cacheKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt)
            || expiresAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        var value = reader.GetString(0);
        _memoryCache.Set(cacheKey, value, expiresAt);
        return value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitializeDatabaseAsync(stoppingToken).ConfigureAwait(false);
        _ = Task.Run(() => MaintenanceLoopAsync(stoppingToken), stoppingToken);

        var workers = Enumerable.Range(0, GetWorkerCount())
            .Select(_ => Task.Run(() => WorkerLoopAsync(stoppingToken), stoppingToken))
            .ToArray();

        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    private async Task WorkerLoopAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            if (!string.Equals(job.Status, "Queued", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var limiter = GetLimiter(job.Kind);
            await limiter.WaitAsync(stoppingToken).ConfigureAwait(false);
            Interlocked.Increment(ref _activeWorkers);

            try
            {
                await ApplyRateLimitAsync(job.Kind, stoppingToken).ConfigureAwait(false);
                await RunJobAsync(job, stoppingToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _activeWorkers);
                limiter.Release();
            }
        }
    }

    private async Task RunJobAsync(JobQueueWorkItem job, CancellationToken stoppingToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, job.Cancellation.Token);
        var token = linked.Token;
        var progress = new Progress<JobQueueProgress>(update =>
        {
            job.Progress = Math.Clamp(update.Progress, 0, 100);
            job.Phase = update.Phase;
            job.Summary = update.Summary;
            AddLog(job, $"{update.Phase}: {update.Summary}");
            PersistJobSafe(job);
        });

        job.Status = "Running";
        job.StartedAt = DateTimeOffset.UtcNow;
        job.Phase = "Executando";
        job.Summary = "Processamento iniciado.";
        AddLog(job, "Processamento iniciado.");
        PersistJobSafe(job);

        try
        {
            await job.Handler(token, progress).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            job.Status = "Completed";
            job.Progress = 100;
            job.Phase = "Concluido";
            job.Summary = "Trabalho concluido.";
            job.FinishedAt = DateTimeOffset.UtcNow;
            AddLog(job, "Trabalho concluido.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            MarkCancelled(job, "Trabalho cancelado.");
        }
        catch (Exception ex)
        {
            job.Status = "Failed";
            job.ErrorMessage = ex.Message;
            job.Summary = ex.Message;
            job.FinishedAt = DateTimeOffset.UtcNow;
            AddLog(job, $"Falha: {ex.Message}");
            _logger.LogError(ex, "Job {JobId} failed", job.Id);
        }
        finally
        {
            PersistJobSafe(job);
        }
    }

    private async Task MaintenanceLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                foreach (var job in _jobs.Values)
                {
                    PersistJobSafe(job);
                }

                await CleanupDatabaseAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Job queue maintenance failed");
            }
        }
    }

    private async Task ApplyRateLimitAsync(string kind, CancellationToken cancellationToken)
    {
        var delay = GetStartDelay(kind);
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var lastStart = _lastStartByKind.GetOrAdd(kind, now.Subtract(delay));
        var wait = delay - (now - lastStart);
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }

        _lastStartByKind[kind] = DateTimeOffset.UtcNow;
    }

    private static TimeSpan GetStartDelay(string kind)
    {
        return kind switch
        {
            "AiMetadata" => TimeSpan.FromMilliseconds(1500),
            "BookMetadata" => TimeSpan.FromMilliseconds(500),
            "ChannelMetadata" => TimeSpan.FromMilliseconds(300),
            "MovieMetadata" or "SeriesMetadata" => TimeSpan.FromMilliseconds(100),
            _ => TimeSpan.FromMilliseconds(50)
        };
    }

    private SemaphoreSlim GetLimiter(string kind)
    {
        return _kindLimits.TryGetValue(kind, out var limiter) ? limiter : _kindLimits["Maintenance"];
    }

    private static int GetWorkerCount()
    {
        return Math.Clamp(Environment.ProcessorCount * 2, 4, 16);
    }

    private static bool IsActiveStatus(string status)
    {
        return string.Equals(status, "Queued", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase);
    }

    private static void MarkCancelled(JobQueueWorkItem job, string summary)
    {
        job.Status = "Cancelled";
        job.Phase = "Cancelado";
        job.Summary = summary;
        job.FinishedAt = DateTimeOffset.UtcNow;
        AddLog(job, summary);
    }

    private static void AddLog(JobQueueWorkItem job, string message)
    {
        job.Logs.Enqueue($"[{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}] {message}");
        while (job.Logs.Count > 80 && job.Logs.TryDequeue(out _))
        {
        }
    }

    private static JobQueueItemDto ToDto(JobQueueWorkItem job)
    {
        return new JobQueueItemDto
        {
            Id = job.Id,
            CorrelationId = job.CorrelationId,
            Kind = job.Kind,
            Title = job.Title,
            Status = job.Status,
            Progress = job.Progress,
            Phase = job.Phase,
            Summary = job.Summary,
            ErrorMessage = job.ErrorMessage,
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            FinishedAt = job.FinishedAt,
            Cancellable = job.Cancellable && IsActiveStatus(job.Status),
            Logs = job.Logs.ToArray()
        };
    }

    private async Task InitializeDatabaseAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureDatabaseAsync(connection, cancellationToken).ConfigureAwait(false);
        await LoadRecentJobsAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private async Task LoadRecentJobsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, CorrelationId, Kind, Title, Status, Progress, Phase, Summary, ErrorMessage, CreatedAt, StartedAt, FinishedAt, Logs
            FROM JobQueue
            ORDER BY CreatedAt DESC
            LIMIT 100;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var job = new JobQueueWorkItem
            {
                Id = reader.GetString(0),
                CorrelationId = reader.IsDBNull(1) ? null : reader.GetString(1),
                Kind = reader.GetString(2),
                Title = reader.GetString(3),
                Status = NormalizeLoadedStatus(reader.GetString(4)),
                Progress = reader.GetInt32(5),
                Phase = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                Summary = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                ErrorMessage = reader.IsDBNull(8) ? null : reader.GetString(8),
                CreatedAt = ParseDate(reader.GetString(9)) ?? DateTimeOffset.UtcNow,
                StartedAt = reader.IsDBNull(10) ? null : ParseDate(reader.GetString(10)),
                FinishedAt = reader.IsDBNull(11) ? null : ParseDate(reader.GetString(11)),
                Handler = (_, _) => Task.CompletedTask
            };

            var logs = reader.IsDBNull(12) ? Array.Empty<string>() : JsonSerializer.Deserialize<string[]>(reader.GetString(12), JsonOptions) ?? Array.Empty<string>();
            foreach (var log in logs)
            {
                job.Logs.Enqueue(log);
            }

            _jobs[job.Id] = job;
        }
    }

    private static string NormalizeLoadedStatus(string status)
    {
        return IsActiveStatus(status) ? "Cancelled" : status;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
            ? date
            : null;
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={_databasePath}");
    }

    private static async Task EnsureDatabaseAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS JobQueue (
                Id TEXT PRIMARY KEY,
                CorrelationId TEXT NULL,
                Kind TEXT NOT NULL,
                Title TEXT NOT NULL,
                Status TEXT NOT NULL,
                Progress INTEGER NOT NULL,
                Phase TEXT NULL,
                Summary TEXT NULL,
                ErrorMessage TEXT NULL,
                CreatedAt TEXT NOT NULL,
                StartedAt TEXT NULL,
                FinishedAt TEXT NULL,
                Logs TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS JobCache (
                CacheKey TEXT PRIMARY KEY,
                Value TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private void PersistJobSafe(JobQueueWorkItem job)
    {
        try
        {
            using var connection = CreateConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO JobQueue (Id, CorrelationId, Kind, Title, Status, Progress, Phase, Summary, ErrorMessage, CreatedAt, StartedAt, FinishedAt, Logs)
                VALUES ($id, $correlationId, $kind, $title, $status, $progress, $phase, $summary, $errorMessage, $createdAt, $startedAt, $finishedAt, $logs)
                ON CONFLICT(Id) DO UPDATE SET
                    CorrelationId = excluded.CorrelationId,
                    Kind = excluded.Kind,
                    Title = excluded.Title,
                    Status = excluded.Status,
                    Progress = excluded.Progress,
                    Phase = excluded.Phase,
                    Summary = excluded.Summary,
                    ErrorMessage = excluded.ErrorMessage,
                    StartedAt = excluded.StartedAt,
                    FinishedAt = excluded.FinishedAt,
                    Logs = excluded.Logs;
                """;
            command.Parameters.AddWithValue("$id", job.Id);
            command.Parameters.AddWithValue("$correlationId", (object?)job.CorrelationId ?? DBNull.Value);
            command.Parameters.AddWithValue("$kind", job.Kind);
            command.Parameters.AddWithValue("$title", job.Title);
            command.Parameters.AddWithValue("$status", job.Status);
            command.Parameters.AddWithValue("$progress", job.Progress);
            command.Parameters.AddWithValue("$phase", job.Phase);
            command.Parameters.AddWithValue("$summary", job.Summary);
            command.Parameters.AddWithValue("$errorMessage", (object?)job.ErrorMessage ?? DBNull.Value);
            command.Parameters.AddWithValue("$createdAt", job.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$startedAt", job.StartedAt?.ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$finishedAt", job.FinishedAt?.ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$logs", JsonSerializer.Serialize(job.Logs.ToArray(), JsonOptions));
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to persist job {JobId}", job.Id);
        }
    }

    private async Task CleanupDatabaseAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-7).ToString("O", CultureInfo.InvariantCulture);
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM JobQueue
            WHERE FinishedAt IS NOT NULL
              AND FinishedAt < $cutoff;
            DELETE FROM JobCache
            WHERE ExpiresAt < $now;
            """;
        command.Parameters.AddWithValue("$cutoff", cutoff);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class JobQueueWorkItem
    {
        public string Id { get; init; } = string.Empty;

        public string? CorrelationId { get; init; }

        public string Kind { get; init; } = string.Empty;

        public string Title { get; init; } = string.Empty;

        public string Status { get; set; } = "Queued";

        public int Progress { get; set; }

        public string Phase { get; set; } = "Fila";

        public string Summary { get; set; } = string.Empty;

        public string? ErrorMessage { get; set; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset? StartedAt { get; set; }

        public DateTimeOffset? FinishedAt { get; set; }

        public bool Cancellable { get; init; } = true;

        public CancellationTokenSource Cancellation { get; } = new();

        public ConcurrentQueue<string> Logs { get; } = new();

        public required Func<CancellationToken, IProgress<JobQueueProgress>, Task> Handler { get; init; }
    }
}
