"""
GAP-059: Canonical trust-audit lane for consent and access-sensitive surfaces.

Best-effort structured events persisted via existing AuditLogger JSONL (metadata field).
Failures to write MUST NOT fail the calling request (log warning only).
"""

from __future__ import annotations

import logging
from dataclasses import asdict, dataclass
from typing import Any
from uuid import uuid4

from backend.api.models_additional import SpeechToSpeechRequest, StsMarkingStatus
from backend.services.audit_logger import AuditAction, AuditSeverity, get_audit_logger

logger = logging.getLogger(__name__)


@dataclass
class TrustAuditEvent:
    """Structured trust audit event (answers six trust questions when combined with route context)."""

    event_id: str
    event_type: str
    timestamp_utc: str
    surface: str
    action: str
    auth_subject: str | None
    result: str
    reason_code: str | None
    artifact_id: str | None
    source_artifact_id: str | None
    target_profile_id: str | None
    is_transformed: bool | None
    consent_acknowledged: bool | None
    consent_id: str | None
    watermark_applied: bool | None
    correlation_id: str | None
    user_role: str | None = None

    def to_metadata_dict(self) -> dict[str, Any]:
        """Serialize for AuditEntry.metadata (JSONL-safe)."""
        return asdict(self)


_trust_audit_service: TrustAuditService | None = None


class TrustAuditService:
    """Single authority for trust-lane audit records."""

    async def _emit(self, event: TrustAuditEvent) -> None:
        try:
            audit = get_audit_logger()
            meta = event.to_metadata_dict()
            success = event.result != "failed"
            await audit.log(
                action=AuditAction.EXECUTE,
                entity_type="trust_audit",
                entity_id=event.artifact_id or event.event_id,
                user_id=event.auth_subject,
                metadata=meta,
                success=success,
                severity=AuditSeverity.INFO,
            )
        except Exception as exc:
            logger.warning("Trust audit write failed (non-blocking): %s", exc, exc_info=True)

    async def record_sts_conversion(
        self,
        *,
        request: SpeechToSpeechRequest,
        audio_id: str | None,
        result: str,
        reason_code: str | None,
        auth_subject: str | None,
        correlation_id: str | None,
        watermark_applied: bool | None,
        user_role: str | None = None,
    ) -> None:
        from datetime import datetime, timezone

        event = TrustAuditEvent(
            event_id=uuid4().hex,
            event_type="sts_conversion",
            timestamp_utc=datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
            surface="POST /api/voice/sts/convert",
            action="sts_convert",
            auth_subject=auth_subject,
            result=result,
            reason_code=reason_code,
            artifact_id=audio_id,
            source_artifact_id=request.source_audio_id,
            target_profile_id=request.target_voice_profile_id,
            is_transformed=True if result == "success" else None,
            consent_acknowledged=request.consent_acknowledged,
            consent_id=request.consent_id.strip() if request.consent_id else None,
            watermark_applied=watermark_applied,
            correlation_id=correlation_id,
            user_role=user_role,
        )
        await self._emit(event)

    async def record_audio_export(
        self,
        *,
        source_audio_id: str,
        artifact_meta: dict[str, Any],
        result: str,
        reason_code: str | None,
        auth_subject: str | None,
        correlation_id: str | None,
        user_role: str | None = None,
    ) -> None:
        from datetime import datetime, timezone

        is_tf = bool(artifact_meta.get("is_transformed", False))
        event = TrustAuditEvent(
            event_id=uuid4().hex,
            event_type="audio_export",
            timestamp_utc=datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
            surface="POST /api/audio/export",
            action="export",
            auth_subject=auth_subject,
            result=result,
            reason_code=reason_code,
            artifact_id=source_audio_id,
            source_artifact_id=artifact_meta.get("source") if isinstance(artifact_meta, dict) else None,
            target_profile_id=None,
            is_transformed=is_tf,
            consent_acknowledged=None,
            consent_id=None,
            watermark_applied=artifact_meta.get("watermark_applied") if isinstance(artifact_meta, dict) else None,
            correlation_id=correlation_id,
            user_role=user_role,
        )
        await self._emit(event)

    async def record_audio_download(
        self,
        *,
        audio_id: str,
        artifact_meta: dict[str, Any],
        result: str,
        reason_code: str | None,
        auth_subject: str | None,
        correlation_id: str | None,
    ) -> None:
        from datetime import datetime, timezone

        is_tf = bool(artifact_meta.get("is_transformed", False))
        event = TrustAuditEvent(
            event_id=uuid4().hex,
            event_type="audio_download",
            timestamp_utc=datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
            surface="GET /api/audio/file/{id}",
            action="download",
            auth_subject=auth_subject,
            result=result,
            reason_code=reason_code,
            artifact_id=audio_id,
            source_artifact_id=artifact_meta.get("source") if isinstance(artifact_meta, dict) else None,
            target_profile_id=None,
            is_transformed=is_tf,
            consent_acknowledged=None,
            consent_id=None,
            watermark_applied=artifact_meta.get("watermark_applied") if isinstance(artifact_meta, dict) else None,
            correlation_id=correlation_id,
            user_role=None,
        )
        await self._emit(event)

    async def record_marking_read(
        self,
        *,
        audio_id: str,
        marking: StsMarkingStatus,
        auth_subject: str | None,
        correlation_id: str | None,
    ) -> None:
        from datetime import datetime, timezone

        event = TrustAuditEvent(
            event_id=uuid4().hex,
            event_type="marking_read",
            timestamp_utc=datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
            surface="GET /api/audio/{id}/marking",
            action="marking_read",
            auth_subject=auth_subject,
            result="success",
            reason_code=None,
            artifact_id=audio_id,
            source_artifact_id=marking.source_reference_id,
            target_profile_id=None,
            is_transformed=marking.is_transformed,
            consent_acknowledged=None,
            consent_id=None,
            watermark_applied=marking.watermark_applied,
            correlation_id=correlation_id,
            user_role=None,
        )
        await self._emit(event)


def get_trust_audit_service() -> TrustAuditService:
    """Lazy singleton for TrustAuditService."""
    global _trust_audit_service
    if _trust_audit_service is None:
        _trust_audit_service = TrustAuditService()
    return _trust_audit_service
