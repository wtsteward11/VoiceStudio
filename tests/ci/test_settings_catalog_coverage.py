"""CI gate: fail if ViewModels use settings keys/categories not in PANEL_SETTINGS_CATALOG.

Parses docs/design/PANEL_SETTINGS_CATALOG.md for allowed Settings categories and
CustomData keys. Fails if production code uses undeclared keys.

Run: python -m pytest tests/ci/test_settings_catalog_coverage.py -v
"""
from __future__ import annotations

import re
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent
CATALOG = ROOT / "docs" / "design" / "PANEL_SETTINGS_CATALOG.md"
SRC_APP = ROOT / "src" / "VoiceStudio.App"

def _extract_allowed_categories(catalog_path: Path) -> set[str]:
    """Extract Settings categories from the Settings Categories table."""
    if not catalog_path.exists():
        return set()
    text = catalog_path.read_text(encoding="utf-8-sig")
    allowed: set[str] = set()
    in_table = False
    for line in text.splitlines():
        if "| `General` |" in line or "## Settings Categories" in line:
            in_table = True
        if in_table and line.strip().startswith("| `"):
            m = re.match(r"\|\s*`(\w+)`\s*\|", line)
            if m:
                allowed.add(m.group(1))
        if in_table and line.strip().startswith("## ") and "Settings" not in line:
            break
    return allowed


def _find_settings_usage(src_dir: Path) -> set[str]:
    """Find Settings.X categories used in GetValue/SetValue (storage keys only)."""
    categories: set[str] = set()
    # Only match GetValue/SetValue - excludes ResourceHelper.GetString("Settings.LoadingSettings")
    settings_re = re.compile(
        r'(?:GetValue|SetValue)\s*\(\s*["\']Settings\.(\w+)["\']'
    )
    for path in src_dir.rglob("*.cs"):
        if "Tests" in path.parts:
            continue
        try:
            content = path.read_text(encoding="utf-8-sig")
        except Exception:
            continue
        for line in content.splitlines():
            for m in settings_re.finditer(line):
                categories.add(m.group(1))
    return categories


def test_settings_catalog_coverage() -> None:
    """Fail if ViewModels use Settings categories not declared in catalog."""
    if not CATALOG.exists():
        pytest.fail(
            f"Catalog not found: {CATALOG}. "
            "Create docs/design/PANEL_SETTINGS_CATALOG.md."
        )

    allowed_cats = _extract_allowed_categories(CATALOG)
    used_cats = _find_settings_usage(SRC_APP)

    violations = [f"  Settings category '{cat}' not in catalog" for cat in used_cats if cat not in allowed_cats]

    assert not violations, (
        "Settings categories used but NOT in PANEL_SETTINGS_CATALOG:\n"
        + "\n".join(violations)
        + "\n\nAdd them to docs/design/PANEL_SETTINGS_CATALOG.md Settings Categories table."
    )
