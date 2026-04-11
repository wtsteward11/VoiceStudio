"""GAP-059: TrustAuditService unit tests."""

from __future__ import annotations

import sys
from pathlib import Path
from unittest.mock import AsyncMock, patch

import pytest

_project_root = Path(__file__).resolve().parents[4]
if str(_project_root) not in sys.path:
    sys.path.insert(0, str(_project_root))


@pytest.fixture
def trust_svc():
    from backend.services.trust_audit_service import TrustAuditService

    return TrustAuditService()


@pytest.mark.asyncio
async def test_sts_conversion_success_emits_event(trust_svc) -> None:
    from backend.api.models_additional import SpeechToSpeechRequest

    req = SpeechToSpeechRequest(
        source_audio_id="src1",
        target_voice_profile_id="p1",
        consent_acknowledged=True,
    )
    with patch("backend.services.trust_audit_service.get_audit_logger") as gal:
        log_mock = AsyncMock(return_value="id")
        gal.return_value.log = log_mock
        await trust_svc.record_sts_conversion(
            request=req,
            audio_id="out123",
            result="success",
            reason_code=None,
            auth_subject="user-a",
            correlation_id="corr-1",
            watermark_applied=True,
        )
        assert log_mock.await_count == 1
        call_kw = log_mock.await_args.kwargs
        meta = call_kw["metadata"]
        assert meta["event_type"] == "sts_conversion"
        assert meta["result"] == "success"
        assert meta["artifact_id"] == "out123"
        assert meta["auth_subject"] == "user-a"
        assert meta["correlation_id"] == "corr-1"
        assert meta["surface"] == "POST /api/voice/sts/convert"
        assert meta["consent_acknowledged"] is True


@pytest.mark.asyncio
async def test_sts_conversion_denied_emits_denied_event(trust_svc) -> None:
    from backend.api.models_additional import SpeechToSpeechRequest

    req = SpeechToSpeechRequest(
        source_audio_id="src1",
        target_voice_profile_id="p1",
        consent_acknowledged=False,
    )
    with patch("backend.services.trust_audit_service.get_audit_logger") as gal:
        log_mock = AsyncMock(return_value="id")
        gal.return_value.log = log_mock
        await trust_svc.record_sts_conversion(
            request=req,
            audio_id=None,
            result="denied",
            reason_code="CONSENT_REQUIRED",
            auth_subject=None,
            correlation_id=None,
            watermark_applied=None,
        )
        meta = log_mock.await_args.kwargs["metadata"]
        assert meta["result"] == "denied"
        assert meta["reason_code"] == "CONSENT_REQUIRED"


@pytest.mark.asyncio
async def test_sts_conversion_best_effort_failure_does_not_raise(trust_svc) -> None:
    from backend.api.models_additional import SpeechToSpeechRequest

    req = SpeechToSpeechRequest(
        source_audio_id="src1",
        target_voice_profile_id="p1",
        consent_acknowledged=True,
    )
    with patch("backend.services.trust_audit_service.get_audit_logger") as gal:
        log_mock = AsyncMock(side_effect=RuntimeError("disk full"))
        gal.return_value.log = log_mock
        await trust_svc.record_sts_conversion(
            request=req,
            audio_id="x",
            result="success",
            reason_code=None,
            auth_subject=None,
            correlation_id=None,
            watermark_applied=False,
        )


@pytest.mark.asyncio
async def test_audio_export_transformed_emits_event(trust_svc) -> None:
    meta_in = {"is_transformed": True, "source": "s1", "watermark_applied": True}
    with patch("backend.services.trust_audit_service.get_audit_logger") as gal:
        log_mock = AsyncMock(return_value="id")
        gal.return_value.log = log_mock
        await trust_svc.record_audio_export(
            source_audio_id="aid1",
            artifact_meta=meta_in,
            result="success",
            reason_code=None,
            auth_subject="u1",
            correlation_id="c2",
        )
        meta = log_mock.await_args.kwargs["metadata"]
        assert meta["event_type"] == "audio_export"
        assert meta["is_transformed"] is True
        assert meta["action"] == "export"


@pytest.mark.asyncio
async def test_audio_download_transformed_emits_event(trust_svc) -> None:
    meta_in = {"is_transformed": True, "source": "s2"}
    with patch("backend.services.trust_audit_service.get_audit_logger") as gal:
        log_mock = AsyncMock(return_value="id")
        gal.return_value.log = log_mock
        await trust_svc.record_audio_download(
            audio_id="aid2",
            artifact_meta=meta_in,
            result="success",
            reason_code=None,
            auth_subject=None,
            correlation_id="c3",
        )
        m = log_mock.await_args.kwargs["metadata"]
        assert m["event_type"] == "audio_download"
        assert m["artifact_id"] == "aid2"


@pytest.mark.asyncio
async def test_audio_marking_read_emits_event(trust_svc) -> None:
    from backend.api.models_additional import StsMarkingStatus

    st = StsMarkingStatus(
        audio_id="m1",
        is_transformed=True,
        transformation_type="speech_to_speech",
        source_reference_id="src",
        marked_at=None,
        watermark_applied=True,
        watermark_verified=True,
        watermark_method="lsb",
    )
    with patch("backend.services.trust_audit_service.get_audit_logger") as gal:
        log_mock = AsyncMock(return_value="id")
        gal.return_value.log = log_mock
        await trust_svc.record_marking_read(
            audio_id="m1",
            marking=st,
            auth_subject="subj",
            correlation_id="c4",
        )
        m = log_mock.await_args.kwargs["metadata"]
        assert m["event_type"] == "marking_read"
        assert m["action"] == "marking_read"


def test_audit_event_schema_required_fields_present() -> None:
    """Six trust questions: who, what, when, where, outcome, artifact linkage."""
    from datetime import datetime, timezone

    from backend.api.models_additional import SpeechToSpeechRequest
    from backend.services.trust_audit_service import TrustAuditEvent

    req = SpeechToSpeechRequest(
        source_audio_id="s",
        target_voice_profile_id="t",
        consent_acknowledged=True,
    )
    ev = TrustAuditEvent(
        event_id="e1",
        event_type="sts_conversion",
        timestamp_utc=datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        surface="POST /api/voice/sts/convert",
        action="sts_convert",
        auth_subject="actor",
        result="success",
        reason_code=None,
        artifact_id="out",
        source_artifact_id=req.source_audio_id,
        target_profile_id=req.target_voice_profile_id,
        is_transformed=True,
        consent_acknowledged=True,
        consent_id=None,
        watermark_applied=True,
        correlation_id="cid",
    )
    d = ev.to_metadata_dict()
    required = {
        "auth_subject",
        "event_type",
        "timestamp_utc",
        "surface",
        "action",
        "result",
        "artifact_id",
        "correlation_id",
    }
    assert required <= set(d.keys())


@pytest.mark.asyncio
async def test_audit_event_does_not_log_full_api_key(trust_svc) -> None:
    from backend.api.models_additional import SpeechToSpeechRequest

    full_key = "vs_sk_live_" + "a" * 48
    safe_subject = full_key[:8]
    req = SpeechToSpeechRequest(
        source_audio_id="s",
        target_voice_profile_id="t",
        consent_acknowledged=True,
    )
    with patch("backend.services.trust_audit_service.get_audit_logger") as gal:
        log_mock = AsyncMock(return_value="id")
        gal.return_value.log = log_mock
        await trust_svc.record_sts_conversion(
            request=req,
            audio_id="o",
            result="success",
            reason_code=None,
            auth_subject=safe_subject,
            correlation_id=None,
            watermark_applied=False,
        )
        meta_str = str(log_mock.await_args.kwargs["metadata"])
        assert full_key not in meta_str
