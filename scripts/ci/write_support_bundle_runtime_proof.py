#!/usr/bin/env python3
"""
SSOT-compliant Support Bundle Runtime Proof Writer.

Invokes collect-support-bundle.ps1 on Windows, verifies output
files exist with real sizes and SHA-256 hashes, checks UI wiring.
Never stores full bundle path; requires crash evidence and BUNDLE_MANIFEST.json.

Requires Windows (PowerShell + LOCALAPPDATA paths).

Usage:
    python scripts/ci/write_support_bundle_runtime_proof.py
"""
from __future__ import annotations

import hashlib
import json
import re
import subprocess
import sys
import tempfile
import zipfile
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from scripts.ci.proof_fingerprint import compute_fingerprint

VERIFICATION_DIR = ROOT / "docs" / "reports" / "verification"
SCRIPT_PATH = ROOT / "scripts" / "collect-support-bundle.ps1"
DIAG_VIEW_CS = ROOT / "src" / "VoiceStudio.App" / "Views" / "Panels" / "DiagnosticsView.xaml.cs"
DIAG_VIEW_XAML = ROOT / "src" / "VoiceStudio.App" / "Views" / "Panels" / "DiagnosticsView.xaml"

# All category entries (one per category must be satisfied)
REQUIRED_FILE_NAMES = [
    "system_info.json",
    "hardware_info.json",
    "engine_manifests_list.txt",
    "engine_manifests.json",
    "build_version_source.txt",
    "build_version.json",
    "BUNDLE_MANIFEST.json",
]


def _get_git_commit() -> str:
    try:
        out = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            capture_output=True, text=True, cwd=ROOT, timeout=5,
        )
        if out.returncode == 0 and out.stdout.strip():
            return out.stdout.strip()[:40]
    # ALLOWED: bare except - best effort, failure acceptable
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
    # ALLOWED: bare except - best effort, failure acceptable
    except (subprocess.TimeoutExpired, FileNotFoundError):
        pass
    return "unknown"


def _bytes_sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _check_ui_wiring() -> bool:
    """Require both .xaml.cs handler and .xaml binding."""
    if not DIAG_VIEW_CS.exists() or not DIAG_VIEW_XAML.exists():
        return False
    try:
        cs_text = DIAG_VIEW_CS.read_text(encoding="utf-8", errors="replace")
        xaml_text = DIAG_VIEW_XAML.read_text(encoding="utf-8", errors="replace")
        has_handler = bool(re.search(r"ExportSupportBundle_Click", cs_text))
        has_xaml = (
            "Export Support Bundle" in xaml_text
            and "ExportSupportBundle_Click" in xaml_text
        )
        return has_handler and has_xaml
    except Exception:
        return False


def _has_crash_evidence(names: list[str]) -> bool:
    """True if bundle contains crash*.log, crash_marker*, or crashes/ with files."""
    for n in names:
        norm = n.replace("\\", "/").lower()
        if "/crash" in norm and norm.endswith(".log"):
            return True
        if "crash_marker" in norm:
            return True
        if norm.startswith("crashes/") and "/" in norm[8:]:
            return True
        if norm == "crashes" or norm == "crashes/":
            continue
        if norm.startswith("crashes/"):
            return True
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

    ui_ok = _check_ui_wiring()
    if not ui_ok:
        print(
            "UI wiring failed: DiagnosticsView.xaml.cs must contain "
            "ExportSupportBundle_Click AND DiagnosticsView.xaml must contain "
            "'Export Support Bundle' and 'ExportSupportBundle_Click'.",
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
        if not zips:
            print("No zip produced by collect script.", file=sys.stderr)
            return 1
        zip_path = zips[0]
        bundle_path_hash = hashlib.sha256(
            str(zip_path).encode("utf-8")
        ).hexdigest()

        file_entries: list[dict] = []
        zip_names: list[str] = []
        name_to_info: dict[str, tuple[int, str]] = {}

        with zipfile.ZipFile(zip_path, "r") as zf:
            for zi in zf.infolist():
                if zi.is_dir():
                    continue
                name = zi.filename.replace("\\", "/")
                zip_names.append(name)
                sz = zi.file_size
                data = zf.read(zi.filename) if sz > 0 else b""
                sha = _bytes_sha256(data) if sz > 0 else ""
                name_to_info[name] = (sz, sha)

        for name in REQUIRED_FILE_NAMES:
            found = None
            for zip_name in name_to_info:
                if zip_name.endswith(name) or zip_name == name:
                    found = zip_name
                    break
            if found:
                sz, sha = name_to_info[found]
                file_entries.append({
                    "name": name,
                    "exists": True,
                    "bytes": sz,
                    "sha256": sha,
                })
            else:
                file_entries.append({
                    "name": name,
                    "exists": False,
                    "bytes": 0,
                    "sha256": "",
                })

        bundle_manifest_present = any(
            e["name"] == "BUNDLE_MANIFEST.json" and e["exists"] and e["bytes"] > 0
            for e in file_entries
        )
        if not bundle_manifest_present:
            print(
                "BUNDLE_MANIFEST.json missing or empty.",
                file=sys.stderr,
            )
            return 1

        crash_evidence_present = _has_crash_evidence(zip_names)
        if not crash_evidence_present:
            print(
                "No crash evidence in bundle (need crash*.log, crash_marker*, "
                "or crashes/ with files).",
                file=sys.stderr,
            )
            return 1

        categories = [
            (["system_info.json", "hardware_info.json"], "system_info"),
            (["engine_manifests_list.txt", "engine_manifests.json"], "engine_manifests"),
            (["build_version_source.txt", "build_version.json"], "build_version"),
            (["BUNDLE_MANIFEST.json"], "bundle_manifest"),
        ]
        for names, cat in categories:
            satisfied = any(
                e["name"] == n and e["exists"] and e["bytes"] > 0
                for e in file_entries for n in names
            )
            if not satisfied:
                print(
                    f"Category '{cat}' not satisfied (need one of {names} with bytes>0).",
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
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "git_commit": _get_git_commit(),
        "git_branch": _get_git_branch(),
        "required_files": file_entries,
        "ui_wiring_verified": ui_ok,
        "bundle_path_hash": bundle_path_hash,
        "bundle_manifest_present": bundle_manifest_present,
        "crash_evidence_present": crash_evidence_present,
    }
    proof["evidence_fingerprint"] = compute_fingerprint(
        proof, "PROOF_SUPPORT_BUNDLE_RUNTIME"
    )

    VERIFICATION_DIR.mkdir(parents=True, exist_ok=True)
    date_str = datetime.now(timezone.utc).strftime("%Y-%m-%d")
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
