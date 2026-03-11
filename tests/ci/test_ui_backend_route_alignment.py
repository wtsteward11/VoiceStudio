"""
CI gate: fail if frontend targets an unregistered backend route prefix.

Uses the same logic as scripts/scan_ui_api_calls.py. Allowlisted prefixes
(archived routes whose panels are hidden) do not cause failure.

Run: python -m pytest tests/ci/test_ui_backend_route_alignment.py -v
"""
from __future__ import annotations

import re
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent
SRC_DIR = ROOT / "src"
BACKEND_ROUTES = ROOT / "backend" / "api" / "routes"

ALLOWLIST_PREFIXES = frozenset({
    "/api/todo-panel",
    "/api/text-highlighting",
    "/api/ultimate-dashboard",
    "/api/mcp-dashboard",
    "/api/script-editor",
})

ALLOWLIST_NO_BACKEND = frozenset({
    "/api/enhancement",
    "/api/mcp",
    "/api/v1",
    "/api/visualization",
})


def _extract_api_prefix(path: str) -> str | None:
    m = re.match(r"^/api/([a-zA-Z0-9_-]+)", path)
    if m:
        return f"/api/{m.group(1)}"
    return None


def _scan_frontend_prefixes() -> dict[str, list[tuple[str, int]]]:
    prefix_to_sites: dict[str, list[tuple[str, int]]] = {}
    pattern = re.compile(r'["\'](/api/[a-zA-Z0-9_-]+)(?:[/{?]|["\'])')
    for ext in (".cs", ".xaml"):
        for f in SRC_DIR.rglob(f"*{ext}"):
            if ".Tests" in str(f) or "obj" in str(f) or "bin" in str(f):
                continue
            try:
                text = f.read_text(encoding="utf-8", errors="replace")
            except Exception:
                continue
            rel = str(f.relative_to(ROOT)).replace("\\", "/")
            for i, line in enumerate(text.splitlines(), 1):
                for m in pattern.finditer(line):
                    prefix = _extract_api_prefix(m.group(1))
                    if prefix:
                        if prefix not in prefix_to_sites:
                            prefix_to_sites[prefix] = []
                        prefix_to_sites[prefix].append((rel, i))
    return prefix_to_sites


def _scan_backend_prefixes() -> set[str]:
    prefixes: set[str] = set()
    prefix_re = re.compile(r'prefix\s*=\s*["\'](/api/[^"\']+)["\']')
    for py_file in BACKEND_ROUTES.rglob("*.py"):
        if "_archived" in str(py_file):
            continue
        try:
            text = py_file.read_text(encoding="utf-8", errors="replace")
        except Exception:
            continue
        for m in prefix_re.finditer(text):
            base = _extract_api_prefix(m.group(1))
            if base:
                prefixes.add(base)
    prefixes.add("/api/deepfake-creator")
    return prefixes


def test_ui_backend_route_alignment() -> None:
    """Fail if any frontend prefix has no backend and is not allowlisted."""
    frontend = _scan_frontend_prefixes()
    backend = _scan_backend_prefixes()
    failures: list[str] = []
    for prefix, sites in frontend.items():
        if not sites:
            continue
        if prefix in ALLOWLIST_PREFIXES or prefix in ALLOWLIST_NO_BACKEND:
            continue
        if prefix not in backend:
            call_sites = ", ".join(f"{f}:{ln}" for f, ln in sites[:3])
            if len(sites) > 3:
                call_sites += f" (+{len(sites) - 3} more)"
            failures.append(f"{prefix}: no backend (call sites: {call_sites})")
    assert not failures, (
        "Frontend targets unregistered backend routes. "
        "Hide panels or add backend. Run: python scripts/scan_ui_api_calls.py\n"
        + "\n".join(failures)
    )
