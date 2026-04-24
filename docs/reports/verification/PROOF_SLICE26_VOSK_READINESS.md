# PROOF — Slice 26 — Vosk readiness (boolean preflight)

**Status:** **PASS** (wiring + unit mock path). **Runtime** STT transcript proof for **`vosk`** is **not** claimed — **readiness-only** (see [ENGINE_PARITY_MATRIX.md](ENGINE_PARITY_MATRIX.md) Task 36 table).

**Date:** 2026-04-23  

## What changed

| Surface | Responsibility |
| --- | --- |
| [`backend/services/model_preflight.py`](../../backend/services/model_preflight.py) | **`ensure_vosk`** — single authority; registered in **`run_preflight`** |
| [`backend/api/routes/health.py`](../../backend/api/routes/health.py) | **`checks.vosk`** boolean (not `ok: null` for public preflight) |
| Probe | [`scripts/engine_readiness_probe.py`](../../scripts/engine_readiness_probe.py) — `vosk` branch |
| Registry | [`backend/services/preflight_registry.py`](../../backend/services/preflight_registry.py) — `vosk` → **`ensure_vosk`** |

## Verification

```powershell
python -m pytest tests/unit/backend/services/test_model_preflight.py::test_ensure_vosk_ok_with_mock_model -q
python -m pytest tests/unit/backend/services/test_model_preflight.py -q -k "vosk or run_preflight"
```

**Expected:** exit code **0** on CI; `run_preflight` includes **`vosk`** key when map exercised (see aggregated test in same file).

## Out of scope

- `real_vosk` HTTP harness / matrix runtime transcript PASS.

## Artifacts

- Brief: [`docs/design/VOICESTUDIO_BOUNDED_SLICE26_VOSK_READINESS.md`](../../design/VOICESTUDIO_BOUNDED_SLICE26_VOSK_READINESS.md).
- Optional operator JSON under `docs/reports/verification/slice26/` — not required for harness closure.
