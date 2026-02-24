# Voice Route Split Plan (v1.1.0)

**Status**: Planned
**File**: `backend/api/routes/voice.py` (4,340 lines, 16 endpoints)
**Target**: Split into `backend/api/routes/voice/` package with domain-scoped modules

## Proposed Module Structure

```
backend/api/routes/voice/
    __init__.py          # Re-exports combined router from all sub-modules
    synthesis.py         # /synthesize, /synthesize/multipass, /synthesize/style, /synthesize/cross-lingual
    analysis.py          # /analyze, /analyze-characteristics, /test-pronunciation
    cloning.py           # /clone
    processing.py        # /remove-artifacts, /prosody-control, /post-process
    streaming.py         # /synthesize/stream (WebSocket), /streaming/capabilities, /streaming/capabilities/{engine_id}
    testing.py           # /ab-test
    audio.py             # /audio/{audio_id}
```

## Shared Dependencies (extract to voice/_shared.py)

- All FastAPI imports (APIRouter, HTTPException, etc.)
- Engine service dependencies (IEngineService, get_engine_service)
- Pydantic models (all *Request/*Response classes defined inline)
- Helper functions (audio processing, file validation)

## Migration Steps

1. Create `backend/api/routes/voice/` directory
2. Extract Pydantic models to `voice/_models.py`
3. Extract helper functions to `voice/_helpers.py`
4. Split endpoints into domain modules
5. Create `__init__.py` that combines all sub-routers into one router
6. Update `backend/api/main.py` route registration (currently imports `voice` module)
7. Run full test suite to verify no regressions
8. Update any tests that import from `backend.api.routes.voice` directly
