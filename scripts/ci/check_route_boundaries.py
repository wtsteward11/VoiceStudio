#!/usr/bin/env python3
"""
CI check: fail if routes reintroduce prototype soup behaviors.

Scans backend/api/routes/**/*.py for:
- Route-to-route imports (from ..routes, from .voice import synthesize, etc.)
- from app. / import app. (packaging poison; use backend.* or proper module)
- sys.path.insert usage
- Repo-relative persistent writes (Path("backups"), Path("data"), os.path.join("data",...), open("data/..."))

Exits 0 if clean; 1 if violations found.
Output: file:line: PATTERN_DESC: <matched_line>

Migration bypass: set VOICESTUDIO_SKIP_ROUTE_BOUNDARIES=1 to skip (local use only; CI does not set it).
"""
from __future__ import annotations

import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent

if os.environ.get("VOICESTUDIO_SKIP_ROUTE_BOUNDARIES") == "1":
    sys.exit(0)

# (pattern, description) - order matters for reporting
# (?!_) = allow only underscore-prefixed internal modules (_persistent_store, _engine_shared, etc.)
VIOLATION_PATTERNS = [
    (r"from\s+api\.utils\.", "route imports api.utils (use backend.services)"),
    (r"from\s+backend\.api\.utils\.", "route imports backend.api.utils (use backend.services)"),
    (r"from\s+\.\.?routes\b", "route-to-route import (from ..routes)"),
    (r"from\s+\.(?!_)\w+\s+import", "route-to-route import (from .<route>; use services)"),
    (r"from\s+backend\.api\.routes\.(?!_)\w+", "route-to-route import (from backend.api.routes.<route>; use services)"),
    (r"import\s+backend\.api\.routes\.(?!_)\w+", "route-to-route import (import backend.api.routes.<route>; use services)"),
    (r"from\s+app\.", "from app. import (use backend.* or proper module)"),
    (r"import\s+app\.", "import app. (use backend.* or proper module)"),
    (r"sys\.path\.insert\s*\(", "sys.path.insert (use proper module imports)"),
    (r'Path\s*\(\s*["\']backups["\']\s*\)', "repo-relative Path(backups) - use get_path"),
    (r'Path\s*\(\s*["\']data["\']\s*\)', "repo-relative Path(data) - use get_path"),
    (r'Path\s*\(\s*["\']data/', "repo-relative Path(data/...) - use get_path"),
    (r'os\.path\.join\s*\(\s*["\']data["\']\s*', "repo-relative os.path.join(data,...) - use get_path"),
    (r'open\s*\(\s*["\']data/', "repo-relative open(data/...) - use get_path"),
    (r'os\.makedirs\s*\(\s*["\']data', "repo-relative os.makedirs(data...) - use get_path"),
    # GAP A: CWD/repo-relative and hardcoded profile paths
    (r'f["\']profiles/', "CWD-relative profiles path (use PathService/ProfileService)"),
    (r'["\']profiles/', "repo-relative profiles path (use get_path)"),
    (r'Path\s*\(\s*["\']profiles["\']', "repo-relative Path(profiles) - use get_path"),
    (r'os\.path\.join\s*\(\s*["\']profiles["\']', "repo-relative os.path.join(profiles,...) - use get_path"),
    (r'os\.path\.join\s*\(\s*os\.expanduser\s*\(\s*["\']~["\']\s*\)\s*,\s*["\']\.voicestudio["\']', "hardcoded ~/.voicestudio (use get_path)"),
    (r'Path\.home\s*\(\s*\)\s*/\s*["\']\.voicestudio["\']', "hardcoded Path.home()/.voicestudio (use get_path)"),
    (r'os\.path\.join\s*\([^)]*["\']\.voicestudio["\']', "hardcoded .voicestudio path (use get_path)"),
    # M8: CWD-relative path literals
    (r'f["\']projects/', "CWD-relative projects path (use PathService.get_projects_dir())"),
    (r'["\']projects/', "repo-relative projects path (use get_path)"),
    (r'Path\s*\(\s*["\']projects["\']', "repo-relative Path(projects) - use get_path"),
    (r'os\.path\.join\s*\(\s*["\']projects["\']', "repo-relative os.path.join(projects,...) - use get_path"),
    (r'f["\']models/', "CWD-relative models path (use PathService.get_models_dir())"),
    (r'["\']models/', "repo-relative models path (use get_path)"),
    (r'Path\s*\(\s*["\']models["\']', "repo-relative Path(models) - use get_path"),
    (r'os\.path\.join\s*\(\s*["\']models["\']', "repo-relative os.path.join(models,...) - use get_path"),
    (r'["\']runtime/', "repo-relative runtime path (use get_path)"),
    (r'["\']outputs/', "repo-relative outputs path (use get_path)"),
    (r'f["\'][^"\']*profiles/[^"\']*reference\.wav', "CWD-relative f-string profiles/reference.wav path"),
    # Phase 3.4: forbid inline regression in routes (delegate to quality_trends_service)
    (r"numerator\s*=\s*sum\s*\(\s*\(.*?-\s*x_mean\)", "inline regression (numerator); use quality_trends_service"),
    (r"denominator\s*=\s*sum\s*\(\s*\(.*?-\s*x_mean\)", "inline regression (denominator); use quality_trends_service"),
]

QUALITY_ROUTE_EXTRA_PATTERNS = [
    (r"datetime\.utcnow\s*\(", "datetime.utcnow (use datetime.now(timezone.utc))"),
]

# Path-related pattern indices (0-based in VIOLATION_PATTERNS) that may be exempted
# when the line contains allowed substrings (get_path, PathService, etc.).
# Import, route-to-route, sys.path, and quality-specific patterns are never exemptable.
EXEMPTABLE_PATTERN_INDICES = frozenset({
    9, 10, 11, 12, 13, 14,  # Path/join/open/makedirs data, backups
    15, 16, 17, 18,          # profiles paths
    19, 20, 21,              # .voicestudio paths
    22, 23, 24, 25,          # projects paths
    26, 27, 28, 29,          # models paths
    30, 31, 32,              # runtime, outputs, profiles/reference.wav
})

EXEMPTION_SUBSTRINGS = (
    "tempfile",
    "get_path(",
    "backend.config.path_config",
    "PathService",
    "ProfileService",
    "resolve_reference_audio_path",
    "get_projects_dir",
    "get_models_dir",
    "ProfileStorageService",
    "get_profile_storage",
)


EXCLUDED_FILES = frozenset({"engine_audit.py", "engines.py", "__init__.py"})


def get_route_files() -> list[Path]:
    """Return Python files in routes, excluding _archived and EXCLUDED_FILES."""
    routes_dir = ROOT / "backend" / "api" / "routes"
    if not routes_dir.exists():
        return []
    files = list(routes_dir.rglob("*.py"))
    return [
        f
        for f in files
        if "_archived" not in str(f) and f.name not in EXCLUDED_FILES
    ]


def is_exempt(line: str) -> bool:
    """Skip lines that use tempfile or get_path (allowed patterns)."""
    return any(ex in line for ex in EXEMPTION_SUBSTRINGS)


def audit_file(path: Path) -> list[tuple[int, str, str]]:
    """Return [(line_num, desc, line), ...] for violations."""
    violations = []
    try:
        content = path.read_text(encoding="utf-8")
        lines = content.split("\n")
    except Exception:
        return violations

    patterns = list(VIOLATION_PATTERNS)
    if path.name == "quality.py":
        patterns = patterns + QUALITY_ROUTE_EXTRA_PATTERNS

    for i, line in enumerate(lines, start=1):
        for idx, (pattern, desc) in enumerate(patterns):
            if re.search(pattern, line):
                if idx in EXEMPTABLE_PATTERN_INDICES and is_exempt(line):
                    continue  # Path pattern + allowed usage; skip
                violations.append((i, desc, line.strip()))
                break

    return violations


def main() -> int:
    route_files = get_route_files()
    all_violations: list[tuple[Path, int, str, str]] = []

    for path in sorted(route_files):
        rel = path.relative_to(ROOT)
        for line_num, desc, line in audit_file(path):
            all_violations.append((path, line_num, desc, line))

    if all_violations:
        for path, line_num, desc, line in all_violations:
            rel = path.relative_to(ROOT)
            snippet = (line[:80] + "..." if len(line) > 80 else line)
            print(f"{rel}:{line_num}: {desc}: {snippet}")
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
