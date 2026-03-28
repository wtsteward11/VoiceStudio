# GOV-VOICESTUDIO-UNIFIED-STARTUP-01 — Lane Closure Report

Date: 2026-03-28  
Lane: `GOV-VOICESTUDIO-UNIFIED-STARTUP-01`  
Purpose: **Single consolidated five-scenario proof** for lane closure (Slice 4). This document does not replace slice proofs; it indexes them honestly and records closure-grade verification.

## 1. Executive truth (what this closure claims)

- **Claims:** All five acceptance scenarios in the lane execution row ([§5](../../design/GOV_VOICESTUDIO_UNIFIED_STARTUP_01_EXECUTION_ROW.md)) are satisfied **at the proof level documented below** — seam tests, archived JSON/TRX, and **bounded** statements where smoke or manual/code evidence applies.
- **Does not claim:** Full production launch UX stability under all real-world timing; cross-process repeat-launch behavior beyond **code-truth + manual/operator classification** (see §5.2).

## 2. Slice index (evidence sources)

| Slice | Scope | Canonical proof |
| --- | --- | --- |
| 1 | Reuse vs spawn decision seam + `startup_decision.json` | [VOICESTUDIO_UNIFIED_STARTUP_SLICE1_PROOF_2026-03-28.md](VOICESTUDIO_UNIFIED_STARTUP_SLICE1_PROOF_2026-03-28.md) |
| 2 | Startup gating; single startup failure authority vs panel modals | [VOICESTUDIO_UNIFIED_STARTUP_SLICE2_PROOF_2026-03-28.md](VOICESTUDIO_UNIFIED_STARTUP_SLICE2_PROOF_2026-03-28.md) |
| 3 | Port collision + extended artifact + in-process repeat invocation | [VOICESTUDIO_UNIFIED_STARTUP_SLICE3_PROOF_2026-03-28.md](VOICESTUDIO_UNIFIED_STARTUP_SLICE3_PROOF_2026-03-28.md) |

## 3. Five-scenario mapping (closure matrix)

| # | Scenario | Evidence | Proof level | Result |
| --- | --- | --- | --- | --- |
| 1 | Backend already running — reuse, no duplicate spawn | `.buildlogs/verification/startup_slice1_reuse_decision.json` + `EnsureBackendRunningAsync_WhenHealthyBackendExists_WritesReuseDecision` (Slice 1 / `BackendProcessManagerDecisionTests`) | Deterministic MSTest + artifact | **PASS** |
| 2 | Backend not running — controlled start reaches ready | `.buildlogs/verification/startup_slice1_spawn_decision.json` + `EnsureBackendRunningAsync_WhenBackendMissing_WritesSpawnDecision` | Deterministic MSTest + artifact | **PASS** |
| 3 | Backend startup failure — explicit failure surface, actionable path | `StartupRetryCoordinator` + `MainWindow` overlay on `BackendFailed` (code-truth); Slice 2 gating tests; failure smoke env `VOICE_STUDIO_SMOKE_FAILURE_*` / `--smoke-failure-port` in [Program.cs](../../src/VoiceStudio.App/Program.cs) and handling in [App.xaml.cs](../../src/VoiceStudio.App/App.xaml.cs) | Deterministic tests for **gating**; full app failure smoke **environment-sensitive** | **PASS** (at stated level) |
| 4 | Port/process conflict — deterministic, no silent hang | `EnsureBackendRunningAsync_WhenPortHeldByNonHttpListener_WritesPortCollisionDecision` + `startup_decision.json` fields `port_collision`, `spawn_attempted=false`, `conflict_category` | Deterministic MSTest + artifact | **PASS** |
| 5 | Repeat launch — no duplicate backend side effects | **5a In-process:** `EnsureBackendRunningAsync_SecondCall_ReusesWithoutSecondSpawn` (Slice 3). **5b Cross-process:** `Program.cs` single-instance mutex `VoiceStudio_SingleInstance_Mutex_v1` — second process exits before app orchestration ([Program.cs](../../src/VoiceStudio.App/Program.cs) L17–30) | **5a** deterministic test. **5b** code-truth + **manual/operator** classification (no new automated multi-process test in this lane) | **PASS** (5a); **DOCUMENTED** (5b) |

## 4. Scenario 5 — proof honesty (mandatory split)

### 4.1 In-process repeat invocation (proven)

- **Evidence:** [VOICESTUDIO_UNIFIED_STARTUP_SLICE3_PROOF_2026-03-28.md](VOICESTUDIO_UNIFIED_STARTUP_SLICE3_PROOF_2026-03-28.md) §1, §4, §5 (AC3b).
- **Strength:** Deterministic — first call `spawn`, second call `reuse`, `reused_existing_backend=true`, `spawn_attempted=false` on second artifact; `manager.IsRunning` remains true for single owned process.

### 4.2 Cross-process second instance (not equivalent to 4.1)

- **Evidence type:** **Code-truth + manual/operator** — not automated in Slices 1–3.
- **Code:** [Program.cs](../../src/VoiceStudio.App/Program.cs): named mutex; `if (!createdNew) return;` before `MainImpl` / backend orchestration.
- **Closure statement:** A concurrent second **process** does not run a second startup/backend orchestration path in the second process because it exits immediately. This does **not** substitute for a full E2E “two sequential user launches” process trace in this report.
- **Limitation:** No archived automated test proves mutex behavior in CI in this lane; operators may run two rapid launches manually and observe second process exit if required.

## 5. Screenshot-class startup regression cross-check (Slice 2 contract)

**Failure mode:** Mixed startup overlay + panel-level backend connection modal + duplicate retry surfaces.

**Closure check:**

| Check | Evidence | Result |
| --- | --- | --- |
| Modal suppression during `Starting` / `BackendStarting` | `StartupGatingDialogSuppressionTests` + `ErrorDialogService` startup guard (Slice 2 proof §4–§5) | **PASS** (deterministic) |
| No `shown` dialogs during startup window in test harness | Pending startup: `attempts=1`, `suppressed=1`, `shown=0` (Slice 2 proof) | **PASS** |
| Ready state does not incorrectly suppress | Ready-state test (Slice 2 proof) | **PASS** |
| Smoke-level race hook | `App.xaml.cs` icon-launch smoke fails on `startup_modal_dialog_race` when `shown > 0` (Slice 2 proof §2) | **SUPPLEMENT** (environment-sensitive timing) |

**Honesty:** Full icon-launch smoke remains **environment-sensitive** per Slice 2/3 notes. Lane closure for this class of bug is anchored on **deterministic** `StartupGatingDialogSuppressionTests` plus the smoke hook as a secondary signal.

## 6. Mandatory verification (closure claim commit)

Commands (must all PASS for lane closure):

1. `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
2. `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64`
3. `python -m pytest tests/ci/ -q --randomly-seed=12345`
4. `.\scripts\verify.ps1 -Quick`

| Step | Result | Notes / artifact |
| --- | --- | --- |
| dotnet build | **PASS** | 0 errors; pre-existing nullable/async warnings only (same posture as prior slices) |
| dotnet test (full App.Tests) | **PASS** | 2788 passed, 274 skipped, 0 failed (2026-03-28) |
| pytest tests/ci | **PASS** | 216 passed, 2 deselected; `--randomly-seed=12345` (2026-03-28) |
| verify.ps1 -Quick | **PASS** | `artifacts/verify/20260328_004504/verification_report.md`; `.buildlogs/verification/last_run.json` (`all_passed`: true, 2026-03-28) |

**Harness hygiene (closure commit):** `empty_catch_check` required non-empty `catch` bodies in `TestAppServicesHelper.cs` and `BackendProcessManagerDecisionTests.cs` (test-only; `Debug.WriteLine` on unexpected paths).

## 7. Archived artifacts referenced

- `.buildlogs/verification/startup_slice1_reuse_decision.json`
- `.buildlogs/verification/startup_slice1_spawn_decision.json`
- `.buildlogs/verification/startup_slice2_targeted.trx`
- `.buildlogs/verification/startup_slice3_targeted.trx`
- `%LocalAppData%\VoiceStudio\crashes\startup_decision.json` (runtime; per test profile)

## 8. Operator

Automation-assisted; lane closure valid only if §6 all **PASS** and governance surfaces match [GOV_VOICESTUDIO_UNIFIED_STARTUP_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_UNIFIED_STARTUP_01_EXECUTION_ROW.md) §0 + §15, `.cursor/STATE.md`, and `CANONICAL_REGISTRY.md`.

## 9. Lane closure declaration

**GOV-VOICESTUDIO-UNIFIED-STARTUP-01** is **closed** as of **2026-03-28** under the claims and limits in §1–§5: all five scenarios are mapped to evidence at the stated proof levels; cross-process repeat launch remains **documented / code-truth**, not CI-automated multi-process E2E. §6 mandatory verification completed **PASS** on this date; lane doc §0, §15.1, `STATE.md` ACTIVE WINDOW + proof index, and `CANONICAL_REGISTRY.md` are aligned to this report.
