"""Curated override paths and authority_module strings (Task 54)."""

from __future__ import annotations

import json
import re
from pathlib import Path

import pytest

_REPO_ROOT = Path(__file__).resolve().parents[3]
_OVERRIDES = (
    _REPO_ROOT / "tools" / "overseer" / "data" / "engine_truth_overrides.json"
)
_AUTHORITY_RE = re.compile(r"^[\w.]+\:[\w]+$")


def _preflight_authority_allowlist() -> frozenset[str]:
    from backend.services.preflight_registry import get_engine_preflight_callables

    out: set[str] = set()
    for fn in get_engine_preflight_callables().values():
        mod = getattr(fn, "__module__", "") or ""
        name = getattr(fn, "__name__", "") or ""
        if mod and name:
            out.add(f"{mod}:{name}")
    return frozenset(out)


@pytest.fixture(scope="module")
def engine_truth_overrides() -> dict:
    data = json.loads(_OVERRIDES.read_text(encoding="utf-8"))
    assert isinstance(data, dict)
    return data


def test_latest_proof_doc_paths_exist(engine_truth_overrides: dict) -> None:
    engines = engine_truth_overrides.get("engines") or {}
    assert isinstance(engines, dict)
    for eid, row in engines.items():
        if not isinstance(row, dict):
            continue
        doc = row.get("latest_proof_doc")
        if doc is None or doc is False:
            continue
        assert isinstance(doc, str), (
            f"{eid}: latest_proof_doc must be str or null"
        )
        path = _REPO_ROOT / doc
        assert path.is_file(), f"{eid}: latest_proof_doc missing: {doc}"


def test_authority_module_shape_or_allowlist(engine_truth_overrides: dict) -> None:
    allow = _preflight_authority_allowlist()
    engines = engine_truth_overrides.get("engines") or {}
    assert isinstance(engines, dict)
    for eid, row in engines.items():
        if not isinstance(row, dict):
            continue
        auth = row.get("authority_module")
        if auth is None:
            continue
        assert isinstance(auth, str), (
            f"{eid}: authority_module must be str or null"
        )
        assert _AUTHORITY_RE.match(auth), (
            f"{eid}: authority_module bad shape: {auth!r}"
        )
        assert auth in allow, (
            f"{eid}: authority_module not in preflight registry: {auth!r}"
        )
