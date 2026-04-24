# Bounded Slice 29 — Preflight registry (discovery source)

**Status:** Closed (initial registry)  
**Date:** 2026-04-23  

## Goal

Introduce [`backend/services/preflight_registry.py`](../../backend/services/preflight_registry.py) mapping `engine_id` → `ensure_*` for discovery and tooling; unit tests in [`tests/unit/backend/services/test_preflight_registry.py`](../../tests/unit/backend/services/test_preflight_registry.py).

## Follow-up

Health routes may adopt the registry for uniform try/except in a later slice; probe `elif` chains can migrate incrementally.

## Verification

`python -m pytest tests/unit/backend/services/test_preflight_registry.py -q`
