# Artifact Spine

The artifact spine provides a single reusable API for creating and resolving audio artifacts in VoiceStudio. It consists of **AudioArtifactStore** and **AudioRegistryDB** (or the legacy **AudioRegistry** facade).

## Components

### AudioArtifactStore

Handles filesystem operations for audio artifacts:

- **write_from_bytes(audio_id, data_bytes, ext, \*, metadata_hint=None)** — Write bytes to a safe location. Uses temp file + atomic rename. Returns `Path`.
- **write_from_path(audio_id, src_path, ext=None, \*, copy=True)** — Copy or move an existing file into the artifact layout. Returns `Path`.
- **delete(audio_id)** — Remove the artifact directory and its contents.

Storage layout: `<artifacts_root>/audio/<audio_id>/<audio_id>.<ext>`

Allowed extensions: `wav`, `mp3`, `flac`, `m4a`, `ogg`.

### AudioRegistryDB

SQLite-backed registry for metadata and path resolution:

- **register(audio_id, path, \*, ext, duration_sec, created_by, ...)** — Register an existing file.
- **create_from_bytes(data_bytes, ext, \*, created_by, ...)** — Write to store and register.
- **create_from_path(src_path, \*, ext, created_by, ...)** — Copy to store and register.
- **resolve_path(audio_id)** — Resolve to file path. Raises `ArtifactNotFoundError` if not found.
- **get(audio_id)** — Get full `AudioArtifact` record.
- **exists(audio_id)** — Check if registered.
- **delete(audio_id)** — Remove from registry (does not delete file; store handles that).
- **list_artifacts(limit, user_id, project_id)** — List with optional filters.

Database: `get_path("data")/audio_registry.db`

## How to Create Artifacts

### Use-Case Helpers (Recommended for Routes)

**No route may write persistent outputs directly.** All audio-producing routes must use the artifact spine via these helpers:

- **create_audio_artifact_from_wav_array(audio, sr, \*, created_by, audio_id=None, ...)** — Convert numpy array to WAV, store, register, record provenance. Returns `(audio_id, cached_path, metadata)`.
- **create_audio_artifact_from_file(src_path, \*, created_by, audio_id=None, delete_source=False, ...)** — Copy existing file to cache, register, record provenance. Use `delete_source=True` for engine temp outputs. Returns `(audio_id, cached_path, metadata)`.

```python
from backend.services.audio_artifacts import (
    create_audio_artifact_from_wav_array,
    create_audio_artifact_from_file,
)

# From numpy array (e.g. processed audio)
audio_id, cached_path, meta = create_audio_artifact_from_wav_array(
    processed_audio, sample_rate, created_by="effects"
)

# From file (e.g. engine output)
audio_id, cached_path, meta = create_audio_artifact_from_file(
    output_path, created_by="rvc", delete_source=True
)
```

### Low-Level API (AudioRegistryDB)

```python
from backend.services.audio_artifacts import AudioRegistryDB, get_audio_artifact_store

# Registry uses global store by default
registry = AudioRegistryDB(store_factory=get_audio_artifact_store)

# Create from bytes
artifact = registry.create_from_bytes(
    data_bytes,
    ext="wav",
    created_by="synthesis",
    user_id="user-1",
)

# Create from existing file
artifact = registry.create_from_path(
    "/path/to/audio.wav",
    created_by="upload",
)

# Resolve path
path = registry.resolve_path(artifact.audio_id)
```

## Route Migration Checklist

- [x] effects.py
- [x] repair.py
- [x] nr.py
- [x] rvc.py
- [x] spatial_audio.py
- [x] quality_pipelines.py
- [x] batch.py
- [x] style_transfer.py
- [x] ensemble.py
- [x] prosody.py
- [x] emotion.py

**Rule:** No route may write persistent outputs directly (tempfile + sf.write + AudioRegistry.register). Use `create_audio_artifact_from_wav_array` or `create_audio_artifact_from_file` instead.

## Why Routes Must Not Write Files Directly

1. **Path safety** — The spine enforces a layout outside the repo. Direct writes risk writing under the repo root (Cursor brick prevention).
2. **Single pipeline** — Trust features (provenance, usage, policy) require one artifact pipeline. Ad-hoc writes bypass that.
3. **Consistency** — All artifacts go through the same path resolution and metadata flow.

## Provenance, Usage, and Policy

Every artifact created through the spine writes provenance and records usage:

- **Provenance** — Sidecar JSON (`.provenance.json`) written via `write_provenance_sidecar`.
- **Usage** — Synthesis minutes recorded via `record_synthesis_minutes` when duration is known.
- **Policy** — Centralized in `backend.services.provenance_policy`. Two modes:
  - **BEST_EFFORT** (default): Provenance/usage failure logs a warning; artifact is still returned.
  - **STRICT**: Provenance/usage failure triggers rollback (registry entry removed, file deleted) and raises.

**Environment variables:**
- `VOICESTUDIO_PROVENANCE_POLICY=strict|best_effort` — Policy mode.
- `VOICESTUDIO_PROVENANCE_STRICT=1` — Alias for strict mode (overrides policy).

Adapters in `backend.services.audio_artifacts.provenance` and `usage` wrap the canonical writers.
