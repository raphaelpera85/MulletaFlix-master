using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MulletaFlix.Database.Implementations;
using MulletaFlix.Database.Implementations.Contexts;
using MulletaFlix.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MulletaFlix.Server.Implementations.Billing;

public static class DomainDataMigrator
{
    public static async Task MigrateAsync(
        IDbContextFactory<MulletaFlixDbContext> legacyFactory,
        IDbContextFactory<MoviesDbContext> moviesFactory,
        IDbContextFactory<SeriesDbContext> seriesFactory,
        IDbContextFactory<ChannelsDbContext> channelsFactory,
        IDbContextFactory<BooksDbContext> booksFactory,
        ILogger logger,
        CancellationToken ct)
    {
        var legacyCtx = await legacyFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using (legacyCtx.ConfigureAwait(false))
        {
            await MigrateMoviesAsync(legacyCtx, moviesFactory, logger, ct).ConfigureAwait(false);
            await MigrateSeriesAsync(legacyCtx, seriesFactory, logger, ct).ConfigureAwait(false);
            await MigrateChannelsAsync(legacyCtx, channelsFactory, logger, ct).ConfigureAwait(false);
            await MigrateBooksAsync(legacyCtx, booksFactory, logger, ct).ConfigureAwait(false);
        }
    }

    private static async Task MigrateMoviesAsync(
        MulletaFlixDbContext legacy,
        IDbContextFactory<MoviesDbContext> domainFactory,
        ILogger logger,
        CancellationToken ct)
    {
        var moviesCtx = await domainFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using (moviesCtx.ConfigureAwait(false))
        {
            if (await moviesCtx.Movies.AnyAsync(ct).ConfigureAwait(false))
            {
                logger.LogInformation("Movies table already populated, skipping migration.");
                return;
            }

            var baseItems = await legacy.BaseItems
                .Where(b => b.IsMovie || b.Type == "Movie")
                .AsNoTracking()
                .ToListAsync(ct).ConfigureAwait(false);

            logger.LogInformation("Migrating {Count} movies to domain schema...", baseItems.Count);

            foreach (var item in baseItems)
            {
                var meta = TryParseMetadata(item.Data);
                moviesCtx.Movies.Add(new Database.Implementations.Entities.Movies.Movie
                {
                    BaseItemId = item.Id,
                    Name = meta?.Name ?? item.Name ?? ExtractNameFromPath(item.Path),
                    Overview = meta?.Overview,
                    ProductionYear = meta?.ProductionYear,
                    Runtime = meta?.Runtime,
                    CommunityRating = meta?.CommunityRating,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                });
            }

            await moviesCtx.SaveChangesAsync(ct).ConfigureAwait(false);
            logger.LogInformation("Migrated {Count} movies.", baseItems.Count);
        }
    }

    private static async Task MigrateSeriesAsync(
        MulletaFlixDbContext legacy,
        IDbContextFactory<SeriesDbContext> domainFactory,
        ILogger logger,
        CancellationToken ct)
    {
        var seriesCtx = await domainFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using (seriesCtx.ConfigureAwait(false))
        {
            if (await seriesCtx.Series.AnyAsync(ct).ConfigureAwait(false))
            {
                logger.LogInformation("Series table already populated, skipping migration.");
                return;
            }

            var seriesItems = await legacy.BaseItems
                .Where(b => b.Type == "Series")
                .AsNoTracking()
                .ToListAsync(ct).ConfigureAwait(false);

            logger.LogInformation("Migrating {Count} series to domain schema...", seriesItems.Count);

            foreach (var item in seriesItems)
            {
                var meta = TryParseMetadata(item.Data);
                seriesCtx.Series.Add(new Database.Implementations.Entities.Series.Series
                {
                    BaseItemId = item.Id,
                    Name = meta?.Name ?? item.Name ?? ExtractNameFromPath(item.Path),
                    Overview = meta?.Overview,
                    ProductionYear = meta?.ProductionYear,
                    Status = meta?.Status,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                });
            }

            await seriesCtx.SaveChangesAsync(ct).ConfigureAwait(false);

            // Migrate seasons
            var seasons = await legacy.BaseItems
                .Where(b => b.Type == "Season")
                .AsNoTracking()
                .ToListAsync(ct).ConfigureAwait(false);

            var seriesMap = await seriesCtx.Series
                .AsNoTracking()
                .ToDictionaryAsync(s => s.BaseItemId, s => s.Id, ct).ConfigureAwait(false);

            foreach (var item in seasons)
            {
                if (item.ParentId is null || !seriesMap.TryGetValue(item.ParentId.Value, out var seriesId))
                    continue;

                seriesCtx.Seasons.Add(new Database.Implementations.Entities.Series.Season
                {
                    SeriesId = seriesId,
                    BaseItemId = item.Id,
                    Name = item.Name ?? ExtractNameFromPath(item.Path),
                    IndexNumber = item.IndexNumber,
                    IsActive = true,
                });
            }

            await seriesCtx.SaveChangesAsync(ct).ConfigureAwait(false);

            // Migrate episodes
            var episodes = await legacy.BaseItems
                .Where(b => b.Type == "Episode")
                .AsNoTracking()
                .ToListAsync(ct).ConfigureAwait(false);

            var seasonMap = await seriesCtx.Seasons
                .AsNoTracking()
                .ToDictionaryAsync(s => s.BaseItemId, s => s.Id, ct).ConfigureAwait(false);

            foreach (var item in episodes)
            {
                if (item.SeasonId is null || !seasonMap.TryGetValue(item.SeasonId.Value, out var seasonId))
                    continue;

                var meta = TryParseMetadata(item.Data);
                seriesCtx.Episodes.Add(new Database.Implementations.Entities.Series.Episode
                {
                    SeasonId = seasonId,
                    BaseItemId = item.Id,
                    Name = meta?.Name ?? item.Name ?? ExtractNameFromPath(item.Path),
                    IndexNumber = item.IndexNumber,
                    ParentIndexNumber = item.ParentIndexNumber,
                    RunTimeTicks = item.RunTimeTicks,
                    IsActive = true,
                });
            }

            await seriesCtx.SaveChangesAsync(ct).ConfigureAwait(false);
            logger.LogInformation(
                "Migrated {Count} series, {Seasons} seasons, {Episodes} episodes.",
                seriesItems.Count,
                seasons.Count,
                episodes.Count);
        }
    }

    private static async Task MigrateChannelsAsync(
        MulletaFlixDbContext legacy,
        IDbContextFactory<ChannelsDbContext> domainFactory,
        ILogger logger,
        CancellationToken ct)
    {
        var chCtx = await domainFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using (chCtx.ConfigureAwait(false))
        {
            if (await chCtx.Channels.AnyAsync(ct).ConfigureAwait(false))
            {
                logger.LogInformation("Channels already populated, skipping.");
                return;
            }

            var channels = await legacy.BaseItems
                .Where(b => b.Type == "Channel")
                .AsNoTracking()
                .ToListAsync(ct).ConfigureAwait(false);

            foreach (var item in channels)
            {
                chCtx.Channels.Add(new Database.Implementations.Entities.Channels.Channel
                {
                    BaseItemId = item.Id,
                    Name = item.Name ?? ExtractNameFromPath(item.Path),
                    IsActive = true,
                });
            }

            await chCtx.SaveChangesAsync(ct).ConfigureAwait(false);
            logger.LogInformation("Migrated {Count} channels.", channels.Count);
        }
    }

    private static async Task MigrateBooksAsync(
        MulletaFlixDbContext legacy,
        IDbContextFactory<BooksDbContext> domainFactory,
        ILogger logger,
        CancellationToken ct)
    {
        var booksCtx = await domainFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using (booksCtx.ConfigureAwait(false))
        {
            if (await booksCtx.Books.AnyAsync(ct).ConfigureAwait(false))
            {
                logger.LogInformation("Books already populated, skipping.");
                return;
            }

            var books = await legacy.BaseItems
                .Where(b => b.Type == "Book")
                .AsNoTracking()
                .ToListAsync(ct).ConfigureAwait(false);

            foreach (var item in books)
            {
                var meta = TryParseMetadata(item.Data);
                booksCtx.Books.Add(new Database.Implementations.Entities.Books.Book
                {
                    BaseItemId = item.Id,
                    Name = meta?.Name ?? item.Name ?? ExtractNameFromPath(item.Path),
                    Author = meta?.Author,
                    Overview = meta?.Overview,
                    IsActive = true,
                });
            }

            await booksCtx.SaveChangesAsync(ct).ConfigureAwait(false);
            logger.LogInformation("Migrated {Count} books.", books.Count);
        }
    }

    private static string ExtractNameFromPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "Unknown";
        try
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrEmpty(name) ? "Unknown" : name;
        }
        catch
        {
            return "Unknown";
        }
    }

    private static MetadataInfo? TryParseMetadata(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var result = new MetadataInfo();

            if (root.TryGetProperty("Name", out var nameEl))
                result.Name = nameEl.GetString();

            if (root.TryGetProperty("Overview", out var overviewEl))
                result.Overview = overviewEl.GetString();

            if (root.TryGetProperty("ProductionYear", out var yearEl))
                result.ProductionYear = yearEl.GetInt32();

            if (root.TryGetProperty("CommunityRating", out var ratingEl))
                result.CommunityRating = (float?)ratingEl.GetDouble();

            if (root.TryGetProperty("RunTimeTicks", out var runtimeEl))
            {
                var ticks = runtimeEl.GetInt64();
                result.Runtime = ticks > 0 ? ticks / 10_000_000.0 : null;
            }

            if (root.TryGetProperty("Status", out var statusEl))
                result.Status = statusEl.GetString();

            if (root.TryGetProperty("Author", out var authorEl))
                result.Author = authorEl.GetString();

            return result;
        }
        catch
        {
            return null;
        }
    }

    private sealed class MetadataInfo
    {
        public string? Name { get; set; }
        public string? Overview { get; set; }
        public int? ProductionYear { get; set; }
        public double? Runtime { get; set; }
        public float? CommunityRating { get; set; }
        public string? Status { get; set; }
        public string? Author { get; set; }
    }
}
