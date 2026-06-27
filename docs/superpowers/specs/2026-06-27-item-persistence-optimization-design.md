# Design Spec: ItemPersistenceService Performance and Maintainability Optimization

**Date**: 2026-06-27  
**Sprint**: Sprint 2 - Persistência e Escrita  
**Status**: Approved  

---

## 1. Goal

Optimize `ItemPersistenceService` to:
- Reduce execution latency and thread-pool blocking during item saves and deletes.
- Eliminate N+1 SQL queries inside loops for resolving paths and verifying children.
- Improve codebase maintainability by breaking down `UpdateOrInsertItemsCore` into smaller, cohesive methods.

---

## 2. Proposed Changes

### 2.1. Decomposition of `UpdateOrInsertItemsCore`
The monolithic `UpdateOrInsertItemsCore` (400+ lines) will be split into:
1. `UpdateOrInsertItemsCore` (Orchestration): Begins transactions, groups input, calls child persistence methods, and commits.
2. `SaveBaseItemEntities`: Maps DTOs to entities, determines additions/modifications, cleans old metadata/images/providers, and tracks changes.
3. `SaveItemValues`: Resolves and inserts `ItemValue` maps for Genres, Studios, Tags, Artists, and AlbumArtists.
4. `SaveAncestorIds`: Resolves and updates Ancestor IDs for items that support them.
5. `SaveLinkedChildren`: Resolves linked children for folders and videos using optimized batch queries.

### 2.2. Query Optimization (Batching to Eliminate N+1)
We will fetch required lookup data in single database calls:
- **Path Resolution**: Distinct path strings from both folders and videos will be collected and resolved to Guid IDs in one query:
  ```csharp
  var pathToIdMap = allPathsToResolve.Count > 0
      ? context.BaseItems
          .Where(e => e.Path != null && allPathsToResolve.Contains(e.Path))
          .Select(e => new { e.Path, e.Id })
          .GroupBy(e => e.Path!)
          .ToDictionary(g => g.Key, g => g.First().Id)
      : new Dictionary<string, Guid>();
  ```
- **Child Existence**: All potential child IDs will be collected and validated in one database round-trip via `WhereOneOrMany`:
  ```csharp
  var allExistingChildIds = allChildIdsToCheck.Count > 0
      ? context.BaseItems
          .WhereOneOrMany(allChildIdsToCheck.ToList(), e => e.Id)
          .Select(e => e.Id)
          .ToHashSet()
      : new HashSet<Guid>();
  ```
- **Orphan Alternate Versions**: Collect all orphaned local alternate version IDs across all videos, query them in one call, and execute removal in batch:
  ```csharp
  var orphanedItems = allOrphanedLocalVersionIds.Count > 0
      ? context.BaseItems
          .WhereOneOrMany(allOrphanedLocalVersionIds.ToList(), e => e.Id)
          .Where(e => e.OwnerId.HasValue)
          .ToList()
      : new List<BaseItemEntity>();
  ```

### 2.3. Eliminating Sync-Over-Async
Synchronous database methods will be used instead of blocking async tasks:
- `context.SaveChangesAsync(default).GetAwaiter().GetResult()` will be replaced with `context.SaveChanges()`.

---

## 3. Verification Plan

### 3.1. Automated Tests
- Run `dotnet test tests/Jellyfin.Server.Implementations.Tests/` to verify functional correctness.
- Ensure the newly refactored methods pass existing tests for item save/update/delete.
- Add additional test coverage if necessary.
