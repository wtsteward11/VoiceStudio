"""
Tests for DAW (REAPER / Audacity) project import integration.

Covers TD-035: DAW Project Import.
Verifies that import_from_daw no longer raises NotImplementedError and returns
valid file paths for supported project formats.
"""

import asyncio
import sqlite3
import struct
import sys
from pathlib import Path

import pytest

_PROJECT_ROOT = Path(__file__).resolve().parents[4]
if str(_PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(_PROJECT_ROOT))

from backend.integrations.external.daw_integration import (
    DAW_EXPORT_PRESETS,
    AudacityIntegration,
    DAWIntegrationManager,
    DAWType,
    ReaperIntegration,
    _parse_aup3_tracks,
    _parse_rpp_tracks,
    get_daw_export_preset_by_id,
    get_daw_export_presets,
)

FIXTURES_DIR = _PROJECT_ROOT / "tests" / "fixtures" / "daw"


# ── REAPER RPP Parser Tests ──


class TestReaperRPPParser:
    """Tests for the RPP track parser."""

    def test_parse_rpp_tracks_extracts_named_tracks(self):
        rpp_path = FIXTURES_DIR / "sample.rpp"
        content = rpp_path.read_text(encoding="utf-8")
        tracks = _parse_rpp_tracks(content, rpp_path.parent)
        assert len(tracks) == 3
        assert tracks[0]["name"] == "Vocals"
        assert tracks[1]["name"] == "Instrumental"
        assert tracks[2]["name"] == "MIDI Only"

    def test_parse_rpp_tracks_resolves_audio_files(self):
        rpp_path = FIXTURES_DIR / "sample.rpp"
        content = rpp_path.read_text(encoding="utf-8")
        tracks = _parse_rpp_tracks(content, rpp_path.parent)
        vocals_files = tracks[0]["audio_files"]
        assert len(vocals_files) >= 1
        assert "vocals.wav" in Path(vocals_files[0]).name

    def test_parse_rpp_tracks_midi_only_has_no_audio(self):
        rpp_path = FIXTURES_DIR / "sample.rpp"
        content = rpp_path.read_text(encoding="utf-8")
        tracks = _parse_rpp_tracks(content, rpp_path.parent)
        assert tracks[2]["audio_files"] == []

    def test_parse_rpp_empty_content(self):
        tracks = _parse_rpp_tracks("", Path("."))
        assert tracks == []


# ── REAPER Import Tests ──


class TestReaperImport:
    """Integration tests for ReaperIntegration.import_from_daw."""

    @pytest.fixture
    def reaper(self):
        return ReaperIntegration()

    def test_open_project_parses_sample_rate_and_tempo(self, reaper):
        rpp_path = FIXTURES_DIR / "sample.rpp"
        project = asyncio.run(reaper.open_project(rpp_path))
        assert project.sample_rate == 48000
        assert project.tempo == 140.0
        assert len(project.tracks) == 3

    def test_import_from_daw_returns_valid_path(self, reaper):
        rpp_path = FIXTURES_DIR / "sample.rpp"
        project = asyncio.run(reaper.open_project(rpp_path))
        audio_path = asyncio.run(reaper.import_from_daw(project, 0))
        assert audio_path.exists()
        assert audio_path.suffix == ".wav"

    def test_import_track_index_out_of_range(self, reaper):
        rpp_path = FIXTURES_DIR / "sample.rpp"
        project = asyncio.run(reaper.open_project(rpp_path))
        with pytest.raises(IndexError, match="out of range"):
            asyncio.run(reaper.import_from_daw(project, 99))

    def test_import_midi_track_raises(self, reaper):
        rpp_path = FIXTURES_DIR / "sample.rpp"
        project = asyncio.run(reaper.open_project(rpp_path))
        with pytest.raises(FileNotFoundError, match="no audio file"):
            asyncio.run(reaper.import_from_daw(project, 2))

    def test_import_empty_tracks_raises(self, reaper):
        rpp_path = FIXTURES_DIR / "sample.rpp"
        project = asyncio.run(reaper.open_project(rpp_path))
        project.tracks = []
        with pytest.raises(FileNotFoundError, match="No tracks found"):
            asyncio.run(reaper.import_from_daw(project, 0))


# ── AUP3 Fixture Helper ──


def _create_minimal_aup3(path: Path, project_name: str = "TestProject") -> None:
    """Create a minimal AUP3 SQLite fixture with wavetracks and sample data."""
    conn = sqlite3.connect(str(path))
    c = conn.cursor()
    c.execute(
        "CREATE TABLE IF NOT EXISTS autosave " "(id INTEGER PRIMARY KEY, dict TEXT, doc TEXT)"
    )
    xml = (
        '<project xmlns="http://audacity.sourceforge.net/xml/" '
        f'projname="{project_name}" version="1.3.0" rate="44100">'
        '<wavetrack name="Voice Recording" channel="1" />'
        '<wavetrack name="Background Music" channel="2" />'
        "</project>"
    )
    c.execute("INSERT INTO autosave (dict, doc) VALUES ('', ?)", (xml,))
    c.execute(
        "CREATE TABLE IF NOT EXISTS sampleblocks "
        "(blockid INTEGER PRIMARY KEY, sampleformat INTEGER, "
        "summin REAL, summax REAL, sumrms REAL, "
        "summary256 BLOB, summary64k BLOB, samples BLOB)"
    )
    samples = struct.pack("<" + "h" * 480, *([0] * 480))
    c.execute(
        "INSERT INTO sampleblocks "
        "(blockid, sampleformat, summin, summax, sumrms, samples) "
        "VALUES (1, 1, 0.0, 0.0, 0.0, ?)",
        (samples,),
    )
    conn.commit()
    conn.close()


# ── Audacity AUP3 Parser Tests ──


class TestAudacityAUP3Parser:
    """Tests for the AUP3 track parser."""

    def test_parse_aup3_tracks(self, tmp_path):
        aup3 = tmp_path / "test.aup3"
        _create_minimal_aup3(aup3)
        tracks, sr = _parse_aup3_tracks(aup3)
        assert len(tracks) == 2
        assert tracks[0]["name"] == "Voice Recording"
        assert sr == 44100


# ── Audacity Import Tests ──


class TestAudacityImport:
    """Integration tests for AudacityIntegration.import_from_daw."""

    @pytest.fixture
    def audacity(self):
        return AudacityIntegration()

    def test_open_project_aup3(self, audacity, tmp_path):
        aup3 = tmp_path / "TestProject.aup3"
        _create_minimal_aup3(aup3)
        project = asyncio.run(audacity.open_project(aup3))
        assert len(project.tracks) == 2
        assert project.sample_rate == 44100

    def test_import_from_daw_aup3_returns_wav(self, audacity, tmp_path):
        aup3 = tmp_path / "TestProject.aup3"
        _create_minimal_aup3(aup3)
        project = asyncio.run(audacity.open_project(aup3))
        audio = asyncio.run(audacity.import_from_daw(project, 0))
        assert audio.exists()
        assert audio.suffix == ".wav"

    def test_import_track_index_out_of_range(self, audacity, tmp_path):
        aup3 = tmp_path / "TestProject.aup3"
        _create_minimal_aup3(aup3)
        project = asyncio.run(audacity.open_project(aup3))
        with pytest.raises(IndexError):
            asyncio.run(audacity.import_from_daw(project, 99))


# ── Manager Tests ──


class TestDAWIntegrationManager:
    """Tests for the DAWIntegrationManager."""

    def test_manager_has_reaper_and_audacity(self):
        manager = DAWIntegrationManager()
        available = manager.get_available_daws()
        assert DAWType.REAPER in available
        assert DAWType.AUDACITY in available


# ── DAW Export Presets (TD-038) ──


class TestDAWExportPresets:
    """Tests for DAW export presets."""

    def test_get_daw_export_presets_returns_all_without_filter(self):
        presets = get_daw_export_presets(daw_type=None)
        assert len(presets) == len(DAW_EXPORT_PRESETS)
        ids = {p["id"] for p in presets}
        assert "reaper_studio" in ids
        assert "audacity_default" in ids

    def test_get_daw_export_presets_filter_by_daw_type(self):
        reaper = get_daw_export_presets(daw_type="reaper")
        assert len(reaper) == 2
        assert all(p["daw_type"] == "reaper" for p in reaper)
        audacity = get_daw_export_presets(daw_type="audacity")
        assert len(audacity) == 2
        assert all(p["daw_type"] == "audacity" for p in audacity)

    def test_get_daw_export_preset_by_id(self):
        p = get_daw_export_preset_by_id("reaper_studio")
        assert p is not None
        assert p["id"] == "reaper_studio"
        assert p["daw_type"] == "reaper"
        assert p["settings"]["sample_rate"] == 48000
        assert p["settings"]["bit_depth"] == 24

    def test_get_daw_export_preset_by_id_unknown_returns_none(self):
        assert get_daw_export_preset_by_id("nonexistent") is None
