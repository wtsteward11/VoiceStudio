#!/usr/bin/env python3
"""
Generate IBackendClient unresolved queue from code and baseline.

Derives the true unresolved list by:
1. Scanning ViewModels/Panels for IBackendClient constructor parameters
2. Loading baseline - lines with # MIGRATED are resolved
3. Outputting unresolved file paths (and optionally JSON)

Usage:
    python scripts/ci/generate_ibackendclient_queue.py           # Print unresolved list
    python scripts/ci/generate_ibackendclient_queue.py --json   # Emit JSON
    python scripts/ci/generate_ibackendclient_queue.py --validate  # Exit 1 if queue doc contradicts

Exits 0 if successful; 1 if --validate finds contradictions.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
APP = ROOT / "src" / "VoiceStudio.App"
BASELINE = ROOT / ".ci" / "ibackendclient_baseline.txt"
QUEUE_DOC = ROOT / "docs" / "design" / "IBACKENDCLIENT_UNRESOLVED_QUEUE.md"

SCAN_DIRS = [
    APP / "Views" / "Panels",
    APP / "ViewModels",
]

# Match IBackendClient as constructor parameter (exclude field declarations)
IBACKENDCLIENT_PATTERN = re.compile(r"IBackendClient\s+\w+")
READONLY_PATTERN = re.compile(r"readonly\s+IBackendClient|private\s+readonly\s+IBackendClient")
MIGRATED_PATTERN = re.compile(r"#\s*(\w+ViewModel)\s+MIGRATED", re.IGNORECASE)


def is_constructor_param(line: str) -> bool:
    """True if line has IBackendClient and is not a field declaration."""
    if "IBackendClient" not in line:
        return False
    if READONLY_PATTERN.search(line):
        return False
    return bool(IBACKENDCLIENT_PATTERN.search(line))


def scan_for_consumers() -> set[Path]:
    """Return set of file paths that have IBackendClient in constructor."""
    consumers: set[Path] = set()
    for scan_dir in SCAN_DIRS:
        if not scan_dir.exists():
            continue
        for path in scan_dir.rglob("*.cs"):
            try:
                text = path.read_text(encoding="utf-8")
            except (OSError, UnicodeDecodeError):
                continue
            for line in text.splitlines():
                if is_constructor_param(line):
                    consumers.add(path)
                    break
    return consumers


def load_migrated_from_baseline() -> set[str]:
    """
    Parse baseline; return set of ViewModel stem names that are MIGRATED.
    Format: # ViewModelName MIGRATED to IClientName (date)
    """
    migrated: set[str] = set()
    if not BASELINE.exists():
        return migrated
    for line in BASELINE.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line.startswith("#") and "MIGRATED" in line:
            m = MIGRATED_PATTERN.search(line)
            if m:
                migrated.add(m.group(1))
    return migrated


def get_unresolved(consumers: set[Path], migrated_stems: set[str]) -> list[Path]:
    """Return sorted list of unresolved consumer paths."""
    unresolved: list[Path] = []
    for path in consumers:
        if path.stem not in migrated_stems:
            unresolved.append(path)
    return sorted(unresolved, key=lambda p: p.as_posix())


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate IBackendClient unresolved queue from code")
    parser.add_argument("--json", action="store_true", help="Emit JSON output")
    parser.add_argument(
        "--validate",
        action="store_true",
        help="Exit 1 if queue doc lists migrated files as unresolved",
    )
    args = parser.parse_args()

    consumers = scan_for_consumers()
    migrated_stems = load_migrated_from_baseline()
    unresolved = get_unresolved(consumers, migrated_stems)

    # Relative paths for output
    unresolved_rel = [str(p.relative_to(ROOT).as_posix()) for p in unresolved]

    if args.json:
        out = {
            "unresolved": unresolved_rel,
            "count": len(unresolved_rel),
            "migrated_count": len(migrated_stems),
        }
        print(json.dumps(out, indent=2))
        return 0

    if args.validate and QUEUE_DOC.exists():
        # Check if queue doc lists any migrated file as unresolved
        # We only fail if the doc explicitly lists a MIGRATED stem as unresolved
        doc_text = QUEUE_DOC.read_text(encoding="utf-8")
        for stem in migrated_stems:
            # If doc has "ViewModelName" in unresolved context (e.g. rank table without strikethrough)
            # Simple heuristic: if we see "| N | `.../ViewModelName.cs`" without ~~strikethrough~~
            pattern = rf"\|\s*\d+\s*\|\s*`[^`]*{re.escape(stem)}\.cs`"
            if re.search(pattern, doc_text):
                # Check if it's in a MIGRATED/strikethrough section
                if f"~~{stem}~~" not in doc_text and f"MIGRATED" not in doc_text.split(stem)[0][-200:]:
                    print(f"VALIDATE_FAIL: {stem} is migrated but may appear as unresolved in queue doc")
                    return 1
        # Also check: any file in our unresolved list that queue doc says is migrated?
        for rel in unresolved_rel:
            stem = Path(rel).stem
            if stem in migrated_stems:
                print(f"VALIDATE_FAIL: {rel} is in code scan but baseline says MIGRATED")
                return 1
        print("Validate: queue doc consistent with generated list")
        return 0

    for rel in unresolved_rel:
        print(rel)
    return 0


if __name__ == "__main__":
    sys.exit(main())
