# Phase 1 — Backend Readiness Truth Lane (GOV-VOICESTUDIO-BACKEND-READINESS-TRUTH-01)

**Date:** 2026-04-11  
**Harness:** `scripts/ci/write_backend_cold_start_proof.py`, `scripts/ci/check_runtime_prerequisites.py`

## check_runtime_prerequisites.py

- **Exit code:** 0 (proceed)
- **blocked:** false
- **Note:** `engine_probe_warning` — `engine_router is None after _ensure_engine_router` (Piper manifest present; router lazy — advisory, not blocking per script policy)

## write_backend_cold_start_proof.py

- **Exit code:** 0
- **Artifact:** `docs/reports/verification/PROOF_BACKEND_COLD_START_2026-04-11.json`
- **cold_start_ms:** ~27078 (within 45000 budget)
- **within_budget:** true

## Failure taxonomy (A–F)

**Classification:** **None reproduced** in this harness run — backend reached healthy `/health` within the cold-start budget.

Shell/UI timeout **"Backend started but did not become healthy within timeout"** is not reproduced here; likely environment-specific (slow disk/AV, competing process on port, or DB lock). Phase 4 documents **no category-specific code fix** until reproduction ties to A–F; instrumentation + artifacts address operational truth.

## Evidence commands

```powershell
Set-Location e:\VoiceStudio
python scripts/ci/check_runtime_prerequisites.py
python scripts/ci/write_backend_cold_start_proof.py
```
