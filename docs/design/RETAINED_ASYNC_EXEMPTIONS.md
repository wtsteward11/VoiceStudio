# Retained-Async Baseline Exemptions

**Date:** 2026-03-15  
**Purpose:** Document the retained-async baseline strategy and exemption rationale. The baseline (`.ci/retained_async_baseline.txt`) uses delta mode: fail only on NEW violations. Entries in the baseline are either (a) documented exemptions with rationale, or (b) pending remediation.  
**Related:** [RETAINED_ASYNC_RULE.md](RETAINED_ASYNC_RULE.md), [check_retained_async.py](../../scripts/ci/check_retained_async.py)

---

## Baseline Strategy

- **Delta mode:** When `--baseline-file` is used, the gate fails only if violations appear that are NOT in the baseline.
- **Goal:** Shrink the baseline over time. Remove entries when code is fixed. Document justified exemptions.
- **Anti-pattern:** Treating the baseline as a permanent exemption list. Every entry should have a path to removal or explicit rationale.

---

## Violation Categories

| Pattern | Description | Typical Fix |
|---------|-------------|-------------|
| `property_faf_no_cts` | OnSelected*Changed with `_ = .*Async(CancellationToken.None)` | Use selection-specific CTS + staleness guard |
| `task_run_debounce` | `Task.Run` for debounce in ViewModels | Replace with proper debounce (DispatcherQueueTimer, etc.) |
| `continue_with_faf` | `.ContinueWith` without CTS/staleness guard | Replace with async/await + CTS |

---

## Documented Exemptions

### TrainingViewModel

**Not in baseline.** TrainingViewModel lifecycle FAF is formally exempt per [ADR-051](../architecture/decisions/ADR-051-training-viewmodel-lifecycle-faf-retention.md). The six paths (WebSocket, polling, data load) use explicit CTS ownership. See [TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md](TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md).

### AdvancedRealTimeVisualizationViewModel

**Documented exemption** in [CONSTRUCTOR_INVARIANT_COVERAGE_AUDIT.md](CONSTRUCTOR_INVARIANT_COVERAGE_AUDIT.md). Timer-based FAF for real-time viz; `Dispose()` stops timer. Per RETAINED_ASYNC_RULE §Allowed Cases (polling loop).

---

## Baseline Audit (2026-03-15)

The baseline contains 62 entries across ViewModels and Panels. Categories:

- **property_faf_no_cts:** Selection-triggered loads without CTS. Target: migrate to selection-specific CTS + staleness guard per RETAINED_ASYNC_RULE.
- **task_run_debounce:** Task.Run for debounce. Target: replace with DispatcherQueueTimer or equivalent.
- **continue_with_faf:** ContinueWith usage. Target: replace with async/await + CTS.

**Audit action:** As ViewModels are migrated or refactored, remove healed entries from the baseline. Do not add new entries without documenting rationale here or in an ADR.

---

## Changelog

- 2026-03-15: Initial document. Baseline strategy and exemption categories. TrainingViewModel and AdvancedRealTimeVisualizationViewModel documented.
