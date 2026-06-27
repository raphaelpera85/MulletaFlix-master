# ItemPersistenceService Performance and Maintainability Optimization Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor and optimize `ItemPersistenceService` by decomposing `UpdateOrInsertItemsCore` into private helper methods and batching all database queries to eliminate N+1 queries.

**Architecture:** We will break the monolithic `UpdateOrInsertItemsCore` method into distinct helper methods. We will batch path resolutions, child existence checks, and orphaned version checks into single DB queries outside the loops.

**Tech Stack:** .NET 10.0, ASP.NET Core, Entity Framework Core, MariaDB.

## Global Constraints
- Naming, coding style, and encoding must strictly follow the repository's `.editorconfig` (UTF-8).
- Keep all existing comments/docstrings intact unless explicitly changed.
- Run tests on `tests/Jellyfin.Server.Implementations.Tests/` after each task to ensure functionality.

---

### Task 1: Replace async-over-sync with native sync database calls

**Files:**
- Modify: [ItemPersistenceService.cs](file:///D:/Users/Raphael/Documents/Projetos/mulletaflix/MulletaFlix-master/Jellyfin.Server.Implementations/Item/ItemPersistenceService.cs)

**Interfaces:**
- Consumes: `MulletaFlixDbContext.SaveChangesAsync(CancellationToken)`
- Produces: `MulletaFlixDbContext.SaveChanges()`

- [ ] **Step 1: Replace SaveChangesAsync(...).GetAwaiter().GetResult()**

Replace the async-over-sync calls in `DeleteItem`, `UpdateInheritedValues`, and `UpdateOrInsertItemsCore` (lines 154, 165, 435, 688) with the synchronous `context.SaveChanges()`.

- [ ] **Step 2: Run tests to verify compatibility**

Run: `dotnet test tests/Jellyfin.Server.Implementations.Tests/ --filter "FullyQualifiedName~ItemPersistenceServiceTests"`
Expected: PASS

- [ ] **Step 3: Commit**

Since git is not initialized as a repository in this directory, we will skip the git commit command but ensure files are saved properly.

---

### Task 2: Extract SaveBaseItemEntities and SaveItemValues helper methods

**Files:**
- Modify: [ItemPersistenceService.cs](file:///D:/Users/Raphael/Documents/Projetos/mulletaflix/MulletaFlix-master/Jellyfin.Server.Implementations/Item/ItemPersistenceService.cs)

**Interfaces:**
- Consumes: `MulletaFlixDbContext`, `BaseItemEntity` mapping helpers
- Produces: `SaveBaseItemEntities(...)`, `SaveItemValues(...)` methods

- [ ] **Step 1: Write helper methods**

Define `SaveBaseItemEntities` and `SaveItemValues` as private helper methods:
```csharp
    private void SaveBaseItemEntities(
        MulletaFlixDbContext context,
        List<(BaseItemDto Item, List<Guid>? AncestorIds, BaseItemDto TopParent, IEnumerable<string> UserDataKey, List<string> InheritedTags)> tuples,
        HashSet<Guid> existingItems)
    {
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
                context.BaseItemProviders.Where(e => e.ItemId == entity.Id).ExecuteDelete();
                context.BaseItemImageInfos.Where(e => e.ItemId == entity.Id).ExecuteDelete();
                context.BaseItemMetadataFields.Where(e => e.ItemId == entity.Id).ExecuteDelete();

                if (entity.Images is { Count: > 0 })
                {
                    context.BaseItemImageInfos.AddRange(entity.Images);
                }

                if (entity.LockedFields is { Count: > 0 })
                {
                    context.BaseItemMetadataFields.AddRange(entity.LockedFields);
                }

                context.BaseItems.Attach(entity).State = EntityState.Modified;
            }
        }
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

        var existingValues = context.ItemValues
            .Where(e => Enumerable.Contains(types, e.Type) && Enumerable.Contains(values, e.Value))
            .AsEnumerable()
            .Where(e => allListedItemValuesSet.Contains((e.Type, e.Value)))
            .DistinctBy(e => (e.Type, e.Value), ItemValueKeyComparer)
            .ToArray();
        var missingItemValues = allListedItemValues.Except(existingValues.Select(f => (MagicNumber: f.Type, f.Value)), ItemValueKeyComparer).Select(f => new ItemValue()
        {
            CleanValue = f.Value.GetCleanValue(),
            ItemValueId = Guid.NewGuid(),
            Type = f.MagicNumber,
            Value = f.Value
        }).ToArray();
        context.ItemValues.AddRange(missingItemValues);

        var itemValuesStore = existingValues.Concat(missingItemValues).ToArray();
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
```

- [ ] **Step 2: Update UpdateOrInsertItemsCore to call the extracted helpers**

Replace the corresponding inlined segments in `UpdateOrInsertItemsCore` with calls to `SaveBaseItemEntities(context, tuples, existingItems)` and `SaveItemValues(context, tuples, ids)`.

- [ ] **Step 3: Verify tests still pass**

Run: `dotnet test tests/Jellyfin.Server.Implementations.Tests/ --filter "FullyQualifiedName~ItemPersistenceServiceTests"`
Expected: PASS

---

### Task 3: Extract SaveAncestorIds and SaveLinkedChildren (with batching optimization)

**Files:**
- Modify: [ItemPersistenceService.cs](file:///D:/Users/Raphael/Documents/Projetos/mulletaflix/MulletaFlix-master/Jellyfin.Server.Implementations/Item/ItemPersistenceService.cs)

**Interfaces:**
- Consumes: `MulletaFlixDbContext`, `WhereOneOrMany` extension
- Produces: `SaveAncestorIds(...)`, `SaveLinkedChildren(...)`

- [ ] **Step 1: Write SaveAncestorIds and SaveLinkedChildren**

Implement `SaveAncestorIds` and the optimized batched version of `SaveLinkedChildren` in `ItemPersistenceService.cs`:
```csharp
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
                var matchedLinkedChildren = new HashSet<Guid>();

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
```

- [ ] **Step 2: Clean up UpdateOrInsertItemsCore main body**

After extracting all segments, `UpdateOrInsertItemsCore` should look like this:
```csharp
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
```

- [ ] **Step 3: Run all tests to verify everything passes**

Run: `dotnet test tests/Jellyfin.Server.Implementations.Tests/`
Expected: PASS
