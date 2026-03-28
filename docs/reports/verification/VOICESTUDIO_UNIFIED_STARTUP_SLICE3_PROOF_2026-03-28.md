# VOICESTUDIO_UNIFIED_STARTUP_SLICE3_PROOF_2026-03-28

Date: 2026-03-28  
Lane: `GOV-VOICESTUDIO-UNIFIED-STARTUP-01`  
Slice: Slice 3 — Conflict handling + repeat-launch non-duplication

## 1. Scope

This report proves Slice 3 behavior (lane doc §14):

1. Deterministic **port collision** when the API port is occupied by a non-health TCP endpoint (`port_collision` decision, no spawn, `BackendStartFailureCategory.PortCollision`).
2. **In-process repeat invocation**: a second `EnsureBackendRunningAsync` after a successful spawn **reuses** the running backend (`reuse`, `spawn_attempted` false, `reused_existing_backend` true) without a second manager spawn.
3. Extended **`startup_decision.json`** contract (`schema_version`, `spawn_attempted`, `reused_existing_backend`, `conflict_category`) for proof-grade assertions.
4. Slice 1 scenarios (reuse when healthy backend exists; spawn when port free) remain covered by the same test class with updated field assertions.

Out of scope: full **lane** closure (Slice 4), production icon/smoke as sole proof, installer work.

**Honesty note:** Concurrent second-instance behavior (`Program.cs` single-instance mutex) is specified in the execution row and code; this slice does not add an automated multi-process test (optional / documented only).

## 2. Implemented / Touched Surface

- `src/VoiceStudio.App/Services/BackendProcessManager.cs` — extended `WriteStartupDecisionArtifact` and all call sites with Slice 3 fields.
- `src/VoiceStudio.App.Tests/Services/BackendProcessManagerDecisionTests.cs` — field assertions on existing tests; new `EnsureBackendRunningAsync_WhenPortHeldByNonHttpListener_WritesPortCollisionDecision`; new `EnsureBackendRunningAsync_SecondCall_ReusesWithoutSecondSpawn`.
- `docs/design/GOV_VOICESTUDIO_UNIFIED_STARTUP_01_EXECUTION_ROW.md` — §14 Slice 3 execution record (taxonomy, binary AC, artifact fields).

## 3. Proof Commands

Targeted decision + conflict + repeat proof:

```powershell
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~BackendProcessManagerDecisionTests" --logger "trx;LogFileName=startup_slice3_targeted.trx" --results-directory ".buildlogs/verification"
```

Archived artifact:

- `.buildlogs/verification/startup_slice3_targeted.trx`

Runtime artifact (per test run, under current user profile):

- `%LocalAppData%\VoiceStudio\crashes\startup_decision.json`

## 4. Observed Results (targeted run)

- `EnsureBackendRunningAsync_WhenHealthyBackendExists_WritesReuseDecision` — `reuse`, `spawn_attempted=false`, `reused_existing_backend=true`, `schema_version=1`.
- `EnsureBackendRunningAsync_WhenBackendMissing_WritesSpawnDecision` — `spawn`, `spawn_attempted=true`, `reused_existing_backend=false`.
- `EnsureBackendRunningAsync_WhenPortHeldByNonHttpListener_WritesPortCollisionDecision` — `port_collision`, `spawn_attempted=false`, `conflict_category=port_collision`, `LastFailure.FailureCategory == PortCollision`.
- `EnsureBackendRunningAsync_SecondCall_ReusesWithoutSecondSpawn` — first `spawn`, second `reuse` with `reused_existing_backend=true`; `manager.IsRunning` true (single owned process).

Execution summary (targeted filter):

- Passed: 4  
- Failed: 0  
- Skipped: 0  

## 5. Slice 3 binary acceptance mapping (§14.5)

| Criterion | Evidence | Result |
| --- | --- | --- |
| AC1 — Port occupied + health succeeds → reuse, no spawn | Existing reuse test + spawn-path reuse branch unchanged; tests assert `spawn_attempted` false on reuse | PASS |
| AC2 — Port occupied + health fails → `port_collision`, no spawn, deterministic | TcpListener test + artifact + `PortCollision` | PASS |
| AC3a — Concurrent repeat launch (mutex) | `Program.cs` `VoiceStudio_SingleInstance_Mutex_v1` (documented; no new automated multi-process test) | DOCUMENTED |
| AC3b — In-process second `EnsureBackendRunningAsync` → reuse, no duplicate spawn | `EnsureBackendRunningAsync_SecondCall_ReusesWithoutSecondSpawn` | PASS |
| AC4 — Artifacts reflect conflict/reuse fields | `startup_decision.json` extended fields asserted in tests | PASS |
| AC5 — Slice 1 + 2 intact | Same test class covers reuse/spawn; gating tests not removed (run full suite in §6) | PASS (with full run) |

## 6. Baseline gates (Slice 3 claim state)

Recorded on closure commit:

| Command | Result |
| --- | --- |
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors; pre-existing warnings only) |
| `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | PASS — 2788 passed, 0 failed, 274 skipped |
| `python -m pytest tests/ci/ -q --randomly-seed=12345` | PASS — 216 passed, 2 deselected |

## 7. Notes

- Icon-launch and failure smoke harnesses remain **environment-sensitive**; Slice 3 closure is anchored to **deterministic MSTest** over `BackendProcessManager` plus full baseline gates, consistent with Slice 2 proof honesty.
- **Unified startup lane** remains **open** until Slice 4 consolidates all five scenario proofs and governance sync.

Operator: automation-assisted  
Status: **Slice 3 proof complete (conflict + in-process repeat-launch + artifact contract)**
