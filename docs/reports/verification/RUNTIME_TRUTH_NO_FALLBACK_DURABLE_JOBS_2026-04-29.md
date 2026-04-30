# Runtime Truth v1 — No-Fallback and Durable Jobs Report (2026-04-29)

## 1. Executive Summary

Runtime Truth v1 broadens VoiceStudio's product-runtime validation from the prior generated-audio proof bundle to encompass proof freshness, global no-fallback enforcement, training simulation honesty, durable job authority, async durability scanning, engine readiness truth, and verification evidence freshness.

## 2. Global No-Fallback Enforcement

### Engine Service (`backend/services/engine_service.py`)
- **Removed:** `ENGINE_FALLBACK_CHAIN` dict, `_get_fallback_engines()` method.
- **Removed:** Multi-engine try loops in `synthesize()` and `clone_voice()`.
- **Added:** Single-engine attempts with explicit error payloads including `engine_id`, error message, and `degraded: true`.
- **Retained:** Per-engine circuit breakers for failure containment (not cross-engine substitution).

### Voice Helpers (`backend/api/routes/voice/_helpers.py`)
- **Removed:** `_select_engine_with_fallback()` (58 lines) — config-loaded fallback chain.
- **Removed:** `get_config` import.
- No remaining callers of the deleted function.

### Repository Fallbacks
- **`backend/data/repositories/job_repository.py`:** `get_job_repository()` now raises `RuntimeError` on SQLite init failure instead of silently switching to `InMemoryJobRepository`.
- **`backend/data/repositories/library_repository.py`:** In-memory repos renamed as test-only with explicit non-production docstrings.

### Batch Processing (`backend/api/routes/batch.py`)
- **Changed:** `quality_metrics = {}` → `quality_metrics = None` — eliminates empty-dict placeholder metrics.

## 3. Training Simulation Honesty

### Before (policy violation)
`run_training()` caught `ImportError` and silently called `_simulate_training()` — "falling back to simulation."

### After (fail-closed)
`run_training()` now fails the training job with status `"failed"` and an actionable error message when Coqui TTS is not installed. Callers receive an explicit error, not a simulated result masquerading as real training.

### Bare Except Remediation
Two `except Exception: pass` blocks in training progress broadcast were replaced with `except Exception as exc: logger.debug(...)` — non-fatal broadcast failures are now logged.

## 4. Durable Job Authority

### Voice Cloning Wizard Fix (P1)
- **Problem:** `process_wizard` ran `asyncio.create_task(process_voice_cloning())` but only persisted to disk on restart reconciliation — not during normal completion or failure.
- **Fix:** Added `_persist_wizard_job(job)` calls at both completion and failure terminal states.
- **Placeholder Metrics:** `quality_metrics` on analysis failure changed from hardcoded `{mos_score: 4.0, ...}` to `None`.

### Inventory
See `docs/reports/verification/DURABLE_JOB_AUTHORITY_INVENTORY_2026-04-29.md` for full inventory of 13 background work paths.

## 5. Proof Freshness

- **Validator:** `scripts/ci/check_proof_freshness.py` enforces HEAD matching, dirty-tree detection, and historical proof consistency.
- **Policy:** Product-closure proof at `c41bedda` generated with dirty tree; marked with `dirty_proof_policy` field and validated with `--allow-dirty-proof`.

## 6. Async Durability Scanner

- **Scanner:** `scripts/ci/check_async_task_durability.py` detects untracked `asyncio.create_task`, `threading.Thread`, `subprocess.Popen`, `Task.Run`, and fire-and-forget discards.
- **Allowlist:** Infrastructure lifecycle tasks (startup, shutdown, heartbeat, scheduler, engine subprocess management) are explicitly allowed with justification.

## 7. Release Engine Readiness

- **Scanner:** `scripts/ci/check_release_engine_readiness_truth.py` scans engine manifests for release-critical engines (xtts_v2, piper).
- **RHVoice:** Excluded from release scope.
- **ENGINE_PARITY_MATRIX.md:** Not written or mutated.

## 8. Verification Evidence Freshness

- **Validator:** `scripts/ci/check_verification_evidence_freshness.py` checks artifact existence and non-emptiness.

## 9. Non-claims

- This is not a runtime synthesis proof (no live engine invocation for this report).
- This is not a claim that all P1/P2 durable job paths are fixed — only the voice cloning wizard (#2) was remediated.
- This is not a claim that `origin/main` includes these changes (local commit only, no push).
- This is not a claim that all `asyncio.create_task` usage has been remediated — the scanner inventories; the allowlist documents accepted patterns.
- Proof freshness validator passes with `--allow-dirty-proof` due to ambient working tree modifications.
