# GOV-VOICESTUDIO-SELECTION-AUTHORITY-01 — Execution row

**Status:** Closed (2026-03-28)  
**Objective:** One documented authority for active profile, timeline primary clip/track, and playable transport context, so hero workflows do not maintain contradictory parallel selection buses.

## Binary acceptance (frozen)

| Slice | Acceptance |
|-------|------------|
| 1 — Audit + decision | Publisher → subscriber map + canonical owners written in this row (§2–§3). |
| 2 — Profile bus | All production publishers use `ProfileSelectedEvent`; `VoiceProfileSelectedEvent` obsolete (no new publishes). Features `SynthesisViewModel` subscribes to `ProfileSelectedEvent`. |
| 3 — Clip / track | `IContextManager` exposes `ActiveTimelinePrimaryClipId` / `ActiveTimelinePrimaryTrackId` and `SetActiveTimelineSelection`; main `TimelineViewModel` syncs on clip and track selection changes. Playable remains `SetCurrentPlayable` (unchanged). |
| 4 — Proof | MSTest (`SelectionAuthorityTests`, `ContextManagerTests`, `WorkflowCoordinatorServiceTests`); `dotnet build`; `dotnet test` App.Tests; `pytest tests/ci`; `verify.ps1 -Quick`; `python scripts/run_verification.py` (**completion_guard** PASS). |

## Hard OUT (not this lane)

- SQLite / timeline DB / unified project save (GAP-016–018)
- Full transport bar parity — **closed** 2026-03-28: `GOV-VOICESTUDIO-TRANSPORT-AUTHORITY-01` + GAP-009; see [VOICESTUDIO_TRANSPORT_AUTHORITY_LANE_CLOSURE_2026-03-28.md](../reports/verification/VOICESTUDIO_TRANSPORT_AUTHORITY_LANE_CLOSURE_2026-03-28.md)
- MainWindow decomposition except selection seams touched here (GAP-008)

## Follow-on lanes (historical queue; this row is closed)

1. ~~`GOV-VOICESTUDIO-TRANSPORT-AUTHORITY-01`~~ — **Done** 2026-03-28 (GAP-009 Closed).  
2. `GOV-VOICESTUDIO-PERSISTENCE-FOUNDATION-01` — SQLite + per-project timeline (GAP-016, GAP-017, GAP-021) — **next**.

---

## §2 Slice 1 — Publisher → event → subscriber map

### Profile selection

| Publisher | Event | Subscribers (representative) | Duplication risk | Canonical path |
|-----------|-------|------------------------------|------------------|----------------|
| `ProfilesViewModel` | `ProfileSelectedEvent` | `VoiceSynthesisViewModel`, `LibraryViewModel`, `Features/Timeline/TimelineViewModel` (debug), store via other flows | Low | **Keep** — primary user intent from Profiles panel |
| `ContextManager` / `AppStateStore` | `ProfileSelectedEvent` | Same bus | Medium if panels also publish | **Authority:** store-driven profile is truth; panels publish on direct UI selection |
| `LibraryViewModel` (workflow null fallback) | ~~`VoiceProfileSelectedEvent`~~ → `ProfileSelectedEvent` | Features synthesis, panels | Was high | **Unified** to `ProfileSelectedEvent` |
| `WorkflowCoordinatorService.StartSynthesizeWithVoiceAsync` | ~~`VoiceProfileSelectedEvent`~~ → `ProfileSelectedEvent` | Features synthesis | Was high | **Unified**; source `workflow-coordinator` |

**Decision:** **`ProfileSelectedEvent` is the only profile selection bus.** `VoiceProfileSelectedEvent` is `[Obsolete]` for compile-time migration; remove remaining references in a later cleanup if desired.

### Clip / track / playable

| Owner | Mechanism | Consumers | Canonical path |
|-------|-----------|-----------|----------------|
| Timeline panel (`Views/Panels/TimelineViewModel`) | `MultiSelectService` + `SetActiveTimelineSelection` | `IContextManager` readers (transport-adjacent, future panels) | **Timeline VM** writes context; **no** second clip authority |
| `IContextManager` | `ActiveTimelinePrimaryClipId` / `ActiveTimelinePrimaryTrackId` | Status/transport coordinators (future), tests | **Read model** |
| Library / synthesis / timeline play | `SetCurrentPlayable` | `GlobalTransportOrchestrator`, `StatusBarCoordinator` | **Unchanged** — playable remains explicit transport ownership |

**Bridging / removal order:** (1) Obsolete duplicate event class. (2) Switch publishers + Features subscriber. (3) Add timeline → context sync. (4) Delete `VoiceProfileSelectedEvent` only when zero references (optional follow-up).

---

## §3 Closure

- **Proof:** `docs/reports/verification/VOICESTUDIO_SELECTION_AUTHORITY_LANE_CLOSURE_2026-03-28.md`
- **Tracker:** GAP-011 → **Closed** (foundation: context read model + canonical event bus; optional `ISelectionService` facade deferred).

## Changelog

| Date | Note |
|------|------|
| 2026-03-28 | Lane opened; slices 1–4 executed; row closed. |
