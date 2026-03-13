# EffectsMixer Lifecycle Verification

> **Purpose:** Document confirmed runtime lifecycle risk and panel-close leak analysis for EffectsMixerViewModel. No seam migration until lifecycle hardening is complete.  
> **Related:** [EFFECTSMIXER_DOMAIN_SPLIT_ANALYSIS.md](EFFECTSMIXER_DOMAIN_SPLIT_ANALYSIS.md), [RETAINED_ASYNC_RULE.md](RETAINED_ASYNC_RULE.md)

---

## 1. Runtime Lifecycle Risk (Pre-Hardening State — Resolved)

**Lifecycle hardening complete (2026-03-13).** The following described the pre-hardening state; all items have been addressed.

| Risk | Pre-hardening evidence | Resolution |
|------|------------------------|------------|
| **Polling ownership** | `_pollingCts` exists; `StartPolling()` creates it; `StopPolling()` cancels/disposes it. Real-time toggle controls start/stop. | Unchanged; ownership correct. |
| **ContinueWith** | `OnSelectedProjectIdChanged`, `OnSelectedAudioIdChanged` used `ContinueWith` — no CTS, no staleness guard. | Replaced with proper async + `_selectionLoadCts` + staleness guard. |
| **No IPanelLifecycle** | EffectsMixerViewModel implemented `IPanelView` only — **not** `IPanelLifecycle`. | Implemented; `OnDeactivatedAsync` calls `StopPolling()`. |
| **No IDisposable** | ViewModel did not implement `IDisposable`. | Implemented; `Dispose` calls `StopPolling()` and cancels `_disposalCts`, `_selectionLoadCts`. |

---

## 2. Panel-Close Leak (Pre-Hardening Trace — Resolved)

**Lifecycle hardening complete (2026-03-13).** The following trace described the pre-hardening state. EffectsMixerViewModel now implements `IPanelLifecycle` and `IDisposable`; `OnDeactivatedAsync` calls `StopPolling()` and `CancelSelectionLoad()`; `Dispose` cancels all CTSs.

**Pre-hardening trace (for archival):**

1. **Panel load:** EffectsMixerView is loaded via PanelHost (CorePanelRegistrationService registers `EffectsMixerView` + `EffectsMixerViewModel`). PanelHost creates View and ViewModel, sets Content.

2. **Deactivation:** When user navigates away, PanelHost calls `DeactivateViewModelAsync(oldContent, ct)`. `DeactivateViewModelAsync` gets the ViewModel from `content.DataContext` and checks `if (viewModel is IPanelLifecycle lifecycle)`. **Before hardening:** EffectsMixerViewModel did not implement IPanelLifecycle → nothing invoked on deactivation. **After hardening:** EffectsMixerViewModel implements IPanelLifecycle → `OnDeactivatedAsync` is called → `StopPolling()` and `CancelSelectionLoad()` run.

3. **StopPolling:** `StopPolling()` is now invoked on panel deactivation (via `OnDeactivatedAsync`) and on `Dispose`.

4. **Disposal:** PanelHost's `CleanupCacheAsync` and `EvictIfOverCapacity` dispose `IDisposable` ViewModels. EffectsMixerViewModel now implements `IDisposable`; `Dispose` cancels all CTSs.

5. **Result:** Polling leak is resolved. Panel close/deactivation now reaches the stop path.

---

## 3. End-to-End Verification Status

**What is established:** Runtime lifecycle risk exists. Panel-close leak is plausible and likely.

**What is not yet proven:** An automated or manual test that definitively proves the leak (navigate to EffectsMixer, enable real-time, navigate away, verify PollMetersAsync stops or that no background work continues).

**Recommendation:** Add lifecycle hardening (IPanelLifecycle, IDisposable, StopPolling on deactivation) before investing in leak-proof tests. The fix is clear; the test would validate the fix.

---

## 4. Required Fixes (Lifecycle Hardening) — DONE

Per [EFFECTSMIXER_DOMAIN_SPLIT_ANALYSIS.md](EFFECTSMIXER_DOMAIN_SPLIT_ANALYSIS.md) §6:

1. ✅ Implement `IPanelLifecycle`; in `OnDeactivatedAsync`, call `StopPolling()`.
2. ✅ Add `IDisposable`; in `Dispose`, call `StopPolling()`.
3. ✅ Replace `ContinueWith` with proper async + selection-specific CTS + staleness guard.
4. ✅ Add `_disposalCts` for selection-triggered loads; cancel in Dispose/OnDeactivatedAsync.

---

## Changelog

- 2026-03-13: Initial verification. Confirmed runtime lifecycle risk; confirmed panel-close leak plausible and likely.
- 2026-03-13: Lifecycle hardening implemented (IPanelLifecycle, IDisposable, no ContinueWith, staleness guards).
- 2026-03-13: Doc sync. §1–2 updated to reflect post-hardening state; pre-hardening evidence retained for archival.
