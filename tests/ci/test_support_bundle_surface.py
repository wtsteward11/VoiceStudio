"""
Support Bundle surface gate: verify the support bundle
collection script and UI handler exist and cover required items.

This is a static analysis gate (not runtime), safe to run on
ubuntu-latest CI runners even though the script is Windows-only.
"""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent

SCRIPT_PATH = ROOT / "scripts" / "collect-support-bundle.ps1"
DIAG_VIEW_PATH = (
    ROOT / "src" / "VoiceStudio.App" / "Views" / "Panels"
    / "DiagnosticsView.xaml.cs"
)

REQUIRED_BUNDLE_ITEMS = [
    "crashes",
    "system_info",
    "engine_manifests",
    "build_version",
]


def _read_file(path: Path) -> str:
    if not path.exists():
        return ""
    try:
        return path.read_text(encoding="utf-8", errors="replace")
    except Exception:
        return ""


def get_support_bundle_results() -> dict:
    """Scan files and return structured results for proof writer."""
    script_text = _read_file(SCRIPT_PATH)
    diag_text = _read_file(DIAG_VIEW_PATH)

    script_exists = SCRIPT_PATH.exists()
    ui_handler_exists = bool(
        re.search(r"ExportSupportBundle_Click", diag_text)
    )
    items_found = {
        item: bool(re.search(re.escape(item), script_text, re.IGNORECASE))
        for item in REQUIRED_BUNDLE_ITEMS
    }
    all_present = all(items_found.values())

    return {
        "script_exists": script_exists,
        "ui_handler_exists": ui_handler_exists,
        "required_items_present": all_present,
        "items_checked": items_found,
    }


def test_support_bundle_script_exists() -> None:
    """collect-support-bundle.ps1 must exist."""
    assert SCRIPT_PATH.exists(), (
        f"Missing: {SCRIPT_PATH.relative_to(ROOT)}"
    )


def test_support_bundle_ui_handler_exists() -> None:
    """DiagnosticsView must have ExportSupportBundle_Click."""
    text = _read_file(DIAG_VIEW_PATH)
    assert re.search(r"ExportSupportBundle_Click", text), (
        f"ExportSupportBundle_Click not found in "
        f"{DIAG_VIEW_PATH.relative_to(ROOT)}"
    )


def test_support_bundle_required_items() -> None:
    """Script must collect all required bundle items."""
    results = get_support_bundle_results()
    missing = [
        k for k, v in results["items_checked"].items() if not v
    ]
    assert not missing, (
        f"Missing bundle items in script: {missing}"
    )
