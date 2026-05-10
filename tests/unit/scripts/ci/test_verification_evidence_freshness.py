"""Tests for scripts/ci/check_verification_evidence_freshness.py."""

from __future__ import annotations

import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(ROOT))

from scripts.ci.check_verification_evidence_freshness import (
    main,
    validate_artifact,
    validate_latest_dir,
)


class TestValidateArtifact:
    def test_existing_non_empty_passes(self, tmp_path: Path) -> None:
        p = tmp_path / "report.md"
        p.write_text("# Report", encoding="utf-8")
        assert validate_artifact(p) == []

    def test_missing_fails(self, tmp_path: Path) -> None:
        v = validate_artifact(tmp_path / "nope.md")
        assert v and v[0].rule == "MISSING_ARTIFACT"

    def test_empty_fails(self, tmp_path: Path) -> None:
        p = tmp_path / "empty.md"
        p.write_text("", encoding="utf-8")
        v = validate_artifact(p)
        assert v and v[0].rule == "EMPTY_ARTIFACT"


class TestValidateLatestDir:
    def test_populated_dir_passes(self, tmp_path: Path) -> None:
        d = tmp_path / "latest"
        d.mkdir()
        (d / "file.txt").write_text("ok", encoding="utf-8")
        assert validate_latest_dir(d) == []

    def test_missing_dir_fails(self, tmp_path: Path) -> None:
        v = validate_latest_dir(tmp_path / "nope")
        assert v and v[0].rule == "MISSING_LATEST_DIR"

    def test_empty_dir_fails(self, tmp_path: Path) -> None:
        d = tmp_path / "empty"
        d.mkdir()
        v = validate_latest_dir(d)
        assert v and v[0].rule == "EMPTY_LATEST_DIR"


class TestSelfTest:
    def test_self_test_passes(self) -> None:
        assert main(["--self-test-examples"]) == 0

    @pytest.mark.parametrize("json_flag", [True, False])
    def test_self_test_json(self, json_flag: bool) -> None:
        argv = ["--self-test-examples"] + (["--json"] if json_flag else [])
        assert main(argv) == 0
