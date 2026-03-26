# SceneBuilderViewModel Lifecycle Async Patterns

**Date:** 2026-03-13  
**Purpose:** Document lifecycle patterns in SceneBuilderViewModel. Seam-migrated (ISceneBuilderClient); lifecycle ownership complete (2026-03-13): OnActivatedAsync awaits LoadScenesAsync; staleness guard in LoadScenesAsync; IDispatcherTimer debounce (no Task.Run); disposal stops timer and cleans CTS.  
**Related:** [RETAINED_ASYNC_RULE.md](RETAINED_ASYNC_RULE.md), [SEAM_MATURITY_AUDIT.md](SEAM_MATURITY_AUDIT.md), [BATCH_PROCESSING_LIFECYCLE_PATTERNS.md](BATCH_PROCESSING_LIFECYCLE_PATTERNS.md)

---

## Summary

SceneBuilderViewModel implements `ILifecyclePanelView`. Initial load runs from `OnActivatedAsync` and **awaits** LoadScenesAsync (PanelHost activation completes when load completes). Selection-triggered loads (project) use _loadScenesCts; project change cancels prior load and stops search debounce timer. Search debounce uses `IDispatcherTimer` (UI-thread, no Task.Run). LoadScenesAsync has **explicit staleness guard** (projectSnapshot/searchSnapshot; discard result if selection/query changed after request started). Disposal stops timer, unsubscribes Tick, cancels _loadScenesCts.

---

## Lifecycle Paths

| Trigger | Method | Cancellation | Staleness Guard | Status |
|---------|--------|--------------|-----------------|--------|
| OnActivatedAsync (PanelHost) | LoadScenesAsync | Linked to _disposalCts + caller token | Snapshot before apply | **Owned** (awaited) |
| OnSelectedProjectIdChanged | LoadScenesAsync | _loadScenesCts (linked to _disposalCts) | Snapshot before apply | Gated (fire-and-forget; staleness guard) |
| OnSearchQueryChanged | LoadScenesAsync (debounced 300ms) | _searchDebounceTimer; _loadScenesCts on tick | Snapshot before apply | Gated (IDispatcherTimer) |
| RefreshCommand | LoadScenesAsync | Command token | User-initiated | Gated |
| LoadScenesCommand | LoadScenesAsync | Command token | User-initiated | Gated |

### Retained Fire-and-Forget (Justified)

| Path | Justification |
|------|---------------|
| OnSelectedProjectIdChanged | Partial void property callback cannot await; staleness guard prevents stale apply |

---

## Cancellation Ownership

- `_disposalCts`: Cancelled when the ViewModel is disposed. All fire-and-forget work that uses it will stop.
- `_loadScenesCts`: Cancelled when project changes or on disposal. Prevents stale LoadScenesAsync from overwriting UI. Linked to _disposalCts.
- `_searchDebounceCts`: Cancelled when search changes or on disposal. Prevents stale debounced LoadScenesAsync. Linked to _disposalCts.

---

## Changelog

- 2026-03-13: Lifecycle ownership complete. OnActivatedAsync now awaits LoadScenesAsync; staleness guard (projectSnapshot/searchSnapshot) in LoadScenesAsync; Task.Run debounce replaced with IDispatcherTimer; OnSelectedProjectIdChanged stops debounce timer; Dispose stops timer and unsubscribes Tick.
- 2026-03-13: Initial document. Constructor fire-and-forget removed; OnActivatedAsync for initial load; _loadScenesCts for project change; _searchDebounceCts for search debounce; Dispose cleanup.
