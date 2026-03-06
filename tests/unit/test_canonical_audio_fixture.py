"""Tests for canonical audio fixtures and utilities."""

import pytest


@pytest.mark.canonical_audio
class TestCanonicalAudioFixture:
    """Verify canonical audio fixture and files."""

    def test_full_wav_exists(self, canonical_audio_path):
        """Full canonical WAV exists and is valid."""
        assert canonical_audio_path is not None
        assert canonical_audio_path.exists(), f"Missing: {canonical_audio_path}"
        assert canonical_audio_path.suffix == ".wav"
        assert canonical_audio_path.name == "allan_watts.wav"
        size = canonical_audio_path.stat().st_size
        if size < 100_000_000:  # Full asset may be in payload store; stub is ~1.3MB
            pytest.skip(
                f"Canonical WAV is {size} bytes (expected >100MB). "
                "Full asset may be in VoiceStudioPayloads; run payload restore if needed."
            )

    def test_segment_exists(self, canonical_audio_segment_path):
        """15-second segment exists and is reasonable size."""
        assert canonical_audio_segment_path.exists(), f"Missing: {canonical_audio_segment_path}"
        assert canonical_audio_segment_path.suffix == ".wav"
        size = canonical_audio_segment_path.stat().st_size
        assert 500_000 < size < 1_000_000  # ~660KB expected

    def test_manifest_loads(self):
        """Manifest can be loaded and has required fields."""
        from tests.fixtures.canonical import get_manifest

        manifest = get_manifest()
        assert "canonical_audio" in manifest
        assert "formats" in manifest["canonical_audio"]
        assert "wav_full" in manifest["canonical_audio"]["formats"]


@pytest.mark.canonical_audio
class TestCanonicalUtilities:
    """Test canonical.py utility functions."""

    def test_get_canonical_wav_path_validates(self):
        """get_canonical_wav_path returns path when file exists (validate=True)."""
        from tests.fixtures.canonical import get_canonical_wav_path

        path = get_canonical_wav_path(validate=True)
        assert path.exists()

    def test_get_duration_from_manifest(self):
        """Duration can be retrieved from manifest."""
        from tests.fixtures.canonical import get_canonical_duration

        duration = get_canonical_duration(segment=False)
        assert 3100 < duration < 3200  # ~52 minutes

        segment_duration = get_canonical_duration(segment=True)
        assert segment_duration == 15.0
