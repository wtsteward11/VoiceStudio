"""Tests for scripts/ci/check_proof_freshness.py."""

from __future__ import annotations

import json
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(ROOT))

import scripts.ci.check_proof_freshness as pf
from scripts.ci.check_proof_freshness import (
    changed_proof_json_files,
    current_git_head,
    main,
    validate_proof_freshness,
)


def _write(path: Path, payload: dict) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, sort_keys=True, indent=2), encoding="utf-8")
    return path


def test_stale_head_fails(tmp_path: Path) -> None:
    head = "c" * 40
    p = _write(
        tmp_path / "proof.json",
        {
            "schema_version": "voice_synthesis_proof.v1",
            "git": {"head": "a" * 40, "origin_main": "b" * 40, "dirty_summary": "clean"},
        },
    )
    v = validate_proof_freshness(p, current_head=head, allow_dirty_proof=False)
    assert [x.rule for x in v] == ["STALE_PROOF_HEAD"]


def test_clean_current_passes(tmp_path: Path) -> None:
    head = "d" * 40
    p = _write(
        tmp_path / "proof.json",
        {
            "schema_version": "voice_synthesis_proof.v1",
            "git": {"head": head, "origin_main": "e" * 40, "dirty_summary": "clean"},
        },
    )
    assert validate_proof_freshness(p, current_head=head, allow_dirty_proof=False) == []


def test_dirty_forbidden_by_default(tmp_path: Path) -> None:
    head = "f" * 40
    p = _write(
        tmp_path / "proof.json",
        {
            "schema_version": "voice_synthesis_proof.v1",
            "git": {"head": head, "origin_main": "0" * 40, "dirty_summary": "M foo.txt"},
        },
    )
    v = validate_proof_freshness(p, current_head=head, allow_dirty_proof=False)
    assert [x.rule for x in v] == ["DIRTY_PROOF_NOT_ALLOWED"]


def test_dirty_allowed_flag(tmp_path: Path) -> None:
    head = "f" * 40
    p = _write(
        tmp_path / "proof.json",
        {
            "schema_version": "voice_synthesis_proof.v1",
            "git": {"head": head, "origin_main": "0" * 40, "dirty_summary": "M foo.txt"},
        },
    )
    assert validate_proof_freshness(p, current_head=head, allow_dirty_proof=True) == []


def test_historical_contradiction_fails(tmp_path: Path) -> None:
    head = "g" * 40
    p = _write(
        tmp_path / "proof.json",
        {
            "schema_version": "voice_synthesis_proof.v1",
            "historical": True,
            "git": {"head": head, "origin_main": "0" * 40, "dirty_summary": "clean"},
        },
    )
    v = validate_proof_freshness(p, current_head=head, allow_dirty_proof=False)
    assert [x.rule for x in v] == ["HISTORICAL_PROOF_NOT_CURRENT_HEAD"]


def test_historical_old_head_passes(tmp_path: Path) -> None:
    head = "g" * 40
    p = _write(
        tmp_path / "proof.json",
        {
            "schema_version": "voice_synthesis_proof.v1",
            "historical": True,
            "git": {"head": "h" * 40, "origin_main": "0" * 40, "dirty_summary": "clean"},
        },
    )
    assert validate_proof_freshness(p, current_head=head, allow_dirty_proof=False) == []


def test_missing_git_object_fails(tmp_path: Path) -> None:
    head = "i" * 40
    p = _write(tmp_path / "proof.json", {"schema_version": "voice_synthesis_proof.v1"})
    v = validate_proof_freshness(p, current_head=head, allow_dirty_proof=False)
    assert [x.rule for x in v] == ["MISSING_GIT_HEAD"]


def test_invalid_json(tmp_path: Path) -> None:
    head = "j" * 40
    p = tmp_path / "bad.json"
    p.write_text("{", encoding="utf-8")
    v = validate_proof_freshness(p, current_head=head, allow_dirty_proof=False)
    assert [x.rule for x in v] == ["INVALID_PROOF_JSON"]


def test_non_proof_schema_skipped(tmp_path: Path) -> None:
    head = "k" * 40
    p = _write(tmp_path / "other.json", {"schema_version": "other", "git": {"head": "0" * 40}})
    assert validate_proof_freshness(p, current_head=head, allow_dirty_proof=False) == []


@pytest.mark.parametrize("flag", [True, False])
def test_main_self_test_examples(flag: bool) -> None:
    argv = ["--self-test-examples"] + (["--json"] if flag else [])
    assert main(argv) == 0


def test_repo_has_git_head() -> None:
    head, errs = current_git_head()
    assert head and not errs


def test_changed_from_unions_committed_staged_unstaged_untracked(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    p_committed = ROOT / "docs/reports/verification/_pf_committed.json"
    p_staged = ROOT / "docs/reports/verification/_pf_staged.json"
    p_unstaged = ROOT / "docs/reports/verification/_pf_unstaged.json"
    p_untracked = ROOT / "docs/reports/verification/_pf_untracked.json"
    p_noise = tmp_path / "not_under_verification.json"

    def fake_git_names(args: list[str]) -> list[Path]:
        if args == ["diff", "--name-only", "--diff-filter=ACM", "origin/main..HEAD"]:
            return [p_committed]
        if args == ["diff", "--name-only", "--cached", "--diff-filter=ACM"]:
            return [p_staged]
        if args == ["diff", "--name-only", "--diff-filter=ACM"]:
            return [p_unstaged]
        if args[:4] == ["ls-files", "--others", "--exclude-standard", "docs/reports/verification/"]:
            return [p_untracked]
        return []

    monkeypatch.setattr(pf, "_git_names", fake_git_names)

    got = {p.resolve() for p in changed_proof_json_files("origin/main")}
    assert p_committed.resolve() in got
    assert p_staged.resolve() in got
    assert p_unstaged.resolve() in got
    assert p_untracked.resolve() in got
    assert p_noise.resolve() not in got
