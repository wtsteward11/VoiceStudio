# BatchProcessingViewModel Lifecycle Async Patterns

**Date:** 2026-03-13  
**Purpose:** Document fire-and-forget lifecycle patterns in BatchProcessingViewModel. Seam-migrated (IBatchProcessingClient); lifecycle hardening adds staleness guards for selection-triggered loads.  
**Related:** [RETAINED_ASYNC_RULE.md](RETAINED_ASYNC_RULE.md), [SEAM_MATURITY_AUDIT.md](SEAM_MATURITY_AUDIT.md), [TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md](TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md)

---

## Summary

BatchProcessingViewModel uses `_disposalCts` for cancellation of fire-and-forget operations. Selection-triggered loads (filter, project, job) use dedicated CancellationTokenSources that are cancelled when the selection changes, preventing stale data overwrite. Polling and WebSocket lifecycle are inherently fire-and-forget; retained by design.

---

## Fire-and-Forget Paths

| Trigger | Method | Cancellation | Staleness Guard | Status |
|---------|--------|--------------|-----------------|--------|
| OnFilterStatusChanged | LoadJobsAsync | _loadJobsCts (linked to _disposalCts) | Cancel prior load on filter change | Gated |
| OnSelectedProjectIdChanged | LoadJobsAsync | _loadJobsCts (linked to _disposalCts) | Cancel prior load on project change | Gated |
| OnSelectedJobChanged | LoadQualityReportAsync | _selectedJobLoadCts (linked to _disposalCts) | Cancel prior load; verify SelectedJob.Id before apply | Gated |
| OnJobCompleted (WebSocket) | LoadQualityStatisticsAsync | _disposalCts.Token | Disposal cancels; no stale apply | Gated |
| StartPolling (AutoRefresh) | ConnectWebSocketAsync | — | — | Retained (documented) |
| StartPolling (AutoRefresh) | LoadJobsAsync, LoadQueueStatusAsync | _disposalCts.Token | — | Retained (justified) |
| StartPolling (fallback) | PollJobsAsync | _pollingCts.Token | — | Retained (justified) |
| StopPolling | DisconnectWebSocketAsync | — | — | Retained (documented) |

### Justification for Retained Paths

| Path | Justification |
|------|---------------|
| ConnectWebSocketAsync / DisconnectWebSocketAsync | WebSocket lifecycle; connect/disconnect are one-shot. Fire-and-forget; Dispose may run before disconnect completes. Risk: low (one-shot, no shared state). |
| LoadJobsAsync / LoadQueueStatusAsync (StartPolling) | One-shot startup when AutoRefresh enabled. Uses _disposalCts.Token; disposal cancels. |
| PollJobsAsync | Polling loop with _pollingCts.Token; StopPolling cancels. Explicit cancellation ownership. |

---

## Cancellation Ownership

- `_disposalCts`: Cancelled when the ViewModel is disposed. All fire-and-forget work that uses it will stop.
- `_loadJobsCts`: Cancelled when filter or project changes, or on disposal. Prevents stale LoadJobsAsync from overwriting UI. Linked to _disposalCts.
- `_selectedJobLoadCts`: Cancelled when job selection changes or on disposal. Prevents stale LoadQualityReportAsync. Linked to _disposalCts.
- `_pollingCts`: Cancelled when auto-refresh is disabled or panel unloads. PollJobsAsync respects it.

---

## Changelog

- 2026-03-13: OnJobCompleted: LoadQualityStatisticsAsync now uses _disposalCts.Token (was CancellationToken.None). Added OnJobCompleted to table; StartPolling/StopPolling marked Retained (documented).
- 2026-03-13: Initial document. Added _loadJobsCts for OnFilterStatusChanged/OnSelectedProjectIdChanged; OnSelectedJobChanged already had _selectedJobLoadCts; no silent catches.
