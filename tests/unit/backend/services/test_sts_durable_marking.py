"""STS durable marking (GAP-056 slice 2): provenance, registry, HTTP surface."""

from __future__ import annotations

import json
from pathlib import Path
from unittest.mock import AsyncMock, MagicMock, patch

from fastapi.testclient import TestClient

from backend.core.audio.conversion import ConversionResult


def test_marking_sidecar_contains_is_transformed(tmp_path: Path) -> None:
    from backend.services.security_service import write_provenance_sidecar

    out = tmp_path / "clip.wav"
    out.write_bytes(b"x")
    write_provenance_sidecar(
        str(out),
        model_used="speech_to_speech",
        is_transformed=True,
        transformation_type="speech_to_speech",
        source_reference_id="src-audio-1",
    )
    sidecar = out.with_suffix(out.suffix + ".provenance.json")
    assert sidecar.is_file()
    payload = json.loads(sidecar.read_text(encoding="utf-8"))
    assert payload.get("is_transformed") is True
    assert payload.get("transformation_type") == "speech_to_speech"
    assert payload.get("source_reference_id") == "src-audio-1"


def test_record_artifact_provenance_passes_transformation_meta(tmp_path: Path) -> None:
    from backend.services.artifact_provenance import record_artifact_provenance_and_usage

    out = tmp_path / "a.wav"
    out.write_bytes(b"RIFF" + b"\x00" * 64)

    with (
        patch("backend.services.security_service.write_provenance_sidecar") as wps,
        patch("backend.services.usage_stats.record_synthesis_minutes"),
    ):
        record_artifact_provenance_and_usage(
            str(out),
            model_used="speech_to_speech",
            duration_seconds=1.0,
            transformation_meta={
                "is_transformed": True,
                "transformation_type": "speech_to_speech",
                "source_reference_id": "src1",
            },
        )
        wps.assert_called_once()
        kwargs = wps.call_args.kwargs
        assert kwargs.get("is_transformed") is True
        assert kwargs.get("transformation_type") == "speech_to_speech"
        assert kwargs.get("source_reference_id") == "src1"


def test_registry_metadata_contains_is_transformed_from_store(tmp_path: Path) -> None:
    from backend.services.audio_artifacts.models import AudioArtifact
    from backend.services.audio_artifacts.store import AudioArtifactStore

    src = tmp_path / "in.wav"
    src.write_bytes(b"RIFF" + b"\x00" * 80)

    art = AudioArtifact(
        audio_id="test_id",
        path=str(src),
        ext="wav",
        duration_sec=1.0,
        created_at="2020-01-01T00:00:00",
        created_by="speech_to_speech",
        metadata={},
    )

    mock_reg = MagicMock()
    mock_reg.create_from_path.return_value = art

    with patch("backend.services.audio_registry_service.get_registry", return_value=mock_reg):
        with patch("backend.services.artifact_provenance.record_artifact_provenance_and_usage"):
            store = AudioArtifactStore(artifacts_root=tmp_path, provenance_writer=None, usage_recorder=None)
            store.store_from_file(
                src,
                audio_id="test_id",
                source="source-a",
                model_used="speech_to_speech",
                is_transformed=True,
                transformation_type="speech_to_speech",
            )

    meta = mock_reg.create_from_path.call_args.kwargs.get("metadata") or {}
    assert meta.get("is_transformed") is True
    assert meta.get("transformation_type") == "speech_to_speech"
    assert meta.get("source") == "source-a"


def test_marking_endpoint_returns_transformed_status() -> None:
    from backend.api.main import app
    from backend.services.audio_artifacts.models import AudioArtifact

    artifact = AudioArtifact(
        audio_id="x1",
        path="/tmp/x.wav",
        ext="wav",
        duration_sec=1.0,
        created_at="2020-01-01",
        created_by="t",
        metadata={
            "is_transformed": True,
            "transformation_type": "speech_to_speech",
            "source": "src1",
        },
    )

    def fake_get(_aid: str):
        return artifact

    with (
        patch(
            "backend.services.audio_registry_service.get_registry",
        ) as gr,
        patch(
            "backend.services.trust_audit_service.get_trust_audit_service",
        ) as gtas,
    ):
        reg = MagicMock()
        reg.get.side_effect = fake_get
        gr.return_value = reg

        tas = MagicMock()
        tas.record_marking_read = AsyncMock()
        gtas.return_value = tas

        client = TestClient(app)
        r = client.get("/api/audio/x1/marking")
        assert r.status_code == 200
        data = r.json()
        assert data["is_transformed"] is True
        assert data["transformation_type"] == "speech_to_speech"
        assert data["source_reference_id"] == "src1"
        tas.record_marking_read.assert_awaited_once()


def test_marking_endpoint_returns_not_transformed_for_plain_artifact() -> None:
    from backend.api.main import app
    from backend.services.audio_artifacts.models import AudioArtifact

    # Distinct id avoids collision with other tests / dev DB rows using "plain".
    audio_id = "plain-no-transform"
    artifact = AudioArtifact(
        audio_id=audio_id,
        path="/tmp/p.wav",
        ext="wav",
        duration_sec=1.0,
        created_at="2020-01-01",
        created_by="t",
        metadata={"source": "abc"},
    )

    with (
        patch(
            "backend.services.audio_registry_service.get_registry",
        ) as gr,
        patch(
            "backend.services.trust_audit_service.get_trust_audit_service",
        ) as gtas,
    ):
        reg = MagicMock()
        reg.get.return_value = artifact
        gr.return_value = reg

        tas = MagicMock()
        tas.record_marking_read = AsyncMock()
        gtas.return_value = tas

        client = TestClient(app)
        r = client.get(f"/api/audio/{audio_id}/marking")
        assert r.status_code == 200
        assert r.json()["is_transformed"] is False
        tas.record_marking_read.assert_awaited_once()


def test_marking_endpoint_source_field_alone_does_not_imply_transformed() -> None:
    """metadata['source'] is lineage reference; it must not set is_transformed."""
    from backend.api.main import app
    from backend.services.audio_artifacts.models import AudioArtifact

    aid = "marking-source-only-1"
    artifact = AudioArtifact(
        audio_id=aid,
        path="/tmp/s.wav",
        ext="wav",
        duration_sec=1.0,
        created_at="2020-01-01",
        created_by="t",
        metadata={"source": "src-ref-123"},
    )

    with (
        patch("backend.services.audio_registry_service.get_registry") as gr,
        patch(
            "backend.services.trust_audit_service.get_trust_audit_service",
        ) as gtas,
    ):
        reg = MagicMock()
        reg.get.return_value = artifact
        gr.return_value = reg
        tas = MagicMock()
        tas.record_marking_read = AsyncMock()
        gtas.return_value = tas

        client = TestClient(app)
        r = client.get(f"/api/audio/{aid}/marking")
        assert r.status_code == 200
        data = r.json()
        assert data["is_transformed"] is False
        assert data["source_reference_id"] == "src-ref-123"
        tas.record_marking_read.assert_awaited_once()


def test_marking_endpoint_watermark_alone_does_not_imply_transformed() -> None:
    """Watermark flags are orthogonal to is_transformed unless metadata says so."""
    from backend.api.main import app
    from backend.api.routes import audio as audio_routes
    from backend.services.audio_artifacts.models import AudioArtifact

    aid = "marking-wm-only-1"
    artifact = AudioArtifact(
        audio_id=aid,
        path="/tmp/w.wav",
        ext="wav",
        duration_sec=1.0,
        created_at="2020-01-01",
        created_by="t",
        metadata={"watermark_applied": True, "watermark_method": "lsb"},
    )

    with (
        patch("backend.services.audio_registry_service.get_registry") as gr,
        patch(
            "backend.services.trust_audit_service.get_trust_audit_service",
        ) as gtas,
        patch.object(audio_routes, "_verify_watermark_on_artifact", return_value=False),
    ):
        reg = MagicMock()
        reg.get.return_value = artifact
        gr.return_value = reg
        tas = MagicMock()
        tas.record_marking_read = AsyncMock()
        gtas.return_value = tas

        client = TestClient(app)
        r = client.get(f"/api/audio/{aid}/marking")
        assert r.status_code == 200
        data = r.json()
        assert data["is_transformed"] is False
        assert data["watermark_applied"] is True
        assert data["watermark_method"] == "lsb"
        assert data["watermark_verified"] is False
        tas.record_marking_read.assert_awaited_once()


def test_marking_endpoint_is_transformed_derived_from_canonical_metadata_only() -> None:
    """Handler reads top-level metadata['is_transformed'], not nested model_provenance only."""
    from backend.api.main import app
    from backend.services.audio_artifacts.models import AudioArtifact

    aid = "marking-canonical-1"
    artifact = AudioArtifact(
        audio_id=aid,
        path="/tmp/c.wav",
        ext="wav",
        duration_sec=1.0,
        created_at="2020-01-01",
        created_by="t",
        metadata={
            "is_transformed": True,
            "transformation_type": "speech_to_speech",
            "source": "src1",
            "model_provenance": {"is_transformed": True},
        },
    )

    with (
        patch("backend.services.audio_registry_service.get_registry") as gr,
        patch(
            "backend.services.trust_audit_service.get_trust_audit_service",
        ) as gtas,
    ):
        reg = MagicMock()
        reg.get.return_value = artifact
        gr.return_value = reg
        tas = MagicMock()
        tas.record_marking_read = AsyncMock()
        gtas.return_value = tas

        client = TestClient(app)
        r = client.get(f"/api/audio/{aid}/marking")
        assert r.status_code == 200
        data = r.json()
        assert data["is_transformed"] is True
        assert data["transformation_type"] == "speech_to_speech"
        tas.record_marking_read.assert_awaited_once()


def test_export_response_includes_transformed_headers(tmp_path: Path) -> None:
    from backend.api.routes import audio as audio_routes

    wav = tmp_path / "reg.wav"
    wav.write_bytes(b"RIFF" + b"\x00" * 120)

    artifact = MagicMock()
    artifact.metadata = {"is_transformed": True, "transformation_type": "speech_to_speech"}

    async def fake_convert(**kwargs):
        outp = Path(kwargs["output_path"])
        outp.write_bytes(b"export-bytes")
        return ConversionResult(success=True, file_size_bytes=len(outp.read_bytes()), error=None)

    mock_engine = MagicMock()
    mock_engine.convert_to_format.side_effect = fake_convert

    with (
        patch.object(audio_routes, "_get_audio_path", return_value=str(wav)),
        patch(
            "backend.core.audio.conversion.get_conversion_service",
            return_value=mock_engine,
        ),
        patch(
            "backend.services.audio_registry_service.get_registry",
        ) as gr,
    ):
        reg = MagicMock()
        reg.get.return_value = artifact
        gr.return_value = reg

        from fastapi.testclient import TestClient

        from backend.api.main import app

        client = TestClient(app)
        response = client.post(
            "/api/audio/export",
            json={
                "source": "sts_abc123",
                "format": "wav",
            },
        )
        assert response.status_code == 200
        assert response.headers.get("X-VoiceStudio-IsTransformed") == "true"
        assert response.headers.get("X-VoiceStudio-TransformationType") == "speech_to_speech"
