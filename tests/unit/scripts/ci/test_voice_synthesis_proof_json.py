"""Tests for scripts/ci/check_voice_synthesis_proof_json.py."""
from __future__ import annotations

import contextlib
import copy
import io
import json
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent.parent.parent
sys.path.insert(0, str(ROOT))

import scripts.ci.check_voice_synthesis_proof_json as mod
from scripts.ci.check_voice_synthesis_proof_json import (
    _get_changed_files,
    _is_relevant,
    _valid_real_fixture,
    _valid_unknown_fixture,
    main,
    validate_proof_json,
)


def _write(path: Path, payload: dict) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, sort_keys=True, indent=2), encoding="utf-8")
    return path


def _rules(path: Path) -> list[str]:
    return [v.rule for v in validate_proof_json(path)]


def _with_product_closure(payload: dict) -> dict:
    payload["project"] = {
        "project_id": "project-123",
        "project_name": "Proof Project",
        "session_id": "session-123",
        "persistence_scope": "sqlite",
        "reload_verified": True,
    }
    payload["generated_audio"] = {
        "generated_audio_id": "a1",
        "audio_id": "a1",
        "source_engine": "xtts_v2",
        "routed_engine": "xtts_v2",
        "profile_id": "p1",
        "artifact_path": "C:/tmp/a1.wav",
        "artifact_sha256": "a" * 64,
        "duration_seconds": 1.0,
        "library_asset_id": "asset1",
        "timeline_track_id": "trk1",
        "timeline_clip_id": "clip1",
        "provenance": {"source": "voice_synthesis"},
    }
    payload["export"] = {
        "claimed": True,
        "export_id": "export-1",
        "path": "C:/tmp/export.wav",
        "size_bytes": 4096,
        "sha256": "b" * 64,
        "container": "RIFF/WAVE",
        "duration_seconds_from_wav": 1.0,
        "sample_rate_hz": 44100,
        "channels": 1,
        "non_silent": True,
        "blocker": None,
    }
    return payload


def test_valid_real_engine_json_passes(tmp_path: Path) -> None:
    assert validate_proof_json(_write(tmp_path / "proof.json", _valid_real_fixture())) == []


def test_valid_unknown_json_with_blockers_passes(tmp_path: Path) -> None:
    assert validate_proof_json(_write(tmp_path / "unknown.json", _valid_unknown_fixture())) == []


def test_real_engine_routed_stub_fails(tmp_path: Path) -> None:
    payload = _valid_real_fixture()
    payload["routed_engine"] = "stub"
    assert "REAL_ENGINE_STUB_ROUTED" in _rules(_write(tmp_path / "proof.json", payload))


def test_real_engine_missing_audio_artifact_fails(tmp_path: Path) -> None:
    payload = _valid_real_fixture()
    del payload["audio_artifact"]
    violations = validate_proof_json(_write(tmp_path / "proof.json", payload))
    assert any("audio_artifact" in v.field or "audio_artifact" in v.detail for v in violations)


def test_real_engine_missing_library_id_or_audio_id_fails(tmp_path: Path) -> None:
    payload = _valid_real_fixture()
    payload["library"]["asset_id"] = None
    payload["library"]["audio_id"] = None
    assert "REAL_ENGINE_MISSING_LIBRARY" in _rules(_write(tmp_path / "proof.json", payload))


def test_real_engine_missing_timeline_clip_or_track_fails(tmp_path: Path) -> None:
    payload = _valid_real_fixture()
    payload["timeline"]["track_id"] = None
    payload["timeline"]["clip_id"] = None
    assert "REAL_ENGINE_MISSING_TIMELINE" in _rules(_write(tmp_path / "proof.json", payload))


def test_real_engine_json_error_body_fails(tmp_path: Path) -> None:
    payload = _valid_real_fixture()
    payload["audio_artifact"]["not_json_error_body"] = False
    assert "REAL_ENGINE_JSON_ERROR_BODY" in _rules(_write(tmp_path / "proof.json", payload))


def test_unknown_without_blockers_fails(tmp_path: Path) -> None:
    payload = _valid_unknown_fixture()
    payload["blockers"] = []
    assert "UNKNOWN_MISSING_BLOCKERS" in _rules(_write(tmp_path / "proof.json", payload))


def test_stub_engine_claiming_real_synthesis_fails(tmp_path: Path) -> None:
    payload = _valid_unknown_fixture()
    payload["classification"] = "STUB_ENGINE"
    payload["engine_mode_source"] = "test_mode_env"
    payload["verdict"] = "REAL_ENGINE confirmed"
    assert "STUB_MOCK_CLAIMS_REAL" in _rules(_write(tmp_path / "proof.json", payload))


def test_durability_claimed_without_restart_evidence_fails(tmp_path: Path) -> None:
    payload = _valid_real_fixture()
    payload["durability"]["claimed"] = True
    payload["durability"]["restart_performed"] = False
    assert "DURABILITY_CLAIMED_WITHOUT_EVIDENCE" in _rules(_write(tmp_path / "proof.json", payload))


def test_invalid_sha256_fails(tmp_path: Path) -> None:
    payload = _valid_real_fixture()
    payload["audio_artifact"]["sha256"] = "A" * 64
    assert "INVALID_SHA256" in _rules(_write(tmp_path / "proof.json", payload))


def test_changed_file_mode_includes_committed_staged_unstaged_untracked(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    subprocess.run(["git", "init"], cwd=tmp_path, check=True, stdout=subprocess.PIPE)
    proof_dir = tmp_path / "docs" / "reports" / "verification" / "runtime_proofs"
    _write(proof_dir / "base.json", _valid_real_fixture())
    subprocess.run(["git", "add", "."], cwd=tmp_path, check=True)
    subprocess.run(
        ["git", "-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "-m", "base"],
        cwd=tmp_path,
        check=True,
        stdout=subprocess.PIPE,
    )

    _write(proof_dir / "committed.json", _valid_real_fixture())
    subprocess.run(["git", "add", "."], cwd=tmp_path, check=True)
    subprocess.run(
        ["git", "-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "-m", "second"],
        cwd=tmp_path,
        check=True,
        stdout=subprocess.PIPE,
    )

    _write(proof_dir / "staged.json", _valid_real_fixture())
    subprocess.run(["git", "add", str(proof_dir / "staged.json")], cwd=tmp_path, check=True)
    _write(proof_dir / "unstaged.json", _valid_real_fixture())
    subprocess.run(["git", "add", str(proof_dir / "unstaged.json")], cwd=tmp_path, check=True)
    unstaged = copy.deepcopy(_valid_real_fixture())
    unstaged["verdict"] = "changed after staging"
    _write(proof_dir / "unstaged.json", unstaged)
    _write(proof_dir / "untracked.json", _valid_real_fixture())

    monkeypatch.setattr(mod, "ROOT", tmp_path)
    files = _get_changed_files("HEAD~1")
    names = {p.name for p in files}
    assert {"committed.json", "staged.json", "unstaged.json", "untracked.json"} <= names


def test_unrelated_json_files_are_ignored() -> None:
    assert not _is_relevant(Path("docs/reports/verification/not_runtime.json"))
    assert _is_relevant(Path("docs/reports/verification/runtime_proofs/proof.json"))


def test_json_output_contains_file_rule_field_and_fix(tmp_path: Path) -> None:
    payload = _valid_real_fixture()
    payload["routed_engine"] = "stub"
    proof = _write(tmp_path / "proof.json", payload)
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        rc = main(["--path", str(proof), "--json"])
    assert rc == 1
    out = json.loads(buf.getvalue())
    violation = out["violations"][0]
    assert {"file", "rule", "field", "fix"} <= set(violation)


def test_product_closure_mode_requires_project_generated_audio_and_export(tmp_path: Path) -> None:
    proof = _write(tmp_path / "proof.json", _valid_real_fixture())
    rules = [v.rule for v in validate_proof_json(proof, product_closure=True)]
    assert "PRODUCT_CLOSURE_MISSING_PROJECT" in rules
    assert "PRODUCT_CLOSURE_MISSING_GENERATED_AUDIO" in rules
    assert "PRODUCT_CLOSURE_MISSING_EXPORT" in rules
    assert validate_proof_json(proof) == []


def test_valid_product_closure_real_engine_json_passes(tmp_path: Path) -> None:
    proof = _write(tmp_path / "proof.json", _with_product_closure(_valid_real_fixture()))
    assert validate_proof_json(proof, product_closure=True) == []


def test_product_closure_cli_json_output(tmp_path: Path) -> None:
    proof = _write(tmp_path / "proof.json", _valid_real_fixture())
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        rc = main(["--path", str(proof), "--product-closure", "--json"])
    assert rc == 1
    out = json.loads(buf.getvalue())
    assert out["status"] == "fail"
    assert any(v["rule"] == "PRODUCT_CLOSURE_MISSING_PROJECT" for v in out["violations"])


def test_self_test_examples_exit_zero() -> None:
    assert main(["--self-test-examples"]) == 0
