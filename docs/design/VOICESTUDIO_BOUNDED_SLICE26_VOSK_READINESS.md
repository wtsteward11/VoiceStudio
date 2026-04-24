# Bounded Slice 26 — Vosk readiness (boolean preflight)

**Status:** Closed (implementation)  
**Date:** 2026-04-23  

## Goal

`ensure_vosk` in [`backend/services/model_preflight.py`](../../backend/services/model_preflight.py); boolean `checks.vosk` on [`GET /api/health/preflight`](../../backend/api/routes/health.py); probe parity in [`scripts/engine_readiness_probe.py`](../../scripts/engine_readiness_probe.py); frozen `first_blocker` in error dicts when red.

## Verification

`python -m pytest tests/unit/backend/services/test_model_preflight.py::test_ensure_vosk_ok_with_mock_model -q`
