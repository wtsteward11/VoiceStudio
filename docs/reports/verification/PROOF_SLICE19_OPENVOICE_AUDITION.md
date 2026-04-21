# PROOF — Bounded Slice 19 (OpenVoice readiness truth)

**Date:** 2026-04-21  
**Scope:** Readiness only — `ensure_openvoice` + `GET /api/health/preflight` → `checks.openvoice` with **boolean** `ok` (never `null` for this engine family). **No** matrix PASS for `openvoice` in this slice.

## Outcome A / B ruling

| Outcome | Condition |
| --- | --- |
| **A (green readiness)** | On a host with `venv_advanced_tts` containing OpenVoice imports **and** valid checkpoint trees under `<models>/openvoice/base_speakers` and `.../converter`, `checks.openvoice.ok == true`. |
| **B (first blocker)** | Otherwise — typical ordered blockers: (1) `venv_advanced_tts_not_created`, (2) import failure in advanced venv, (3) missing / incomplete checkpoint trees (`424`-class `PreflightError` message lists paths). |

**Operator / CI note:** Clean CI images without `runtime/venvs/torch26` (advanced TTS tree) or without local OpenVoice weights will land on **Outcome B** until provisioned — this is **honest readiness**, not a regression of the harness.

## Evidence (automated)

- **Unit:** `tests/unit/backend/services/test_model_preflight.py` — `ensure_openvoice` venv-missing, import-fail, and success-with-layout cases.  
- **Implementation:** `ensure_openvoice` + `checks["openvoice"]` wiring in `backend/api/routes/health.py`; mirror in `backend/ml/models/model_preflight.py` and `backend/services/model_preflight.py`; `scripts/engine_readiness_probe.py` `openvoice` branch.  
- **Regression bar (post-Slice-19 commit):** `python scripts/run_verification.py` **PASS** (`.buildlogs/verification/last_run.json`); `.\scripts\verify.ps1 -Quick` **VERIFICATION PASSED** — [`artifacts/verify/20260420_205102/verification_report.md`](../../../artifacts/verify/20260420_205102/verification_report.md).

## Explicit non-claims

- **No** `real_openvoice` pytest or C# live-backend closure in Slice 19.  
- **No** `ENGINE_PARITY_MATRIX.md` **PASS** for `openvoice` until a future bounded live slice.

## Changelog

| Date | Note |
| --- | --- |
| 2026-04-21 | Initial proof doc for readiness-only slice 19. |
