#!/usr/bin/env python3
"""
Scan UI for /api/ path usage and compare against backend route registry.

Produces docs/reports/verification/UI_BACKEND_ROUTE_ALIGNMENT_YYYY-MM-DD.md
with columns: prefix, call_sites, backend_provides, decision.

Usage:
  python scripts/scan_ui_api_calls.py [--output PATH]

Output path defaults to docs/reports/verification/UI_BACKEND_ROUTE_ALIGNMENT_<date>.md
"""
from __future__ import annotations

import argparse
import re
from datetime import date
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC_DIR = ROOT / "src"
BACKEND_ROUTES = ROOT / "backend" / "api" / "routes"
REPORTS_DIR = ROOT / "docs" / "reports" / "verification"

# API prefixes that are allowlisted (archived routes whose panels are hidden)
ALLOWLIST_PREFIXES = frozenset({
    "/api/todo-panel",
    "/api/text-highlighting",
    "/api/ultimate-dashboard",
    "/api/mcp-dashboard",  # archived; panel hidden in Step 2
    "/api/script-editor",   # archived; panel hidden in Step 2
})

# API prefixes with no backend - allowlisted pending investigation
ALLOWLIST_NO_BACKEND = frozenset({
    "/api/enhancement",     # ImageVideoEnhancementPipelineViewModel; may map to ai-enhancement
    "/api/mcp",            # BackendClient; legacy MCP path
    "/api/v1",             # versioned API path
    "/api/visualization",  # AdvancedRealTimeVisualizationViewModel
})

# API prefixes that are dead but panels may still be visible (require hiding)
DEAD_PREFIXES_NEED_HIDING = frozenset({
    "/api/mcp-dashboard",
    "/api/script-editor",
})


def _extract_api_prefix(path: str) -> str | None:
    """Extract /api/XXX prefix from path like /api/foo/bar or /api/foo/{id}."""
    m = re.match(r"^/api/([a-zA-Z0-9_-]+)", path)
    if m:
        return f"/api/{m.group(1)}"
    return None


def scan_frontend_prefixes() -> dict[str, list[tuple[str, int]]]:
    """Scan src/ for /api/ string literals, return {prefix: [(file, line), ...]}."""
    prefix_to_sites: dict[str, list[tuple[str, int]]] = {}
    # Match "/api/xxx" or $"/api/xxx" or "/api/xxx" in C# strings
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


def scan_backend_prefixes() -> set[str]:
    """Scan backend routes for prefix= values. Excludes _archived."""
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
            raw = m.group(1)
            # Normalize: /api/projects/{project_id}/timeline -> /api/projects
            base = _extract_api_prefix(raw)
            if base:
                prefixes.add(base)
    # face_swap registers deepfake_alias_router with /api/deepfake-creator
    prefixes.add("/api/deepfake-creator")
    return prefixes


def generate_report(
    frontend: dict[str, list[tuple[str, int]]],
    backend: set[str],
    output_path: Path,
) -> None:
    """Write markdown report."""
    output_path.parent.mkdir(parents=True, exist_ok=True)
    lines = [
        "# UI–Backend Route Alignment Report",
        "",
        f"Generated: {date.today().isoformat()}",
        "",
        "| Prefix | Call Sites | Backend Provides | Decision |",
        "|--------|------------|--------------------|----------|",
    ]
    all_prefixes = sorted(set(frontend.keys()) | backend)
    for prefix in all_prefixes:
        sites = frontend.get(prefix, [])
        call_sites = ", ".join(f"{f}:{ln}" for f, ln in sites[:5])
        if len(sites) > 5:
            call_sites += f" (+{len(sites) - 5} more)"
        if not call_sites:
            call_sites = "-"
        provides = "yes" if prefix in backend else "no"
        if prefix in ALLOWLIST_PREFIXES:
            decision = "allowlisted (panel hidden)"
        elif prefix in ALLOWLIST_NO_BACKEND:
            decision = "allowlisted (no backend)"
        elif prefix in DEAD_PREFIXES_NEED_HIDING:
            decision = "FAIL: hide panel"
        elif prefix in backend:
            decision = "ok"
        elif sites:
            decision = "FAIL: no backend"
        else:
            decision = "backend-only"
        lines.append(f"| {prefix} | {call_sites} | {provides} | {decision} |")
    lines.append("")
    lines.append("## Allowlist (archived + panel hidden)")
    lines.append("")
    for p in sorted(ALLOWLIST_PREFIXES):
        lines.append(f"- {p}")
    lines.append("")
    output_path.write_text("\n".join(lines), encoding="utf-8")
    print(f"Wrote {output_path}")


def main() -> int:
    parser = argparse.ArgumentParser(description="Scan UI API calls vs backend routes")
    parser.add_argument(
        "--output",
        type=Path,
        default=None,
        help="Output report path (default: docs/reports/verification/UI_BACKEND_ROUTE_ALIGNMENT_<date>.md)",
    )
    args = parser.parse_args()
    output = args.output or REPORTS_DIR / f"UI_BACKEND_ROUTE_ALIGNMENT_{date.today().isoformat()}.md"
    frontend = scan_frontend_prefixes()
    backend = scan_backend_prefixes()
    generate_report(frontend, backend, output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
