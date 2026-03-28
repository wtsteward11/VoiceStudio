# VOICESTUDIO_UNIFIED_STARTUP_SLICE1_PROOF_2026-03-28

Date: 2026-03-28  
Lane: `GOV-VOICESTUDIO-UNIFIED-STARTUP-01`  
Slice: Slice 1 - Startup decision seam (reuse vs controlled-start)

## 1. Scope

This report proves only Slice 1 acceptance criteria:

1. Reuse healthy backend without duplicate spawn.
2. Controlled-start path when backend is unavailable.
3. Startup decision artifact (`startup_decision.json`) records deterministic branch output.

Out of scope: Slice 2 failure UX, Slice 3 conflict hardening, installer/packaging.

## 2. Commands Executed

```powershell
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~EnsureBackendRunningAsync_WhenHealthyBackendExists_WritesReuseDecision"
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~EnsureBackendRunningAsync_WhenBackendMissing_WritesSpawnDecision"
```

Artifact snapshots copied after each scenario:

- `\.buildlogs\verification\startup_slice1_reuse_decision.json`
- `\.buildlogs\verification\startup_slice1_spawn_decision.json`

## 3. Scenario Evidence

### Scenario A - Reuse path (backend already healthy)

Artifact: `\.buildlogs\verification\startup_slice1_reuse_decision.json`

Observed:

- `decision = "reuse"`
- `health_probe_result = true`
- `backend_pid = null`
- `timeout_seconds = 45`

Verdict: **PASS** (reuse branch selected; no spawned PID recorded)

### Scenario B - Controlled-start path (backend unavailable)

Artifact: `\.buildlogs\verification\startup_slice1_spawn_decision.json`

Observed:

- `decision = "spawn"`
- `health_probe_result = false`
- `backend_pid = 24092` (non-null spawned process)
- `timeout_seconds = 45`

Verdict: **PASS** (spawn branch selected and healthy startup completed)

## 4. Acceptance Criteria Check

| Criterion | Evidence | Result |
| --- | --- | --- |
| Healthy backend is reused and not respawned | Reuse artifact shows `decision=reuse` and `backend_pid=null` | PASS |
| Backend unavailable enters controlled-start path | Spawn artifact shows `decision=spawn` and non-null PID | PASS |
| Deterministic startup branch artifact exists | Both scenario artifacts written and archived | PASS |
| Slice 1 timeout policy frozen at 45 seconds | Both artifacts report `timeout_seconds=45` | PASS |

## 5. Notes

- During exploratory app-level smoke execution, app launch did not reliably emit icon/UI smoke summaries in this environment.  
- Slice 1 branch proof was therefore captured with focused integration tests over `BackendProcessManager`, which is the owned seam for this slice and directly emits `startup_decision.json`.

Operator: Codex (automation-assisted)  
Status: **Slice 1 proof complete (scenario A + B)**
