"""Tests for scripts/ci/check_generated_audio_identity_spine.py."""
from __future__ import annotations

import contextlib
import io
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent.parent.parent
sys.path.insert(0, str(ROOT))

from scripts.ci.check_generated_audio_identity_spine import (
    _valid_real_fixture,
    _valid_unknown_fixture,
    main,
    validate_identity_spine,
)


def _write(path: Path, payload: dict) -> Path:
    path.write_text(json.dumps(payload, sort_keys=True, indent=2), encoding="utf-8")
    return path


def _rules(path: Path) -> list[str]:
    return [v.rule for v in validate_identity_spine(path)]


def test_valid_real_engine_identity_spine_passes(tmp_path: Path) -> None:
    assert validate_identity_spine(_write(tmp_path / "proof.json", _valid_real_fixture())) == []


def test_unknown_with_blockers_does_not_require_full_graph(tmp_path: Path) -> None:
    assert validate_identity_spine(_write(tmp_path / "unknown.json", _valid_unknown_fixture())) == []


def test_real_engine_missing_project_id_fails(tmp_path: Path) -> None:
    payload = _valid_real_fixture()
    payload["project"]["project_id"] = None
    assert "MISSING_PROJECT_ID" in _rules(_write(tmp_path / "proof.json", payload))


def test_project_id_blocker_allows_missing_project_id(tmp_path: Path) -> None:
    payload = _valid_real_fixture()
    payload["project"]["project_id"] = None
    payload["blockers"] = ["project_id unavailable from current backend"]
    assert "MISSING_PROJECT_ID" not in _rules(_write(tmp_path / "proof.json", payload))


def test_missing_generated_audio_id_fails(tmp_path: Path) -> None:
    payload = _valid_real_fixture()
    payload["generated_audio"].pop("generated_audio_id")
    payload["generated_audio"].pop("audio_id")
    assert "MISSING_GENERATED_AUDIO_ID" in _rules(_write(tmp_path / "proof.json", payload))


def test_missing_library_link_fails(tmp_path: Path) -> None:
    payload = _valid_real_fixture()
    payload["generated_audio"].pop("library_asset_id")
    payload["library"]["asset_id"] = None
    payload["library"]["audio_id"] = None
    assert "MISSING_LIBRARY_LINK" in _rules(_write(tmp_path / "proof.json", payload))


def test_missing_timeline_link_fails(tmp_path: Path) -> None:
    payload = _valid_real_fixture()
    payload["generated_audio"].pop("timeline_clip_id")
    payload["timeline"]["clip_id"] = None
    payload["timeline"]["track_id"] = None
    assert "MISSING_TIMELINE_LINK" in _rules(_write(tmp_path / "proof.json", payload))


def test_conflicting_session_id_fails(tmp_path: Path) -> None:
    payload = _valid_real_fixture()
    payload["timeline"]["session_id"] = "different-session"
    assert "CONFLICTING_SESSION_ID" in _rules(_write(tmp_path / "proof.json", payload))


def test_duration_mismatch_fails(tmp_path: Path) -> None:
    payload = _valid_real_fixture()
    payload["timeline"]["duration_seconds"] = 12.0
    assert "DURATION_MISMATCH" in _rules(_write(tmp_path / "proof.json", payload))


def test_export_claimed_without_forensics_fails(tmp_path: Path) -> None:
    payload = _valid_real_fixture()
    payload["export"] = {"claimed": True, "path": "C:/tmp/export.wav"}
    assert "MISSING_EXPORT_EVIDENCE" in _rules(_write(tmp_path / "proof.json", payload))


def test_stub_routed_engine_fails(tmp_path: Path) -> None:
    payload = _valid_real_fixture()
    payload["generated_audio"]["routed_engine"] = "stub"
    assert "STUB_ROUTED_ENGINE" in _rules(_write(tmp_path / "proof.json", payload))


def test_missing_artifact_hash_fails(tmp_path: Path) -> None:
    payload = _valid_real_fixture()
    payload["generated_audio"]["artifact_sha256"] = None
    payload["audio_artifact"]["sha256"] = None
    assert "MISSING_ARTIFACT_HASH" in _rules(_write(tmp_path / "proof.json", payload))


def test_cli_json_output_and_self_test_examples(tmp_path: Path) -> None:
    payload = _valid_real_fixture()
    payload["generated_audio"]["routed_engine"] = "mock"
    proof = _write(tmp_path / "proof.json", payload)
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        rc = main(["--proof-json", str(proof), "--json"])
    assert rc == 1
    out = json.loads(buf.getvalue())
    violation = out["violations"][0]
    assert {"file", "rule", "field", "fix"} <= set(violation)
    assert main(["--self-test-examples"]) == 0
