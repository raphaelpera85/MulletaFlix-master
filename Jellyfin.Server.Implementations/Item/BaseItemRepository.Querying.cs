#pragma warning disable RS0030 // Do not use banned APIs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.EntityFrameworkCore;
using MulletaFlix.Data.Enums;
using MulletaFlix.Database.Implementations;
using MulletaFlix.Database.Implementations.Entities;
using MulletaFlix.Extensions;
using BaseItemDto = MediaBrowser.Controller.Entities.BaseItem;

namespace MulletaFlix.Server.Implementations.Item;

public sealed partial class BaseItemRepository
{
    /// <inheritdoc />
    public IReadOnlyList<Guid> GetItemIdsList(InternalItemsQuery filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        PrepareFilterQuery(filter);

        using var context = _dbProvider.CreateDbContext();
        return ApplyQueryFilter(context.BaseItems.AsNoTracking().Where(e => e.Id != EF.Constant(PlaceholderId)), context, filter).Select(e => e.Id).ToArray();
    }

    /// <inheritdoc />
    public QueryResult<BaseItemDto> GetItems(InternalItemsQuery filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (!filter.EnableTotalRecordCount || (!filter.Limit.HasValue && (filter.StartIndex ?? 0) == 0))
        {
            var returnList = GetItemList(filter);
            return new QueryResult<BaseItemDto>(
                filter.StartIndex,
                returnList.Count,
                returnList);
        }

        PrepareFilterQuery(filter);
        var result = new QueryResult<BaseItemDto>();

        using var context = _dbProvider.CreateDbContext();

        IQueryable<BaseItemEntity> dbQuery = PrepareItemQuery(context, filter);

        dbQuery = TranslateQuery(dbQuery, context, filter);
        dbQuery = ApplyGroupingFilter(context, dbQuery, filter);

        if (filter.EnableTotalRecordCount)
        {
            result.TotalRecordCount = dbQuery.Count();
        }

        dbQuery = ApplyQueryPaging(dbQuery, filter);
        dbQuery = ApplyNavigations(dbQuery, filter);

        result.Items = dbQuery.AsEnumerable().Where(e => e != null).Select(w => DeserializeBaseItem(w, filter.SkipDeserialization)).Where(dto => dto != null).ToArray()!;
        result.StartIndex = filter.StartIndex ?? 0;
        return result;
    }

    /// <inheritdoc />
    public IReadOnlyList<BaseItemDto> GetItemList(InternalItemsQuery filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        PrepareFilterQuery(filter);

        using var context = _dbProvider.CreateDbContext();
        IQueryable<BaseItemEntity> dbQuery = PrepareItemQuery(context, filter);

        dbQuery = TranslateQuery(dbQuery, context, filter);

        dbQuery = ApplyGroupingFilter(context, dbQuery, filter);
        dbQuery = ApplyQueryPaging(dbQuery, filter);

        var hasRandomSort = filter.OrderBy.Any(e => e.OrderBy == ItemSortBy.Random);
        if (hasRandomSort)
        {
            var orderedIds = dbQuery.AsNoTracking().Select(e => e.Id).ToList();
            if (orderedIds.Count == 0)
            {
                return Array.Empty<BaseItemDto>();
            }

            var itemsById = ApplyNavigations(context.BaseItems.AsNoTracking().WhereOneOrMany(orderedIds, e => e.Id), filter)
                .AsSplitQuery()
                .AsEnumerable()
                .Select(w => DeserializeBaseItem(w, filter.SkipDeserialization))
                .Where(dto => dto != null)
                .ToDictionary(i => i!.Id);

            return orderedIds.Where(itemsById.ContainsKey).Select(id => itemsById[id]).ToArray()!;
        }

        dbQuery = ApplyNavigations(dbQuery, filter);

        return dbQuery.AsEnumerable().Where(e => e != null).Select(w => DeserializeBaseItem(w, filter.SkipDeserialization)).Where(dto => dto != null).ToArray()!;
    }

    /// <inheritdoc/>
    public IReadOnlyList<BaseItemDto> GetLatestItemList(InternalItemsQuery filter, CollectionType collectionType)
    {
        ArgumentNullException.ThrowIfNull(filter);
        PrepareFilterQuery(filter);

        // Early exit if collection type is not supported
        if (collectionType is not CollectionType.movies and not CollectionType.tvshows and not CollectionType.music)
        {
            return [];
        }

        var limit = filter.Limit;
        using var context = _dbProvider.CreateDbContext();

        var baseQuery = PrepareItemQuery(context, filter);
        baseQuery = TranslateQuery(baseQuery, context, filter);

        if (collectionType == CollectionType.tvshows)
        {
            return GetLatestTvShowItems(context, baseQuery, filter, limit);
        }

        if (collectionType is CollectionType.movies)
        {
            // MariaDB can struggle with LIMIT inside grouped subqueries produced by First().
            // Stream the already ordered rows and pick the first item per presentation key in-memory.
            var orderedMovieCandidates = baseQuery
                .Where(e => e.PresentationUniqueKey != null)
                .OrderByDescending(e => e.DateCreated)
                .ThenByDescending(e => e.Id)
                .Select(e => new { e.Id, e.PresentationUniqueKey, e.DateCreated })
                .AsEnumerable();

            var seenPresentationKeys = new HashSet<string?>(StringComparer.Ordinal);
            var firstIds = new List<Guid>();

            foreach (var candidate in orderedMovieCandidates)
            {
                if (!seenPresentationKeys.Add(candidate.PresentationUniqueKey))
                {
                    continue;
                }

                firstIds.Add(candidate.Id);
                if (filter.Limit.HasValue && firstIds.Count >= filter.Limit.Value)
                {
                    break;
                }
            }

            return LoadLatestByIds(context, firstIds, filter);
        }

        // Albums whose Id is the parent of any track matching the user's filter.
        var albumIdsWithMatchingTrack = context.AncestorIds
            .Join(baseQuery, ai => ai.ItemId, t => t.Id, (ai, _) => ai.ParentItemId)
            .Distinct()
            .ToList();

        var musicAlbumTypeName = _itemTypeLookup.BaseItemKindNames[BaseItemKind.MusicAlbum]!;
        var topAlbumsQuery = context.BaseItems.AsNoTracking()
            .Where(album => album.Type == musicAlbumTypeName)
            .WhereOneOrMany(albumIdsWithMatchingTrack, album => album.Id)
            .OrderByDescending(album => album.DateCreated)
            .ThenByDescending(album => album.Id);

        var albumIds = (filter.Limit.HasValue
            ? topAlbumsQuery.Take(filter.Limit.Value).Select(a => a.Id)
            : topAlbumsQuery.Select(a => a.Id))
            .ToList();

        return LoadLatestByIds(context, albumIds, filter);
    }

    // Materialize ids first so MariaDB does not see a LIMIT inside an IN subquery.
    private IReadOnlyList<BaseItemDto> LoadLatestByIds(
        MulletaFlixDbContext context,
        IList<Guid> ids,
        InternalItemsQuery filter)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var itemsQuery = ApplyNavigations(
            context.BaseItems.AsNoTracking().WhereOneOrMany(ids, e => e.Id),
            filter);

        return itemsQuery
            .OrderByDescending(e => e.DateCreated)
            .ThenByDescending(e => e.Id)
            .AsEnumerable()
            .Select(w => DeserializeBaseItem(w, filter.SkipDeserialization))
            .Where(dto => dto != null)
            .ToArray()!;
    }

    /// <summary>
    /// Gets the latest TV show items.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="baseQuery">The base query with filters already applied.</param>
    /// <param name="filter">The query filter options.</param>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <returns>A list of BaseItemDto representing the latest TV content.</returns>
    private IReadOnlyList<BaseItemDto> GetLatestTvShowItems(MulletaFlixDbContext context, IQueryable<BaseItemEntity> baseQuery, InternalItemsQuery filter, int? limit)
    {
        // ponytail: stream the newest episodes first and stop after we have one series per card.
        var seriesIds = new HashSet<Guid>();
        var topSeriesIds = baseQuery
            .Where(e => e.SeriesId.HasValue)
            .OrderByDescending(e => e.DateCreated)
            .ThenByDescending(e => e.Id)
            .Select(e => new { e.SeriesId, e.DateCreated })
            .AsEnumerable()
            .Where(e => e.SeriesId.HasValue && seriesIds.Add(e.SeriesId.Value))
            .Select(e => e.SeriesId!.Value);

        if (limit.HasValue)
        {
            topSeriesIds = topSeriesIds.Take(limit.Value);
        }

        var orderedSeriesIds = topSeriesIds.ToList();
        if (orderedSeriesIds.Count == 0)
        {
            return [];
        }

        var seriesEntities = ApplyNavigations(
                context.BaseItems.AsNoTracking().WhereOneOrMany(orderedSeriesIds, e => e.Id),
                filter)
            .AsSplitQuery()
            .AsEnumerable()
            .ToDictionary(e => e.Id);

        return orderedSeriesIds
            .Where(seriesEntities.ContainsKey)
            .Select(id => seriesEntities[id])
            .Select(e => DeserializeBaseItem(e, filter.SkipDeserialization))
            .Where(dto => dto is not null)
            .ToArray()!;
    }

    /// <inheritdoc/>
    public async Task<bool> ItemExistsAsync(Guid id)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            return await dbContext.BaseItems.AnyAsync(f => f.Id == id).ConfigureAwait(false);
        }
    }

    /// <inheritdoc  />
    public BaseItemDto? RetrieveItem(Guid id)
    {
        if (id.IsEmpty())
        {
            throw new ArgumentException("Guid can't be empty", nameof(id));
        }

        using var context = _dbProvider.CreateDbContext();
        var dbQuery = PrepareItemQuery(context, new()
        {
            DtoOptions = new()
            {
                EnableImages = true
            }
        });
        dbQuery = dbQuery.Include(e => e.TrailerTypes)
            .Include(e => e.Provider)
            .Include(e => e.LockedFields)
            .Include(e => e.UserData)
            .Include(e => e.Images)
            .Include(e => e.LinkedChildEntities)
            .AsSingleQuery();

        var item = dbQuery.FirstOrDefault(e => e.Id == id);
        if (item is null)
        {
            return null;
        }

        return DeserializeBaseItem(item);
    }

    /// <inheritdoc />
    public bool GetIsPlayed(User user, Guid id, bool recursive)
    {
        using var dbContext = _dbProvider.CreateDbContext();

        if (recursive)
        {
            var descendantIds = DescendantQueryHelper.GetAllDescendantIds(dbContext, id);

            return dbContext.BaseItems
                    .Where(e => descendantIds.Contains(e.Id) && !e.IsFolder && !e.IsVirtualItem)
                    .All(f => f.UserData!.Any(e => e.UserId == user.Id && e.Played));
        }

        return dbContext.BaseItems.Where(e => e.ParentId == id).All(f => f.UserData!.Any(e => e.UserId == user.Id && e.Played));
    }

    /// <inheritdoc />
    public QueryFiltersLegacy GetQueryFiltersLegacy(InternalItemsQuery filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        PrepareFilterQuery(filter);

        using var context = _dbProvider.CreateDbContext();
        var baseQuery = PrepareItemQuery(context, filter);
        baseQuery = TranslateQuery(baseQuery, context, filter);

        var matchingItemIds = baseQuery.Select(e => e.Id);

        var years = baseQuery
            .Where(e => e.ProductionYear != null && e.ProductionYear > 0)
            .Select(e => e.ProductionYear!.Value)
            .Distinct()
            .OrderBy(y => y)
            .ToArray();

        var officialRatings = baseQuery
            .Where(e => e.OfficialRating != null && e.OfficialRating != string.Empty)
            .Select(e => e.OfficialRating!)
            .Distinct()
            .OrderBy(r => r)
            .ToArray();

        var tags = context.ItemValuesMap
            .Where(ivm => ivm.ItemValue.Type == ItemValueType.Tags)
            .Join(baseQuery, ivm => ivm.ItemId, e => e.Id, (ivm, e) => ivm.ItemValue)
            .GroupBy(iv => iv.CleanValue)
            .Select(g => g.Min(iv => iv.Value))
            .OrderBy(t => t)
            .ToArray();

        var genres = context.ItemValuesMap
            .Where(ivm => ivm.ItemValue.Type == ItemValueType.Genre)
            .Join(baseQuery, ivm => ivm.ItemId, e => e.Id, (ivm, e) => ivm.ItemValue)
            .GroupBy(iv => iv.CleanValue)
            .Select(g => g.Min(iv => iv.Value))
            .OrderBy(g => g)
            .ToArray();

        return new QueryFiltersLegacy
        {
            Years = years,
            OfficialRatings = officialRatings,
            Tags = tags,
            Genres = genres
        };
    }
}


