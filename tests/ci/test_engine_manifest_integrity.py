"""
CI gate: Engine manifest integrity.

All engine.manifest.json files must load successfully with required fields.
Engines listed in .ci/disabled_engines.json are exempt (quarantine with reason).
"""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from app.core.engines.manifest_loader import load_engine_manifest


def _get_disabled_engines() -> dict[str, str]:
    """Load quarantine list: engine_id -> reason."""
    path = Path(__file__).resolve().parents[2] / ".ci" / "disabled_engines.json"
    if not path.exists():
        return {}
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    return data if isinstance(data, dict) else {}


def _find_manifest_paths() -> list[Path]:
    """Return path for each engine.manifest.json."""
    repo_root = Path(__file__).resolve().parents[2]
    engines_dir = repo_root / "engines"
    if not engines_dir.exists():
        return []
    return sorted(engines_dir.rglob("engine.manifest.json"))


def _manifest_id(path: Path) -> str:
    repo = Path(__file__).resolve().parents[2]
    return str(path.relative_to(repo)) if path.is_relative_to(repo) else path.name


@pytest.mark.parametrize("manifest_path", _find_manifest_paths(), ids=_manifest_id)
def test_engine_manifest_integrity(manifest_path: Path) -> None:
    """
    Each engine manifest must load with required fields (engine_id, type, name, version, entry_point).
    Quarantined engines in .ci/disabled_engines.json are exempt.
    """
    disabled = _get_disabled_engines()
    engine_dir = manifest_path.parent.name

    try:
        manifest = load_engine_manifest(str(manifest_path))
        engine_id = manifest.get("engine_id") or manifest.get("id")
        if engine_id and engine_id in disabled:
            pytest.skip(f"Engine {engine_id} quarantined: {disabled[engine_id]}")
        if engine_dir in disabled and (not engine_id or engine_id not in disabled):
            pytest.skip(f"Engine {engine_dir} quarantined: {disabled[engine_dir]}")
    except ValueError as e:
        pytest.fail(f"Manifest {manifest_path}: {e}")
    except FileNotFoundError as e:
        pytest.fail(f"Manifest {manifest_path}: {e}")
