#!/usr/bin/env python3
"""
SSOT-compliant Support Bundle Runtime Proof Writer.

Invokes collect-support-bundle.ps1 on Windows, verifies output
files exist with real sizes and SHA-256 hashes, checks UI wiring.

Requires Windows (PowerShell + LOCALAPPDATA paths).

Usage:
    python scripts/ci/write_support_bundle_runtime_proof.py
"""
from __future__ import annotations

import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from scripts.ci.proof_fingerprint import compute_fingerprint

VERIFICATION_DIR = ROOT / "docs" / "reports" / "verification"
SCRIPT_PATH = ROOT / "scripts" / "collect-support-bundle.ps1"
DIAG_VIEW = (
    ROOT / "src" / "VoiceStudio.App" / "Views" / "Panels"
    / "DiagnosticsView.xaml.cs"
)

REQUIRED_FILES = [
    "system_info.json",
    "engine_manifests_list.txt",
    "build_version_source.txt",
]


def _get_git_commit() -> str:
    try:
        out = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            capture_output=True, text=True, cwd=ROOT, timeout=5,
        )
        if out.returncode == 0 and out.stdout.strip():
            return out.stdout.strip()[:40]
    except (subprocess.TimeoutExpired, FileNotFoundError):
        pass
    return "0" * 40


def _get_git_branch() -> str:
    try:
        out = subprocess.run(
            ["git", "branch", "--show-current"],
            capture_output=True, text=True, cwd=ROOT, timeout=5,
        )
        if out.returncode == 0 and out.stdout.strip():
            return out.stdout.strip()
    except (subprocess.TimeoutExpired, FileNotFoundError):
        pass
    return "unknown"


def _file_sha256(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


def _check_ui_wiring() -> bool:
    if not DIAG_VIEW.exists():
        return False
    try:
        text = DIAG_VIEW.read_text(encoding="utf-8", errors="replace")
        return bool(re.search(r"ExportSupportBundle_Click", text))
    except Exception:
        return False


def main() -> int:
    if sys.platform != "win32":
        print(
            "Support bundle runtime proof requires Windows.",
            file=sys.stderr,
        )
        return 1

    if not SCRIPT_PATH.exists():
        print(
            f"Script not found: {SCRIPT_PATH}",
            file=sys.stderr,
        )
        return 1

    with tempfile.TemporaryDirectory(
        prefix="voicestudio_bundle_"
    ) as tmpdir:
        result = subprocess.run(
            [
                "powershell", "-ExecutionPolicy", "Bypass",
                "-File", str(SCRIPT_PATH),
                "-OutputDir", tmpdir,
            ],
            cwd=ROOT,
            capture_output=True, text=True, timeout=60,
        )

        if result.returncode != 0:
            print(
                f"Bundle script failed (exit {result.returncode}):\n"
                f"{result.stderr[:500]}",
                file=sys.stderr,
            )
            return 1

        bundle_dir = Path(tmpdir)
        zips = list(bundle_dir.glob("*.zip"))
        bundle_path = str(zips[0]) if zips else ""

        file_entries: list[dict] = []
        for name in REQUIRED_FILES:
            candidates = list(bundle_dir.rglob(name))
            if candidates:
                fp = candidates[0]
                sz = fp.stat().st_size
                file_entries.append({
                    "name": name,
                    "exists": True,
                    "bytes": sz,
                    "sha256": _file_sha256(fp) if sz > 0 else "",
                })
            else:
                file_entries.append({
                    "name": name,
                    "exists": False,
                    "bytes": 0,
                    "sha256": "",
                })

    all_exist = all(e["exists"] and e["bytes"] > 0 for e in file_entries)
    ui_ok = _check_ui_wiring()

    if not all_exist:
        missing = [e["name"] for e in file_entries if not e["exists"]]
        print(
            f"Required files missing from bundle: {missing}",
            file=sys.stderr,
        )
        return 1

    if not ui_ok:
        print(
            "ExportSupportBundle_Click not found in DiagnosticsView.",
            file=sys.stderr,
        )
        return 1

    command = (
        f"powershell -ExecutionPolicy Bypass "
        f"-File {SCRIPT_PATH.name}"
    )

    proof = {
        "command": command,
        "exit_code": 0,
        "timestamp": datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ"),
        "git_commit": _get_git_commit(),
        "git_branch": _get_git_branch(),
        "required_files": file_entries,
        "ui_wiring_verified": ui_ok,
        "bundle_path": bundle_path,
    }
    proof["evidence_fingerprint"] = compute_fingerprint(
        proof, "PROOF_SUPPORT_BUNDLE_RUNTIME"
    )

    VERIFICATION_DIR.mkdir(parents=True, exist_ok=True)
    date_str = datetime.utcnow().strftime("%Y-%m-%d")
    out_path = (
        VERIFICATION_DIR
        / f"PROOF_SUPPORT_BUNDLE_RUNTIME_{date_str}.json"
    )

    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(proof, f, indent=2)

    print(f"Proof written to {out_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
