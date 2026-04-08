# GAP-004 — Single canonical synthesis execution path (lane closure)

**Lane:** `GOV_VOICESTUDIO_GAP004_SINGLE_CANONICAL_SYNTHESIS_PATH_01`  
**Tracker:** [GAP-004](../design/PROFESSIONAL_GAP_TRACKER.md) **Closed**  
**Date:** 2026-04-08

## 1. Scope delivered

- **`SynthesisService`** (`backend/services/synthesis_service.py`): primary `synthesize()` passes `speed`, `pitch`, `stability`, `clarity`, `temperature` into engine kwargs; added **`synthesize_multipass`**, **`synthesize_with_style`**, **`synthesize_cross_lingual`** (demo gates, OpenVoice checks, artifacts, `ServiceError`); temp-file cleanup logs `OSError` on unlink (no empty catch).
- **`backend/api/routes/voice/synthesis.py`**: thin handlers delegating to `SynthesisService`; `ServiceError` → `HTTPException` via `_raise_synthesis_service_error`; routes registered with **`router.add_api_route`** (mypy strict scope: no `untyped-decorator` on `@router.post`).
- **Removed** `backend/services/voice_synthesis_service.py`; **`backend/api/routes/voice/testing.py`** imports `SynthesisService` directly.
- **Tests:** `tests/unit/backend/api/routes/test_synthesis.py` — delegation, `ServiceError` mapping, multipass, import regression (`voice_synthesis_service` absent).

## 2. Behavioral note (intentional)

- Primary `POST /api/voice/synthesize` now runs **`SynthesisService`** consent + demo policy paths that were absent from the old inline route — **stricter / more correct**, not a silent duplicate of the old permissive route.

## 3. Verification matrix (closure)

| Step | Command / artifact | Result |
| --- | --- | --- |
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors; pre-existing nullable warnings only) |
| Pytest CI | `python -m pytest tests/ci/ -q --randomly-seed=12345` | **217** selected PASS (**2** deselected) |
| Synthesis routes | `python -m pytest tests/unit/backend/api/routes/test_synthesis.py -q` | **5** PASS |
| Mypy strict scope | `python -m pytest tests/ci/test_mypy_strict_scope.py -q` | PASS (**31** errors ≤ budget **110**) |
| XAML resources | `python scripts/validate_xaml_resources.py` | PASS |
| Empty catch gate | `python scripts/check_empty_catches.py` | PASS |
| Ledger / guard | `python scripts/run_verification.py` | PASS — `.buildlogs/verification/last_run.json` **20260407-220905** (**completion_guard** PASS) |
| Quick verify | `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260407_220155/` |

## 4. Proof pointers

- Verification JSON: `.buildlogs/verification/last_run.json` — **20260407-220905**
- Quick verify folder: `artifacts/verify/20260407_220155/`
- Closure: this file; execution row: [GOV_VOICESTUDIO_GAP004_SINGLE_CANONICAL_SYNTHESIS_PATH_01_EXECUTION_ROW.md](../design/GOV_VOICESTUDIO_GAP004_SINGLE_CANONICAL_SYNTHESIS_PATH_01_EXECUTION_ROW.md)

## 5. Rollback

Revert GAP-004 commit(s). Restores inline route orchestration + wrapper module if present in parent commit.
