"""Tests for runtime product-path no-fallback audit."""
from __future__ import annotations

import contextlib
import io
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent.parent.parent
sys.path.insert(0, str(ROOT))

from scripts.ci.check_runtime_no_fallback_product_path import (
    main,
    scan_file,
    scan_paths,
)


def _write(path: Path, text: str) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")
    return path


def test_detects_silent_fallback(tmp_path: Path) -> None:
    path = _write(tmp_path / "backend" / "api" / "routes" / "timeline.py", "x = fallback_engine()\n")

    violations = scan_file(path)

    assert [v.rule for v in violations] == ["SILENT_FALLBACK"]


def test_detects_fake_success(tmp_path: Path) -> None:
    path = _write(
        tmp_path / "backend" / "api" / "routes" / "timeline.py",
        "return {'success': True, 'message': 'empty success without audio'}\n",
    )

    violations = scan_file(path)

    assert [v.rule for v in violations] == ["FAKE_SUCCESS"]


def test_detects_stub_production_code(tmp_path: Path) -> None:
    path = _write(tmp_path / "backend" / "services" / "synthesis_service.py", "audio_id = stub_audio_id\n")

    violations = scan_file(path)

    assert [v.rule for v in violations] == ["STUB_PRODUCTION_CODE"]


def test_allows_test_paths(tmp_path: Path) -> None:
    path = _write(tmp_path / "tests" / "test_product.py", "assert mock_audio_id\n")

    assert scan_file(path) == []


def test_allows_explicit_error_and_blocker_lines(tmp_path: Path) -> None:
    path = _write(
        tmp_path / "backend" / "api" / "routes" / "timeline.py",
        "\n".join(
            [
                "raise HTTPException(status_code=400, detail='fallback rejected')",
                "blocker = 'restart command not supplied; non-claim'",
            ]
        ),
    )

    assert scan_file(path) == []


def test_scan_paths_reports_missing_file() -> None:
    violations = scan_paths([Path("missing-product-path.py")])

    assert violations[0].rule == "FILE_READ"


def test_self_test_cli_json_passes() -> None:
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        rc = main(["--self-test-examples", "--json"])

    assert rc == 0
    output = buf.getvalue()
    payload = json.loads(output[output.index("{"):])
    assert payload["status"] == "pass"
