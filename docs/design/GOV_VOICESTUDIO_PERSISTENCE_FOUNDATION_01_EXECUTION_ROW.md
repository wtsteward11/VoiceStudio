# GOV-VOICESTUDIO-PERSISTENCE-FOUNDATION-01 — Execution row

**Lane ID:** `GOV-VOICESTUDIO-PERSISTENCE-FOUNDATION-01`  
**Status:** Closed (2026-03-28)  
**Tracker:** GAP-017, GAP-018, GAP-021 — **Closed** (this lane); GAP-016 **Open** (SQLite/Alembic explicitly out of scope — see § Hard OUT).  
**Closure:** [VOICESTUDIO_PERSISTENCE_FOUNDATION_LANE_CLOSURE_2026-03-28.md](../reports/verification/VOICESTUDIO_PERSISTENCE_FOUNDATION_LANE_CLOSURE_2026-03-28.md)

## Frozen objective

Establish **one authoritative persistence foundation** for project state so save/load/reopen is coherent, versioned, and durable across hero workflows — without inventing a storage empire.

## Binary acceptance (lane)

| Slice | Acceptance |
|-------|------------|
| 1 — Authority map | Exactly one canonical **save** owner and one **load** owner (or façade) named; competing paths labeled canonical \| adapter \| deprecated; project-scoped vs app-scoped boundary written. |
| 2 — Authoritative save | Single shell save entrypoint persists agreed project-scoped state (mixer + backend project metadata + local JSON snapshot); parallel menu vs file.save ambiguity reduced; failures propagate (no false-success from coordinator when handler throws). |
| 3 — Load / reopen | Round-trip tests for JSON persistence; malformed / unsupported schema version surfaces explicit failure (no silent partial lie). |
| 4 — Versioning + closure | `PersistedProjectSchemaVersion` on `Project`; read rejects newer unknown versions; migration posture documented; closure report + gates. |

## Hard IN (this lane)

- Project metadata persistence (backend + local mirror on shell save).
- Timeline track/clip snapshot on shell save (`SnapTracksOntoSelectedProject` → `Project.Tracks` in JSON).
- Mixer state persistence on shell save (existing `EffectsMixerViewModel` path).
- Workspace vs project-session boundary (explicit below).
- Load/reopen behavior and honest failure for JSON (`JsonProjectRepository`).
- Versioning / schema boundary for persisted project file (v1 integer field).

## Hard OUT

- Export redesign, transcript editing, waveform editing, metering, plugin hosting, collaboration, marketplace, shell/theme polish, telemetry expansion.
- **SQLite / Alembic “empire”** (GAP-016): not required for this lane; JSON + schema version + explicit upgrade path only.

## Why next

Selection, transport, cloning, and runtime honesty are closed; the next product risk is **fragmented save/reopen truth** (metadata, timeline, mixer, layout). This lane makes one **shell Save** path that aligns backend project metadata, mixer persistence, and a durable local `Project` JSON snapshot using the same `VoiceStudio.Core.Models.Project` type as the API.

---

## §2 Persistence authority map (Slice 1 — frozen)

| Domain | Current owners | Canonical owner | Decision | Migration impact |
|--------|----------------|-----------------|----------|------------------|
| Project metadata (API) | `IProjectsClient` / `IBackendClient` | **Canonical:** `UnifiedProjectSaveHandler` → `UpdateProjectAsync` on shell Save | Single shell path updates backend from `TimelineViewModel.SelectedProject` | None for API consumers |
| Project JSON (local) | `JsonProjectRepository` via `FileOperationsHandler.SaveProjectAsync` | **Canonical for shell Save:** same handler → `IProjectRepository.SaveAsync` after track snap | **Adapter:** `FileOperationsHandler` remains for command palette / explicit file workflows until merged | Old JSON files without schema version treated as v0 legacy |
| Timeline tracks/clips (runtime) | `ITimelineTrackService` + `TimelineViewModel.Tracks` | **Canonical:** backend per edit operation; **snapshot:** `SnapTracksOntoSelectedProject` before shell save | Tracks stay authoritative on backend; JSON carries best-effort copy for reopen/offline | Large projects: JSON size growth — monitor |
| Mixer / effects | `EffectsMixerViewModel` + `IMixerStateClient` | **Canonical:** mixer VM on shell Save (via `SaveMixerStateCommand`) | Unchanged transport; invoked from unified handler | — |
| Open / recent | `TimelineProjectOpenHandler` + backend `GetProjectAsync` | **Canonical load (picker/recent):** backend → `TimelineViewModel` | — | — |
| Open from file | `FileOperationsHandler` + `JsonProjectRepository` | **Adapter** for file picker path | Not unified with picker in this lane | — |
| Workspace / layout | `PanelStateService`, workspace menus | **App-scoped** (not written by unified project save) | See boundary below | — |
| Crash recovery | `CrashRecoveryService` (if present) | **Adapter / parallel** — not replaced here | — | — |
| Selected context | `IContextManager` | **Ephemeral** selection; not a persistence root | — | — |

### Project-scoped vs app-scoped (frozen)

- **Project-scoped:** `Project` model fields, timeline track snapshot attached to `Project.Tracks`, mixer state keyed by project id, backend project record.
- **App-scoped:** panel layout, workspace presets, theme, global settings, window geometry, recent list (stored separately).

### Deprecated / dual-path notes

- **Before lane:** `MainWindow` Save → `ProjectWorkflowCoordinator` → **mixer only** (no backend metadata, no JSON).
- **After lane:** Save → `UnifiedProjectSaveHandler` (mixer + `UpdateProjectAsync` + `SaveAsync` JSON).

---

## §3 Implementation summary (Slices 2–3)

- **`IProjectSaveHandler.SaveProjectAsync`:** single interface method; `ProjectWorkflowCoordinator` calls it.
- **`UnifiedProjectSaveHandler`:** `SnapTracksOntoSelectedProject` → mixer command → `IProjectsClient.UpdateProjectAsync` → `IProjectRepository.SaveAsync`.
- **`Project.PersistedProjectSchemaVersion`:** written by `JsonProjectRepository` at save; validated on load.
- **Tests:** `JsonProjectRepository` schema + round-trip; `ProjectWorkflowCoordinator` verifies `SaveProjectAsync` on handler.

## §4 Versioning / migration posture (Slice 4)

- **Current file schema version:** `1` (`JsonProjectRepository.CurrentPersistedProjectSchemaVersion`).
- **Legacy files:** missing or `0` → treated as version **0**, accepted as read-compatible with v1 writer.
- **Forward incompatibility:** file version **greater** than current → `InvalidDataException` on open (honest failure).
- **Migration strategy:** additive fields use JSON defaults; breaking changes require bumping `CurrentPersistedProjectSchemaVersion` and adding a dedicated migrator in a future lane (not required here).

---

## §5 Verification (mandatory at closure)

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
- `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64`
- `python -m pytest tests/ci/ -q --randomly-seed=12345`
- `.\scripts\verify.ps1 -Quick` → record `artifacts/verify/<id>/verification_report.md`
- `python scripts/run_verification.py` → **completion_guard** PASS

Proof paths are recorded in the lane closure report.
