# VoiceStudio Integration Patches

**Maintained by**: Integration Engineer
**Phase**: Populated from Phase 4A onward
**Purpose**: Track every `# INTEGRATION-PATCH` comment applied during phased reintegration.
When a phase is applied that the patch targets, the patch must be reverted.

## Active Patches

### PATCH-001: health.py engine_service import (NOT NEEDED)
- **File**: `backend/api/routes/health.py`
- **Status**: No patch applied. `backend.ml.models.engine_service` was confirmed present
  in Phase 1 and the import at line 15 succeeds. No lazy import wrapper was needed.

### PATCH-002: docker_runner.py docker SDK import (NOT NEEDED)
- **File**: `backend/plugins/sandbox/docker_runner.py`
- **Status**: No patch applied. The existing file already contains a `try: import docker`
  guard at line 42-46 with `except ImportError` fallback.

### PATCH-003: pyproject.toml coverage threshold reduced
- **File**: `pyproject.toml`
- **Applied in**: Phase 4A, Step 4A.1
- **Original value**: `fail_under = 95`
- **Patched value**: `fail_under = 0`
- **Revert when**: Phase 6 completes (all engine routes covered).
- **Revert action**: Restore `fail_under = 95`.

## Reverted Patches

_(none yet)_
