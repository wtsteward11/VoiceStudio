"""Tests for scripts/ci/check_runtime_no_fallback_global.py."""

from __future__ import annotations

import json
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(ROOT))

from scripts.ci.check_runtime_no_fallback_global import (
    Violation,
    main,
    scan_file,
)


def _write(path: Path, content: str) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")
    return path


ALLOW_NONE: dict = {"paths": [], "line_regex": []}


class TestScanFile:
    """Core scanner rule detection."""

    def test_detects_fallback_engine_call(self, tmp_path: Path) -> None:
        p = _write(tmp_path / "backend" / "svc.py", "    return fallback_engine()\n")
        v = scan_file(p, ALLOW_NONE)
        assert any(x.rule == "PRODUCTION_SILENT_FALLBACK" for x in v)

    def test_detects_fallback_json_true(self, tmp_path: Path) -> None:
        p = _write(tmp_path / "backend" / "cfg.py", '    data = {"fallback": true}\n')
        v = scan_file(p, ALLOW_NONE)
        assert any(x.rule == "PRODUCTION_SILENT_FALLBACK" for x in v)

    def test_detects_fake_success(self, tmp_path: Path) -> None:
        p = _write(
            tmp_path / "backend" / "svc.py",
            "    return {'message': 'empty success without audio'}\n",
        )
        v = scan_file(p, ALLOW_NONE)
        assert any(x.rule == "PRODUCTION_FAKE_SUCCESS" for x in v)

    def test_detects_placeholder_metric(self, tmp_path: Path) -> None:
        p = _write(tmp_path / "backend" / "svc.py", "    quality_metrics = {}\n")
        v = scan_file(p, ALLOW_NONE)
        assert any(x.rule == "PRODUCTION_PLACEHOLDER_METRIC" for x in v)

    def test_detects_simulation_masquerade(self, tmp_path: Path) -> None:
        p = _write(tmp_path / "backend" / "svc.py", "    real_training_performed = false\n")
        v = scan_file(p, ALLOW_NONE)
        assert any(x.rule == "PRODUCTION_SIMULATION_MASQUERADES_REAL" for x in v)

    def test_detects_best_effort_success(self, tmp_path: Path) -> None:
        p = _write(tmp_path / "backend" / "svc.py", "    return best_effort success\n")
        v = scan_file(p, ALLOW_NONE)
        assert any(x.rule == "PRODUCTION_BEST_EFFORT_SUCCESS" for x in v)

    def test_detects_graceful_degradation(self, tmp_path: Path) -> None:
        p = _write(tmp_path / "backend" / "svc.py", "    degrade gracefully\n")
        v = scan_file(p, ALLOW_NONE)
        assert any(x.rule == "UNCLASSIFIED_FALLBACK_TERM" for x in v)

    def test_skips_comment_lines(self, tmp_path: Path) -> None:
        p = _write(tmp_path / "backend" / "svc.py", "# fallback_engine pattern removed\n")
        v = scan_file(p, ALLOW_NONE)
        assert v == []

    def test_skips_docstring_lines(self, tmp_path: Path) -> None:
        content = '"""\nThis engine uses fallback_engine for reliability.\n"""\ndef ok(): pass\n'
        p = _write(tmp_path / "backend" / "svc.py", content)
        v = scan_file(p, ALLOW_NONE)
        assert v == []

    def test_skips_test_directories(self, tmp_path: Path) -> None:
        """iter_files_under skips tests/ directories, but scan_file on an explicit path does not."""
        from scripts.ci.check_runtime_no_fallback_global import iter_files_under

        test_dir = tmp_path / "tests"
        _write(test_dir / "test_svc.py", "    return fallback_engine()\n")
        files = iter_files_under([tmp_path])
        test_files = [f for f in files if "tests" in str(f).lower()]
        assert test_files == []

    def test_path_allowlist_suppresses(self, tmp_path: Path) -> None:
        p = _write(tmp_path / "backend" / "svc.py", "    return fallback_engine()\n")
        allow = {"paths": ["backend/svc.py"], "line_regex": []}
        v = scan_file(p, allow)
        assert v == []

    def test_line_regex_allowlist_suppresses(self, tmp_path: Path) -> None:
        p = _write(tmp_path / "backend" / "svc.py", "    no fallback allowed here\n")
        allow = {"paths": [], "line_regex": ["no.fallback"]}
        v = scan_file(p, allow)
        assert v == []


class TestSelfTest:
    """Self-test mode."""

    def test_self_test_passes(self) -> None:
        assert main(["--self-test-examples"]) == 0

    @pytest.mark.parametrize("json_flag", [True, False])
    def test_self_test_json(self, json_flag: bool) -> None:
        argv = ["--self-test-examples"] + (["--json"] if json_flag else [])
        assert main(argv) == 0
