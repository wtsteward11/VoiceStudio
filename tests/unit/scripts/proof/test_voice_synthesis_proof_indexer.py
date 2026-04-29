"""Tests for scripts.proof.index_voice_synthesis_proofs."""
from __future__ import annotations

import contextlib
import io
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent.parent.parent
sys.path.insert(0, str(ROOT))

from scripts.ci.check_voice_synthesis_proof_json import _valid_real_fixture, _valid_unknown_fixture
from scripts.proof.index_voice_synthesis_proofs import build_index, main


def _write(path: Path, payload: dict) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, sort_keys=True, indent=2), encoding="utf-8")
    return path


def test_indexes_multiple_proof_json_files(tmp_path: Path) -> None:
    _write(tmp_path / "a.json", _valid_real_fixture())
    _write(tmp_path / "b.json", _valid_unknown_fixture())
    index, rc = build_index(tmp_path)
    assert rc == 0
    assert len(index["proof_files"]) == 2


def test_picks_latest_by_timestamp(tmp_path: Path) -> None:
    old = _valid_real_fixture()
    old["timestamp_utc"] = "2026-04-28T00:00:00Z"
    new = _valid_unknown_fixture()
    new["timestamp_utc"] = "2026-04-29T00:00:00Z"
    _write(tmp_path / "old.json", old)
    _write(tmp_path / "new.json", new)
    index, _ = build_index(tmp_path)
    assert index["latest_proof"]["file"].endswith("new.json")


def test_picks_latest_real_engine(tmp_path: Path) -> None:
    first = _valid_real_fixture()
    first["timestamp_utc"] = "2026-04-28T00:00:00Z"
    second = _valid_real_fixture()
    second["timestamp_utc"] = "2026-04-29T00:00:00Z"
    _write(tmp_path / "first.json", first)
    _write(tmp_path / "second.json", second)
    index, _ = build_index(tmp_path)
    assert index["latest_real_engine"]["file"].endswith("second.json")


def test_counts_classifications(tmp_path: Path) -> None:
    _write(tmp_path / "real.json", _valid_real_fixture())
    _write(tmp_path / "unknown.json", _valid_unknown_fixture())
    index, _ = build_index(tmp_path)
    assert index["counts_by_classification"] == {"REAL_ENGINE": 1, "UNKNOWN": 1}


def test_strict_mode_fails_invalid_json(tmp_path: Path) -> None:
    bad = _valid_real_fixture()
    bad["routed_engine"] = "stub"
    _write(tmp_path / "bad.json", bad)
    index, rc = build_index(tmp_path, strict=True)
    assert rc == 1
    assert index["validation_status"]["valid"] is False


def test_non_strict_mode_records_invalid_json(tmp_path: Path) -> None:
    bad = _valid_real_fixture()
    bad["audio_artifact"]["not_json_error_body"] = False
    _write(tmp_path / "bad.json", bad)
    index, rc = build_index(tmp_path, strict=False)
    assert rc == 0
    assert index["validation_status"]["invalid_files"]


def test_ignores_unrelated_files(tmp_path: Path) -> None:
    (tmp_path / "other.json").write_text(json.dumps({"not": "proof"}), encoding="utf-8")
    index, _ = build_index(tmp_path)
    assert index["proof_files"] == []


def test_writes_deterministic_sorted_output(tmp_path: Path) -> None:
    _write(tmp_path / "b.json", _valid_unknown_fixture())
    _write(tmp_path / "a.json", _valid_real_fixture())
    out = tmp_path / "index.json"
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        rc = main(["--dir", str(tmp_path), "--output", str(out), "--json"])
    assert rc == 0
    assert out.read_text(encoding="utf-8") == json.dumps(json.loads(out.read_text()), sort_keys=True, indent=2) + "\n"
