# LibraryViewModel Lifecycle Async Patterns

**Date:** 2026-03-13  
**Purpose:** Document fire-and-forget lifecycle patterns in LibraryViewModel. Seam-migrated (ILibraryClient).  
**Related:** [SEAM_MATURITY_AUDIT.md](SEAM_MATURITY_AUDIT.md), [BATCH_PROCESSING_LIFECYCLE_PATTERNS.md](BATCH_PROCESSING_LIFECYCLE_PATTERNS.md)

---

## Summary

LibraryViewModel uses `_disposalCts` for cancellation of fire-and-forget operations. Selection-triggered loads (folder, asset type) use `_loadAssetsCts` cancelled when selection changes. Event-driven refreshes (AssetAdded, ProfileCreated, SynthesisCompleted) use _disposalCts.Token.

---

## Fire-and-Forget Paths

| Trigger | Method | Cancellation | Staleness Guard | Status |
|---------|--------|--------------|-----------------|--------|
| OnAssetAdded | LoadAssetsAsync | _disposalCts.Token | — | Gated |
| OnProfileCreatedRefresh | LoadAssetsAsync | _disposalCts.Token | — | Gated |
| OnSynthesisCompleted | LoadAssetsAsync | _disposalCts.Token | — | Gated |
| OnSelectedFolderChanged | LoadAssetsAsync | _loadAssetsCts (linked to _disposalCts) | Cancel prior load on folder change | Gated |
| OnSelectedAssetTypeChanged | LoadAssetsAsync | _loadAssetsCts (linked to _disposalCts) | Cancel prior load on type change | Gated |
| OnSearchQueryChanged | SearchAssetsAsync | _searchDebounceCts.Token | Debounce 300ms; cancel prior on query change | Gated |
| OnActivatedAsync | LoadAssetTypesAsync, LoadFoldersAsync, LoadAssetsAsync | Passed token from panel | — | Gated |

---

## Cancellation Ownership

- `_disposalCts`: Cancelled when the ViewModel is disposed.
- `_loadAssetsCts`: Cancelled when folder or asset type changes, or on disposal. Linked to _disposalCts.
- `_searchDebounceCts`: Cancelled when search query changes (debounce). Disposed in Dispose.

---

## Changelog

- 2026-03-13: Initial document. Added _disposalCts, _loadAssetsCts. Replaced CancellationToken.None with disposal-linked tokens. Staleness guard for selection-triggered loads.
