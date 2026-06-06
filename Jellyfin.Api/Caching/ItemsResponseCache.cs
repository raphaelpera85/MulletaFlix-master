using System;
using System.Text;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Caching.Memory;

namespace Jellyfin.Api.Caching;

/// <summary>
/// Cache for ItemsController query results with short TTL.
/// </summary>
public class ItemsResponseCache
{
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemsResponseCache"/> class.
    /// </summary>
    /// <param name="cache">Instance of the <see cref="IMemoryCache"/> interface.</param>
    public ItemsResponseCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// Try to get a cached result.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="result">The cached result, if found.</param>
    /// <returns>Whether a cached result was found.</returns>
    public bool TryGet(string key, out QueryResult<BaseItemDto>? result)
    {
        return _cache.TryGetValue(key, out result);
    }

    /// <summary>
    /// Set a cached result.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="result">The result to cache.</param>
    public void Set(string key, QueryResult<BaseItemDto> result)
    {
        _cache.Set(key, result, DefaultTtl);
    }

    /// <summary>
    /// Build a deterministic cache key from an InternalItemsQuery.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <returns>A cache key string.</returns>
    public static string BuildCacheKey(InternalItemsQuery query)
    {
        var sb = new StringBuilder();
        sb.Append(query.User?.Id.ToString() ?? "anon");
        sb.Append('|');
        sb.Append(query.Recursive ? '1' : '0');
        sb.Append('|');
        sb.Append(query.Limit.GetValueOrDefault());
        sb.Append('|');
        sb.Append(query.StartIndex.GetValueOrDefault());
        sb.Append('|');

        if (query.IncludeItemTypes.Length > 0)
        {
            for (var i = 0; i < query.IncludeItemTypes.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append((int)query.IncludeItemTypes[i]);
            }
        }

        sb.Append('|');
        if (query.ExcludeItemTypes.Length > 0)
        {
            for (var i = 0; i < query.ExcludeItemTypes.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append((int)query.ExcludeItemTypes[i]);
            }
        }

        sb.Append('|');
        if (query.OrderBy.Count > 0)
        {
            for (var i = 0; i < query.OrderBy.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append((int)query.OrderBy[i].OrderBy);
                sb.Append('-');
                sb.Append((int)query.OrderBy[i].SortOrder);
            }
        }

        sb.Append('|');
        sb.Append(query.IsPlayed?.ToString() ?? "n");
        sb.Append('|');
        sb.Append(query.IsFavorite?.ToString() ?? "n");

        sb.Append('|');
        if (query.MediaTypes.Length > 0)
        {
            for (var i = 0; i < query.MediaTypes.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append((int)query.MediaTypes[i]);
            }
        }

        sb.Append('|');
        sb.Append(query.ParentId.ToString());

        if (query.DtoOptions?.Fields.Count > 0)
        {
            sb.Append('|');
            for (var i = 0; i < query.DtoOptions.Fields.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append((int)query.DtoOptions.Fields[i]);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Check if a query result should be cached.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <returns>Whether the query result is cacheable.</returns>
    public static bool IsCacheable(InternalItemsQuery query)
    {
        if (!query.EnableTotalRecordCount)
        {
            return false;
        }

        if (!query.Limit.HasValue || query.Limit.Value <= 0)
        {
            return false;
        }

        if (query.StartIndex.GetValueOrDefault() != 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            return false;
        }

        if (query.Genres.Count > 0)
        {
            return false;
        }

        if (query.Years.Length > 0)
        {
            return false;
        }

        if (query.Tags.Length > 0)
        {
            return false;
        }

        if (query.OfficialRatings.Length > 0)
        {
            return false;
        }

        if (query.ArtistIds.Length > 0 || query.AlbumArtistIds.Length > 0 || query.ContributingArtistIds.Length > 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.Person))
        {
            return false;
        }

        if (query.PersonIds.Length > 0)
        {
            return false;
        }

        if (query.AlbumIds.Length > 0)
        {
            return false;
        }

        if (query.GenreIds.Count > 0)
        {
            return false;
        }

        if (query.StudioIds.Length > 0)
        {
            return false;
        }

        if (query.HasParentalRating.HasValue)
        {
            return false;
        }

        if (query.HasOverview.HasValue)
        {
            return false;
        }

        if (query.HasOfficialRating.HasValue)
        {
            return false;
        }

        if (query.IsHD.HasValue || query.Is4K.HasValue || query.Is3D.HasValue)
        {
            return false;
        }

        if (query.VideoTypes.Length > 0)
        {
            return false;
        }

        if (query.ImageTypes.Length > 0)
        {
            return false;
        }

        return true;
    }
}
