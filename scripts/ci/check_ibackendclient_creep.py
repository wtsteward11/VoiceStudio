#!/usr/bin/env python3
"""
CI guardrail: detect new IBackendClient usage in panel/ViewModel constructors,
and direct SynthesizeVoiceAsync calls in panels/ViewModels.

Per Timeline hardening:
1. IBackendClient: If a class in Views/Panels/ or ViewModels/ takes IBackendClient
   as a constructor parameter AND a domain seam exists, flag it.
2. SynthesizeVoiceAsync: Disallowed as direct call in panel/ViewModel code.
   Allowed caller: TimelineSynthesisService (and other canonical services) only.

Scans:
- src/VoiceStudio.App/Views/Panels/**/*.cs
- src/VoiceStudio.App/ViewModels/**/*.cs

Baseline: .ci/ibackendclient_baseline.txt lists approved IBackendClient usages.
SynthesizeVoiceAsync: .ci/synthesizevoice_baseline.txt lists deferred migrations.

Exits 0 if clean; 1 if new violations found.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
APP = ROOT / "src" / "VoiceStudio.App"
BASELINE = ROOT / ".ci" / "ibackendclient_baseline.txt"
SYNTHESIZE_BASELINE = ROOT / ".ci" / "synthesizevoice_baseline.txt"

SCAN_DIRS = [
    APP / "Views" / "Panels",
    APP / "ViewModels",
]

# ViewModels that have a domain seam and MUST NOT take IBackendClient.
# If they do, fail regardless of baseline (anti-backslide). Add when migrating.
MIGRATED_NO_IBACKENDCLIENT = [
    "EmotionStyleControlViewModel",
    "EmotionControlViewModel",
]

# Match IBackendClient as constructor parameter (exclude field declarations with readonly)
IBACKENDCLIENT_PATTERN = re.compile(r"IBackendClient\s+\w+")
READONLY_PATTERN = re.compile(r"readonly\s+IBackendClient|private\s+readonly\s+IBackendClient")


def is_constructor_param(line: str) -> bool:
    """True if line has IBackendClient and is not a field declaration."""
    if "IBackendClient" not in line:
        return False
    if READONLY_PATTERN.search(line):
        return False
    return bool(IBACKENDCLIENT_PATTERN.search(line))


def scan_file(path: Path) -> list[tuple[int, str]]:
    """Return list of (line_no, line_text) for constructor param matches."""
    matches = []
    try:
        text = path.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return matches

    for i, line in enumerate(text.splitlines(), 1):
        if is_constructor_param(line):
            matches.append((i, line.strip()))
    return matches


def load_baseline() -> set[str]:
    """Load baseline of allowed usages (path:line, strip comments)."""
    if not BASELINE.exists():
        return set()
    allowed = set()
    for line in BASELINE.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            key = line.split("#")[0].strip()
            if key:
                allowed.add(key)
    return allowed


def load_synthesize_baseline() -> set[str]:
    """Load baseline of deferred SynthesizeVoiceAsync calls."""
    if not SYNTHESIZE_BASELINE.exists():
        return set()
    allowed = set()
    for line in SYNTHESIZE_BASELINE.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            key = line.split("#")[0].strip()
            if key:
                allowed.add(key)
    return allowed


SYNTHESIZE_PATTERN = re.compile(r"SynthesizeVoiceAsync\s*\(")


def is_synthesis_service_call(line: str) -> bool:
    """True if SynthesizeVoiceAsync is called through IVoiceSynthesisService (allowed)."""
    return bool(re.search(r"\w*[Vv]oice[Ss]ynthesis[Ss]ervice\s*\.\s*SynthesizeVoiceAsync", line))


def scan_synthesize_file(path: Path) -> list[tuple[int, str]]:
    """Return list of (line_no, line_text) for SynthesizeVoiceAsync calls (excludes service calls)."""
    matches = []
    try:
        text = path.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return matches

    for i, line in enumerate(text.splitlines(), 1):
        if SYNTHESIZE_PATTERN.search(line) and not is_synthesis_service_call(line):
            matches.append((i, line.strip()))
    return matches


def main() -> int:
    violations: list[tuple[Path, int, str, str]] = []  # path, line, text, kind
    baseline = load_baseline()
    syn_baseline = load_synthesize_baseline()

    # 1. IBackendClient creep
    for scan_dir in SCAN_DIRS:
        if not scan_dir.exists():
            continue
        for path in scan_dir.rglob("*.cs"):
            stem = path.stem
            for line_no, line_text in scan_file(path):
                rel = path.relative_to(ROOT)
                key = f"{rel.as_posix()}:{line_no}"
                # Anti-backslide: migrated domains must not take IBackendClient
                if stem in MIGRATED_NO_IBACKENDCLIENT:
                    violations.append((path, line_no, line_text, "IBackendClient"))
                elif key not in baseline:
                    violations.append((path, line_no, line_text, "IBackendClient"))

    # 2. SynthesizeVoiceAsync ownership: no direct calls in panels/ViewModels (unless in baseline)
    for scan_dir in SCAN_DIRS:
        if not scan_dir.exists():
            continue
        for path in scan_dir.rglob("*.cs"):
            for line_no, line_text in scan_synthesize_file(path):
                rel = path.relative_to(ROOT)
                key = f"{rel.as_posix()}:{line_no}"
                if key not in syn_baseline:
                    violations.append((path, line_no, line_text, "SynthesizeVoiceAsync"))

    if violations:
        ibc = [v for v in violations if v[3] == "IBackendClient"]
        syn = [v for v in violations if v[3] == "SynthesizeVoiceAsync"]

        if ibc:
            print("IBACKENDCLIENT_CREEP: New IBackendClient in panel/ViewModel constructor")
            print("Add to .ci/ibackendclient_baseline.txt with justification, or migrate to domain seam.")
            for path, line_no, line_text, _ in ibc:
                rel = path.relative_to(ROOT)
                print(f"  {rel.as_posix()}:{line_no}: {line_text[:70]}{'...' if len(line_text) > 70 else ''}")
            print()

        if syn:
            print("SYNTHESIZE_OWNERSHIP: Direct SynthesizeVoiceAsync in panel/ViewModel (use ITimelineSynthesisService)")
            for path, line_no, line_text, _ in syn:
                rel = path.relative_to(ROOT)
                print(f"  {rel.as_posix()}:{line_no}: {line_text[:70]}{'...' if len(line_text) > 70 else ''}")
            print()

        print("See docs/reports/verification/REQUEST_COORDINATION_AUDIT_*.md")
        return 1

    print("IBackendClient creep + SynthesizeVoiceAsync ownership: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
