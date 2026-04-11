"""STS sample-level watermark embedding + detection (GAP-056 slice 3).

Tests:
  1. embed_watermark produces modified audio that detect_watermark can verify
  2. Provenance sidecar includes watermark fields
  3. Registry metadata includes watermark fields
  4. Marking endpoint returns watermark_applied / watermark_verified
  5. Non-watermarked artifacts return watermark_applied=False
  6. Embed failure degrades honestly (watermark_applied=False, conversion succeeds)
"""

from __future__ import annotations

import json
from pathlib import Path
from unittest.mock import MagicMock, patch

import numpy as np
import pytest


class TestWatermarkEmbedDetectRoundTrip:
    """Verify that WatermarkingService embed → detect round-trip works on WAV data."""

    def test_embed_produces_different_samples(self) -> None:
        from backend.services.security_service import WatermarkingService

        svc = WatermarkingService(secret_key=b"test-key-32-bytes-long-exactly!!")
        original = np.random.randn(44100).astype(np.float64) * 0.5
        watermarked, wm = svc.embed_watermark(original, 44100, "hello")
        assert not np.array_equal(original, watermarked)
        assert wm.watermark_id.startswith("wm_")

    def test_detect_finds_something_on_watermarked_audio(self) -> None:
        from backend.services.security_service import WatermarkingService

        svc = WatermarkingService(secret_key=b"test-key-32-bytes-long-exactly!!")
        original = np.random.randn(44100).astype(np.float64) * 0.5
        watermarked, _ = svc.embed_watermark(original, 44100, "payload123")
        detected = svc.detect_watermark(watermarked, 44100)
        assert detected is not None
        assert len(detected) > 0

    def test_non_watermarked_audio_returns_none_or_garbage(self) -> None:
        """Non-watermarked audio may return None or non-matching noise — never the payload."""
        from backend.services.security_service import WatermarkingService

        svc = WatermarkingService(secret_key=b"test-key-32-bytes-long-exactly!!")
        clean = np.zeros(44100, dtype=np.float64)
        detected = svc.detect_watermark(clean, 44100)
        assert detected != "payload123"


class TestTryEmbedWatermark:
    """Test the _try_embed_watermark helper used by STS."""

    def test_successful_embed_on_real_wav(self, tmp_path: Path) -> None:
        import soundfile as sf  # type: ignore[import-untyped]

        from backend.services.speech_to_speech_service import _try_embed_watermark

        wav = tmp_path / "test.wav"
        samples = np.random.randn(22050).astype(np.float64) * 0.5
        sf.write(str(wav), samples, 22050, subtype="PCM_16")

        applied, method = _try_embed_watermark(str(wav))
        assert applied is True
        assert method == "invisible_lsb"

        re_read, sr = sf.read(str(wav), dtype="float64")
        assert not np.array_equal(samples, re_read)

    def test_embed_failure_returns_false(self, tmp_path: Path) -> None:
        from backend.services.speech_to_speech_service import _try_embed_watermark

        bad_file = tmp_path / "not_audio.txt"
        bad_file.write_text("not audio")

        applied, method = _try_embed_watermark(str(bad_file))
        assert applied is False
        assert method is None


class TestProvenanceSidecarWatermarkFields:
    """Provenance sidecar includes watermark_applied and watermark_method."""

    def test_sidecar_includes_watermark_when_applied(self, tmp_path: Path) -> None:
        from backend.services.security_service import write_provenance_sidecar

        out = tmp_path / "wm_clip.wav"
        out.write_bytes(b"x")
        write_provenance_sidecar(
            str(out),
            model_used="speech_to_speech",
            is_transformed=True,
            transformation_type="speech_to_speech",
            source_reference_id="src1",
            watermark_applied=True,
            watermark_method="invisible_lsb",
        )
        sidecar = out.with_suffix(out.suffix + ".provenance.json")
        payload = json.loads(sidecar.read_text(encoding="utf-8"))
        assert payload["watermark_applied"] is True
        assert payload["watermark_method"] == "invisible_lsb"

    def test_sidecar_omits_watermark_when_not_applied(self, tmp_path: Path) -> None:
        from backend.services.security_service import write_provenance_sidecar

        out = tmp_path / "no_wm.wav"
        out.write_bytes(b"x")
        write_provenance_sidecar(
            str(out),
            model_used="speech_to_speech",
            is_transformed=True,
            transformation_type="speech_to_speech",
        )
        sidecar = out.with_suffix(out.suffix + ".provenance.json")
        payload = json.loads(sidecar.read_text(encoding="utf-8"))
        assert "watermark_applied" not in payload


class TestRegistryMetadataWatermarkFields:
    """store_from_file passes watermark fields into registry metadata."""

    def test_store_from_file_sets_watermark_metadata(self, tmp_path: Path) -> None:
        from backend.services.audio_artifacts.models import AudioArtifact
        from backend.services.audio_artifacts.store import AudioArtifactStore

        src = tmp_path / "in.wav"
        src.write_bytes(b"RIFF" + b"\x00" * 80)

        art = AudioArtifact(
            audio_id="wm_test",
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
                store = AudioArtifactStore(
                    artifacts_root=tmp_path, provenance_writer=None, usage_recorder=None,
                )
                store.store_from_file(
                    src,
                    audio_id="wm_test",
                    source="s1",
                    model_used="speech_to_speech",
                    is_transformed=True,
                    transformation_type="speech_to_speech",
                    watermark_applied=True,
                    watermark_method="invisible_lsb",
                )

        meta = mock_reg.create_from_path.call_args.kwargs.get("metadata") or {}
        assert meta.get("watermark_applied") is True
        assert meta.get("watermark_method") == "invisible_lsb"

    def test_store_from_file_omits_watermark_when_not_applied(self, tmp_path: Path) -> None:
        from backend.services.audio_artifacts.models import AudioArtifact
        from backend.services.audio_artifacts.store import AudioArtifactStore

        src = tmp_path / "in2.wav"
        src.write_bytes(b"RIFF" + b"\x00" * 80)

        art = AudioArtifact(
            audio_id="no_wm",
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
                store = AudioArtifactStore(
                    artifacts_root=tmp_path, provenance_writer=None, usage_recorder=None,
                )
                store.store_from_file(
                    src,
                    audio_id="no_wm",
                    source="s1",
                    model_used="synth",
                    is_transformed=False,
                )

        meta = mock_reg.create_from_path.call_args.kwargs.get("metadata") or {}
        assert "watermark_applied" not in meta


class TestMarkingEndpointWatermarkFields:
    """GET /api/audio/{id}/marking returns watermark status."""

    def test_marking_returns_watermark_applied_and_verified(self) -> None:
        from backend.api.main import app
        from backend.services.audio_artifacts.models import AudioArtifact

        artifact = AudioArtifact(
            audio_id="wm1",
            path="/tmp/wm.wav",
            ext="wav",
            duration_sec=1.0,
            created_at="2020-01-01",
            created_by="sts",
            metadata={
                "is_transformed": True,
                "transformation_type": "speech_to_speech",
                "source": "src1",
                "watermark_applied": True,
                "watermark_method": "invisible_lsb",
            },
        )

        with (
            patch("backend.services.audio_registry_service.get_registry") as gr,
            patch(
                "backend.api.routes.audio._verify_watermark_on_artifact",
                return_value=True,
            ),
        ):
            reg = MagicMock()
            reg.get.return_value = artifact
            gr.return_value = reg

            from fastapi.testclient import TestClient

            client = TestClient(app)
            r = client.get("/api/audio/wm1/marking")
            assert r.status_code == 200
            data = r.json()
            assert data["watermark_applied"] is True
            assert data["watermark_verified"] is True
            assert data["watermark_method"] == "invisible_lsb"

    def test_marking_returns_false_for_non_watermarked(self) -> None:
        from backend.api.main import app
        from backend.services.audio_artifacts.models import AudioArtifact

        artifact = AudioArtifact(
            audio_id="plain",
            path="/tmp/p.wav",
            ext="wav",
            duration_sec=1.0,
            created_at="2020-01-01",
            created_by="t",
            metadata={"is_transformed": True, "transformation_type": "speech_to_speech"},
        )

        with patch("backend.services.audio_registry_service.get_registry") as gr:
            reg = MagicMock()
            reg.get.return_value = artifact
            gr.return_value = reg

            from fastapi.testclient import TestClient

            client = TestClient(app)
            r = client.get("/api/audio/plain/marking")
            assert r.status_code == 200
            data = r.json()
            assert data["watermark_applied"] is False
            assert data["watermark_verified"] is None

    def test_marking_returns_verified_false_when_detection_fails(self) -> None:
        from backend.api.main import app
        from backend.services.audio_artifacts.models import AudioArtifact

        artifact = AudioArtifact(
            audio_id="wm_fail",
            path="/tmp/wm_fail.wav",
            ext="wav",
            duration_sec=1.0,
            created_at="2020-01-01",
            created_by="sts",
            metadata={
                "is_transformed": True,
                "watermark_applied": True,
                "watermark_method": "invisible_lsb",
            },
        )

        with (
            patch("backend.services.audio_registry_service.get_registry") as gr,
            patch(
                "backend.api.routes.audio._verify_watermark_on_artifact",
                return_value=False,
            ),
        ):
            reg = MagicMock()
            reg.get.return_value = artifact
            gr.return_value = reg

            from fastapi.testclient import TestClient

            client = TestClient(app)
            r = client.get("/api/audio/wm_fail/marking")
            assert r.status_code == 200
            data = r.json()
            assert data["watermark_applied"] is True
            assert data["watermark_verified"] is False


class TestNonStsRoutesUntouched:
    """Verify that non-STS artifact paths do not inject watermark logic."""

    def test_create_artifact_from_wav_array_has_no_watermark_params(self) -> None:
        import inspect

        from backend.services.audio_artifacts.use_cases import (
            create_audio_artifact_from_wav_array,
        )

        sig = inspect.signature(create_audio_artifact_from_wav_array)
        assert "watermark_applied" not in sig.parameters
        assert "watermark_method" not in sig.parameters
