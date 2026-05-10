"""Tests for scripts/ci/check_async_task_durability.py."""

from __future__ import annotations

import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(ROOT))

from scripts.ci.check_async_task_durability import (
    main,
    scan_file,
)

ALLOW_NONE: dict = {"paths": [], "line_regex": []}


def _write(path: Path, content: str) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")
    return path


class TestScanFile:
    def test_detects_create_task(self, tmp_path: Path) -> None:
        p = _write(tmp_path / "backend" / "svc.py", "    asyncio.create_task(do_work())\n")
        v = scan_file(p, ALLOW_NONE)
        assert any(x.rule == "FIRE_AND_FORGET_ASYNC_TASK" for x in v)

    def test_detects_background_task(self, tmp_path: Path) -> None:
        p = _write(tmp_path / "backend" / "svc.py", "    background_tasks.add_task(run)\n")
        v = scan_file(p, ALLOW_NONE)
        assert any(x.rule == "FASTAPI_BACKGROUND_TASK_UNDURABLE" for x in v)

    def test_detects_thread(self, tmp_path: Path) -> None:
        p = _write(tmp_path / "backend" / "svc.py", "    threading.Thread(target=work).start()\n")
        v = scan_file(p, ALLOW_NONE)
        assert any(x.rule == "UNTRACKED_THREAD" for x in v)

    def test_detects_popen(self, tmp_path: Path) -> None:
        p = _write(tmp_path / "backend" / "svc.py", "    p = subprocess.Popen(['cmd'])\n")
        v = scan_file(p, ALLOW_NONE)
        assert any(x.rule == "UNTRACKED_PROCESS" for x in v)

    def test_detects_csharp_task_run_discard(self, tmp_path: Path) -> None:
        p = _write(tmp_path / "src" / "VoiceStudio.App" / "Svc.cs", "    _ = Task.Run(() => Work());\n")
        v = scan_file(p, ALLOW_NONE)
        assert any(x.rule == "UNTRACKED_CSHARP_TASK" for x in v)

    def test_skips_test_dir(self, tmp_path: Path) -> None:
        """iter_files_under skips tests/ directories."""
        from scripts.ci.check_async_task_durability import iter_files_under

        test_dir = tmp_path / "tests"
        _write(test_dir / "test_svc.py", "    asyncio.create_task(do_work())\n")
        files = iter_files_under([tmp_path])
        test_files = [f for f in files if "tests" in str(f).lower()]
        assert test_files == []

    def test_skips_comment(self, tmp_path: Path) -> None:
        p = _write(tmp_path / "backend" / "svc.py", "# asyncio.create_task removed\n")
        v = scan_file(p, ALLOW_NONE)
        assert v == []

    def test_path_allowlist(self, tmp_path: Path) -> None:
        p = _write(tmp_path / "backend" / "svc.py", "    asyncio.create_task(do_work())\n")
        allow = {"paths": ["backend/svc.py"], "line_regex": []}
        v = scan_file(p, allow)
        assert v == []

    def test_line_regex_allowlist(self, tmp_path: Path) -> None:
        p = _write(tmp_path / "backend" / "svc.py", "    asyncio.create_task(do_work())  # durable-job-backed\n")
        allow = {"paths": [], "line_regex": ["durable-job-backed"]}
        v = scan_file(p, allow)
        assert v == []

    def test_detects_fire_and_forget_discard(self, tmp_path: Path) -> None:
        p = _write(tmp_path / "backend" / "svc.py", "    _ = LoadDataAsync()\n")
        v = scan_file(p, ALLOW_NONE)
        assert any(x.rule == "FIRE_AND_FORGET_DISCARD" for x in v)


class TestSelfTest:
    def test_self_test_passes(self) -> None:
        assert main(["--self-test-examples"]) == 0

    @pytest.mark.parametrize("json_flag", [True, False])
    def test_self_test_json(self, json_flag: bool) -> None:
        argv = ["--self-test-examples"] + (["--json"] if json_flag else [])
        assert main(argv) == 0
