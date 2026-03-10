"""CI gate: fail if EventAggregator uses event types not in PANEL_WIRING_CATALOG.

Parses docs/design/PANEL_WIRING_CATALOG.md for the Allowed Event Types allowlist,
then scans src/ for Publish<T> and Subscribe<T> usage. Fails if any event type
is used but not declared in the catalog.

Run: python -m pytest tests/ci/test_event_catalog_completeness.py -v
"""
from __future__ import annotations

import re
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent
CATALOG = ROOT / "docs" / "design" / "PANEL_WIRING_CATALOG.md"
SRC_DIRS = [
    ROOT / "src" / "VoiceStudio.App",
    ROOT / "src" / "VoiceStudio.Core",
]

# Event types used only in tests (excluded from allowlist check)
TEST_ONLY_EVENTS = frozenset({"TestEvent", "OtherTestEvent"})
# Generic type parameters (not concrete event types)
GENERIC_PARAMS = frozenset({"TEvent"})


def _extract_allowed_events(catalog_path: Path) -> set[str]:
    """Extract event type names from the Allowed Event Types section."""
    if not catalog_path.exists():
        return set()
    text = catalog_path.read_text(encoding="utf-8-sig")
    allowed: set[str] = set()
    in_section = False
    for line in text.splitlines():
        stripped = line.strip()
        if "## Allowed Event Types" in stripped or "Allowed Event Types (Allowlist" in stripped:
            in_section = True
            continue
        if in_section:
            if stripped.startswith("## ") and "Allowed" not in stripped:
                break
            # Match "- `EventName`" or "- EventName
            m = re.match(r"^[-*]\s*`?(\w+Event)`?\s*$", stripped)
            if m:
                allowed.add(m.group(1))
    return allowed


def _find_event_usage(src_dirs: list[Path]) -> dict[str, list[tuple[Path, int, str]]]:
    """Find all event types used in Publish/Subscribe. Returns {EventType: [(path, line, snippet)]}."""
    # Subscribe<XEvent>( or Subscribe<XEvent> (
    subscribe_re = re.compile(r"Subscribe\s*<\s*(\w+Event)\s*>")
    # Publish(new XEvent( or Publish(new VoiceStudio.Core.Events.XEvent(
    publish_re = re.compile(r"Publish\s*\(\s*new\s+(?:\w+\.)*(\w+Event)\s*\(")
    # Publish<XEvent>( - less common
    publish_generic_re = re.compile(r"Publish\s*<\s*(\w+Event)\s*>")

    usage: dict[str, list[tuple[Path, int, str]]] = {}
    for src_dir in src_dirs:
        if not src_dir.exists():
            continue
        for path in src_dir.rglob("*.cs"):
            # Exclude test files - they use TestEvent etc.
            if "Tests" in path.parts or path.name.endswith("Tests.cs"):
                continue
            try:
                content = path.read_text(encoding="utf-8-sig")
            except Exception:
                continue
            for i, line in enumerate(content.splitlines(), 1):
                for pattern in (subscribe_re, publish_re, publish_generic_re):
                    for m in pattern.finditer(line):
                        evt = m.group(1)
                        if evt not in usage:
                            usage[evt] = []
                        usage[evt].append((path, i, line.strip()[:80]))
    return usage


def test_event_catalog_completeness() -> None:
    """Fail if any Publish/Subscribe uses an event type not in PANEL_WIRING_CATALOG allowlist."""
    if not CATALOG.exists():
        pytest.fail(
            f"Catalog not found: {CATALOG}. "
            "Create docs/design/PANEL_WIRING_CATALOG.md with Allowed Event Types section."
        )

    allowed = _extract_allowed_events(CATALOG)
    if not allowed:
        pytest.fail(
            "Could not extract allowed event types from catalog. "
            "Ensure PANEL_WIRING_CATALOG.md has '## Allowed Event Types' with bullet list."
        )

    usage = _find_event_usage(SRC_DIRS)
    violations: list[str] = []
    for evt, locations in usage.items():
        if evt in TEST_ONLY_EVENTS or evt in GENERIC_PARAMS:
            continue
        if evt not in allowed:
            loc_str = "; ".join(f"{p.relative_to(ROOT)}:{ln}" for p, ln, _ in locations[:3])
            violations.append(f"  {evt} (used in {loc_str})")

    assert not violations, (
        "Event types used in Publish/Subscribe but NOT in PANEL_WIRING_CATALOG allowlist:\n"
        + "\n".join(violations)
        + "\n\nAdd them to docs/design/PANEL_WIRING_CATALOG.md 'Allowed Event Types' section."
    )
