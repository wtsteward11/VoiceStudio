"""Unit tests for SpeechToSpeechService (GAP-051)."""

from __future__ import annotations

import os
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from backend.api.models_additional import SpeechToSpeechRequest
from backend.core.exceptions import ServiceError
from backend.services.model_provenance_service import ModelProvenanceRecord


@pytest.fixture(autouse=True)
def _patch_trust_audit_for_sts_tests(monkeypatch: pytest.MonkeyPatch) -> None:
    """GAP-059: Trust audit is best-effort; mock to avoid AuditLogger/async coupling in unit tests."""
    m = MagicMock()
    m.record_sts_conversion = AsyncMock()
    monkeypatch.setattr(
        "backend.services.speech_to_speech_service.get_trust_audit_service",
        lambda: m,
    )


@pytest.fixture(autouse=True)
def _patch_model_provenance_for_sts_tests(monkeypatch: pytest.MonkeyPatch) -> None:
    """GAP-060: Model provenance uses engine manifest + registry; mock in unit tests."""

    def _dummy_build(**kwargs: object) -> ModelProvenanceRecord:
        aid = kwargs.get("artifact_id", "x")
        assert isinstance(aid, str)
        eid = kwargs.get("engine_id", "rvc")
        assert isinstance(eid, str)
        corr = kwargs.get("correlation_id")
        assert corr is None or isinstance(corr, str)
        return ModelProvenanceRecord(
            artifact_id=aid,
            engine_id=eid,
            engine_version="0",
            model_name=None,
            model_family=None,
            is_transformed=bool(kwargs.get("is_transformed", True)),
            transformation_type=(
                str(kwargs["transformation_type"])
                if kwargs.get("transformation_type") is not None
                else None
            ),
            correlation_id=corr,
            recorded_at="2020-01-01T00:00:00Z",
        )

    mp = MagicMock()
    mp.build = MagicMock(side_effect=_dummy_build)
    mp.attach = AsyncMock()
    monkeypatch.setattr(
        "backend.services.speech_to_speech_service.get_model_provenance_service",
        lambda: mp,
    )


@pytest.mark.asyncio
async def test_convert_source_missing_raises_404() -> None:
    from backend.services.speech_to_speech_service import SpeechToSpeechService

    with patch(
        "backend.services.speech_to_speech_service.AudioRegistry.get_path",
        return_value=None,
    ):
        req = SpeechToSpeechRequest(
            source_audio_id="missing",
            target_voice_profile_id="p1",
            consent_acknowledged=True,
        )
        with pytest.raises(ServiceError) as ei:
            await SpeechToSpeechService.convert(req)
        assert ei.value.status_code == 404


@pytest.mark.asyncio
async def test_convert_rvc_unavailable_raises_503(tmp_path) -> None:
    from backend.services.speech_to_speech_service import SpeechToSpeechService

    src = tmp_path / "in.wav"
    src.write_bytes(b"RIFF" + b"\x00" * 32)

    with (
        patch(
            "backend.services.speech_to_speech_service.AudioRegistry.get_path",
            return_value=str(src),
        ),
        patch(
            "backend.services.speech_to_speech_service._find_rvc_checkpoint_for_profile",
            return_value=None,
        ),
        patch(
            "backend.ml.models.engine_service.get_engine_service",
        ) as gem,
    ):
        eng = MagicMock()
        eng.get_rvc_engine.return_value = None
        gem.return_value = eng

        req = SpeechToSpeechRequest(
            source_audio_id="a1",
            target_voice_profile_id="p1",
            consent_acknowledged=True,
        )
        with pytest.raises(ServiceError) as ei:
            await SpeechToSpeechService.convert(req)
        assert ei.value.status_code == 503


@pytest.mark.asyncio
async def test_convert_without_consent_raises_400() -> None:
    from backend.services.speech_to_speech_service import SpeechToSpeechService

    req = SpeechToSpeechRequest(
        source_audio_id="any",
        target_voice_profile_id="p1",
        consent_acknowledged=False,
    )
    with pytest.raises(ServiceError) as ei:
        await SpeechToSpeechService.convert(req)
    assert ei.value.status_code == 400
    detail = ei.value.detail
    assert isinstance(detail, dict)
    assert detail.get("code") == "CONSENT_REQUIRED"


@pytest.mark.asyncio
async def test_convert_with_invalid_consent_id_raises_403() -> None:
    from backend.services.speech_to_speech_service import SpeechToSpeechService

    mock_sec = MagicMock()
    mock_sec.consent.get_consent_by_id.return_value = None

    with patch(
        "backend.services.security_service.get_security_service",
        return_value=mock_sec,
    ):
        req = SpeechToSpeechRequest(
            source_audio_id="a1",
            target_voice_profile_id="p1",
            consent_acknowledged=True,
            consent_id="bad-id",
        )
        with pytest.raises(ServiceError) as ei:
            await SpeechToSpeechService.convert(req)
    assert ei.value.status_code == 403
    detail = ei.value.detail
    assert isinstance(detail, dict)
    assert detail.get("code") == "CONSENT_NOT_FOUND"


@pytest.mark.asyncio
async def test_convert_success_registers_artifact(tmp_path) -> None:
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
        return "out_id", str(path), {"quality_score": None}

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
    ):
        eng = MagicMock()
        eng.get_rvc_engine.return_value = rvc
        gem.return_value = eng

        req = SpeechToSpeechRequest(
            source_audio_id="a1",
            target_voice_profile_id="p1",
            consent_acknowledged=True,
        )
        resp = await SpeechToSpeechService.convert(req)
        assert resp.audio_id == "out_id"
        assert "/api/audio/out_id" in resp.audio_url
        assert resp.engine_used == "rvc"


@pytest.mark.asyncio
async def test_convert_returns_is_transformed_true(tmp_path) -> None:
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
        return "out_id", str(path), {"quality_score": None}

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
    ):
        eng = MagicMock()
        eng.get_rvc_engine.return_value = rvc
        gem.return_value = eng

        req = SpeechToSpeechRequest(
            source_audio_id="a1",
            target_voice_profile_id="p1",
            consent_acknowledged=True,
        )
        resp = await SpeechToSpeechService.convert(req)
        assert resp.is_transformed is True
        assert resp.transformation_type == "speech_to_speech"


@pytest.mark.asyncio
async def test_convert_returns_source_audio_id_in_response(tmp_path) -> None:
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
        assert kwargs.get("source") == "src-99"
        return "out_id", str(path), {"quality_score": None}

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
    ):
        eng = MagicMock()
        eng.get_rvc_engine.return_value = rvc
        gem.return_value = eng

        req = SpeechToSpeechRequest(
            source_audio_id="src-99",
            target_voice_profile_id="p1",
            consent_acknowledged=True,
        )
        resp = await SpeechToSpeechService.convert(req)
        assert resp.source_audio_id == "src-99"


@pytest.mark.asyncio
async def test_convert_returns_non_empty_disclosure_text(tmp_path) -> None:
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
        return "out_id", str(path), {"quality_score": None}

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
    ):
        eng = MagicMock()
        eng.get_rvc_engine.return_value = rvc
        gem.return_value = eng

        req = SpeechToSpeechRequest(
            source_audio_id="a1",
            target_voice_profile_id="p1",
            consent_acknowledged=True,
        )
        resp = await SpeechToSpeechService.convert(req)
        assert resp.disclosure_text
        assert len(resp.disclosure_text.strip()) > 0


@pytest.mark.asyncio
async def test_convert_engine_failure_raises_500(tmp_path) -> None:
    from backend.services.speech_to_speech_service import SpeechToSpeechService

    src = tmp_path / "in.wav"
    src.write_bytes(b"RIFF" + b"\x00" * 64)

    rvc = MagicMock()
    rvc.convert_voice.side_effect = RuntimeError("boom")
    rvc.is_available.return_value = True

    with (
        patch(
            "backend.services.speech_to_speech_service.AudioRegistry.get_path",
            return_value=str(src),
        ),
        patch(
            "backend.services.speech_to_speech_service._find_rvc_checkpoint_for_profile",
            return_value=None,
        ),
        patch(
            "backend.ml.models.engine_service.get_engine_service",
        ) as gem,
        patch(
            "backend.services.speech_to_speech_service.asyncio.to_thread",
            side_effect=lambda fn: fn(),
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
        with pytest.raises(ServiceError) as ei:
            await SpeechToSpeechService.convert(req)
        assert ei.value.status_code == 500


@pytest.mark.asyncio
async def test_convert_passes_is_transformed_to_artifact_store(tmp_path) -> None:
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
        assert kwargs.get("is_transformed") is True
        assert kwargs.get("transformation_type") == "speech_to_speech"
        return "out_id", str(path), {"quality_score": None}

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
    ):
        eng = MagicMock()
        eng.get_rvc_engine.return_value = rvc
        gem.return_value = eng

        req = SpeechToSpeechRequest(
            source_audio_id="a1",
            target_voice_profile_id="p1",
            consent_acknowledged=True,
        )
        await SpeechToSpeechService.convert(req)
