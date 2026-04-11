"""
Audio artifact store: unified write path for audio artifacts.

Backend Spine Migration: All routes that create audio_id must use this store
(or AudioRegistry.register) for consistent path, registry, and provenance.

Milestone 2: write_from_bytes, write_from_path, delete use artifacts layout
<artifacts_root>/audio/<audio_id>/<audio_id>.<ext>.
"""

from __future__ import annotations

import logging
import os
import tempfile
import uuid
from pathlib import Path
from typing import Callable

from backend.config.path_config import get_path
from backend.services.audio_artifacts.errors import InvalidExtensionError

logger = logging.getLogger(__name__)

ALLOWED_EXTENSIONS = ("wav", "mp3", "flac", "m4a", "ogg")


def _get_wav_duration_seconds(path: str) -> float | None:
    """Get duration of WAV file in seconds."""
    try:
        import wave

        with wave.open(path, "rb") as wav_file:
            frames = wav_file.getnframes()
            sample_rate = wav_file.getframerate()
            if sample_rate:
                return frames / float(sample_rate)
    except Exception as e:
        logger.debug("Duration check failed for %s: %s", path, e)
    return None


def _sanitize_ext(ext: str) -> str:
    """Normalize extension to lowercase; raise InvalidExtensionError if not allowed."""
    ext_clean = ext.lower().lstrip(".")
    if ext_clean not in ALLOWED_EXTENSIONS:
        raise InvalidExtensionError(
            f"Extension '{ext}' not allowed. Allowed: {', '.join(ALLOWED_EXTENSIONS)}"
        )
    return ext_clean


class AudioArtifactStore:
    """
    Unified store for audio artifacts: write, cache, register, provenance.

    Milestone 2: artifacts_root override for tests; write_from_bytes,
    write_from_path, delete use explicit path layout.
    """

    def __init__(
        self,
        artifacts_root: Path | None = None,
        provenance_writer: Callable[[str, dict], None] | None = None,
        usage_recorder: Callable[[float | None, str], None] | None = None,
    ) -> None:
        self._artifacts_root = artifacts_root or get_path("artifacts")
        self._provenance_writer = provenance_writer
        self._usage_recorder = usage_recorder

    def _artifact_path(self, audio_id: str, ext: str) -> Path:
        """Path for artifact: <artifacts_root>/audio/<audio_id>/<audio_id>.<ext>."""
        return self._artifacts_root / "audio" / audio_id / f"{audio_id}.{ext}"

    def write_from_bytes(
        self,
        audio_id: str,
        data_bytes: bytes,
        ext: str = "wav",
        *,
        metadata_hint: dict | None = None,
    ) -> Path:
        """
        Write bytes to artifact path. Uses temp file + os.replace for atomicity.

        Returns:
            Path to the written file.
        """
        ext_clean = _sanitize_ext(ext)
        out_path = self._artifact_path(audio_id, ext_clean)
        out_path.parent.mkdir(parents=True, exist_ok=True)

        temp_dir = self._artifacts_root / "audio" / audio_id
        temp_dir.mkdir(parents=True, exist_ok=True)
        fd, tmp_path = tempfile.mkstemp(suffix=f".{ext_clean}", dir=str(temp_dir))
        try:
            with os.fdopen(fd, "wb") as f:
                f.write(data_bytes)
            os.replace(tmp_path, str(out_path))
        except OSError:
            try:
                os.unlink(tmp_path)
            # ALLOWED: bare except - best effort, failure acceptable
            except OSError:
                pass
            raise

        duration = _get_wav_duration_seconds(str(out_path)) if ext_clean == "wav" else None
        if self._provenance_writer and metadata_hint:
            try:
                self._provenance_writer(str(out_path), metadata_hint)
            except Exception as e:
                logger.debug("Provenance write skipped: %s", e)
        if self._usage_recorder:
            try:
                self._usage_recorder(duration, "write_from_bytes")
            except Exception as e:
                logger.debug("Usage record skipped: %s", e)

        return out_path

    def write_from_path(
        self,
        audio_id: str,
        src_path: str | Path,
        ext: str | None = None,
        *,
        copy: bool = True,
    ) -> Path:
        """
        Write from existing file. Infers ext from path if not provided.

        Returns:
            Path to the written file.
        """
        src = Path(src_path)
        if not src.exists():
            raise FileNotFoundError(f"Source file not found: {src_path}")

        ext_clean = ext or src.suffix.lstrip(".")
        if not ext_clean:
            ext_clean = "wav"
        ext_clean = _sanitize_ext(ext_clean)

        out_path = self._artifact_path(audio_id, ext_clean)
        out_path.parent.mkdir(parents=True, exist_ok=True)

        if copy:
            import shutil

            shutil.copy2(src, out_path)
        else:
            os.replace(str(src), str(out_path))

        return out_path

    def delete(self, audio_id: str) -> None:
        """
        Delete artifact directory and contents.

        Removes <artifacts_root>/audio/<audio_id>/ entirely.
        Does not update any registry; caller must remove registry entry.
        """
        dir_path = self._artifacts_root / "audio" / audio_id
        if dir_path.exists() and dir_path.is_dir():
            import shutil

            shutil.rmtree(dir_path, ignore_errors=False)

    def store_from_file(
        self,
        source_path: str | Path,
        *,
        audio_id: str | None = None,
        project_id: str | None = None,
        source: str | None = None,
        model_used: str = "artifact_store",
        write_provenance: bool = True,
        is_transformed: bool = False,
        transformation_type: str | None = None,
        watermark_applied: bool = False,
        watermark_method: str | None = None,
    ) -> tuple[str, str, dict]:
        """
        Store an existing audio file. Uses registry_db (artifacts_root layout).

        Returns:
            (audio_id, path, metadata)
        """
        from backend.services.audio_registry_service import get_registry

        source_path = Path(source_path)
        if not source_path.exists():
            raise FileNotFoundError(f"Audio file not found: {source_path}")

        meta_parts: dict = {}
        if source:
            meta_parts["source"] = source
        if is_transformed:
            meta_parts["is_transformed"] = True
            meta_parts["transformation_type"] = transformation_type
        if watermark_applied:
            meta_parts["watermark_applied"] = True
            meta_parts["watermark_method"] = watermark_method
        metadata_dict = meta_parts if meta_parts else None
        registry = get_registry()
        artifact = registry.create_from_path(
            source_path,
            audio_id=audio_id,
            project_id=project_id,
            created_by=model_used,
            metadata=metadata_dict,
        )

        duration = _get_wav_duration_seconds(artifact.path)
        meta = {"duration": duration, "hash": ""}

        if write_provenance:
            try:
                from backend.services.artifact_provenance import (
                    record_artifact_provenance_and_usage,
                )
                from backend.services.provenance_policy import POLICY, ProvenancePolicy

                transformation_meta = None
                if is_transformed:
                    transformation_meta = {
                        "is_transformed": True,
                        "transformation_type": transformation_type,
                        "source_reference_id": source,
                    }
                    if watermark_applied:
                        transformation_meta["watermark_applied"] = True
                        transformation_meta["watermark_method"] = watermark_method

                record_artifact_provenance_and_usage(
                    artifact.path,
                    model_used=model_used,
                    duration_seconds=duration,
                    transformation_meta=transformation_meta,
                )
            except Exception as e:
                if POLICY == ProvenancePolicy.STRICT:
                    try:
                        registry.delete(artifact.audio_id)
                    except Exception as rb_e:
                        logger.warning("Rollback registry remove failed: %s", rb_e)
                    try:
                        self.delete(artifact.audio_id)
                    except Exception as rb_e:
                        logger.warning("Rollback file delete failed: %s", rb_e)
                    raise
                logger.warning("Provenance write failed (%s): %s", model_used, e)

        return artifact.audio_id, artifact.path, meta

    def store_from_bytes(
        self,
        data: bytes,
        *,
        audio_id: str | None = None,
        project_id: str | None = None,
        source: str | None = None,
        model_used: str = "artifact_store",
        write_provenance: bool = True,
    ) -> tuple[str, str, dict]:
        """
        Store audio from bytes. Writes to temp, then caches and registers.

        Returns:
            (audio_id, cached_path, metadata)
        """
        aid = audio_id or str(uuid.uuid4())
        temp_dir = get_path("temp")
        temp_dir.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile(
            suffix=".wav", dir=str(temp_dir), delete=False
        ) as f:
            f.write(data)
            tmp_path = f.name
        try:
            return self.store_from_file(
                tmp_path,
                audio_id=aid,
                project_id=project_id,
                source=source,
                model_used=model_used,
                write_provenance=write_provenance,
            )
        finally:
            try:
                Path(tmp_path).unlink(missing_ok=True)
            # ALLOWED: bare except - best effort, failure acceptable
            except OSError:
                pass


_store_instance: AudioArtifactStore | None = None


def get_audio_artifact_store() -> AudioArtifactStore:
    """Get global AudioArtifactStore instance."""
    global _store_instance
    if _store_instance is None:
        _store_instance = AudioArtifactStore()
    return _store_instance
