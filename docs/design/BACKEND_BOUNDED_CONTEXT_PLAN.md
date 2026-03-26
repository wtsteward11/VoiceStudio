# Backend Bounded-Context Plan

**Status:** Audit only (no code yet)  
**Date:** 2026-03-21  
**Related:** [FULL_SCOPE_ARCHITECTURE_NEXT_WAVE.md](FULL_SCOPE_ARCHITECTURE_NEXT_WAVE.md) Rank 5, ADR-022 (bounded contexts)

---

## Audit Output

### File Counts (2026-03-21)

| Directory | Python files |
|-----------|--------------|
| `backend/services/` | 132 |
| `backend/domain/` | 28 |

### Directory Layout

```
backend/
├── services/          # ~132 files (flat + subdirs: audio_artifacts/, ml_optimization/)
│   ├── audio_artifacts/
│   ├── ml_optimization/
│   └── *.py (root)
└── domain/            # ~28 files (entities/, events/, repositories/, services/, value_objects/, script/)
    ├── entities/
    ├── events/
    ├── repositories/
    ├── services/
    ├── value_objects/
    └── script/
```

---

## Prioritized Hit List

### Forwarding Theater (thin wrappers)

| Path | Reason |
|------|--------|
| `backend/services/health_facade.py` | Re-exports only; no logic. Routes import from here instead of `app.core.*`. |
| `backend/services/model_facade.py` | Re-exports only; no logic. Routes import from here instead of `app.core.models.*`. |

**Note:** These facades enforce import boundaries (backend vs app.core). They are intentional; "forwarding" is architectural, not accidental. Flag for review: could consolidate or document as canonical boundary layer.

### Stub Services / No-Op Implementations

| Path | Reason |
|------|--------|
| `backend/services/training_broadcaster.py` — `NoOpBroadcaster` | Intentional no-op when WebSocket unavailable. `pass` in `broadcast_training_progress`. |
| `backend/services/plugin_service.py` — `PluginBase`, `EnginePlugin`, `ProcessorPlugin`, etc. | Abstract base classes with `pass` in abstract methods. **Deprecated** (ADR-038); migrate to `app.core.plugins_api`. |
| `backend/services/engine_pool.py` — inner `cleanup()` | Stub cleanup for a nested context; no-op. |
| `backend/services/engine_loader.py` — `cleanup()` | Stub engine cleanup; no-op. |

### Exception Classes (not stubs)

| Path | Reason |
|------|--------|
| `backend/services/circuit_breaker.py` — exception classes | `pass` is standard Python for empty exception body. Not a stub service. |
| `backend/services/plugin_sandbox.py` — exception classes | Same. |

### Exception Handlers (not stubs)

| Path | Reason |
|------|--------|
| Various `except X: pass` | Documented as ALLOWED (best effort, optional dependency). Not stub services. |

---

## Summary

- **Forwarding theater:** 2 re-export facades (health_facade, model_facade). Intentional boundary layer.
- **Stub services:** 4 items (NoOpBroadcaster, deprecated plugin base classes, 2 cleanup stubs). NoOpBroadcaster is intentional; plugin bases are deprecated; cleanup stubs are minimal.
- **No refactor recommendations yet.** Audit + hit list only.

---

## Changelog

| Date       | Change |
|------------|--------|
| 2026-03-21 | Initial audit; file counts; prioritized hit list. |
