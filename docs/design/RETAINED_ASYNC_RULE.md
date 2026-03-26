# Retained Async Rule (ViewModel Fire-and-Forget)

> **Purpose:** Single canonical rule for when retained fire-and-forget is acceptable in ViewModels. Aligns SceneBuilder, BatchProcessing, and Training under one standard.  
> **Related:** [SCENEBUILDER_LIFECYCLE_PATTERNS.md](SCENEBUILDER_LIFECYCLE_PATTERNS.md), [BATCH_PROCESSING_LIFECYCLE_PATTERNS.md](BATCH_PROCESSING_LIFECYCLE_PATTERNS.md), [TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md](TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md), ADR-047 (XamlRoot deferral)

---

## Core Principle

**Constructor fire-and-forget is banned** (ADR-047). Lifecycle fire-and-forget is allowed **only** when all of the following hold:

1. **Cancellation owned** — A dedicated `CancellationTokenSource` (or linked token) is used; disposal cancels it.
2. **Disposal safe** — `Dispose(bool)` stops timers, unsubscribes events, and cancels all CTSs used by fire-and-forget paths.
3. **Staleness guard** — Selection-triggered loads verify state before applying results (e.g., snapshot before request, compare before apply).
4. **UI thread re-entry controlled** — Debounce uses `IDispatcherTimer` or equivalent; no `Task.Run` for UI-owned flows.

---

## Allowed Cases

| Case | Example | Required Protections |
|------|---------|----------------------|
| Selection-triggered load | Project changed → LoadScenesAsync | CTS owned; staleness guard; disposal cancels |
| Debounced search | Search query → debounce → LoadScenesAsync | IDispatcherTimer; CTS on tick; disposal stops timer |
| Polling loop | AutoRefresh → PollJobsAsync | _pollingCts; StopPolling cancels |
| WebSocket connect/disconnect | StartPolling → ConnectWebSocketAsync | One-shot; disposal cancels _disposalCts; no shared state |
| One-shot startup load | StartPolling → LoadJobsAsync | _disposalCts.Token; disposal cancels |

---

## Required Protections

| Protection | Requirement |
|------------|-------------|
| **CTS ownership** | Each fire-and-forget path uses a CancellationToken from a CTS that is cancelled on disposal or selection change. |
| **Disposal cleanup** | `Dispose(bool)` stops timers, unsubscribes events, and disposes CTSs. No orphaned work. |
| **Staleness guard** | Selection-triggered loads capture a snapshot before the request; discard result if selection changed before apply. |
| **UI thread** | Debounce uses `IDispatcherTimer` (or equivalent). No `Task.Run` for debounce. |

---

## Required Test Coverage

| Test Type | Coverage |
|-----------|----------|
| Lifecycle cancellation | Dispose cancels in-flight work; no stale apply after disposal. |
| Staleness guard | Rapid selection change does not overwrite UI with stale data. |
| Timer cleanup | Disposal stops debounce timer; no Tick after dispose. |

---

## Panels Aligned to This Rule

| Panel | Status | Notes |
|-------|--------|-------|
| SceneBuilderViewModel | Aligned | OnActivatedAsync awaits; OnSelectedProjectIdChanged / OnSearchDebounceTick retained with CTS + staleness guard. |
| BatchProcessingViewModel | Aligned | Selection-triggered loads gated; polling/WebSocket retained with justification. |
| TrainingViewModel | Aligned | Selection-triggered loads gated; polling/WebSocket retained with justification. |

---

## Prohibited Patterns

| Pattern | Reason |
|---------|--------|
| `_ = LoadAsync();` in constructor | ADR-047; XamlRoot not available. |
| `Task.Run` for debounce | Use IDispatcherTimer. |
| Fire-and-forget without CTS | No cancellation on disposal. |
| Selection-triggered load without staleness guard | Stale data overwrites UI. |
| Empty catch / swallow | no-suppression rule. |

---

## Enforcement (RA-01 Verified 2026-03-14; strengthened 2026-03-14)

**Integrated:** `python scripts/run_verification.py` runs `check_retained_async.py --baseline-file .ci/retained_async_baseline.txt`. The check is **always run** when the script exists; it is **no longer skippable**.

**Behavior:**
- **Baseline exists:** Check runs; fails if any violation is not in baseline. Known violations in baseline are allowed.
- **Baseline missing:** Check **FAILS** (exit 1). Do not skip. Create baseline with `python scripts/ci/check_retained_async.py --baseline` and commit `.ci/retained_async_baseline.txt`.
- **Reduce baseline over time:** Fix violations and remove from `.ci/retained_async_baseline.txt` to tighten the gate.

**Manual check:** `python scripts/ci/check_retained_async.py` — without --baseline-file, fails on any violation.

---

## Changelog

- 2026-03-14: Truth Reset Task 4. Baseline missing now FAILS (no skip). Retained-async is gated, not advisory.
- 2026-03-13: Added check_retained_async.py advisory script. Enforcement section.
- 2026-03-13: Initial document. Unified rule for SceneBuilder, BatchProcessing, Training. Aligns with ADR-047 and no-suppression.mdc.
