# Wave 2 Lifecycle Audit

**Date:** 2026-03-13  
**Purpose:** Classify async lifecycle debt in the three Wave 2 migrations (BatchProcessingViewModel, VoiceCloningWizardViewModel, LibraryViewModel). Each path is classified as Acceptable Retained (with justification) or Required Cleanup.  
**Related:** [TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md](TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md), [SEAM_MATURITY_AUDIT.md](SEAM_MATURITY_AUDIT.md)

---

## Repo Rule Reference

Per [TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md](TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md):

- **Constructor fire-and-forget:** Banned (ADR-047).
- **Lifecycle fire-and-forget:** Allowed only when (a) cancellation owned, (b) staleness guard if selection-triggered, (c) documented.
- **Selection-triggered async:** Must use selection-specific cancellation + staleness guard.

---

## 1. BatchProcessingViewModel

**File:** [src/VoiceStudio.App/Views/Panels/BatchProcessingViewModel.cs](../../src/VoiceStudio.App/Views/Panels/BatchProcessingViewModel.cs)

| Trigger | Method | Line | Classification | Rationale |
|---------|--------|------|----------------|-----------|
| OnFilterStatusChanged | LoadJobsAsync | 288 | Required Cleanup | No cancellation; filter change can race. Use _disposalCts.Token. |
| OnSelectedProjectIdChanged | LoadJobsAsync | 293 | Required Cleanup | No cancellation; project change can race. Use _disposalCts.Token. |
| StartPolling | ConnectWebSocketAsync | 478 | Acceptable Retained | WebSocket lifecycle; one-shot. Document with cancellation ownership. |
| StartPolling | LoadJobsAsync, LoadQueueStatusAsync | 484-485 | Required Cleanup | CancellationToken.None; no disposal link. Use _disposalCts.Token. |
| StartPolling | PollJobsAsync | 496 | Acceptable Retained | Uses _pollingCts.Token; StopPolling cancels. Good. |
| StopPolling | DisconnectWebSocketAsync | 509 | Acceptable Retained | WebSocket teardown; one-shot. |
| OnSelectedJobChanged | LoadQualityReportAsync | 989 | **Required Cleanup** | **Stale-write risk.** No selection-specific cancellation. Add _selectedJobLoadCts + staleness guard. |

**Silent catches (constructor):** Lines 139-140, 148-149 — `catch { }` when resolving ToastNotificationService and UndoRedoService. **Required Cleanup** — per no-suppression.mdc, must log or rethrow.

**Missing infrastructure:** No _disposalCts; no _selectedJobLoadCts; BatchProcessingViewModel does not implement IDisposable/IPanelLifecycle — polling/WebSocket may leak on panel unload. **Required Cleanup.**

---

## 2. VoiceCloningWizardViewModel

**File:** [src/VoiceStudio.App/ViewModels/VoiceCloningWizardViewModel.cs](../../src/VoiceStudio.App/ViewModels/VoiceCloningWizardViewModel.cs)

| Trigger | Method | Line | Classification | Rationale |
|---------|--------|------|----------------|-----------|
| Constructor | LoadEnginesAsync | 157 | **Required Cleanup** | **ADR-047 violation** — constructor fire-and-forget banned. Move to Loaded/OnActivatedAsync. |

**Action:** DONE. LoadEnginesAsync moved to InitializeAsync, called from View Loaded (2026-03-13).

---

## 3. LibraryViewModel

**File:** [src/VoiceStudio.App/ViewModels/LibraryViewModel.cs](../../src/VoiceStudio.App/ViewModels/LibraryViewModel.cs)

| Trigger | Method | Line | Classification | Rationale |
|---------|--------|------|----------------|-----------|
| Constructor | LoadAssetTypesAsync, LoadFoldersAsync, LoadAssetsAsync | 185-187 | **Required Cleanup** | Constructor fire-and-forget. Move to OnActivatedAsync (IPanelLifecycle). |
| OnAssetAdded | LoadAssetsAsync | 221 | Acceptable Retained | Event-driven refresh; no selection staleness. |
| OnProfileCreatedRefresh | LoadAssetsAsync | 232 | Acceptable Retained | Event-driven refresh. |
| OnSynthesisCompleted | LoadAssetsAsync | 243 | Acceptable Retained | Event-driven refresh. |
| OnSelectedFolderChanged | LoadAssetsAsync | 526 | Acceptable Retained | Selection-triggered; low stale-write risk for folder list. Document. Consider _selectedFolderLoadCts if issues arise. |
| OnSelectedProfileIdChanged | LoadAssetsAsync | (similar) | Acceptable Retained | Same as folder. |
| OnSearchQueryChanged | SearchAssetsAsync | 534-550 | Acceptable Retained | Debounce exists (_searchDebounceCts). Complex but acceptable. |

**Action:** DONE. Constructor loads moved to OnActivatedAsync (2026-03-13).

---

## Summary

| ViewModel | Required Cleanup | Acceptable Retained |
|-----------|------------------|---------------------|
| BatchProcessingViewModel | 5 paths + silent catches + infrastructure | 3 paths |
| VoiceCloningWizardViewModel | 1 path (constructor) | 0 |
| LibraryViewModel | 1 path (constructor loads) | 6 paths |

**Priority order for cleanup:**

1. BatchProcessingViewModel — most paths; silent catches; stale-write risk.
2. VoiceCloningWizardViewModel — single ADR-047 violation.
3. LibraryViewModel — move constructor loads to OnActivatedAsync.

---

---

## 4. BatchProcessing Retained Paths (Post-Cleanup)

After Phase 2 cleanup, the following fire-and-forget paths are **Retained (justified)**:

| Trigger | Method | Cancellation | Staleness Guard | Justification |
|---------|--------|--------------|-----------------|---------------|
| OnFilterStatusChanged | LoadJobsAsync | _disposalCts.Token | — | List refresh; disposal cancels. |
| OnSelectedProjectIdChanged | LoadJobsAsync | _disposalCts.Token | — | List refresh; disposal cancels. |
| StartPolling | ConnectWebSocketAsync | — | — | WebSocket lifecycle; one-shot. |
| StartPolling | LoadJobsAsync, LoadQueueStatusAsync | _disposalCts.Token | — | One-shot startup; disposal cancels. |
| StartPolling | PollJobsAsync | _pollingCts.Token | — | Polling loop; StopPolling cancels. |
| StopPolling | DisconnectWebSocketAsync | — | — | WebSocket teardown; one-shot. |
| OnSelectedJobChanged | LoadQualityReportAsync | _selectedJobLoadCts (linked to _disposalCts) | Yes: verify SelectedJob.Id before apply | Gated. |

**Cancellation ownership:** _disposalCts cancelled in Dispose; _selectedJobLoadCts cancelled when selection changes or on disposal; _pollingCts cancelled in StopPolling or Dispose.

---

## Changelog

- 2026-03-13: Initial audit per Wave 2 Lifecycle Follow-Through Plan.
- 2026-03-13: BatchProcessing lifecycle cleanup complete; retained paths documented.
