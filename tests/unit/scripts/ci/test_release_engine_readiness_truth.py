"""Tests for scripts/ci/check_release_engine_readiness_truth.py."""

from __future__ import annotations

import json
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(ROOT))

from scripts.ci.check_release_engine_readiness_truth import (
    EngineReadiness,
    _generate_markdown,
    assess_engine,
    main,
)


def _manifest(path: Path, data: dict) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data), encoding="utf-8")
    return path


class TestAssessEngine:
    def test_ready_engine(self, tmp_path: Path) -> None:
        p = _manifest(
            tmp_path / "piper" / "engine.manifest.json",
            {"engine_id": "piper", "entry_point": "app.core.engines.piper_engine", "capabilities": ["synthesis"], "schema_version": 3},
        )
        r = assess_engine("piper", {"piper": p})
        assert r.status == "READY"

    def test_missing_manifest(self) -> None:
        r = assess_engine("nonexistent", {})
        assert r.status == "MISSING_MANIFEST"

    def test_excluded_rhvoice(self) -> None:
        r = assess_engine("rhvoice", {})
        assert r.status == "EXCLUDED"

    def test_config_error_bad_json(self, tmp_path: Path) -> None:
        p = tmp_path / "bad" / "engine.manifest.json"
        p.parent.mkdir(parents=True)
        p.write_text("{invalid", encoding="utf-8")
        r = assess_engine("bad", {"bad": p})
        assert r.status == "CONFIG_ERROR"

    def test_missing_entry_point(self, tmp_path: Path) -> None:
        p = _manifest(
            tmp_path / "x" / "engine.manifest.json",
            {"engine_id": "x", "capabilities": ["synthesis"]},
        )
        r = assess_engine("x", {"x": p})
        assert r.status == "CONFIG_ERROR"
        assert any("entry_point" in b for b in r.blockers)

    def test_old_schema_version_recommendation(self, tmp_path: Path) -> None:
        p = _manifest(
            tmp_path / "old" / "engine.manifest.json",
            {"engine_id": "old", "entry_point": "mod", "schema_version": 2},
        )
        r = assess_engine("old", {"old": p})
        assert r.status == "READY"
        assert any("schema_version 3" in rec for rec in r.recommendations)


class TestMarkdown:
    def test_does_not_mention_rhvoice_in_engine_table_by_default(self) -> None:
        results = [EngineReadiness(engine_id="piper", status="READY")]
        md = _generate_markdown(results)
        lines_before_nonclaims = md.split("## Non-claims")[0]
        assert "rhvoice" not in lines_before_nonclaims.lower()

    def test_does_not_write_engine_parity_matrix(self) -> None:
        results = [EngineReadiness(engine_id="piper", status="READY")]
        md = _generate_markdown(results)
        assert "ENGINE_PARITY_MATRIX" in md
        assert "Does not write" in md

    def test_nonclaims_present(self) -> None:
        md = _generate_markdown([])
        assert "Non-claims" in md


class TestSelfTest:
    def test_self_test_passes(self) -> None:
        assert main(["--self-test-examples"]) == 0

    @pytest.mark.parametrize("json_flag", [True, False])
    def test_self_test_json(self, json_flag: bool) -> None:
        argv = ["--self-test-examples"] + (["--json"] if json_flag else [])
        assert main(argv) == 0
