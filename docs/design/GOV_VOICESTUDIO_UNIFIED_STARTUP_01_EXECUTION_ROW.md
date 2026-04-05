# GOV-VOICESTUDIO-UNIFIED-STARTUP-01 — Unified Startup Lane

## 0. Status

- **State:** **Closed** (2026-03-28) — Slices 1–4 complete; lane closure report [VOICESTUDIO_UNIFIED_STARTUP_LANE_CLOSURE_2026-03-28.md](../reports/verification/VOICESTUDIO_UNIFIED_STARTUP_LANE_CLOSURE_2026-03-28.md)
- **Opened:** 2026-03-28
- **Owner:** Tyler + agent execution support
- **Predecessor lane:** `GOV-PERSONAL-STUDIO-FINISH-LINE-AUDIT-01` (workspace lane closed)

---

## 1. Objective (Frozen)

Make VoiceStudio launch as one unified Windows application where backend readiness is handled by product flow, not manual shell orchestration.

Required behavior:

1. If backend is already healthy, app connects and proceeds.
2. If backend is not healthy, app starts backend automatically.
3. App waits for readiness with clear progress/error UX.
4. App avoids duplicate backend spawn and handles port/process conflicts deterministically.
5. Normal use does not require manual PowerShell backend startup.

---

## 2. In Scope

- Startup orchestration decisions and implementation in app startup path and/or bounded helper seam.
- Backend health probing and readiness timeout policy.
- User-visible startup status and failure messaging.
- Duplicate-process and port-conflict handling policy.
- Verification artifacts for the five required proof scenarios.

---

## 3. Out of Scope (Hard)

- New stash extraction lanes (`T1`, `T3`, `T4`) unless a direct blocker is proven.
- Commercialization, distribution, installer re-architecture.
- Unrelated UI polish or feature expansion.
- OpenAPI/schema contract migrations unrelated to startup behavior.

---

## 4. Architecture Decision (Frozen)

### 4.1 Chosen model

- **Decision:** **App-native orchestrator** is the only normal-user launch path.
- **Rationale:** deterministic startup UX, testability inside app lifecycle, and no manual shell dependency for day-to-day use.

### 4.2 Ownership boundaries

- **Startup owner seam:** `src/VoiceStudio.App` application startup path (`App`/startup services), not scripts.
- **Backend lifecycle seam:** `BackendProcessManager` owns backend process start/stop/health checks and readiness waiting.
- **Scripts (`scripts/start_backend.ps1`, `scripts/dev-launch.ps1`):** dev/operator helpers only; they are not authoritative for normal-user startup behavior.

### 4.3 Frozen runtime policy

- **Reuse-first:** probe backend health before any spawn attempt.
- **Spawn-if-needed:** only spawn when probe shows backend unavailable.
- **Readiness timeout:** fixed startup timeout policy (single source in startup orchestrator; no ad hoc per-call timeouts).
- **Failure UX:** explicit blocking startup status with actionable message (no silent retry loops, no false-ready).
- **Repeat launch behavior:** no duplicate backend spawn when a healthy instance is already available.
- **Shutdown policy:** frontend shutdown must not orphan unmanaged duplicate backend processes.

---

## 5. Acceptance Proof Scenarios

All scenarios are required for lane closure:

1. **Backend already running**
   - App detects healthy backend and proceeds without redundant spawn.
2. **Backend not running**
   - App starts backend, waits for ready, then proceeds.
3. **Backend startup failure**
   - App shows explicit failure state/message with actionable next step.
4. **Port/process conflict**
   - App handles conflict deterministically (no silent hang, no duplicate loops).
5. **Repeat launch**
   - Repeated app starts do not create duplicate backend side effects.

---

## 6. Proof Plan

- Record command/test evidence in a dedicated verification report under `docs/reports/verification/`.
- Capture runtime logs for startup decision path (health-check result, start attempt, readiness outcome).
- Re-run baseline quality commands on implementation commit:
  - `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
  - `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64`
  - `python -m pytest tests/ci/ -q --randomly-seed=12345`

---

## 7. Risks and Controls

- **Risk:** Scope expands into installer/commercial concerns.
  - **Control:** Enforce hard OUT list.
- **Risk:** Non-deterministic startup race conditions.
  - **Control:** Single orchestrator seam + explicit timeout/retry policy.
- **Risk:** Duplicate backend instances after repeated app launches.
  - **Control:** Health/lock/port checks before any start action.

---

## 8. Rollback and Safety

- Keep startup changes isolated and reversible by seam.
- If startup regressions are detected, disable new orchestrator path via guarded fallback and restore previous launch behavior while retaining diagnostics.

---

## 9. Closure Criteria

Lane closes only when:

1. All five acceptance proof scenarios pass with artifacts.
2. Baseline build/tests/CI remain green on closure commit.
3. `.cursor/STATE.md` and `CANONICAL_REGISTRY.md` reflect closure truth.

---

## 10. Slice Map (Frozen Before Coding)

### Slice 1 — Startup Decision Seam

- **Objective:** deterministic reuse-vs-start decision on app launch.
- **Primary files:** `src/VoiceStudio.App/App.xaml.cs`, `src/VoiceStudio.App/Services/BackendProcessManager.cs`, `src/VoiceStudio.App/Services/AppServices.cs`.
- **Out of slice:** installer/package scripts, unrelated panel/viewmodel changes.
- **Proof:** **Closed** — scenario 1 and scenario 2 decision-path evidence captured in `docs/reports/verification/VOICESTUDIO_UNIFIED_STARTUP_SLICE1_PROOF_2026-03-28.md`.
- **Rollback trigger:** any regression where healthy backend is ignored or duplicate spawn occurs.

### Slice 2 — Spawn + Readiness + Failure UX

- **Objective:** controlled start path with readiness gate and explicit timeout/failure UI state.
- **Primary files:** `src/VoiceStudio.App/App.xaml.cs`, `src/VoiceStudio.App/MainWindow.xaml.cs` (or startup status surface), `src/VoiceStudio.App/Services/BackendProcessManager.cs`.
- **Out of slice:** broad UI redesign; non-startup backend API refactors.
- **Proof:** **Closed** — startup gating/failure-surface coherence proof in `docs/reports/verification/VOICESTUDIO_UNIFIED_STARTUP_SLICE2_PROOF_2026-03-28.md`.
- **Rollback trigger:** startup hang, silent failure, or user-visible false-ready state.

### Slice 3 — Conflict and Non-Duplication Hardening

- **Objective:** deterministic handling for port/process conflicts and repeated launches.
- **Primary files:** `src/VoiceStudio.App/Services/BackendProcessManager.cs`, startup orchestration path, targeted startup tests/harness.
- **Out of slice:** schema/OpenAPI/stash extraction work.
- **Proof:** **Closed** (2026-03-28) — scenario 4 + scenario 5 (in-process repeat) deterministic MSTest + `startup_decision.json` v1 fields; see `docs/reports/verification/VOICESTUDIO_UNIFIED_STARTUP_SLICE3_PROOF_2026-03-28.md`.
- **Rollback trigger:** unresolved conflict ambiguity or duplicate process side effects.

### Slice 4 — Closure Verification + Governance Sync

- **Objective:** produce closure-grade artifacts and align docs/state.
- **Primary files:** `docs/reports/verification/*startup*`, `.cursor/STATE.md`, `docs/governance/CANONICAL_REGISTRY.md`, this execution row.
- **Out of slice:** new feature implementation.
- **Proof:** **Closed** (2026-03-28) — five-scenario matrix + §6 gates in `docs/reports/verification/VOICESTUDIO_UNIFIED_STARTUP_LANE_CLOSURE_2026-03-28.md`; `verify.ps1 -Quick` artifact `artifacts/verify/20260328_004504/`.
- **Rollback trigger:** artifact/doc mismatch with actual runtime behavior.

---

## 11. Proof Matrix (Frozen Pre-Implementation)

| Scenario | Test Surface | Expected Observable Output | Artifact Destination |
| --- | --- | --- | --- |
| Backend already running (reuse) | Manual/system smoke + startup diagnostics | App reaches ready state without duplicate spawn; startup overlay clears | Lane verification report + startup diagnostics artifact |
| Backend not running (controlled start) | Icon-launch smoke path | App starts backend, reaches ready, and post-ready backend action succeeds | Smoke summary artifact + lane verification report |
| Backend startup failure | Failure smoke paths (`port` / `runtime`) | Deterministic `BackendFailed` state with actionable message/retry | Failure summary artifact + lane verification report |
| Port/process conflict | Deterministic conflict setup + failure smoke | Conflict categorized deterministically (no silent hang/loop) | Conflict evidence + failure summary + lane verification report |
| Repeat launch | Repeat-launch smoke/manual proof | No duplicate app/backend side effects across repeated launches | Process/port snapshots + diagnostics + lane verification report |

---

## 12. Slice 1 Execution Record (Frozen)

### 12.1 Status

- **State:** Closed (2026-03-28)
- **Scope intent:** Startup decision seam only (reuse vs controlled-start), no Slice 2/3 behavior expansion.
- **Proof reference:** `docs/reports/verification/VOICESTUDIO_UNIFIED_STARTUP_SLICE1_PROOF_2026-03-28.md`

### 12.2 Touched files (frozen)

- `src/VoiceStudio.App/Services/BackendProcessManager.cs` (required)
- `src/VoiceStudio.App/App.xaml.cs` (only if diagnostics plumbing is strictly required)
- `src/VoiceStudio.App/Services/AppServices.cs` (only if DI wiring is strictly required)

### 12.3 Explicitly not touched in Slice 1

- `src/VoiceStudio.App/MainWindow.xaml`
- `src/VoiceStudio.App/MainWindow.xaml.cs`
- `src/VoiceStudio.App/Program.cs`
- Installer/packaging/commercialization artifacts
- Stash lanes (`T1`/`T3`/`T4`) and schema/OpenAPI work

### 12.4 Binary acceptance criteria

1. If backend is healthy before launch, app **reuses** and **does not spawn** a duplicate backend process.
2. If backend is unavailable before launch, app enters a **controlled start path** and reaches ready.
3. Startup diagnostics produce a deterministic branch artifact (`startup_decision.json`) with decision and branch inputs.
4. Reuse path shows no duplicate backend side effect.

### 12.5 Frozen policy values and ownership

- **Startup readiness timeout (spawn path):** `45` seconds, single authoritative constant in `BackendProcessManager`.
- **Lifecycle rule:**
  - Backend process spawned by app is terminated on app exit (`BackendProcessManager.Dispose()` -> `StopBackend()` -> process tree kill).
  - Reused pre-existing backend is not terminated by app shutdown (`_backendProcess` remains null in reuse-only path).

### 12.6 Rollback trigger

- Trigger rollback if any of the following occurs:
  - Healthy backend is ignored and app spawns a duplicate process.
  - Controlled-start path regresses (hang/fail) relative to baseline.
  - Startup decision artifact is missing or contradictory to observed behavior.

---

## 13. Slice 2 Execution Record (Frozen)

### 13.1 Status

- **State:** Closed (2026-03-28)
- **Scope intent:** eliminate mixed startup/panel error surfaces while preserving Slice 1 decision seam behavior.
- **Proof reference:** `docs/reports/verification/VOICESTUDIO_UNIFIED_STARTUP_SLICE2_PROOF_2026-03-28.md`

### 13.2 Touched files (frozen)

- `src/VoiceStudio.App/App.xaml.cs` (startup orchestration + smoke proof payload)
- `src/VoiceStudio.App/MainWindow.xaml.cs` (startup-authoritative surface wiring only if needed)
- `src/VoiceStudio.App/Services/ErrorDialogService.cs` (startup-time modal suppression/reroute guard)
- `src/VoiceStudio.App/Services/StartupRetryCoordinator.cs` (retry authority, only if required)
- `src/VoiceStudio.App.Tests/Services/*` (targeted startup gating tests)

### 13.3 Explicitly not touched in Slice 2

- `src/VoiceStudio.App/Services/BackendProcessManager.cs` conflict/repeat-launch hardening (Slice 3)
- Installer/packaging/commercialization artifacts
- Stash lanes (`T1`/`T3`/`T4`) and schema/OpenAPI work
- Broad panel UX redesign or unrelated panel refactors

### 13.4 Binary acceptance criteria

1. While startup state is `Starting` or `BackendStarting`, backend-dependent panels must not show independent backend connection-error dialogs.
2. During startup pending/failure window, exactly one startup-authoritative pending/failure surface is visible.
3. On `BackendReady`, startup overlay clears and normal panel behavior resumes.
4. On `BackendFailed`, one deterministic startup failure surface remains with one retry authority (`StartupRetryCoordinator` path).
5. Screenshot-class mixed-state race (overlay + panel modal) is not allowed.

### 13.5 Rollback trigger

- Trigger rollback if any of the following occurs:
  - Any startup-time modal dialog appears in parallel with startup overlay/failure surface.
  - Retry authority fragments (multiple competing retry/error entry points).
  - Slice 1 decision seam regresses while implementing Slice 2.

---

## 14. Slice 3 Execution Record (Frozen)

### 14.1 Status

- **State:** Closed (2026-03-28)
- **Scope intent (exact):** **Conflict handling + repeat-launch non-duplication**
- **Proof reference:** `docs/reports/verification/VOICESTUDIO_UNIFIED_STARTUP_SLICE3_PROOF_2026-03-28.md`

### 14.2 Touched files (frozen at slice close — list actual commit surface)

- `src/VoiceStudio.App/Services/BackendProcessManager.cs` (startup decision artifact schema + conflict path consistency)
- `src/VoiceStudio.App.Tests/Services/BackendProcessManagerDecisionTests.cs` (or sibling) — deterministic port-conflict + repeat-invocation proofs
- `docs/reports/verification/VOICESTUDIO_UNIFIED_STARTUP_SLICE3_PROOF_*.md` — slice proof matrix
- `.buildlogs/verification/startup_slice3_targeted.trx` (or equivalent) — archived test run
- `docs/design/GOV_VOICESTUDIO_UNIFIED_STARTUP_01_EXECUTION_ROW.md` — this §14 status flip to Closed
- `.cursor/STATE.md`, `docs/governance/CANONICAL_REGISTRY.md` — governance sync when slice closes

### 14.3 Explicitly not touched in Slice 3

- Installer/packaging/commercialization
- Stash lanes (`T1`/`T3`/`T4`) and unrelated OpenAPI/schema work
- Broad startup UX redesign (unless a closure gap forces a minimal fix)

### 14.4 Conflict taxonomy (frozen — Slice 3)

| Taxonomy label | `startup_decision.json` `decision` | `BackendStartFailureCategory` | `conflict_category` field |
| --- | --- | --- | --- |
| Healthy backend reuse | `reuse` | (success) | `null` |
| Unhealthy port occupation (non-health listener / non-VoiceStudio) | `port_collision` | `PortCollision` | `port_collision` |
| Spawn refused — port occupied and unhealthy | `port_collision` | `PortCollision` | `port_collision` |
| Repeat invocation reuse (no second spawn) | `reuse` | (success) | `null` |

New taxonomy rows require an execution-row amendment before coding.

### 14.5 Binary acceptance criteria

1. Port occupied **and** VoiceStudio health probe **succeeds** → **reuse**, **`spawn_attempted` false** (including port-first branch inside spawn path).
2. Port occupied **and** health probe **fails** → **single** deterministic outcome: `decision` = `port_collision`, **`spawn_attempted` false**, **`conflict_category` = `port_collision`**, no silent hang, `PortCollision` is **no-retry** in `StartupRetryCoordinator`.
3. **Repeat launch**
   - **Concurrent:** second app process exits via `VoiceStudio_SingleInstance_Mutex_v1` in `Program.cs` before backend orchestration — documented in proof (optional smoke); no duplicate app/backend orchestration from that instance.
   - **In-process:** second `EnsureBackendRunningAsync` after successful ready backend → **`reuse`**, **`spawn_attempted` false**, **`reused_existing_backend` true**, no second backend PID from manager spawn.
4. Conflict/failure outcomes are reflected in **`startup_decision.json`** (including `schema_version`, `spawn_attempted`, `reused_existing_backend`, `conflict_category` per §14.6) — proof maps each AC to fields and tests.
5. Slice 1 and Slice 2 behaviors and tests remain green (decision seam + startup modal gating).

### 14.6 Authoritative `startup_decision.json` fields (Slice 3)

- **Carried forward:** `timestamp_utc`, `decision`, `health_probe_result`, `port_occupied`, `backend_pid`, `timeout_seconds`, `elapsed_ms`
- **Added (Slice 3):** `schema_version` (int), `spawn_attempted` (bool), `reused_existing_backend` (bool), `conflict_category` (string or null — `port_collision` when collision branch)

### 14.7 Rollback trigger

- Trigger rollback if any of the following occurs:
  - Healthy backend ignored and duplicate spawn occurs.
  - Port conflict ambiguous (hang, unbounded retry, or artifact contradicts observed branch).
  - Repeat invocation causes duplicate manager-spawned backend or contradictory artifacts.
  - Slice 1 or Slice 2 regressions.

---

## 15. Slice 4 Execution Record (Frozen — Closure-Only)

### 15.1 Status

- **State:** **Closed** (2026-03-28) — consolidated closure report + mandatory gates (build, full App.Tests, `pytest tests/ci`, `verify.ps1 -Quick`) recorded **PASS** in [VOICESTUDIO_UNIFIED_STARTUP_LANE_CLOSURE_2026-03-28.md](../reports/verification/VOICESTUDIO_UNIFIED_STARTUP_LANE_CLOSURE_2026-03-28.md) §6.
- **Scope intent (exact):** **Consolidated lane-closure verification + final governance sync**
- **Proof reference:** `docs/reports/verification/VOICESTUDIO_UNIFIED_STARTUP_LANE_CLOSURE_2026-03-28.md`

### 15.2 Closure-only boundaries (non-negotiable)

- **In slice:** evidence consolidation across Slices 1–3, proof-honesty reconciliation (especially scenario 5), screenshot-class regression cross-check statement, mandatory verification commands, alignment of lane doc + `STATE.md` + `CANONICAL_REGISTRY.md`.
- **Out of slice:** new startup features, `BackendProcessManager` behavior changes beyond doc truth, installer/packaging, stash/OpenAPI/schema work, broad panel or shell redesign.

### 15.3 Touched files (expected at slice close)

- `docs/reports/verification/VOICESTUDIO_UNIFIED_STARTUP_LANE_CLOSURE_2026-03-28.md` (new — single five-scenario closure report)
- `docs/design/GOV_VOICESTUDIO_UNIFIED_STARTUP_01_EXECUTION_ROW.md` (§0 lane status, §15 status)
- `.cursor/STATE.md` (ACTIVE WINDOW, LATEST MILESTONE, LATEST PROOF INDEX)
- `docs/governance/CANONICAL_REGISTRY.md` (banner, Session State row, closure proof row)

### 15.4 Binary closure conditions (all required)

1. One closure report maps all **five** lane acceptance scenarios to evidence with pass/fail per scenario.
2. **Scenario 5 (repeat launch):** proof level is split — **in-process** vs **cross-process** — with no equivalence claim between them unless both are proven at the stated level.
3. Explicit **screenshot-class regression** statement: startup authority (modal suppression / single failure surface contract) tied to deterministic tests and/or documented smoke limits.
4. On closure claim commit: `dotnet build`, full `dotnet test` App.Tests, `pytest tests/ci` with `--randomly-seed=12345`, and **`./scripts/verify.ps1 -Quick`** all **PASS**.
5. Lane doc §0, `STATE.md`, and `CANONICAL_REGISTRY.md` agree on **lane closed** and point at the same closure report.

### 15.5 Rollback trigger

- Trigger rollback if any of the following occurs:
  - Closure report overstates evidence (especially cross-process repeat launch or full production UX).
  - Mandatory verification fails while docs claim closure.
  - Any truth surface (lane / STATE / registry) disagrees with the others.
