#!/usr/bin/env python3
"""CI check: fail if installer size audit doc still contains placeholder text."""
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
AUDIT = ROOT / "docs/reports/INSTALLER_SIZE_AUDIT.md"
PLACEHOLDERS = ["_run script_", "_if used_", "_build_"]

if not AUDIT.exists():
    print(f"FAIL: {AUDIT} does not exist")
    sys.exit(1)

content = AUDIT.read_text(encoding="utf-8")
bad = [p for p in PLACEHOLDERS if p in content]
if bad:
    for p in bad:
        print(f"FAIL: placeholder found: {p!r}")
    print("Run installer/prepare-runtime.ps1 and fill in real MB values.")
    sys.exit(1)

print("OK: Installer audit contains no placeholders.")
