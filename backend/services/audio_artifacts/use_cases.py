"""
Use-case helpers for creating audio artifacts from routes.

Milestone 4: Single entry point for routes to create artifacts without
manual tempfile + sf.write + AudioRegistry.register.
"""

from __future__ import annotations

import io
from pathlib import Path
from typing import Any

from backend.services.audio_artifacts.store import get_audio_artifact_store


def wav_array_to_bytes(audio: Any, sr: int, *, format: str = "WAV") -> bytes:
    """
    Write audio array to WAV/FLAC/OGG bytes (no file path).

    Use from routes to avoid sf.write(path, ...) compliance violations.
    """
    import soundfile as sf

    buf = io.BytesIO()
    sf.write(buf, audio, sr, format=format)
    return buf.getvalue()


def create_audio_artifact_from_wav_array(
    audio: Any,  # np.ndarray
    sr: int,
    *,
    created_by: str,
    audio_id: str | None = None,
    project_id: str | None = None,
    source: str | None = None,
) -> tuple[str, str, dict]:
    """
    Create an audio artifact from a numpy array (WAV format).

    Writes via AudioArtifactStore (safe path), registers, records provenance.

    Returns:
        (audio_id, cached_path, metadata)
    """
    import soundfile as sf

    buf = io.BytesIO()
    sf.write(buf, audio, sr, format="WAV")
    data = buf.getvalue()

    store = get_audio_artifact_store()
    return store.store_from_bytes(
        data,
        audio_id=audio_id,
        project_id=project_id,
        source=source,
        model_used=created_by,
        write_provenance=True,
    )


def create_audio_artifact_from_file(
    src_path: Path | str,
    *,
    created_by: str,
    audio_id: str | None = None,
    delete_source: bool = False,
    project_id: str | None = None,
    source: str | None = None,
) -> tuple[str, str, dict]:
    """
    Create an audio artifact from an existing file.

    Copies to cache, registers, records provenance. Optionally deletes
    the source file after success (for temp files from engines).

    Returns:
        (audio_id, cached_path, metadata)
    """
    src = Path(src_path)
    store = get_audio_artifact_store()
    aid, cached_path, metadata = store.store_from_file(
        src,
        audio_id=audio_id,
        project_id=project_id,
        source=source,
        model_used=created_by,
        write_provenance=True,
    )
    if delete_source and src.exists():
        try:
            src.unlink(missing_ok=True)
        except OSError:
            pass
    return aid, cached_path, metadata
