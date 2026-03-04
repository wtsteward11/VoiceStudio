"""
Route-enumeration regression test for audio provenance (Trust Audit Phase 4).

Asserts that every route file that calls _register_audio_file also records
provenance and usage (via record_artifact_provenance_and_usage or
write_provenance_sidecar). Exclusions must be documented.
"""

from __future__ import annotations

import ast
import os
from pathlib import Path

import pytest

# Routes that call _register_audio_file - must also record provenance/usage
# Exclusions:
# - _helpers.py: defines _register_audio_file, does not produce artifacts
_EXCLUDED_FILES = frozenset({
    "_helpers.py",
})


def _find_route_files_with_register() -> list[tuple[Path, list[int]]]:
    """Find all route files that call _register_audio_file."""
    backend_routes = Path(__file__).resolve().parents[2] / "backend" / "api" / "routes"
    if not backend_routes.exists():
        return []

    results = []
    for py_file in backend_routes.rglob("*.py"):
        if py_file.name in _EXCLUDED_FILES:
            continue
        try:
            content = py_file.read_text(encoding="utf-8")
            if "_register_audio_file" in content:
                # Find line numbers
                lines = content.splitlines()
                reg_lines = [i + 1 for i, line in enumerate(lines) if "_register_audio_file" in line]
                results.append((py_file, reg_lines))
        except (OSError, UnicodeDecodeError):
            continue
    return results


def _file_has_provenance_recording(content: str) -> bool:
    """Check if file records provenance (record_artifact_provenance_and_usage or write_provenance_sidecar)."""
    return (
        "record_artifact_provenance_and_usage" in content
        or "write_provenance_sidecar" in content
    )


@pytest.mark.unit
def test_all_register_audio_routes_record_provenance():
    """
    Every route that calls _register_audio_file must also record provenance and usage.

    Trust audit: All artifact producers must write provenance sidecars and
    record synthesis/processing minutes for traceability.
    """
    route_files = _find_route_files_with_register()
    failures = []

    for py_file, _ in route_files:
        try:
            content = py_file.read_text(encoding="utf-8")
            if not _file_has_provenance_recording(content):
                rel = py_file.relative_to(Path(__file__).resolve().parents[2])
                failures.append(str(rel))
        except (OSError, UnicodeDecodeError) as e:
            failures.append(f"{py_file.name}: read error {e}")

    assert not failures, (
        "Routes that call _register_audio_file must also call "
        "record_artifact_provenance_and_usage or write_provenance_sidecar. "
        f"Missing in: {failures}"
    )
