"""
GAP-060: Single authority for structured model provenance on artifact outputs.

Writes under AudioArtifact.metadata['model_provenance'] (registry metadata_json).
Correlates with GAP-059 trust audit via shared artifact_id and correlation_id.
Best-effort: attach failures log a warning and never raise to callers.
"""

from __future__ import annotations

import asyncio
import logging
from dataclasses import asdict, dataclass
from typing import Any

logger = logging.getLogger(__name__)

_model_provenance_service: ModelProvenanceService | None = None


@dataclass
class ModelProvenanceRecord:
    """Structured provenance for joinability with TrustAuditEvent."""

    artifact_id: str
    engine_id: str
    engine_version: str
    model_name: str | None
    model_family: str | None
    is_transformed: bool
    transformation_type: str | None
    correlation_id: str | None
    recorded_at: str

    def to_metadata_dict(self) -> dict[str, Any]:
        """Nested payload for registry metadata_json."""
        return asdict(self)


class ModelProvenanceService:
    """Build and attach model provenance records to registry metadata."""

    def build(
        self,
        *,
        engine_id: str,
        artifact_id: str,
        correlation_id: str | None,
        is_transformed: bool,
        transformation_type: str | None,
    ) -> ModelProvenanceRecord:
        from datetime import datetime, timezone

        engine_version = "unknown"
        model_name: str | None = None
        model_family: str | None = None
        try:
            from backend.ml.models.engine_service import get_engine_service

            svc = get_engine_service()
            manifest = svc.get_engine_manifest(engine_id)
            if manifest:
                raw_ver = manifest.get("version")
                engine_version = str(raw_ver) if raw_ver is not None else "unknown"
                raw_name = manifest.get("name")
                model_name = str(raw_name) if raw_name is not None else None
                raw_fam = manifest.get("venv_family")
                model_family = str(raw_fam) if raw_fam is not None else None
        except Exception as exc:
            logger.debug("Engine manifest lookup failed for %s: %s", engine_id, exc)

        recorded_at = (
            datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
        )
        return ModelProvenanceRecord(
            artifact_id=artifact_id,
            engine_id=engine_id,
            engine_version=engine_version,
            model_name=model_name,
            model_family=model_family,
            is_transformed=is_transformed,
            transformation_type=transformation_type,
            correlation_id=correlation_id,
            recorded_at=recorded_at,
        )

    async def attach(self, audio_id: str, record: ModelProvenanceRecord) -> None:
        """Merge model_provenance into registry metadata (best-effort)."""
        try:
            from backend.services.audio_registry_service import get_registry

            payload = {"model_provenance": record.to_metadata_dict()}

            def _write() -> None:
                get_registry().update_metadata(audio_id, payload)

            await asyncio.to_thread(_write)
        except Exception as exc:
            logger.warning(
                "Model provenance attach failed (non-blocking): %s",
                exc,
                exc_info=True,
            )


def get_model_provenance_service() -> ModelProvenanceService:
    """Singleton for dependency injection and tests."""
    global _model_provenance_service
    if _model_provenance_service is None:
        _model_provenance_service = ModelProvenanceService()
    return _model_provenance_service
