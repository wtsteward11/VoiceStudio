# Phase 1.1: Engine 503 Root Cause Diagnosis

**Date**: 2026-03-07
**Plan**: Architect Hardening Plan

## Summary

Engine 503s occur because `EngineService.list_engines()` returns `[]` when the router's `_engine_types` is empty. The router only populates engines via `load_all_engines()`, which is never called by `EngineService`.

## Root Cause

1. **EngineService** (`backend/services/engine_service.py`):
   - `_ensure_engines_loaded()` imports `router` from `app.core.engines` (the singleton `EngineRouter()`)
   - `list_engines()` returns `self._engine_router.list_engines()` directly
   - **Never calls** `load_all_engines()` on the router

2. **EngineRouter** (`app/core/engines/router.py`):
   - `list_engines()` returns `list(self._engine_types.keys())`
   - `_engine_types` is populated only by `load_all_engines()` or `load_engine_from_manifest()`
   - Without calling `load_all_engines("engines")`, `_engine_types` stays empty → `list_engines()` returns `[]`

3. **Evidence**:
   - `EngineService().list_engines()` → `[]`
   - `router.load_all_engines("engines")` then `router.list_engines()` → 50+ engines

## quality_metrics Bug (Plan-Identified)

- `_shared.quality_metrics` is `None` and never set
- Synthesis branches guarded by `if quality_metrics:` are dead code
- `_helpers._get_quality_metrics()` returns a dict from the engine service and is the live path
- Wire `_shared.quality_metrics` to the actual quality metrics module for consistency

## Manifest Warnings (Non-Blocking)

- `engines/audio/coqui_tts/engine.manifest.json`: missing `engine_id`
- `engines/audio/styletts2/engine.manifest.json`: missing `engine_id`
- These are logged but do not prevent other engines from loading

## Recommended Fix (Phase 1.2)

1. In `EngineService.list_engines()`: when router returns empty, call `load_all_engines("engines")` and retry
2. Add diagnostic logging to `_ensure_engines_loaded()` for model paths and load failures
3. Wire `_shared.quality_metrics` to `app.core.engines.quality_metrics` (or remove dead branches)
