#pragma warning disable RS0030 // Do not use banned APIs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Playlists;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MulletaFlix.Database.Implementations;
using MulletaFlix.Database.Implementations.Entities;
using MulletaFlix.Extensions;
using MySqlConnector;
using BaseItemDto = MediaBrowser.Controller.Entities.BaseItem;
using DbLinkedChildType = MulletaFlix.Database.Implementations.Entities.LinkedChildType;
using LinkedChildType = MediaBrowser.Controller.Entities.LinkedChildType;

namespace MulletaFlix.Server.Implementations.Item;

/// <summary>
/// Handles item persistence operations (save, delete, update).
/// </summary>
public class ItemPersistenceService : IItemPersistenceService
{
    internal static readonly IEqualityComparer<(ItemValueType MagicNumber, string Value)> ItemValueKeyComparer = new ItemValueKeyEqualityComparer();
    private static readonly SemaphoreSlim[] _updateOrInsertLocks = Enumerable.Range(0, 16).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private readonly IDbContextFactory<MulletaFlixDbContext> _dbProvider;
    private readonly IServerApplicationHost _appHost;
    private readonly ILogger<ItemPersistenceService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemPersistenceService"/> class.
    /// </summary>
    /// <param name="dbProvider">The database context factory.</param>
    /// <param name="appHost">The application host.</param>
    /// <param name="logger">The logger.</param>
    public ItemPersistenceService(
        IDbContextFactory<MulletaFlixDbContext> dbProvider,
        IServerApplicationHost appHost,
        ILogger<ItemPersistenceService> logger)
    {
        _dbProvider = dbProvider;
        _appHost = appHost;
        _logger = logger;
    }

    /// <inheritdoc />
    public void DeleteItem(params IReadOnlyList<Guid> ids)
    {
        if (ids is null || ids.Count == 0 || ids.Any(f => f.Equals(BaseItemRepository.PlaceholderId)))
        {
            throw new ArgumentException("Guid can't be empty or the placeholder id.", nameof(ids));
        }

        using var context = _dbProvider.CreateDbContext();
        using var transaction = context.Database.BeginTransaction();

        var date = (DateTime?)DateTime.UtcNow;

        var descendantIds = DescendantQueryHelper.GetOwnedDescendantIdsBatch(context, ids);
        foreach (var id in ids)
        {
            descendantIds.Add(id);
        }

        var extraIds = context.BaseItems
            .Where(e => e.OwnerId.HasValue)
            .WhereOneOrMany(descendantIds.ToList(), e => e.OwnerId!.Value)
            .Select(e => e.Id)
            .ToArray();

        foreach (var extraId in extraIds)
        {
            descendantIds.Add(extraId);
        }

        var relatedItems = descendantIds.ToArray();

        // When batch-deleting, multiple items may have UserData for the same (UserId, CustomDataKey).
        // Moving all of them to PlaceholderId would violate the UNIQUE constraint.
        // Deduplicate by loading keys client-side, keeping the best row per group.
        var batchUserData = context.UserData.WhereOneOrMany(relatedItems, e => e.ItemId);

        var allRows = batchUserData
            .Select(ud => new { ud.ItemId, ud.UserId, ud.CustomDataKey, ud.LastPlayedDate, ud.PlayCount })
            .ToList();

        var duplicateRows = allRows
            .GroupBy(ud => new { ud.UserId, ud.CustomDataKey })
            .Where(g => g.Count() > 1)
            .SelectMany(g => g
                .OrderByDescending(ud => ud.LastPlayedDate)
                .ThenByDescending(ud => ud.PlayCount)
                .Skip(1))
            .ToList();

        if (duplicateRows.Count > 0)
        {
            var dupItemIds = duplicateRows.Select(d => d.ItemId).Distinct().ToList();
            var candidates = context.UserData
                .WhereOneOrMany(dupItemIds, ud => ud.ItemId)
                .ToList();
            var duplicateKeys = duplicateRows
                .Select(d => (d.ItemId, d.UserId, d.CustomDataKey))
                .ToHashSet();
            var toDelete = candidates
                .Where(ud => duplicateKeys.Contains((ud.ItemId, ud.UserId, ud.CustomDataKey)))
                .ToList();
            if (toDelete.Count > 0)
            {
                context.UserData.RemoveRange(toDelete);
            }
        }

        // Delete existing placeholder rows that would conflict with the incoming ones
        context.UserData
            .Join(
                batchUserData,
                placeholder => new { placeholder.UserId, placeholder.CustomDataKey },
                userData => new { userData.UserId, userData.CustomDataKey },
                (placeholder, userData) => placeholder)
            .Where(e => e.ItemId == BaseItemRepository.PlaceholderId)
            .ExecuteDelete();

        batchUserData
            .ExecuteUpdate(e => e
                .SetProperty(f => f.RetentionDate, date)
                .SetProperty(f => f.ItemId, BaseItemRepository.PlaceholderId));

        context.AncestorIds.WhereOneOrMany(relatedItems, e => e.ItemId).ExecuteDelete();
        context.AncestorIds.WhereOneOrMany(relatedItems, e => e.ParentItemId).ExecuteDelete();
        context.AttachmentStreamInfos.WhereOneOrMany(relatedItems, e => e.ItemId).ExecuteDelete();
        context.BaseItemImageInfos.WhereOneOrMany(relatedItems, e => e.ItemId).ExecuteDelete();
        context.BaseItemMetadataFields.WhereOneOrMany(relatedItems, e => e.ItemId).ExecuteDelete();
        context.BaseItemProviders.WhereOneOrMany(relatedItems, e => e.ItemId).ExecuteDelete();
        context.BaseItemTrailerTypes.WhereOneOrMany(relatedItems, e => e.ItemId).ExecuteDelete();
        context.Chapters.WhereOneOrMany(relatedItems, e => e.ItemId).ExecuteDelete();
        context.CustomItemDisplayPreferences.WhereOneOrMany(relatedItems, e => e.ItemId).ExecuteDelete();
        context.ItemDisplayPreferences.WhereOneOrMany(relatedItems, e => e.ItemId).ExecuteDelete();
        context.ItemValues.Where(e => e.BaseItemsMap!.Count == 0).ExecuteDelete();
        context.ItemValuesMap.WhereOneOrMany(relatedItems, e => e.ItemId).ExecuteDelete();
        context.LinkedChildren.WhereOneOrMany(relatedItems, e => e.ParentId).ExecuteDelete();
        context.LinkedChildren.WhereOneOrMany(relatedItems, e => e.ChildId).ExecuteDelete();
        context.BaseItems.WhereOneOrMany(relatedItems, e => e.Id).ExecuteDelete();
        context.KeyframeData.WhereOneOrMany(relatedItems, e => e.ItemId).ExecuteDelete();
        context.MediaSegments.WhereOneOrMany(relatedItems, e => e.ItemId).ExecuteDelete();
        context.MediaStreamInfos.WhereOneOrMany(relatedItems, e => e.ItemId).ExecuteDelete();
        var query = context.PeopleBaseItemMap.WhereOneOrMany(relatedItems, e => e.ItemId).Select(f => f.PeopleId).Distinct().ToArray();
        context.PeopleBaseItemMap.WhereOneOrMany(relatedItems, e => e.ItemId).ExecuteDelete();
        context.Peoples.WhereOneOrMany(query, e => e.Id).Where(e => e.BaseItems!.Count == 0).ExecuteDelete();
        context.TrickplayInfos.WhereOneOrMany(relatedItems, e => e.ItemId).ExecuteDelete();
        context.SaveChanges();
        transaction.Commit();
    }

    /// <inheritdoc />
    public void UpdateInheritedValues()
    {
        using var context = _dbProvider.CreateDbContext();
        using var transaction = context.Database.BeginTransaction();

        context.ItemValuesMap.Where(e => e.ItemValue.Type == ItemValueType.InheritedTags).ExecuteDelete();
        context.SaveChanges();

        transaction.Commit();
    }

    /// <inheritdoc />
    public void SaveItems(IReadOnlyList<BaseItemDto> items, CancellationToken cancellationToken)
    {
        UpdateOrInsertItems(items, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveImagesAsync(BaseItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var images = item.ImageInfos.Select(e => BaseItemMapper.MapImageToEntity(item.Id, e)).ToArray();

        var context = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            if (!await context.BaseItems
                .AnyAsync(bi => bi.Id == item.Id, cancellationToken)
                .ConfigureAwait(false))
            {
                _logger.LogWarning("Unable to save ImageInfo for non existing BaseItem");
                return;
            }

            var existingImages = await context.BaseItemImageInfos
                .Where(e => e.ItemId == item.Id)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            bool hasChanges = existingImages.Length != images.Length;
            if (!hasChanges)
            {
                for (int i = 0; i < images.Length; i++)
                {
                    var newImg = images[i];
                    var oldImg = existingImages.FirstOrDefault(e => e.ImageType == newImg.ImageType && e.Path == newImg.Path);
                    if (oldImg == null
                        || oldImg.Width != newImg.Width
                        || oldImg.Height != newImg.Height
                        || oldImg.DateModified != newImg.DateModified
                        || !NullableSequenceEqual(oldImg.Blurhash, newImg.Blurhash))
                    {
                        hasChanges = true;
                        break;
                    }
                }
            }

            if (!hasChanges)
            {
                return;
            }

            await context.BaseItemImageInfos
                .Where(e => e.ItemId == item.Id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await context.BaseItemImageInfos
                .AddRangeAsync(images, cancellationToken)
                .ConfigureAwait(false);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task ReattachUserDataAsync(BaseItemDto item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();

        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        await using (dbContext.ConfigureAwait(false))
        {
            var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var userKeys = item.GetUserDataKeys().ToArray();
                var retentionDate = (DateTime?)null;

                await dbContext.UserData
                    .Where(e => e.ItemId == BaseItemRepository.PlaceholderId)
                    .Where(e => Enumerable.Contains(userKeys, e.CustomDataKey))
                    .ExecuteUpdateAsync(
                        e => e
                            .SetProperty(f => f.ItemId, item.Id)
                            .SetProperty(f => f.RetentionDate, retentionDate),
                        cancellationToken).ConfigureAwait(false);

                item.UserData = await dbContext.UserData
                    .AsNoTracking()
                    .Where(e => e.ItemId == item.Id)
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void UpdateOrInsertItems(IReadOnlyList<BaseItemDto> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        cancellationToken.ThrowIfCancellationRequested();

        var lockIndex = items.Count > 0 ? (items[0].Id.GetHashCode() & 15) : 0;
        var updateLock = _updateOrInsertLocks[lockIndex];
        updateLock.WaitAsync(cancellationToken).GetAwaiter().GetResult();
        try
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    UpdateOrInsertItemsCore(items);
                    return;
                }
                catch (DbUpdateException ex) when (attempt == 1 && IsDuplicateItemValueConflict(ex))
                {
                    _logger.LogWarning(ex, "Concurrent item value insert detected while saving metadata. Retrying once.");
                }
            }
        }
        finally
        {
            updateLock.Release();
        }
    }

    private void UpdateOrInsertItemsCore(IReadOnlyList<BaseItemDto> items)
    {
        var tuples = new List<(BaseItemDto Item, List<Guid>? AncestorIds, BaseItemDto TopParent, IEnumerable<string> UserDataKey, List<string> InheritedTags)>();
        foreach (var item in items.GroupBy(e => e.Id).Select(e => e.Last()).Where(e => e.Id != BaseItemRepository.PlaceholderId))
        {
            var ancestorIds = item.SupportsAncestors ?
                item.GetAncestorIds().Distinct().ToList() :
                null;

            var topParent = item.GetTopParent();

            var userdataKey = item.GetUserDataKeys();
            var inheritedTags = item.GetInheritedTags();

            tuples.Add((item, ancestorIds, topParent, userdataKey, inheritedTags));
        }

        using var context = _dbProvider.CreateDbContext();
        using var transaction = context.Database.BeginTransaction();

        var ids = tuples.Select(f => f.Item.Id).ToArray();
        var existingItems = context.BaseItems.Where(e => Enumerable.Contains(ids, e.Id)).Select(f => f.Id).ToHashSet();

        // 1. Save Base Item Entities
        SaveBaseItemEntities(context, tuples, existingItems);

        // 2. Save Item Values Maps
        SaveItemValues(context, tuples, ids);

        // 3. Save Ancestor IDs
        SaveAncestorIds(context, tuples);

        context.SaveChanges();

        // 4. Save Linked Children (for Folders and Videos)
        var folderIds = tuples
            .Where(t => t.Item is Folder)
            .Select(t => t.Item.Id)
            .ToList();

        var videoIds = tuples
            .Where(t => t.Item is Video)
            .Select(t => t.Item.Id)
            .ToList();

        if (folderIds.Count > 0 || videoIds.Count > 0)
        {
            SaveLinkedChildren(context, tuples, folderIds, videoIds);
        }

        context.SaveChanges();
        transaction.Commit();
    }

    private static bool IsDuplicateItemValueConflict(DbUpdateException exception)
    {
        return exception.InnerException is MySqlException mySqlException
               && mySqlException.Number == 1062
               && mySqlException.Message.Contains("IX_ItemValues_Type_Value", StringComparison.OrdinalIgnoreCase);
    }

    private static List<(ItemValueType MagicNumber, string Value)> GetItemValuesToSave(BaseItemDto item, List<string> inheritedTags)
    {
        var list = new List<(ItemValueType, string)>();

        if (item is IHasArtist hasArtist)
        {
            list.AddRange(hasArtist.Artists.Select(i => ((ItemValueType)0, i)));
        }

        if (item is IHasAlbumArtist hasAlbumArtist)
        {
            list.AddRange(hasAlbumArtist.AlbumArtists.Select(i => (ItemValueType.AlbumArtist, i)));
        }

        list.AddRange(item.Genres.Select(i => (ItemValueType.Genre, i)));
        list.AddRange(item.Studios.Select(i => (ItemValueType.Studios, i)));
        list.AddRange(item.Tags.Select(i => (ItemValueType.Tags, i)));

        list.AddRange(inheritedTags.Select(i => (ItemValueType.InheritedTags, i)));

        list.RemoveAll(i => string.IsNullOrWhiteSpace(i.Item2));

        return list;
    }

    internal static Dictionary<(ItemValueType MagicNumber, string Value), ItemValue> CreateItemValueLookup(IEnumerable<ItemValue> itemValues)
    {
        var lookup = new Dictionary<(ItemValueType MagicNumber, string Value), ItemValue>(ItemValueKeyComparer);

        foreach (var itemValue in itemValues)
        {
            lookup[(itemValue.Type, itemValue.Value)] = itemValue;
        }

        return lookup;
    }

    internal static Dictionary<Guid, Dictionary<Guid, ItemValueMap>> CreateItemValueMapLookup(IEnumerable<ItemValueMap> mappedValues)
    {
        var lookup = new Dictionary<Guid, Dictionary<Guid, ItemValueMap>>();

        foreach (var mappedValue in mappedValues)
        {
            if (!lookup.TryGetValue(mappedValue.ItemId, out var itemLookup))
            {
                itemLookup = new Dictionary<Guid, ItemValueMap>();
                lookup[mappedValue.ItemId] = itemLookup;
            }

            itemLookup[mappedValue.ItemValueId] = mappedValue;
        }

        return lookup;
    }

    internal static Dictionary<Guid, Dictionary<Guid, AncestorId>> CreateAncestorLookup(IEnumerable<AncestorId> ancestorIds)
    {
        var lookup = new Dictionary<Guid, Dictionary<Guid, AncestorId>>();

        foreach (var ancestorId in ancestorIds)
        {
            if (!lookup.TryGetValue(ancestorId.ItemId, out var itemLookup))
            {
                itemLookup = new Dictionary<Guid, AncestorId>();
                lookup[ancestorId.ItemId] = itemLookup;
            }

            itemLookup[ancestorId.ParentItemId] = ancestorId;
        }

        return lookup;
    }

    internal static Dictionary<Guid, LinkedChildEntity> CreateLinkedChildLookup(IEnumerable<LinkedChildEntity> linkedChildren)
    {
        var lookup = new Dictionary<Guid, LinkedChildEntity>();

        foreach (var linkedChild in linkedChildren)
        {
            if (!lookup.ContainsKey(linkedChild.ChildId))
            {
                lookup[linkedChild.ChildId] = linkedChild;
            }
        }

        return lookup;
    }

    private void SaveBaseItemEntities(
        MulletaFlixDbContext context,
        List<(BaseItemDto Item, List<Guid>? AncestorIds, BaseItemDto TopParent, IEnumerable<string> UserDataKey, List<string> InheritedTags)> tuples,
        HashSet<Guid> existingItems)
    {
        var existingIdsList = existingItems.ToList();
        var existingProviders = existingIdsList.Count > 0
            ? context.BaseItemProviders
                .AsNoTracking()
                .WhereOneOrMany(existingIdsList, e => e.ItemId)
                .ToList()
                .GroupBy(e => e.ItemId)
                .ToDictionary(g => g.Key, g => g.ToList())
            : new Dictionary<Guid, List<BaseItemProvider>>();

        var existingMetadataFields = existingIdsList.Count > 0
            ? context.BaseItemMetadataFields
                .AsNoTracking()
                .WhereOneOrMany(existingIdsList, e => e.ItemId)
                .ToList()
                .GroupBy(e => e.ItemId)
                .ToDictionary(g => g.Key, g => g.ToList())
            : new Dictionary<Guid, List<BaseItemMetadataField>>();

        foreach (var item in tuples)
        {
            var entity = BaseItemMapper.Map(item.Item, _appHost);
            entity.TopParentId = item.TopParent?.Id;

            if (!existingItems.Contains(entity.Id))
            {
                context.BaseItems.Add(entity);
            }
            else
            {
                var currentProviders = entity.Provider?.ToArray() ?? [];
                var currentLockedFields = entity.LockedFields?.ToArray();
                var currentImages = entity.Images?.ToArray();
                var currentTrailerTypes = entity.TrailerTypes?.ToArray();

                ClearTrackedNavigationProperties(entity);

                // Check if Providers changed
                var oldProviders = existingProviders.GetValueOrDefault(entity.Id) ?? new List<BaseItemProvider>();
                bool providersChanged = currentProviders.Length != oldProviders.Count ||
                    currentProviders.Any(cp => !oldProviders.Any(op => op.ProviderId == cp.ProviderId && op.ProviderValue == cp.ProviderValue));

                if (providersChanged)
                {
                    context.BaseItemProviders.Where(e => e.ItemId == entity.Id).ExecuteDelete();
                    if (currentProviders.Length > 0)
                    {
                        context.BaseItemProviders.AddRange(currentProviders);
                    }
                }

                // Check if Images changed (only touch if entity.Images is explicitly defined/not null)
                if (currentImages is not null)
                {
                    context.BaseItemImageInfos.Where(e => e.ItemId == entity.Id).ExecuteDelete();
                    if (currentImages.Length > 0)
                    {
                        context.BaseItemImageInfos.AddRange(currentImages);
                    }
                }

                // Check if LockedFields changed
                var oldLockedFields = existingMetadataFields.GetValueOrDefault(entity.Id) ?? new List<BaseItemMetadataField>();
                bool lockedFieldsChanged = currentLockedFields is not null &&
                    (currentLockedFields.Length != oldLockedFields.Count ||
                    currentLockedFields.Any(cf => !oldLockedFields.Any(of => of.Id == cf.Id)));

                if (lockedFieldsChanged)
                {
                    context.BaseItemMetadataFields.Where(e => e.ItemId == entity.Id).ExecuteDelete();
                    if (currentLockedFields.Length > 0)
                    {
                        context.BaseItemMetadataFields.AddRange(currentLockedFields);
                    }
                }

                if (currentTrailerTypes is not null)
                {
                    context.BaseItemTrailerTypes.Where(e => e.ItemId == entity.Id).ExecuteDelete();
                    if (currentTrailerTypes.Length > 0)
                    {
                        context.BaseItemTrailerTypes.AddRange(currentTrailerTypes);
                    }
                }

                context.BaseItems.Attach(entity).State = EntityState.Modified;
            }
        }
    }

    internal static void ClearTrackedNavigationProperties(BaseItemEntity entity)
    {
        entity.Provider = null;
        entity.LockedFields = null;
        entity.Images = null;
        entity.TrailerTypes = null;
    }

    private void SaveItemValues(
        MulletaFlixDbContext context,
        List<(BaseItemDto Item, List<Guid>? AncestorIds, BaseItemDto TopParent, IEnumerable<string> UserDataKey, List<string> InheritedTags)> tuples,
        Guid[] ids)
    {
        var itemValueMaps = tuples
            .Select(e => (e.Item, Values: GetItemValuesToSave(e.Item, e.InheritedTags)))
            .ToArray();
        var allListedItemValues = itemValueMaps
            .SelectMany(f => f.Values)
            .Distinct(ItemValueKeyComparer)
            .ToArray();

        var types = allListedItemValues.Select(e => e.MagicNumber).Distinct().ToArray();
        var values = allListedItemValues.Select(e => e.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var allListedItemValuesSet = allListedItemValues.ToHashSet(ItemValueKeyComparer);

        InsertItemValuesIgnoreDuplicates(context, allListedItemValues);

        var existingValues = context.ItemValues
            .AsNoTracking()
            .Where(e => Enumerable.Contains(types, e.Type) && Enumerable.Contains(values, e.Value))
            .AsEnumerable()
            .Where(e => allListedItemValuesSet.Contains((e.Type, e.Value)))
            .DistinctBy(e => (e.Type, e.Value), ItemValueKeyComparer)
            .ToArray();

        var itemValuesStore = existingValues;
        var itemValuesStoreLookup = CreateItemValueLookup(itemValuesStore);
        var valueMap = itemValueMaps
            .Select(f => (f.Item, Values: f.Values.Select(e => itemValuesStoreLookup[(e.MagicNumber, e.Value)]).DistinctBy(e => e.ItemValueId).ToArray()))
            .ToArray();

        var mappedValues = context.ItemValuesMap.Where(e => Enumerable.Contains(ids, e.ItemId)).ToList();
        var mappedValuesByItemId = CreateItemValueMapLookup(mappedValues);

        foreach (var item in valueMap)
        {
            var itemMappedValues = mappedValuesByItemId.GetValueOrDefault(item.Item.Id);
            foreach (var itemValue in item.Values)
            {
                if (itemMappedValues is null || !itemMappedValues.Remove(itemValue.ItemValueId, out _))
                {
                    context.ItemValuesMap.Add(new ItemValueMap()
                    {
                        Item = null!,
                        ItemId = item.Item.Id,
                        ItemValue = null!,
                        ItemValueId = itemValue.ItemValueId
                    });
                }
            }

            if (itemMappedValues is not null && itemMappedValues.Count > 0)
            {
                context.ItemValuesMap.RemoveRange(itemMappedValues.Values);
            }
        }
    }

    private static void InsertItemValuesIgnoreDuplicates(
        MulletaFlixDbContext context,
        IReadOnlyList<(ItemValueType MagicNumber, string Value)> allListedItemValues)
    {
        if (allListedItemValues.Count == 0)
        {
            return;
        }

        var commandText = new StringBuilder("INSERT IGNORE INTO `ItemValues` (`ItemValueId`, `Type`, `Value`, `CleanValue`) VALUES ");
        var parameters = new List<object>(allListedItemValues.Count * 4);

        for (var index = 0; index < allListedItemValues.Count; index++)
        {
            if (index > 0)
            {
                commandText.Append(", ");
            }

            var value = allListedItemValues[index];
            commandText.Append("(@p").Append(index).Append("_id, @p").Append(index).Append("_type, @p").Append(index).Append("_value, @p").Append(index).Append("_cleanValue)");
            parameters.Add(new MySqlParameter($"@p{index}_id", MySqlDbType.VarChar) { Value = Guid.NewGuid().ToString() });
            parameters.Add(new MySqlParameter($"@p{index}_type", MySqlDbType.Int32) { Value = (int)value.MagicNumber });
            parameters.Add(new MySqlParameter($"@p{index}_value", MySqlDbType.VarChar) { Value = value.Value });
            parameters.Add(new MySqlParameter($"@p{index}_cleanValue", MySqlDbType.VarChar) { Value = value.Value.GetCleanValue() });
        }

        context.Database.ExecuteSqlRaw(commandText.ToString(), parameters.ToArray());
    }

    private void SaveAncestorIds(
        MulletaFlixDbContext context,
        List<(BaseItemDto Item, List<Guid>? AncestorIds, BaseItemDto TopParent, IEnumerable<string> UserDataKey, List<string> InheritedTags)> tuples)
    {
        var itemsWithAncestors = tuples
            .Where(t => t.Item.SupportsAncestors && t.AncestorIds != null)
            .Select(t => t.Item.Id)
            .ToList();

        var allExistingAncestorIds = itemsWithAncestors.Count > 0
            ? context.AncestorIds
                .Where(e => itemsWithAncestors.Contains(e.ItemId))
                .ToList()
            : [];
        var existingAncestorIdsByItemId = CreateAncestorLookup(allExistingAncestorIds);

        var allRequestedAncestorIds = tuples
            .Where(t => t.Item.SupportsAncestors && t.AncestorIds != null)
            .SelectMany(t => t.AncestorIds!)
            .Distinct()
            .ToList();

        var validAncestorIdsSet = allRequestedAncestorIds.Count > 0
            ? context.BaseItems
                .Where(e => allRequestedAncestorIds.Contains(e.Id))
                .Select(f => f.Id)
                .ToHashSet()
            : new HashSet<Guid>();

        foreach (var item in tuples)
        {
            if (item.Item.SupportsAncestors && item.AncestorIds != null)
            {
                var validAncestorIds = item.AncestorIds.Where(id => validAncestorIdsSet.Contains(id)).ToArray();
                var existingAncestorIds = existingAncestorIdsByItemId.GetValueOrDefault(item.Item.Id);
                foreach (var ancestorId in validAncestorIds)
                {
                    if (existingAncestorIds is null || !existingAncestorIds.Remove(ancestorId, out _))
                    {
                        context.AncestorIds.Add(new AncestorId()
                        {
                            ParentItemId = ancestorId,
                            ItemId = item.Item.Id,
                            Item = null!,
                            ParentItem = null!
                        });
                    }
                }

                if (existingAncestorIds is not null && existingAncestorIds.Count > 0)
                {
                    context.AncestorIds.RemoveRange(existingAncestorIds.Values);
                }
            }
        }
    }

    private void SaveLinkedChildren(
        MulletaFlixDbContext context,
        List<(BaseItemDto Item, List<Guid>? AncestorIds, BaseItemDto TopParent, IEnumerable<string> UserDataKey, List<string> InheritedTags)> tuples,
        List<Guid> folderIds,
        List<Guid> videoIds)
    {
        var allParentIds = folderIds.Concat(videoIds).Distinct().ToList();
        var allLinkedChildren = allParentIds.Count > 0
            ? context.LinkedChildren
                .Where(e => allParentIds.Contains(e.ParentId))
                .ToList()
            : new List<LinkedChildEntity>();

        var allLinkedChildrenByParent = allLinkedChildren
            .GroupBy(e => e.ParentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 1. Batch path resolution
        var allFolderPathsToResolve = tuples
            .Where(t => t.Item is Folder)
            .SelectMany(t => ((Folder)t.Item).LinkedChildren)
            .Where(lc => (!lc.ItemId.HasValue || lc.ItemId.Value.IsEmpty()) && !string.IsNullOrEmpty(lc.Path))
            .Select(lc => lc.Path)
            .Distinct();

        var allVideoPathsToResolve = tuples
            .Where(t => t.Item is Video)
            .SelectMany(t => ((Video)t.Item).LocalAlternateVersions)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct();

        var allPathsToResolve = allFolderPathsToResolve
            .Concat(allVideoPathsToResolve)
            .Distinct()
            .ToList();

        var pathToIdMap = allPathsToResolve.Count > 0
            ? context.BaseItems
                .Where(e => e.Path != null && allPathsToResolve.Contains(e.Path))
                .Select(e => new { e.Path, e.Id })
                .GroupBy(e => e.Path!)
                .ToDictionary(g => g.Key, g => g.First().Id)
            : new Dictionary<string, Guid>();

        // 2. Collect all child IDs to check
        var allChildIdsToCheck = new HashSet<Guid>();
        foreach (var item in tuples)
        {
            if (item.Item is Folder folder)
            {
                foreach (var linkedChild in folder.LinkedChildren)
                {
                    var childId = linkedChild.ItemId;
                    if ((!childId.HasValue || childId.Value.IsEmpty()) && !string.IsNullOrEmpty(linkedChild.Path))
                    {
                        if (pathToIdMap.TryGetValue(linkedChild.Path, out var resolvedId))
                        {
                            childId = resolvedId;
                        }
                    }
                    if (childId.HasValue && !childId.Value.IsEmpty())
                    {
                        allChildIdsToCheck.Add(childId.Value);
                    }
                }
            }
            else if (item.Item is Video video)
            {
                foreach (var path in video.LocalAlternateVersions)
                {
                    if (!string.IsNullOrEmpty(path) && pathToIdMap.TryGetValue(path, out var childId))
                    {
                        allChildIdsToCheck.Add(childId);
                    }
                }
                foreach (var linkedChild in video.LinkedAlternateVersions)
                {
                    if (linkedChild.ItemId.HasValue && !linkedChild.ItemId.Value.IsEmpty())
                    {
                        allChildIdsToCheck.Add(linkedChild.ItemId.Value);
                    }
                }
            }
        }

        // 3. Batch query existence of all child IDs
        var allExistingChildIds = allChildIdsToCheck.Count > 0
            ? context.BaseItems
                .WhereOneOrMany(allChildIdsToCheck.ToList(), e => e.Id)
                .Select(e => e.Id)
                .ToHashSet()
            : new HashSet<Guid>();

        // 4. Batch query all potential orphaned local version items
        var allOrphanedLocalVersionIds = new HashSet<Guid>();
        foreach (var item in tuples)
        {
            if (item.Item is Video video)
            {
                var existingLinkedChildren = (allLinkedChildrenByParent.GetValueOrDefault(video.Id) ?? new List<LinkedChildEntity>())
                    .Where(e => (int)e.ChildType == 2 || (int)e.ChildType == 3)
                    .ToList();

                var newLinkedChildren = new List<Guid>();
                if (video.LocalAlternateVersions.Length > 0)
                {
                    foreach (var path in video.LocalAlternateVersions)
                    {
                        if (!string.IsNullOrEmpty(path) && pathToIdMap.TryGetValue(path, out var childId))
                        {
                            newLinkedChildren.Add(childId);
                        }
                    }
                }
                if (video.LinkedAlternateVersions.Length > 0)
                {
                    foreach (var linkedChild in video.LinkedAlternateVersions)
                    {
                        if (linkedChild.ItemId.HasValue && !linkedChild.ItemId.Value.IsEmpty())
                        {
                            newLinkedChildren.Add(linkedChild.ItemId.Value);
                        }
                    }
                }

                var newLinkedChildrenSet = newLinkedChildren.ToHashSet();
                foreach (var existingLink in existingLinkedChildren)
                {
                    if (!newLinkedChildrenSet.Contains(existingLink.ChildId))
                    {
                        if (existingLink.ChildType == DbLinkedChildType.LocalAlternateVersion)
                        {
                            allOrphanedLocalVersionIds.Add(existingLink.ChildId);
                        }
                    }
                }
            }
        }

        var orphanedItems = allOrphanedLocalVersionIds.Count > 0
            ? context.BaseItems
                .WhereOneOrMany(allOrphanedLocalVersionIds.ToList(), e => e.Id)
                .Where(e => e.OwnerId.HasValue)
                .ToDictionary(e => (e.Id, e.OwnerId!.Value), e => e)
            : new Dictionary<(Guid Id, Guid OwnerId), BaseItemEntity>();

        // 5. Process Folder Linked Children
        foreach (var item in tuples)
        {
            if (item.Item is Folder folder)
            {
                var existingLinkedChildren = allLinkedChildrenByParent.GetValueOrDefault(item.Item.Id)?.ToList() ?? new List<LinkedChildEntity>();
                var existingLinkedChildrenByChildId = CreateLinkedChildLookup(existingLinkedChildren);
                var matchedLinkedChildren = new HashSet<LinkedChildEntity>();
                if (folder.LinkedChildren.Length > 0)
                {
                    var resolvedChildren = new List<(LinkedChild Child, Guid ChildId)>();
                    foreach (var linkedChild in folder.LinkedChildren)
                    {
                        var childItemId = linkedChild.ItemId;
                        if (!childItemId.HasValue || childItemId.Value.IsEmpty())
                        {
                            if (!string.IsNullOrEmpty(linkedChild.Path) && pathToIdMap.TryGetValue(linkedChild.Path, out var resolvedId))
                            {
                                childItemId = resolvedId;
                            }
                        }

                        if (childItemId.HasValue && !childItemId.Value.IsEmpty())
                        {
                            resolvedChildren.Add((linkedChild, childItemId.Value));
                        }
                    }

                    resolvedChildren = resolvedChildren
                        .GroupBy(c => c.ChildId)
                        .Select(g => g.Last())
                        .ToList();

                    var isPlaylist = folder is Playlist;
                    var sortOrder = 0;
                    foreach (var (linkedChild, childId) in resolvedChildren)
                    {
                        if (!allExistingChildIds.Contains(childId))
                        {
                            _logger.LogWarning(
                                "Skipping LinkedChild for parent {ParentName} ({ParentId}): child item {ChildId} does not exist in database",
                                item.Item.Name,
                                item.Item.Id,
                                childId);
                            continue;
                        }

                        if (!existingLinkedChildrenByChildId.Remove(childId, out var existingLink))
                        {
                            context.LinkedChildren.Add(new LinkedChildEntity()
                            {
                                ParentId = item.Item.Id,
                                ChildId = childId,
                                ChildType = (DbLinkedChildType)linkedChild.Type,
                                SortOrder = isPlaylist ? sortOrder : null
                            });
                        }
                        else
                        {
                            existingLink.SortOrder = isPlaylist ? sortOrder : null;
                            existingLink.ChildType = (DbLinkedChildType)linkedChild.Type;
                            matchedLinkedChildren.Add(existingLink);
                        }

                        sortOrder++;
                    }
                }

                if (existingLinkedChildren.Count > 0)
                {
                    var linkedChildrenToRemove = existingLinkedChildren
                        .Where(e => !matchedLinkedChildren.Contains(e))
                        .ToList();

                    if (linkedChildrenToRemove.Count > 0)
                    {
                        context.LinkedChildren.RemoveRange(linkedChildrenToRemove);
                    }
                }
            }

            // 6. Process Video Linked Children
            if (item.Item is Video video)
            {
                var existingLinkedChildren = (allLinkedChildrenByParent.GetValueOrDefault(video.Id) ?? new List<LinkedChildEntity>())
                    .Where(e => (int)e.ChildType == 2 || (int)e.ChildType == 3)
                    .ToList();
                var existingLinkedChildrenByChildId = CreateLinkedChildLookup(existingLinkedChildren);
                var matchedLinkedChildren = new HashSet<LinkedChildEntity>();

                var newLinkedChildren = new List<(Guid ChildId, LinkedChildType Type)>();

                if (video.LocalAlternateVersions.Length > 0)
                {
                    foreach (var path in video.LocalAlternateVersions)
                    {
                        if (!string.IsNullOrEmpty(path) && pathToIdMap.TryGetValue(path, out var childId))
                        {
                            newLinkedChildren.Add((childId, LinkedChildType.LocalAlternateVersion));
                        }
                    }
                }

                if (video.LinkedAlternateVersions.Length > 0)
                {
                    foreach (var linkedChild in video.LinkedAlternateVersions)
                    {
                        if (linkedChild.ItemId.HasValue && !linkedChild.ItemId.Value.IsEmpty())
                        {
                            newLinkedChildren.Add((linkedChild.ItemId.Value, LinkedChildType.LinkedAlternateVersion));
                        }
                    }
                }

                newLinkedChildren = newLinkedChildren
                    .GroupBy(c => c.ChildId)
                    .Select(g => g.Last())
                    .ToList();

                int sortOrder = 0;
                foreach (var (childId, childType) in newLinkedChildren)
                {
                    if (!allExistingChildIds.Contains(childId))
                    {
                        _logger.LogWarning(
                            "Skipping alternate version for video {VideoName} ({VideoId}): child item {ChildId} does not exist in database",
                            video.Name,
                            video.Id,
                            childId);
                        continue;
                    }

                    if (!existingLinkedChildrenByChildId.Remove(childId, out var existingLink))
                    {
                        context.LinkedChildren.Add(new LinkedChildEntity
                        {
                            ParentId = video.Id,
                            ChildId = childId,
                            ChildType = (DbLinkedChildType)childType,
                            SortOrder = sortOrder
                        });
                    }
                    else
                    {
                        existingLink.ChildType = (DbLinkedChildType)childType;
                        existingLink.SortOrder = sortOrder;
                        matchedLinkedChildren.Add(existingLink);
                    }

                    sortOrder++;
                }

                if (existingLinkedChildren.Count > 0)
                {
                    var remainingLinkedChildren = existingLinkedChildren
                        .Where(e => !matchedLinkedChildren.Contains(e))
                        .ToList();

                    if (remainingLinkedChildren.Count > 0)
                    {
                        context.LinkedChildren.RemoveRange(remainingLinkedChildren);
                    }

                    var orphanedItemsToRemove = new List<BaseItemEntity>();
                    foreach (var remaining in remainingLinkedChildren)
                    {
                        if (remaining.ChildType == DbLinkedChildType.LocalAlternateVersion &&
                            orphanedItems.TryGetValue((remaining.ChildId, video.Id), out var orphanedItem))
                        {
                            orphanedItemsToRemove.Add(orphanedItem);
                        }
                    }

                    if (orphanedItemsToRemove.Count > 0)
                    {
                        _logger.LogInformation(
                            "Deleting {Count} orphaned LocalAlternateVersion items for video {VideoName} ({VideoId})",
                            orphanedItemsToRemove.Count,
                            video.Name,
                            video.Id);
                        context.BaseItems.RemoveRange(orphanedItemsToRemove);
                    }
                }
            }
        }
    }

    private static bool NullableSequenceEqual(byte[]? a, byte[]? b)
    {
        if (a == null && b == null)
        {
            return true;
        }

        if (a == null || b == null)
        {
            return false;
        }

        return a.SequenceEqual(b);
    }

    private sealed class ItemValueKeyEqualityComparer : IEqualityComparer<(ItemValueType MagicNumber, string Value)>
    {
        public bool Equals((ItemValueType MagicNumber, string Value) x, (ItemValueType MagicNumber, string Value) y)
        {
            return x.MagicNumber == y.MagicNumber && string.Equals(x.Value, y.Value, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((ItemValueType MagicNumber, string Value) obj)
        {
            return HashCode.Combine(obj.MagicNumber, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Value));
        }
    }
}
