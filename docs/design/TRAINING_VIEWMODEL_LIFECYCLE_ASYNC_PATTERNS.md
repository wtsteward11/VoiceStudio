# TrainingViewModel Lifecycle Async Patterns

**Date:** 2026-03-12  
**Purpose:** Document fire-and-forget lifecycle patterns in TrainingViewModel. Constructor fire-and-forget was removed; lifecycle handlers retain fire-and-forget with explicit cancellation ownership.  
**Related:** [SEAM_MATURITY_AUDIT.md](SEAM_MATURITY_AUDIT.md), [closure-protocol.mdc](../../.cursor/rules/workflows/closure-protocol.mdc)

---

## Summary

TrainingViewModel uses `_disposalCts` (GAP-I15) for cancellation of fire-and-forget operations. All lifecycle async work is launched with this token so it can be cancelled when the panel is disposed. Status: **documented, retained by design** until explicit refactor.

---

## Fire-and-Forget Paths

| Trigger | Method | Cancellation | Staleness Guard | Status |
|---------|--------|--------------|-----------------|--------|
| Selected job changed | LoadLogsAsync | _selectedJobLoadCts (linked to _disposalCts) | Yes: verify SelectedTrainingJob.Id before apply | Gated |
| Selected job changed | LoadQualityHistoryAsync | _selectedJobLoadCts (linked to _disposalCts) | Yes: verify SelectedTrainingJob.Id before apply | Gated |
| StartPolling (AutoRefresh) | ConnectWebSocketAsync | — | — | Retained |
| StartPolling (AutoRefresh) | LoadDatasetsAsync | _disposalCts.Token | — | Retained |
| StartPolling (AutoRefresh) | LoadTrainingJobsAsync | _disposalCts.Token | — | Retained |
| Polling startup | PollTrainingStatusAsync | _pollingCts.Token | — | Retained |
| StopPolling | DisconnectWebSocketAsync | — | — | Retained |

---

## Cancellation Ownership

- `_disposalCts`: Cancelled when the ViewModel is disposed (Dispose override). All fire-and-forget work that uses it will stop.
- `_selectedJobLoadCts`: Cancelled when selection changes or on disposal. Prevents stale LoadLogs/LoadQualityHistory from overwriting UI. Linked to _disposalCts.
- `_pollingCts`: Cancelled when auto-refresh is disabled or panel unloads. PollTrainingStatusAsync respects it.

---

## Design Rationale

- **Constructor:** No fire-and-forget. InitializeAsync moved to Loaded event (ADR-047).
- **Lifecycle:** Fire-and-forget retained for UX responsiveness (logs, quality history, WebSocket, polling). Each path is triggered by user action or panel lifecycle; cancellation is explicit and tested via _disposalCts.

---

## Future Work (Out of Scope)

- Full removal or gating of lifecycle fire-and-forget requires design and risk assessment.
- Consider explicit Task tracking for debugging if needed.
