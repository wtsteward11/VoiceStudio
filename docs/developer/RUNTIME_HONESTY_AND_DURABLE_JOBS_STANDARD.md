# Runtime Honesty and Durable Jobs Standard

This document defines VoiceStudio's runtime honesty requirements for production code.

## 1. No Silent Fallbacks

**Rule:** No automatic alternate-engine substitution. If the requested engine cannot perform the operation, callers receive an explicit error — never a silent switch to another engine.

**Enforced by:** `scripts/ci/check_runtime_no_fallback_global.py` (static scan) + `.cursor/rules/core/no-fallbacks.mdc` (governance).

**Scanner rules:**
- `PRODUCTION_SILENT_FALLBACK` — engine fallback chains, "using fallback", `"fallback": true`
- `PRODUCTION_FAKE_SUCCESS` — fake/empty/placeholder success responses
- `PRODUCTION_PLACEHOLDER_METRIC` — `quality_metrics = {}`
- `PRODUCTION_SIMULATION_MASQUERADES_REAL` — `real_training_performed: false` claiming completion
- `PRODUCTION_BEST_EFFORT_SUCCESS` — "best effort success" semantics
- `UNCLASSIFIED_FALLBACK_TERM` — "degrade gracefully" / "graceful degradation"

## 2. No Fake Success

**Rule:** Operations that fail must return failure, not empty/placeholder success payloads.

**Examples of violations:**
- Returning `quality_metrics = {}` when metrics are unavailable (use `None`)
- Returning `Success = true` when the operation did not actually succeed
- Returning placeholder MOS/similarity scores when analysis failed

## 3. Simulation Honesty

**Rule:** Simulation must be explicitly labeled and must never masquerade as real training/cloning.

**Requirements:**
- `SIMULATION_STATUS` must differ from `"completed"` (currently: `"simulation_complete"`)
- Simulation jobs must include `simulation_mode: true` and `simulation_reason`
- Real training that fails due to missing dependencies must fail-closed, not silently simulate
- Simulation must not produce exportable model artifacts

## 4. Durable Jobs

**Rule:** Product-facing background work must create a durable record before starting and persist terminal state (completion/failure) to disk.

**Canonical lifecycle:** `backend/services/canonical_job_lifecycle.py`
- `create_job()` → `mark_job_running()` → `complete_job()` or `fail_job()`
- Backed by SQLite via `backend/data/repositories/job_repository.py`

**Recovery:** `backend/services/job_queue_recovery.py` marks `RUNNING`/`PAUSED` jobs as `failed` on restart.

**Required fields:** `job_id`, operation type, status, progress, timestamps, error code/message, artifact refs, `is_simulated`, `engine_mode` (where relevant).

## 5. Failure Persistence

**Rule:** Terminal failure state must be persisted to durable storage, not just held in memory.

**Restart vocabulary:**
- `recovered` — job was interrupted and successfully resumed
- `failed_interrupted` — job was interrupted and could not resume
- `unknown_interrupted` — job state is ambiguous after restart

## 6. Test-Mode Language

**Rule:** Test-mode and simulation branches must be explicitly labeled in code and responses. Never use the same status codes/fields for simulation output as for real output.

## 7. Engine Readiness Truth

**Rule:** Release builds must declare which engines are ready. Readiness is assessed from manifests, not assumed.

**Scanner:** `scripts/ci/check_release_engine_readiness_truth.py`
**Default release engines:** `xtts_v2`, `piper`
**Excluded:** `rhvoice`

## 8. Proof Freshness

**Rule:** Committed proof artifacts must reference the current `git HEAD`. Proofs from dirty trees must be explicitly marked with `--allow-dirty-proof` and documented non-claims.

**Validator:** `scripts/ci/check_proof_freshness.py`
**Historical proofs:** Marked `historical: true` with the correct generation commit SHA.

## 9. Scanner Commands

```bash
# No-fallback global scan
python scripts/ci/check_runtime_no_fallback_global.py [--json]

# Async task durability scan
python scripts/ci/check_async_task_durability.py [--json]

# Release engine readiness
python scripts/ci/check_release_engine_readiness_truth.py [--json] [--output-json PATH] [--output-md PATH]

# Proof freshness
python scripts/ci/check_proof_freshness.py --changed-from origin/main [--allow-dirty-proof] [--json]

# Evidence freshness
python scripts/ci/check_verification_evidence_freshness.py --artifact PATH [--json]

# Self-tests (all validators)
python scripts/ci/check_runtime_no_fallback_global.py --self-test-examples
python scripts/ci/check_async_task_durability.py --self-test-examples
python scripts/ci/check_release_engine_readiness_truth.py --self-test-examples
python scripts/ci/check_proof_freshness.py --self-test-examples
python scripts/ci/check_verification_evidence_freshness.py --self-test-examples
```
