# Phase 3.1: mypy Error Audit

**Date**: 2026-03-07
**Scope**: backend/api/routes/voice/, backend/services/synthesis_service.py, backend/services/engine_service.py
**Budget**: 110 (from .ci/mypy_strict_baseline.json)

## Summary by File

| File | Count | Top Error Types |
|------|-------|-----------------|
| backend/services/engine_service.py | ~55 | no-untyped-call (_ensure_engines_loaded), no-any-return, no-untyped-def, name-defined (np), var-annotated (failed), type-arg |
| backend/api/routes/voice/synthesis.py | 13 | attr-defined (_shared exports), untyped-decorator, no-untyped-def, no-untyped-call |
| backend/api/routes/voice/_helpers.py | 8 | no-untyped-def, no-untyped-call, no-any-return, type-arg |
| backend/api/routes/voice/cloning.py | 2 | untyped-decorator, **used-before-def (metrics line 398)** |
| backend/api/routes/voice/analysis.py | 4 | untyped-decorator, no-untyped-def |
| backend/api/routes/voice/streaming.py | 8 | untyped-decorator, var-annotated, has-type |
| backend/api/routes/voice/processing.py | 4 | untyped-decorator, attr-defined |
| backend/api/routes/voice/audio.py | 3 | untyped-decorator, no-untyped-def |
| backend/api/routes/voice/testing.py | 1 | untyped-decorator |

## Error Type Categories

| Type | Count (approx) | Fix Strategy |
|------|----------------|--------------|
| no-untyped-call | 25+ | Add return type to _ensure_engines_loaded, _ensure_engine_router, _get_quality_metrics |
| no-any-return | 20+ | Add explicit casts or narrow return types |
| untyped-decorator | 15+ | Type `@router.post` etc. |
| no-untyped-def | 12+ | Add `-> ReturnType` | 
| attr-defined | 9 | Add __all__ to _shared or fix imports |
| used-before-def | 1 | **Fix cloning.py:398** |

## Critical Path (Plan Priority)

1. **cloning.py:398** — `metrics` used before definition (logic bug)
2. **synthesis.py** — return types, _ensure_engine_router call
3. **_helpers.py** — _ensure_engine_router return type, _get_quality_metrics
4. **analysis.py** — return types
5. **engine_service.py** — _ensure_engines_loaded return type, `failed` annotation, `np` import
