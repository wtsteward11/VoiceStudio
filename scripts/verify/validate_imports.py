#!/usr/bin/env python3
"""
Validate Python Imports

Ensures all critical Python modules can be imported successfully.
Catches ModuleNotFoundError and ImportError before commit.

Exit codes:
  0 - All imports valid
  1 - Import validation failed
"""


import io
import sys

# Ensure UTF-8 output on Windows console
if sys.platform == "win32" and hasattr(sys.stdout, "buffer"):
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8", errors="replace")


def validate_imports():
    """Validate that critical modules can be imported."""
    errors = []

    # Critical modules to validate (must be importable for governance tooling)
    # Only includes modules required for verification/governance; backend/engines have optional deps
    critical_modules = [
        "tools.overseer",
        "tools.overseer.models",
        "tools.overseer.cli.main",
        "tools.overseer.issues.store",
        "tools.context",
        "tools.onboarding",
    ]

    print("Validating critical module imports...")

    for module in critical_modules:
        try:
            __import__(module)
            print(f"  ✓ {module}")
        except (ModuleNotFoundError, ImportError) as e:
            errors.append(f"{module}: {e}")
            print(f"  ✗ {module}: {e}")

    if errors:
        print(f"\n❌ Import validation failed ({len(errors)} errors)")
        return 1

    print(f"\n✅ All {len(critical_modules)} critical imports valid")
    return 0


if __name__ == "__main__":
    sys.exit(validate_imports())
