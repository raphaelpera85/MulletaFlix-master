using System;
using System.Collections.Generic;

namespace MulletaFlix.Api.Jobs;

/// <summary>
/// Progress update for an internal MulletaFlix job.
/// </summary>
public sealed record JobQueueProgress(int Progress, string Phase, string Summary);

/// <summary>
/// Public snapshot for an internal job.
/// </summary>
public sealed class JobQueueItemDto
{
    public string Id { get; set; } = string.Empty;

    public string? CorrelationId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Status { get; set; } = "Queued";

    public int Progress { get; set; }

    public string Phase { get; set; } = "Fila";

    public string Summary { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public bool Cancellable { get; set; } = true;

    public IReadOnlyList<string> Logs { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Public queue status.
/// </summary>
public sealed class JobQueueStatusDto
{
    public int Queued { get; set; }

    public int Running { get; set; }

    public int Completed { get; set; }

    public int Failed { get; set; }

    public int Cancelled { get; set; }

    public int ActiveWorkers { get; set; }

    public int MaxWorkers { get; set; }

    public IReadOnlyList<JobQueueItemDto> Jobs { get; set; } = Array.Empty<JobQueueItemDto>();
}

/// <summary>
/// Request used to enqueue image prewarm jobs.
/// </summary>
public sealed class ImagePrewarmRequest
{
    public int Limit { get; set; } = 300;
}
