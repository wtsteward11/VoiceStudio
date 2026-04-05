# Runtime startup smoke — 2026-04-02

## Claim boundary

**Backend runtime verified in this turn** via subprocess cold-start and HTTP health checks.

**WinUI desktop app launch and UI↔backend handshake are not verified in this smoke.** They require an interactive or WinAppDriver session beyond this automated proof. Do not conflate this report with full product runtime certification.

## What was run

| Step | Result |
| --- | --- |
| Subprocess `uvicorn backend.api.main:app` on ephemeral `127.0.0.1` port | PASS |
| First `GET /health` 200 | PASS |
| First `GET /api/health` | PASS |
| Warm `/api/health` samples | PASS |

**Command:** `python scripts/ci/write_backend_cold_start_proof.py`

**Primary artifact:** [PROOF_BACKEND_COLD_START_2026-04-02.json](PROOF_BACKEND_COLD_START_2026-04-02.json)

**Mirror log:** [.buildlogs/verification/startup_smoke_20260402/summary.json](../../../.buildlogs/verification/startup_smoke_20260402/summary.json)

## Measured (from proof JSON)

- `cold_start_ms`: 24360.0  
- `first_api_ms`: 140.0  
- `warm_api_ms`: 143.8  
- `within_budget`: true  

## Honest limits

- No authentication/session matrix exercised.
- No engine subprocess or synthesis workload.
- No `VoiceStudio.App` process start — backend only.

## Next smoke (when required)

- Icon-launch or scripted WinUI smoke with backend discovery, matching `STATE.md` live-revalidation discipline when Overseer requests it.
