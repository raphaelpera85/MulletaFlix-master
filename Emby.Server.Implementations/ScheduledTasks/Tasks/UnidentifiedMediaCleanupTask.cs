using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using MulletaFlix.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Model.Querying;

namespace Emby.Server.Implementations.ScheduledTasks.Tasks;

public class UnidentifiedMediaCleanupTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly ILocalizationManager _localization;
    private readonly ILogger<UnidentifiedMediaCleanupTask> _logger;
    private readonly IFileSystem _fileSystem;

    public UnidentifiedMediaCleanupTask(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        ILocalizationManager localization,
        ILogger<UnidentifiedMediaCleanupTask> logger,
        IFileSystem fileSystem)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _localization = localization;
        _logger = logger;
        _fileSystem = fileSystem;
    }

    public string Name => _localization.GetLocalizedString("TaskUnidentifiedMediaCleanup");

    public string Description => _localization.GetLocalizedString("TaskUnidentifiedMediaCleanupDescription");

    public string Category => _localization.GetLocalizedString("TasksLibraryCategory");

    public string Key => "UnidentifiedMediaCleanup";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromDays(7).Ticks
        };
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(0);

        var types = new[] { BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Episode };

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = types,
            Recursive = true,
            IsVirtualItem = false,
            DtoOptions = new DtoOptions
            {
                EnableImages = false,
                Fields = new[] { ItemFields.ProviderIds }
            }
        });

        var unidentified = items
            .Where(i => i.ProviderIds is null || i.ProviderIds.Count == 0)
            .ToList();

        _logger.LogInformation(
            "UnidentifiedMediaCleanup: Found {Count} unidentified items. Processing...",
            unidentified.Count);

        if (unidentified.Count == 0)
        {
            progress.Report(100);
            return;
        }

        var index = 0;
        var queuedCount = 0;
        foreach (var item in unidentified)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var path = item.Path;
                var originalName = item.Name;

                if (!string.IsNullOrEmpty(path))
                {
                    CleanFileName(path, out var parsedTitle, out var parsedYear);

                    if (!string.IsNullOrEmpty(parsedTitle)
                        && !string.Equals(originalName, parsedTitle, StringComparison.OrdinalIgnoreCase)
                        && parsedTitle.Length > 3)
                    {
                        _logger.LogDebug(
                            "Item {ItemId} name '{OriginalName}' -> parsed title '{ParsedTitle}' (year: {ParsedYear})",
                            item.Id,
                            originalName,
                            parsedTitle,
                            parsedYear);
                    }
                }

                _providerManager.QueueRefresh(
                    item.Id,
                    new MetadataRefreshOptions(new DirectoryService(_fileSystem))
                    {
                        MetadataRefreshMode = MetadataRefreshMode.Default,
                        IsAutomated = true
                    },
                    RefreshPriority.Normal);

                queuedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error queueing refresh for item {ItemId} {ItemName}", item.Id, item.Name);
            }

            index++;
            progress.Report((double)index / unidentified.Count * 100);
        }

        _logger.LogInformation(
            "UnidentifiedMediaCleanup: Queued refresh for {Count} items.",
            queuedCount);

        progress.Report(100);
    }

    private static void CleanFileName(string path, out string title, out int? year)
    {
        title = string.Empty;
        year = null;

        try
        {
            var filename = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(filename))
            {
                return;
            }

            filename = filename.Replace('.', ' ').Replace('_', ' ').Trim();

            var yearMatch = Regex.Match(filename, @"\b(19\d\d|20\d\d)\b");
            if (yearMatch.Success)
            {
                year = int.Parse(yearMatch.Value, CultureInfo.InvariantCulture);
                filename = filename[..yearMatch.Index].Trim();
            }

            var cleanPatterns = new[]
            {
                @"\b(1080[pi]|2160[pi]|720[pi]|480[pi]|576[pi])\b",
                @"\b(BluRay|Blu-ray|WEB-DL|WEBRip|HDRip|BRRip|DVDRip|DVD|HDTV|TS|CAM)\b",
                @"\b(x264|x265|h264|h265|HEVC|AVC|AV1|VP9)\b",
                @"\b(AAC|DTS|AC3|TRUEHD|FLAC|MP3|5\.1|7\.1|2\.0)\b",
                @"\b(IMAX|EXTENDED|UNCUT|UNRATED|DIRECTORS?[-\s]?CUT|THEATRICAL|REMUX|PROPER|REPACK|INTERNAL)\b",
                @"\b(3[Dd]|SBS|Half[-]?SBS|OU|Half[-]?OU)\b",
                @"\[.*?\]|\(.*?\)",
                @"\bS\d{1,2}(E\d{1,2})?\b"
            };

            foreach (var pattern in cleanPatterns)
            {
                filename = Regex.Replace(filename, pattern, " ", RegexOptions.IgnoreCase);
            }

            filename = Regex.Replace(filename, @"\s+", " ").Trim();

            if (filename.Length > 2)
            {
                var ci = CultureInfo.InvariantCulture;
                var ti = ci.TextInfo;
                filename = ti.ToTitleCase(filename.ToLower(ci));
            }

            title = filename;
        }
        catch
        {
            title = string.Empty;
            year = null;
        }
    }
}
