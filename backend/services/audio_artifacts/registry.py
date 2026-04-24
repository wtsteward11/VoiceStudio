"""
Audio registry facade: canonical API for audio_id resolution.

M9: Delegates to audio_registry_service (AudioRegistryDB). No legacy imports.
"""

from __future__ import annotations


def _audio_registry_service():
    """Lazy import avoids circular init (``audio_artifacts`` package ↔ service spine)."""
    from backend.services import audio_registry_service as _svc

    return _svc


def get_audio_registry_facade():
    """Return the canonical AudioRegistryDB instance."""
    return _audio_registry_service().get_registry()


class AudioRegistry:
    """Canonical API for audio_id resolution and registration."""

    @staticmethod
    def get_path(audio_id: str) -> str | None:
        """Resolve audio_id to file path. Returns None if not found."""
        svc = _audio_registry_service()
        result = svc.resolve_audio_path(audio_id)
        return str(result) if result is not None else None

    @staticmethod
    def exists(audio_id: str) -> bool:
        """Check if audio_id is registered."""
        return _audio_registry_service().resolve_audio_path(audio_id) is not None

    @staticmethod
    def count() -> int:
        """Return the number of registered audio IDs."""
        registry = _audio_registry_service().get_registry()
        artifacts = registry.list_artifacts(limit=10000)
        return len(artifacts)

    @staticmethod
    def remove(audio_id: str) -> None:
        """Remove audio_id from registry."""
        _audio_registry_service().remove_audio_id(audio_id)

    @staticmethod
    def items() -> list[tuple[str, str]]:
        """Return list of (audio_id, path) for iteration (e.g. search)."""
        registry = _audio_registry_service().get_registry()
        artifacts = registry.list_artifacts(limit=10000)
        return [(a.audio_id, a.path) for a in artifacts]

    @staticmethod
    def register(
        audio_id: str,
        file_path: str,
        *,
        project_id: str | None = None,
        source: str | None = None,
        model_used: str | None = None,
        duration_seconds: float | None = None,
    ) -> tuple[str, str]:
        """
        Register an audio file. Uses registry_db (artifacts_root layout).

        When model_used is provided, provenance and usage are recorded.

        Returns:
            (path, "") - path to registered file; hash empty.
        """
        from pathlib import Path

        metadata = {"source": source} if source else None
        registry = _audio_registry_service().get_registry()
        artifact = registry.create_from_path(
            Path(file_path),
            audio_id=audio_id,
            project_id=project_id,
            created_by=model_used or "registry",
            metadata=metadata,
        )
        if model_used:
            from backend.services.artifact_provenance import (
                record_artifact_provenance_and_usage,
            )

            record_artifact_provenance_and_usage(
                artifact.path,
                model_used=model_used,
                duration_seconds=duration_seconds,
            )
        return artifact.path, ""
