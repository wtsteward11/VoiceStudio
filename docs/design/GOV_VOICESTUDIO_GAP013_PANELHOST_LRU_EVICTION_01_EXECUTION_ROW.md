# GOV-VOICESTUDIO-GAP013-PANELHOST-LRU-EVICTION-01 — Execution row

**Lane ID:** `GOV_VOICESTUDIO_GAP013_PANELHOST_LRU_EVICTION_01`  
**Status:** **Closed** (2026-04-07)  
**Tracker:** [GAP-013](PROFESSIONAL_GAP_TRACKER.md)  
**Lane type:** **runtime-affecting** (see [EXECUTION_ROW_DISCIPLINE.md](../governance/EXECUTION_ROW_DISCIPLINE.md))

## Problem statement

`PanelHost` mixes visual hosting (`HostedPanel`), LRU cache eviction, and `IPanelLifecycle` / `IPanelStatePersistable` without a single coherent teardown rule. Eviction, explicit unload, and some dispose paths call `IDisposable.Dispose()` on panel view models **without** `OnDeactivatedAsync`, so subscriptions/timers can survive eviction (“zombie” behavior). `HandleContentChangeAsync` runs fire-and-forget with no serialization; `SaveCurrentPanelState` reads `HostedPanel` after the DP has already moved to the **incoming** panel, so outgoing persistence can target the wrong panel. Restore runs fire-and-forget while activation may run before restore completes.

## Frozen architecture decisions

1. **Teardown authority:** For any path that removes a panel instance from the live shell or cache (`HostedPanel` change away from instance, LRU eviction, `UnloadPanelAsync`, `CleanupCacheAsync`, abandoned create-on-unloaded), the host **must** call **`IPanelLifecycle.OnDeactivatedAsync` (or `PanelLifecycleHelper` fallback) before `IDisposable.Dispose`** when a view model is present.
2. **Transition serialization:** At most one `HostedPanel` lifecycle transition runs at a time per `PanelHost` (`SemaphoreSlim(1,1)`), so deactivate/save/restore/activate do not interleave across overlapping DP changes.
3. **Same-instance no-op:** If `ReferenceEquals(oldContent, newContent)`, skip lifecycle transition (no duplicate activate/deactivate).
4. **Outgoing persistence:** Save outgoing panel state using the **explicit outgoing `UIElement`** passed from the DP callback, not ambient `HostedPanel`.
5. **Restore before activate:** Await `IPanelStatePersistable.RestoreStateAsync` (when applicable) **before** `OnActivatedAsync` for the incoming panel.
6. **Reachability subscription:** `BackendReachabilityChanged` is unsubscribed on `PanelHost.Unloaded` to avoid holding host references after teardown.
7. **GAP-007 seam:** `HostedPanel` / `HostedPanelProperty` remain the only hosted-body surface; no `ContentProperty` shadow.

## §0.1 Lifecycle map (pre-change truth)

| Trigger | Path | Deactivate old? | Save outgoing? | Restore incoming? | Activate new? | Dispose / cache |
|--------|------|-----------------|----------------|------------------|---------------|-----------------|
| `HostedPanel` DP change | `OnContentChanged` → `HandleContentChangeAsync` | Yes (`DeactivateViewModelAsync`) | Was broken (used `HostedPanel` = new) | Fire-and-forget | Yes | N/A (visual swap) |
| `LoadPanelAsync` cache hit | `HostedPanel = cached` | Via DP callback | Via callback | Via callback | Via callback | Cache retained |
| `LoadPanelAsync` new + LRU | `EvictIfOverCapacity` | **No** (dispose only) | — | — | — | Evicted VM disposed without deactivate |
| `UnloadPanelAsync` | TryRemove + dispose | **No** (dispose only) | — | — | — | VM disposed without deactivate |
| `CleanupCacheAsync` | Unloaded | **No** (dispose only) | — | — | — | Batch dispose without deactivate |
| Dock complete | `UnloadPanelAsync` + reload | Same as unload | — | — | — | Uses unload path |

## Acceptance contract (all required for Close)

- [x] When `HostedPanel` changes from panel A to B, A is deactivated exactly once and B is activated exactly once (no duplicate lifecycle for `ReferenceEquals` reassignment).
- [x] LRU-evicted cached panels: `OnDeactivatedAsync` (or helper fallback) runs **before** `Dispose` on the view model when present.
- [x] `UnloadPanelAsync` and `CleanupCacheAsync`: same deactivate-before-dispose rule.
- [x] Outgoing `IPanelStatePersistable` / `SavePanelState` uses **outgoing** content from the transition, not post-switch `HostedPanel`.
- [x] `RestoreStateAsync` completes before `OnActivatedAsync` when persistable state exists.
- [x] Overlapping `HostedPanel` changes do not interleave deactivate/save/restore/activate (serialized transition).
- [x] `BackendReachabilityChanged` unsubscribed on `Unloaded`.
- [x] No regression to GAP-007 (`HostedPanel` / `HostedPanelProperty`; `PanelHostSeamTests` still pass).
- [x] Closure matrix + proof — [VOICESTUDIO_GAP013_PANELHOST_LRU_EVICTION_LANE_CLOSURE_2026-04-07.md](../reports/verification/VOICESTUDIO_GAP013_PANELHOST_LRU_EVICTION_LANE_CLOSURE_2026-04-07.md); `completion_guard` PASS.

## Allowlist

`src/VoiceStudio.App/Controls/PanelHost.xaml.cs`, `src/VoiceStudio.App.Tests/Controls/PanelHostSeamTests.cs`, `src/VoiceStudio.App.Tests/Controls/PanelHostLifecycleTests.cs` (new), execution row, closure report, `PROFESSIONAL_GAP_TRACKER.md`, `CANONICAL_REGISTRY.md`, `.cursor/STATE.md`.

## Hard OUT

MainWindow decomposition (GAP-008); workspace save/restore redesign; panel persistence schema redesign; startup overlay work; title bar / Mica; command routing cleanup; `app/ui/...` shadow tree unless a separate row is opened.

## Failure-path parity (closure)

- **Happy path:** Panel swap, cache hit, new load with eviction — lifecycle order verified by tests + logs.
- **Degraded path:** Deactivate or restore throws — transition logs; host does not throw to DP layer; best-effort dispose still attempted where safe; reachability unsubscribed on unload even if prior transition failed.

## Pre-existing vs this lane

- **Pre-existing:** LRU cache, `LoadPanelAsync`, `HostedPanel` DP, reachability retry, dock `UnloadPanelAsync` orchestration.
- **This lane:** Deactivate-before-dispose on eviction/unload/cache clear; serialized transitions; correct outgoing save + awaited restore; reachability unsubscribe on unload; behavioral tests.

## Rollback

Revert GAP-013 scoped commit(s). Restores prior eviction/dispose and transition behavior.

## Changelog

- **2026-04-07:** Row frozen (GAP-013 lifecycle hygiene).
- **2026-04-07:** Lane **Closed** — implementation + `PanelHostLifecycleTests` + closure matrix; see closure report §2.
