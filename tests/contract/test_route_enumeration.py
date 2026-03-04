"""
M6: Route enumeration tests.

Enumerates routes that return audio_id in response and verifies:
1. The set is non-empty and documented
2. Routes that return audio_id pass artifact spine compliance
3. At least one synthesis route can resolve audio_id via AudioRegistry (when called)
"""
from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent


def _get_routes_returning_audio_id() -> list[tuple[str, str]]:
    """Enumerate routes whose response model contains audio_id. Returns [(path, method), ...]."""
    import backend.api.main as main_module
    from backend.api.main import app

    # Force schema regeneration (test isolation: earlier tests may have invalidated cache)
    main_module._openapi_schema_generated = False
    app.openapi_schema = None

    result: list[tuple[str, str]] = []
    schema = app.openapi()
    paths = schema.get("paths", {})
    schemas = schema.get("components", {}).get("schemas", {})

    def _schema_has_audio_id(schema_ref: str | dict, _seen: frozenset[str] | None = None) -> bool:
        seen = _seen or frozenset()

        if isinstance(schema_ref, str) and schema_ref.startswith("#/components/schemas/"):
            name = schema_ref.split("/")[-1]
            if name in seen:
                return False
            sub = schemas.get(name, {})
            return _schema_has_audio_id(sub, seen | {name})

        if isinstance(schema_ref, dict):
            ref = schema_ref.get("$ref")
            if ref:
                return _schema_has_audio_id(ref, seen)
            for sub in schema_ref.get("allOf", []) or schema_ref.get("oneOf", []):
                if _schema_has_audio_id(sub, seen):
                    return True
            props = schema_ref.get("properties", {})
            if "audio_id" in props:
                return True
            for v in props.values():
                if isinstance(v, dict) and "$ref" in v:
                    if _schema_has_audio_id(v["$ref"], seen):
                        return True
                elif isinstance(v, dict):
                    if _schema_has_audio_id(v, seen):
                        return True
            return False
        return False

    for path, path_item in paths.items():
        for method in ["get", "post", "put", "patch", "delete"]:
            op = path_item.get(method, {})
            if not op:
                continue
            responses = op.get("responses", {})
            for code, resp in responses.items():
                if code.startswith("2"):
                    content = resp.get("content", {})
                    for mt, media in content.items():
                        if "application/json" in mt or "json" in mt:
                            schema_ref = media.get("schema", {})
                            if schema_ref and _schema_has_audio_id(schema_ref):
                                result.append((path, method.upper()))
                                break
                    break
    return list(dict.fromkeys(result))


def _load_compliance_module():
    """Load check_artifact_spine_compliance module."""
    path = ROOT / "scripts" / "ci" / "check_artifact_spine_compliance.py"
    spec = importlib.util.spec_from_file_location("check_artifact_spine_compliance", path)
    if spec is None or spec.loader is None:
        raise ImportError(f"Cannot load compliance module from {path}")
    mod = importlib.util.module_from_spec(spec)
    sys.modules["check_artifact_spine_compliance"] = mod
    spec.loader.exec_module(mod)
    return mod


def test_routes_returning_audio_id_are_enumerated():
    """Routes with audio_id in response are enumerated and non-empty."""
    from backend.api.main import _register_all_routes, app

    _register_all_routes()
    app.openapi_schema = None

    routes = _get_routes_returning_audio_id()
    assert len(routes) >= 1, (
        "No routes returning audio_id found. "
        "Expected at least /api/voice/synthesize or similar."
    )
    paths_set = {p for p, _ in routes}
    assert len(paths_set) >= 1, "No unique paths with audio_id"
    assert any("/voice" in p or "synthesize" in p or "audio" in p for p, _ in routes), (
        "Expected at least one voice/synthesis/audio route to return audio_id"
    )


def test_audio_id_routes_pass_compliance():
    """Route source files must pass artifact spine compliance."""
    mod = _load_compliance_module()
    audit_file_ast = mod.audit_file_ast
    get_route_files = mod.get_route_files

    all_violations: list[tuple[Path, int, str, str]] = []
    for path in get_route_files():
        for line_num, rule_id, snippet in audit_file_ast(path):
            all_violations.append((path, line_num, rule_id, snippet))

    if all_violations:
        rel_root = ROOT
        lines = []
        for path, line_num, rule_id, snippet in all_violations:
            try:
                rel = path.relative_to(rel_root)
            except ValueError:
                rel = path
            lines.append(f"{rel}:{line_num}:{rule_id}: {snippet}")
        pytest.fail(
            "Artifact spine compliance violations in route source files:\n" + "\n".join(lines)
        )


def test_audio_id_resolution_contract():
    """AudioRegistry.get_path resolves audio_id for registered artifacts."""
    from backend.services.audio_artifacts import AudioRegistry

    aid = "test_resolution_nonexistent"
    path = AudioRegistry.get_path(aid)
    assert path is None or isinstance(path, str), "get_path should return str or None."

    import numpy as np

    from backend.services.audio_artifacts.use_cases import create_audio_artifact_from_wav_array

    sr = 22050
    duration_sec = 0.1
    samples = int(sr * duration_sec)
    audio = np.zeros(samples, dtype=np.float32)
    registered_id, cached_path, _ = create_audio_artifact_from_wav_array(
        audio, sr, created_by="m6_test"
    )
    resolved = AudioRegistry.get_path(registered_id)
    assert resolved is not None, f"Registered audio_id {registered_id} should resolve"
    assert Path(resolved).exists(), f"Resolved path {resolved} should exist"
