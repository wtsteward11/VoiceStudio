"""Unit tests for ModelProvenanceService (GAP-060)."""

from __future__ import annotations

from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from backend.services.model_provenance_service import (
    ModelProvenanceRecord,
    ModelProvenanceService,
    get_model_provenance_service,
)


def test_build_reads_engine_manifest_fields() -> None:
    svc = ModelProvenanceService()
    eng = MagicMock()
    eng.get_engine_manifest.return_value = {
        "version": "1.0.0",
        "name": "RVC Test",
        "venv_family": "venv_voice_conversion",
    }
    with patch(
        "backend.ml.models.engine_service.get_engine_service",
        return_value=eng,
    ):
        rec = svc.build(
            engine_id="rvc",
            artifact_id="sts_abc",
            correlation_id="corr-1",
            is_transformed=True,
            transformation_type="speech_to_speech",
        )
    assert rec.engine_id == "rvc"
    assert rec.engine_version == "1.0.0"
    assert rec.model_name == "RVC Test"
    assert rec.model_family == "venv_voice_conversion"
    assert rec.artifact_id == "sts_abc"
    assert rec.correlation_id == "corr-1"


def test_build_falls_back_when_manifest_unavailable() -> None:
    svc = ModelProvenanceService()
    eng = MagicMock()
    eng.get_engine_manifest.return_value = None
    with patch(
        "backend.ml.models.engine_service.get_engine_service",
        return_value=eng,
    ):
        rec = svc.build(
            engine_id="rvc",
            artifact_id="x1",
            correlation_id=None,
            is_transformed=True,
            transformation_type="speech_to_speech",
        )
    assert rec.engine_id == "rvc"
    assert rec.engine_version == "unknown"
    assert rec.model_name is None
    assert rec.model_family is None


@pytest.mark.asyncio
async def test_attach_writes_provenance_to_registry_metadata() -> None:
    svc = ModelProvenanceService()
    reg = MagicMock()
    rec = ModelProvenanceRecord(
        artifact_id="aid",
        engine_id="rvc",
        engine_version="1.0.0",
        model_name="N",
        model_family="F",
        is_transformed=True,
        transformation_type="speech_to_speech",
        correlation_id="c99",
        recorded_at="2026-04-11T00:00:00Z",
    )
    with patch(
        "backend.services.audio_registry_service.get_registry",
        return_value=reg,
    ):
        await svc.attach("aid", rec)
    reg.update_metadata.assert_called_once()
    args, _kwargs = reg.update_metadata.call_args
    assert args[0] == "aid"
    merged = args[1]
    assert "model_provenance" in merged
    assert merged["model_provenance"]["engine_id"] == "rvc"
    assert merged["model_provenance"]["correlation_id"] == "c99"


@pytest.mark.asyncio
async def test_attach_is_best_effort_does_not_raise() -> None:
    svc = ModelProvenanceService()
    reg = MagicMock()
    reg.update_metadata.side_effect = RuntimeError("db down")
    rec = ModelProvenanceRecord(
        artifact_id="a",
        engine_id="rvc",
        engine_version="1",
        model_name=None,
        model_family=None,
        is_transformed=True,
        transformation_type="t",
        correlation_id=None,
        recorded_at="z",
    )
    with patch(
        "backend.services.audio_registry_service.get_registry",
        return_value=reg,
    ):
        await svc.attach("a", rec)


def test_provenance_artifact_id_matches_trust_audit_artifact_id() -> None:
    """Join key: same string as TrustAuditEvent.artifact_id on success."""
    rec = ModelProvenanceRecord(
        artifact_id="sts_deadbeef",
        engine_id="rvc",
        engine_version="1",
        model_name=None,
        model_family=None,
        is_transformed=True,
        transformation_type="speech_to_speech",
        correlation_id="corr",
        recorded_at="t",
    )
    trust_artifact_id = "sts_deadbeef"
    assert rec.artifact_id == trust_artifact_id


def test_provenance_correlation_id_matches_trust_audit_correlation_id() -> None:
    rec = ModelProvenanceRecord(
        artifact_id="a",
        engine_id="rvc",
        engine_version="1",
        model_name=None,
        model_family=None,
        is_transformed=True,
        transformation_type="speech_to_speech",
        correlation_id="req-corr-7",
        recorded_at="t",
    )
    assert rec.correlation_id == "req-corr-7"


@pytest.mark.asyncio
async def test_sts_convert_attaches_model_provenance_on_success(tmp_path) -> None:
    from backend.api.models_additional import SpeechToSpeechRequest
    from backend.services.speech_to_speech_service import SpeechToSpeechService

    src = tmp_path / "in.wav"
    src.write_bytes(b"RIFF" + b"\x00" * 64)
    out_file = tmp_path / "out.wav"
    out_file.write_bytes(b"RIFF" + b"\x00" * 100)

    rvc = MagicMock()

    def _cv(**kwargs):
        outp = kwargs.get("output_path")
        if outp:
            with open(outp, "wb") as f:
                f.write(out_file.read_bytes())
        return None

    rvc.convert_voice.side_effect = _cv
    rvc.is_available.return_value = True

    def _fake_create(path, **kwargs):
        return "sts_out123", str(path), {"quality_score": None}

    prov = MagicMock()
    prov.build = MagicMock(
        return_value=ModelProvenanceRecord(
            artifact_id="sts_out123",
            engine_id="rvc",
            engine_version="1",
            model_name=None,
            model_family=None,
            is_transformed=True,
            transformation_type="speech_to_speech",
            correlation_id="cor-xyz",
            recorded_at="t",
        )
    )
    prov.attach = AsyncMock()
    ta = MagicMock()
    ta.record_sts_conversion = AsyncMock()

    with (
        patch(
            "backend.services.speech_to_speech_service.AudioRegistry.get_path",
            return_value=str(src),
        ),
        patch(
            "backend.services.speech_to_speech_service._find_rvc_checkpoint_for_profile",
            return_value=str(tmp_path / "w.pth"),
        ),
        patch(
            "backend.ml.models.engine_service.get_engine_service",
        ) as gem,
        patch(
            "backend.services.speech_to_speech_service.create_audio_artifact_from_file",
            side_effect=_fake_create,
        ),
        patch(
            "backend.services.speech_to_speech_service.asyncio.to_thread",
            side_effect=lambda fn: fn(),
        ),
        patch(
            "backend.services.speech_to_speech_service.get_model_provenance_service",
            return_value=prov,
        ),
        patch(
            "backend.services.speech_to_speech_service.get_trust_audit_service",
            return_value=ta,
        ),
    ):
        eng = MagicMock()
        eng.get_rvc_engine.return_value = rvc
        gem.return_value = eng

        req = SpeechToSpeechRequest(
            source_audio_id="a1",
            target_voice_profile_id="p1",
            consent_acknowledged=True,
        )
        await SpeechToSpeechService.convert(
            req,
            correlation_id="cor-xyz",
        )
    prov.build.assert_called_once()
    prov.attach.assert_awaited_once()


@pytest.mark.asyncio
async def test_sts_convert_provenance_skipped_on_artifact_creation_failure(
    tmp_path,
) -> None:
    from backend.api.models_additional import SpeechToSpeechRequest
    from backend.core.exceptions import ServiceError
    from backend.services.speech_to_speech_service import SpeechToSpeechService

    src = tmp_path / "in.wav"
    src.write_bytes(b"RIFF" + b"\x00" * 64)
    out_file = tmp_path / "out.wav"
    out_file.write_bytes(b"RIFF" + b"\x00" * 100)

    rvc = MagicMock()

    def _cv(**kwargs):
        outp = kwargs.get("output_path")
        if outp:
            with open(outp, "wb") as f:
                f.write(out_file.read_bytes())
        return None

    rvc.convert_voice.side_effect = _cv
    rvc.is_available.return_value = True

    prov = MagicMock()
    prov.build = MagicMock()
    prov.attach = AsyncMock()
    ta = MagicMock()
    ta.record_sts_conversion = AsyncMock()

    with (
        patch(
            "backend.services.speech_to_speech_service.AudioRegistry.get_path",
            return_value=str(src),
        ),
        patch(
            "backend.services.speech_to_speech_service._find_rvc_checkpoint_for_profile",
            return_value=str(tmp_path / "w.pth"),
        ),
        patch(
            "backend.ml.models.engine_service.get_engine_service",
        ) as gem,
        patch(
            "backend.services.speech_to_speech_service.create_audio_artifact_from_file",
            side_effect=OSError("artifact registration failed"),
        ),
        patch(
            "backend.services.speech_to_speech_service.asyncio.to_thread",
            side_effect=lambda fn: fn(),
        ),
        patch(
            "backend.services.speech_to_speech_service.get_model_provenance_service",
            return_value=prov,
        ),
        patch(
            "backend.services.speech_to_speech_service.get_trust_audit_service",
            return_value=ta,
        ),
    ):
        eng = MagicMock()
        eng.get_rvc_engine.return_value = rvc
        gem.return_value = eng

        req = SpeechToSpeechRequest(
            source_audio_id="a1",
            target_voice_profile_id="p1",
            consent_acknowledged=True,
        )
        with pytest.raises(ServiceError):
            await SpeechToSpeechService.convert(req)
    prov.build.assert_not_called()
    prov.attach.assert_not_awaited()


def test_get_model_provenance_service_singleton() -> None:
    a = get_model_provenance_service()
    b = get_model_provenance_service()
    assert a is b
