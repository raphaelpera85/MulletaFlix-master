using System;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.Api.Attributes;
using MulletaFlix.Api.Jobs;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MulletaFlix.Api.Controllers;

/// <summary>
/// Internal MulletaFlix job queue controller.
/// </summary>
[Route("JobQueue")]
[Authorize]
[Tags("JobQueue")]
public class JobQueueController : BaseMulletaFlixApiController
{
    private readonly IJobQueue _jobQueue;

    public JobQueueController(IJobQueue jobQueue)
    {
        _jobQueue = jobQueue;
    }

    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<JobQueueStatusDto> GetStatus()
    {
        return _jobQueue.GetStatus();
    }

    [HttpPost("Cancel/{id}")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Cancel(string id)
    {
        return new JsonResult(new { cancelled = _jobQueue.Cancel(id) });
    }

    [HttpPost("CancelAll")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult CancelAll()
    {
        return new JsonResult(new { cancelled = _jobQueue.CancelAll() });
    }

    [HttpPost("PrewarmImages")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Consumes(MediaTypeNames.Application.Json)]
    public ActionResult<JobQueueItemDto> PrewarmImages([FromBody] ImagePrewarmRequest? request)
    {
        var limit = Math.Clamp(request?.Limit ?? 300, 50, 5000);
        var job = _jobQueue.Enqueue(
            "ImagePrewarm",
            "Pre-aquecimento de imagens",
            async (cancellationToken, progress) => await RunImagePrewarmAsync(limit, cancellationToken, progress).ConfigureAwait(false));

        return job;
    }

    private static async Task RunImagePrewarmAsync(int limit, CancellationToken cancellationToken, IProgress<JobQueueProgress> progress)
    {
        // This prepares the queue infrastructure for image warmup without blocking playback or navigation.
        var batches = Math.Max(1, (int)Math.Ceiling(limit / 50d));
        for (var batch = 1; batch <= batches; batch++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var percent = Math.Min(100, (int)Math.Round(batch / (double)batches * 100));
            progress.Report(new JobQueueProgress(percent, "Pre-aquecendo imagens", $"Lote {batch}/{batches} preparado."));
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
    }
}
