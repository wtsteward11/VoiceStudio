# VoiceStudio Unified Startup Baseline Audit (2026-03-28)

## 0. Purpose and Scope

This is a code-truth baseline for `GOV-VOICESTUDIO-UNIFIED-STARTUP-01`.

- **Purpose:** document what unified startup behavior already exists, what is missing, and how closure evidence will be produced.
- **Scope:** baseline only (no feature implementation in this wave).
- **Primary decision anchor:** `docs/design/GOV_VOICESTUDIO_UNIFIED_STARTUP_01_EXECUTION_ROW.md` (App-native orchestrator frozen).

---

## 1. Sources Reviewed (Code Truth)

- `src/VoiceStudio.App/Program.cs`
- `src/VoiceStudio.App/App.xaml.cs`
- `src/VoiceStudio.App/Services/BackendProcessManager.cs`
- `src/VoiceStudio.App/MainWindow.xaml.cs`
- `src/VoiceStudio.App/MainWindow.xaml`
- `src/VoiceStudio.App/Services/AppServices.cs`
- `scripts/start_backend.ps1`
- `scripts/dev-launch.ps1`

---

## 2. Current Startup Behavior (Observed)

### 2.1 Process and instance control

- `Program.cs` enforces a single app instance via named mutex (`VoiceStudio_SingleInstance_Mutex_v1`).
- If another app instance is running, second launch exits early (no foreground handoff yet).

### 2.2 App launch orchestration

- `App.OnLaunched` creates/activates `MainWindow`.
- In normal mode (non-smoke), it calls `StartBackendWithTracking()` (fire-and-forget).
- Startup state transitions are managed through `IStartupStateService` (`Starting` -> `BackendStarting` -> `BackendReady`/`BackendFailed`).

### 2.3 Backend lifecycle logic

- `BackendProcessManager.EnsureBackendRunningAsync()` does:
  1. Health probe first (`/health`).
  2. Reuse if healthy.
  3. If process exists but unresponsive, waits then may kill and restart.
  4. Detects port occupancy; if occupied and health is not VoiceStudio backend, fails with `PortCollision`.
  5. Spawns backend with resolved runtime path and waits for health (45s timeout).

### 2.4 User-visible startup UX

- `MainWindow` has startup overlay (`StartupOverlay`) and retry button (`StartupRetryButton`).
- On `BackendFailed`, overlay shows failure message and exposes retry.
- Transport/import paths are gated on startup readiness and emit "please wait" toasts when backend is not ready.

### 2.5 Existing smoke/failure hooks

- Launch flags/env support icon-launch smoke and failure-path smoke:
  - `--icon-launch-smoke`
  - `--smoke-failure-port`
  - `--smoke-failure-runtime`
- App writes structured smoke summaries under `%LOCALAPPDATA%\VoiceStudio\crashes\`.

### 2.6 Script role

- `scripts/start_backend.ps1` is an operator/dev script with interactive prompts.
- `scripts/dev-launch.ps1` is a debug launch wrapper.
- Current product code path does not require these scripts for normal app startup.

---

## 3. Gap-to-Lane Mapping

| Lane Scenario | Current State | Gap Status | Notes |
| --- | --- | --- | --- |
| 1. Backend already running (reuse) | Implemented in `EnsureBackendRunningAsync` health-first path | **Needs closure-proof artifact** | Behavior exists; lane still needs deterministic proof output. |
| 2. Backend not running (controlled start) | Implemented via spawn + health wait | **Partially proven** | Path exists; still need formal scenario evidence under this lane. |
| 3. Startup failure (deterministic UX) | Implemented (`BackendFailed` + overlay/retry + failure smoke flags) | **Partially proven** | Need lane-specific pass criteria and artifact capture. |
| 4. Port/process conflict handling | Implemented (`PortCollision` category and failure message) | **Needs explicit conflict proof** | Need deterministic evidence for non-VoiceStudio process on bound port. |
| 5. Repeat launch without duplicate side effects | Partially covered (single-instance mutex + health reuse) | **Needs stronger proof** | Must prove no duplicate backend side effects across repeated launches. |

---

## 4. First-Slice Touch List (No Code Changes Yet)

These are the expected first-slice files from baseline analysis:

- `src/VoiceStudio.App/App.xaml.cs` (startup decision seam / readiness orchestration timing).
- `src/VoiceStudio.App/Services/BackendProcessManager.cs` (reuse/start/conflict determinism and diagnostics).
- `src/VoiceStudio.App/MainWindow.xaml.cs` (startup failure/retry UX alignment to policy).
- `src/VoiceStudio.App/Program.cs` (repeat-launch behavior boundaries; optional diagnostics only if required).
- `docs/design/GOV_VOICESTUDIO_UNIFIED_STARTUP_01_EXECUTION_ROW.md` (already frozen decision/slices).

Hard OUT for slice 1 remains:

- Installer/commercialization work.
- Stash lane reopen (`T1`/`T3`/`T4`).
- OpenAPI/schema work unrelated to startup.

---

## 5. Proof Contract (Pre-Implementation)

| Scenario | Test Surface | Expected Observable Output | Artifact Destination |
| --- | --- | --- | --- |
| 1. Backend already running | Manual smoke + startup diagnostics | App reaches ready state without spawning duplicate backend; startup overlay clears | `docs/reports/verification/` + `%LOCALAPPDATA%/VoiceStudio/crashes/startup_diagnostics.json` |
| 2. Backend not running | Icon-launch smoke (`--icon-launch-smoke`) | Backend transitions to ready; post-ready backend actions pass | `%LOCALAPPDATA%/VoiceStudio/crashes/icon_launch_smoke_summary.json` (copied into verification report) |
| 3. Backend startup failure | Failure smoke (`--smoke-failure-port` / `--smoke-failure-runtime`) | `BackendFailed` state with explicit actionable failure message and retry surfaced | `%LOCALAPPDATA%/VoiceStudio/crashes/failure_smoke_summary.json` / `failure_runtime_smoke_summary.json` |
| 4. Port/process conflict | Deterministic conflict setup + failure smoke | Conflict categorized (port collision), no silent hang/retry loop, explicit fail message | Verification report + crash summary json + captured port occupancy evidence |
| 5. Repeat launch | Manual/system smoke (launch twice) | Second app instance exits; no duplicate backend spawn side effects | Verification report with process/port snapshots and startup diagnostics |

---

## 6. Documentation Overstatement Check

- **No major overstatement found** in startup docs reviewed for this lane.
- Existing startup-hardening behavior is real in code, but closure-grade evidence for all five lane scenarios is not yet fully assembled in one governed proof set.
- Therefore, lane status remains correctly **planning/baseline complete, implementation pending**.

---

## 7. Baseline Verdict

- **Verdict:** Baseline complete; architecture seam is frozen and code-truth map is established.
- **Implementation readiness:** Ready for Slice 1 only (`startup decision seam`), with this proof contract as mandatory acceptance framework.
