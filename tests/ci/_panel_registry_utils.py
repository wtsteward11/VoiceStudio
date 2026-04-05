from __future__ import annotations

import re
from pathlib import Path
from typing import Iterable, Tuple

PANEL_ID_CONST_RE = re.compile(r"PanelId\s*=\s*PanelIds\.(\w+)")
PANEL_ID_STR_RE = re.compile(r'PanelId\s*=\s*["\']([^"\']+)["\']')
CONST_VALUE_RE = re.compile(r'public\s+const\s+string\s+(\w+)\s*=\s*"([^"]+)"')
VIEW_TYPE_RE = re.compile(r"ViewType\s*=\s*typeof\s*\(\s*(\w+)\s*\)")


def load_panel_id_constants(panel_ids_cs: Path) -> dict[str, str]:
    """Load PanelIds constant definitions {ConstName: \"Value\"}."""
    if not panel_ids_cs.exists():
        return {}
    text = panel_ids_cs.read_text(encoding="utf-8-sig", errors="replace")
    return {m.group(1): m.group(2) for m in CONST_VALUE_RE.finditer(text)}


def extract_registered_panel_ids(
    registration_services: Iterable[Path],
    panel_ids_cs: Path,
) -> set[str]:
    """Extract panel ids from registration services, resolving PanelIds constants."""
    ids: set[str] = set()
    const_map = load_panel_id_constants(panel_ids_cs)

    for path in registration_services:
        if not path.exists():
            continue
        text = path.read_text(encoding="utf-8-sig", errors="replace")
        # Literal strings
        ids.update(m.group(1) for m in PANEL_ID_STR_RE.finditer(text))
        # Constants
        for m in PANEL_ID_CONST_RE.finditer(text):
            const_name = m.group(1)
            ids.add(const_map.get(const_name, const_name))
    return ids


def extract_panel_ids_and_view_types(
    registration_services: Iterable[Path],
    panel_ids_cs: Path,
) -> Tuple[set[str], set[str]]:
    """Extract (panel_ids, view_type_names) from registration services."""
    panel_ids = extract_registered_panel_ids(registration_services, panel_ids_cs)
    view_types: set[str] = set()

    for path in registration_services:
        if not path.exists():
            continue
        text = path.read_text(encoding="utf-8-sig", errors="replace")
        view_types.update(m.group(1) for m in VIEW_TYPE_RE.finditer(text))
    return panel_ids, view_types
