---
phase: code-review
reviewed: 2026-07-06T15:44:09.3782381Z
depth: standard
files_reviewed: 10
files_reviewed_list:
  - Emby.Server.Implementations/Dto/DtoService.cs
  - Emby.Server.Implementations/Library/UserViewManager.cs
  - Jellyfin.Api/Controllers/UserLibraryController.cs
  - Jellyfin.Server.Implementations/Item/BaseItemRepository.QueryBuilding.cs
  - Jellyfin.Server.Implementations/Item/BaseItemRepository.Querying.cs
  - src/Jellyfin.Database/Jellyfin.Database.Implementations/ModelConfiguration/BaseItemConfiguration.cs
  - src/Jellyfin.Database/Jellyfin.Database.Implementations/ModelConfiguration/UserDataConfiguration.cs
  - src/Jellyfin.Database/Jellyfin.Database.Implementations/Migrations/MulletaFlixDbContextModelSnapshot.cs
  - src/Jellyfin.Database/Jellyfin.Database.Implementations/Migrations/20260706145845_AddBaseItemSeriesDateIndex.cs
  - src/Jellyfin.Database/Jellyfin.Database.Implementations/Migrations/20260706151652_AddUserDataNextUpIndex.cs
findings:
  critical: 0
  warning: 3
  info: 0
  total: 3
status: issues_found
---

# Phase code-review: Code Review Report

**Reviewed:** 2026-07-06T15:44:09.3782381Z
**Depth:** standard
**Files Reviewed:** 10
**Status:** issues_found

## Summary

I reviewed the latest-items home path from the controller through `UserViewManager.GetLatestItemsAsync` and `BaseItemRepository.GetLatestTvShowItems`, plus the new index definitions and migrations that support the path. The TV series/name regression is fixed, but three performance problems remain in the caller and index shapes.

## Warnings

### WR-01: Non-grouped latest requests still over-fetch 2x

**File:** `Emby.Server.Implementations/Library/UserViewManager.cs:495-507`

**Issue:** `Limit = limit * 2` is applied before the branch that decides whether the request is grouped. When `request.GroupItems` is `false`, the repository still reads and materializes up to twice as many rows as the API can return, even though there is no later grouping step that needs the buffer.

**Fix:**
```csharp
var query = new InternalItemsQuery(user)
{
    IncludeItemTypes = includeItemTypes,
    OrderBy =
    [
        (ItemSortBy.DateCreated, SortOrder.Descending),
        (ItemSortBy.SortName, SortOrder.Descending),
        (ItemSortBy.ProductionYear, SortOrder.Descending)
    ],
    IsFolder = includeItemTypes.Length == 0 ? false : null,
    ExcludeItemTypes = excludeItemTypes,
    IsVirtualItem = false,
    Limit = request.GroupItems ? limit * 2 : limit,
    IsPlayed = isPlayed,
    DtoOptions = options,
    MediaTypes = mediaTypes
};
```

### WR-02: The new TV latest index does not match the actual home-path predicates

**File:** `src/Jellyfin.Database/Jellyfin.Database.Implementations/ModelConfiguration/BaseItemConfiguration.cs:79-83`

**Issue:** `HasIndex(e => new { e.Type, e.SeriesId, e.DateCreated })` ignores the two predicates that are always present in the latest-items query path: `TopParentIds` and `IsVirtualItem = false`. That makes it unlikely the optimizer will choose this new index for the hot request, so the scan bottleneck remains.

**Fix:**
```csharp
// latest TV home query: scope by library folder, type, and series date
builder.HasIndex(e => new { e.TopParentId, e.Type, e.IsVirtualItem, e.SeriesId, e.DateCreated });
```

### WR-03: The new `UserData` index does not help the aggregation query it appears to target

**File:** `src/Jellyfin.Database/Jellyfin.Database.Implementations/ModelConfiguration/UserDataConfiguration.cs:21-23`

**Issue:** `HasIndex(d => new { d.UserId, d.Played, d.LastPlayedDate })` does not line up with the `SeriesDatePlayed` query shape, which filters on `UserId`/`Played` and then joins by `ItemId` before aggregating `MAX(LastPlayedDate)`. The existing `UserId, Played, ItemId` index is the join-friendly one; the new index is unlikely to move the needle for the current hot path.

**Fix:**
```csharp
// Keep the join-friendly index; remove the LastPlayedDate variant unless a direct
// UserId+Played+LastPlayedDate query exists elsewhere.
builder.HasIndex(d => new { d.UserId, d.Played, d.ItemId });
```

---

_Reviewed: 2026-07-06T15:44:09.3782381Z_
_Reviewer: the agent (gsd-code-reviewer)_
_Depth: standard_
