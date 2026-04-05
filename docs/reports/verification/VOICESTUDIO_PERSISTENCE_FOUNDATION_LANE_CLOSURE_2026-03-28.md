# GOV-VOICESTUDIO-PERSISTENCE-FOUNDATION-01 — Lane closure (2026-03-28)

**Lane ID:** `GOV-VOICESTUDIO-PERSISTENCE-FOUNDATION-01`  
**Trackers:** GAP-017, GAP-018, GAP-021 — **Closed** with this artifact (see honesty on GAP-016 below).  
**Execution row:** [GOV_VOICESTUDIO_PERSISTENCE_FOUNDATION_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_PERSISTENCE_FOUNDATION_01_EXECUTION_ROW.md) — status **Closed**

## 1 Substance delivered (what “closed” means)

At **current product maturity**, the following are true and proof-backed:

- **Authority map (Slice 1):** Canonical shell **save** is `UnifiedProjectSaveHandler` / `IProjectSaveHandler.SaveProjectAsync`; canonical **load** for picker/recent is `TimelineProjectOpenHandler` + backend; `FileOperationsHandler` + `JsonProjectRepository` are classified as **adapter** for explicit file workflows; project-scoped vs app-scoped boundary is frozen in the execution row.
- **Authoritative save (Slice 2):** Menu/shell Save persists mixer state (when executable), pushes project metadata via `IProjectsClient.UpdateProjectAsync`, snapshots timeline tracks onto `Project.Tracks`, and writes durable JSON via `IProjectRepository.SaveAsync` (`JsonProjectRepository`).
- **Load / reopen honesty (Slice 3):** `JsonProjectRepository` round-trip tests prove tracks + schema version; legacy JSON without `persistedProjectSchemaVersion` loads as v0; files with **newer** schema than the app throw `InvalidDataException` (no silent accept).
- **Versioning + closure (Slice 4):** `Project.PersistedProjectSchemaVersion` + `JsonProjectRepository.CurrentPersistedProjectSchemaVersion`; forward-incompatible reads fail loudly; migration posture documented in execution row §4.

This closure **does not** claim SQLite/Alembic authoritative backend state (GAP-016 remains **Open**), full merge of command-palette `file.save` with shell Save, workspace layout inside project files, or end-to-end E2E reopen across a cold process (unit/repo proofs only).

## 2 Closure matrix (binary map: slice → proof)

| Slice | Acceptance statement (pass/fail) | Primary implementation files | Tests (representative) | Verification artifacts | Verdict | Honest limits |
|-------|----------------------------------|------------------------------|-------------------------|-------------------------|---------|---------------|
| **1** | One canonical save owner, one load owner, competing paths labeled; project vs app boundary written. | `GOV_VOICESTUDIO_PERSISTENCE_FOUNDATION_01_EXECUTION_ROW.md` §2 | N/A (design freeze) | Execution row | **PASS** | Audit is doc-truth at close date; code may evolve under same contracts. |
| **2** | Single shell save pipeline: snap tracks, mixer, backend update, local JSON; failures propagate via handler exceptions. | `UnifiedProjectSaveHandler`, `IProjectSaveHandler`, `ProjectWorkflowCoordinator`, `ProjectWorkflowBootstrap`, `MainWindow.xaml.cs`, `TimelineViewModel.SnapTracksOntoSelectedProject` | `ProjectWorkflowCoordinatorTests` (`SaveProjectAsync` → handler) | §5 below | **PASS** | Mixer save may still swallow errors inside `EffectsMixerViewModel` (pre-existing); coordinator is honest when handler throws. |
| **3** | JSON round-trip; schema too new → exception; legacy without field → accepted. | `JsonProjectRepository`, `Project` (schema field) | `JsonProjectRepositoryTests` | §5 below | **PASS** | Full process E2E “close app / reopen” not in automated proof; repo-level round-trip only. |
| **4** | Version field + closure doc + governance sync + green gates. | This report; execution row; `CANONICAL_REGISTRY.md`; `PROFESSIONAL_GAP_TRACKER.md`; `.cursor/STATE.md` | Same suites as lane | **This document**; **`artifacts/verify/`** folder from §5 | **PASS** | Standalone full `dotnet test` may hit MSB3027 locks; authoritative suite signal per §5 honesty. |

## 3 Explicit non-goals (this lane did not solve)

- **GAP-016 / SQLite + Alembic** — backend authoritative relational store and migrations empire; explicitly **out** of this lane.
- **Export unification** — effects baked into export, batch export UX.
- **Transcript ↔ clip linkage** — text-driven editing.
- **Waveform editing** — destructive or non-destructive clip waveform ops.
- **Real-time metering** — VU/LUFS in production UI.
- **Collaboration / marketplace / telemetry expansion**
- **PanelHost GAP-007** — lifecycle / content property work.
- **Command-palette vs shell Save unification** — `FileOperationsHandler` remains a parallel adapter until a future row merges UX.

## 4 GAP honesty

| GAP | Status after lane | Note |
|-----|-------------------|------|
| GAP-017 | **Closed** | Timeline state: backend per edit + JSON track snapshot on shell save; contract frozen in execution row. |
| GAP-018 | **Closed** | Unified shell save path (mixer + metadata + JSON). Layout/synthesis meta in gap title not fully in v1 file — **honest limit**. |
| GAP-021 | **Closed** | Same `VoiceStudio.Core.Models.Project` through API client and `JsonProjectRepository` on shell save. |
| GAP-016 | **Open** | SQLite/Alembic not implemented; not required for this lane. |

## 5 Verification (closure change-set)

Executed **2026-03-28** for this persistence closure (post `JsonProjectRepositoryTests` empty-catch hygiene).

| Step | Command | Result |
|------|---------|--------|
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **PASS** (via `verify.ps1 -Quick` build stages) |
| Targeted C# | `dotnet test ... --filter FullyQualifiedName~JsonProjectRepositoryTests` | **PASS** — 3 tests |
| CI pytest | `python -m pytest tests/ci/ -q --randomly-seed=12345` | **PASS** — 216 passed, 2 deselected |
| Quick verify | `.\scripts\verify.ps1 -Quick` | **PASS** — `artifacts/verify/20260328_143335/verification_report.md` |
| Validator | `python scripts/run_verification.py` | **PASS** — **completion_guard** PASS; `.buildlogs/verification/last_run.json` |

**Authoritative Quick verify folder:** `artifacts/verify/20260328_143335/`

**Honesty:** Full standalone `dotnet test` App.Tests was not re-run as a single artifact for this closure slice; `verify.ps1 -Quick` + targeted `JsonProjectRepositoryTests` + `pytest tests/ci` + `run_verification.py` constitute the recorded proof (same discipline as Transport Slice 3/4 notes).

**Test names (Slice 3 / JSON):**

- `JsonProjectRepositoryTests.SaveAsync_roundTrip_sets_PersistedProjectSchemaVersion_and_tracks`
- `JsonProjectRepositoryTests.GetByIdAsync_throws_InvalidDataException_when_schema_newer_than_app`
- `JsonProjectRepositoryTests.GetByIdAsync_accepts_legacy_json_without_persistedProjectSchemaVersion`

## 6 References

- Execution row: [GOV_VOICESTUDIO_PERSISTENCE_FOUNDATION_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_PERSISTENCE_FOUNDATION_01_EXECUTION_ROW.md)
- Transport prerequisite (closed): [VOICESTUDIO_TRANSPORT_AUTHORITY_LANE_CLOSURE_2026-03-28.md](VOICESTUDIO_TRANSPORT_AUTHORITY_LANE_CLOSURE_2026-03-28.md)
