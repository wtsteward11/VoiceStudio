# Panel Lifecycle Audit (Fix 3)

**Date:** 2026-03-16  
**Purpose:** Document lifecycle audit per Ruthless Implementation Plan Fix 3.  
**Rule:** Subscribe in `OnActivatedAsync`, unsubscribe in `OnDeactivatedAsync`; constructors side-effect-light.

---

## Lifecycle Rule

- **Constructor:** No event subscriptions. Light setup only.
- **OnActivatedAsync:** Subscribe to EventAggregator, start polling, refresh data.
- **OnDeactivatedAsync:** Unsubscribe, stop timers, release resources.
- **On reactivate:** Resubscribe in OnActivatedAsync (do not leave permanently unsubscribed).

---

## Audited ViewModels

| ViewModel | Constructor Subscriptions | OnDeactivated Unsubscribes | OnActivated Resubscribes | Status |
|-----------|---------------------------|-----------------------------|---------------------------|--------|
| LibraryViewModel | None | Yes | Yes | Hardened (prior work) |
| VoiceSynthesisViewModel | Was: ProfileSelectedEvent | Yes | Yes | **Fixed** (moved to OnActivatedAsync) |
| JobProgressViewModel | None (ConnectAsync deferred) | Yes | Yes | OK |
| EffectsMixerViewModel | None | Yes | Yes | OK |
| TimelineViewModel | None | Yes | N/A (no subscriptions) | OK |
| ProfilesViewModel | None | Yes | N/A | OK |
| Others (30+ panels) | Audit as needed | Most return Task.CompletedTask | N/A | Spot-check only |

---

## Fix Applied

**VoiceSynthesisViewModel:** Moved `_eventAggregator.Subscribe<ProfileSelectedEvent>(OnProfileSelected)` from constructor to `OnActivatedAsync`. Ensures resubscribe on reactivate; prevents permanent unsubscribe bug.

---

## Verification

- `LibraryViewModelLifecycleTests` (4 tests) — PASS
- `PanelIdConsistencyTests` — PASS

---

## Reference

- [LIFECYCLE_OFFENDER_QUEUE.md](LIFECYCLE_OFFENDER_QUEUE.md) — Ranked offender list (P0–P3) with fix order
- [TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md](TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md) — Fire-and-forget patterns
- [RETAINED_ASYNC_RULE.md](RETAINED_ASYNC_RULE.md) — ADR-047 constructor ban
