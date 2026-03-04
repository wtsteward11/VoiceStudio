#!/usr/bin/env python3
"""
CI check: fail if routes reintroduce manual artifact creation patterns.

Scans backend/api/routes/**/*.py (AST-based) for:
- _register_audio_file (def or call) — use spine use_cases instead
- _audio_storage — use registry
- sf.write(path, ...) — use create_audio_artifact_from_wav_array
- tempfile.mktemp(...) — use spine or tempfile.NamedTemporaryFile
- open(path, "wb").write(...) — use spine for final output
- record_artifact_provenance_and_usage — spine records internally
- import backend.audio.processing.audio_artifact_registry (R7) — use audio_registry_service
- import backend.audio.processing.content_addressed_audio_cache (R8) — use audio_registry_service

Exits 0 if clean; 1 if violations found.
Output: file:line:rule:snippet

Milestone 6: Regression-proof guardrails.
Milestone 9: Legacy registry/cache import guardrails (R7, R8).
"""
from __future__ import annotations

import ast
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent

EXCLUDED_FILES = frozenset({"engine_audit.py", "engines.py"})

R1 = "FORBIDDEN: _register_audio_file (use spine use_cases)"
R2 = "FORBIDDEN: _audio_storage (use registry)"
R3 = "FORBIDDEN: sf.write to path (use create_audio_artifact_from_wav_array)"
R4 = "FORBIDDEN: tempfile.mktemp (use spine or tempfile.NamedTemporaryFile)"
R5 = "FORBIDDEN: open(..., \"wb\").write for output (use spine)"
R6 = "FORBIDDEN: record_artifact_provenance_and_usage in routes (spine records internally)"
R7 = "FORBIDDEN: import backend.audio.processing.audio_artifact_registry (use audio_registry_service)"
R8 = "FORBIDDEN: import backend.audio.processing.content_addressed_audio_cache (use audio_registry_service)"


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


def _get_snippet(lines: list[str], line_num: int, max_len: int = 80) -> str:
    """Get line content for snippet, truncated."""
    if 1 <= line_num <= len(lines):
        line = lines[line_num - 1].strip()
        return (line[:max_len] + "..." if len(line) > max_len else line)
    return ""


def _is_bytesio_call(node: ast.AST) -> bool:
    """True if node is io.BytesIO() or BytesIO()."""
    if not isinstance(node, ast.Call):
        return False
    func = node.func
    if isinstance(func, ast.Name):
        return func.id == "BytesIO"
    if isinstance(func, ast.Attribute):
        return func.attr == "BytesIO" and (
            (isinstance(func.value, ast.Name) and func.value.id == "io")
            or (isinstance(func.value, ast.Name) and func.value.id == "BytesIO")
        )
    return False


def _is_soundfile_write(node: ast.AST) -> bool:
    """True if node is sf.write(...) or soundfile.write(...)."""
    if not isinstance(node, ast.Call):
        return False
    func = node.func
    if not isinstance(func, ast.Attribute) or func.attr != "write":
        return False
    if isinstance(func.value, ast.Name):
        return func.value.id in ("sf", "soundfile")
    return False


def _is_tempfile_mktemp(node: ast.AST) -> bool:
    """True if node is tempfile.mktemp(...)."""
    if not isinstance(node, ast.Call):
        return False
    func = node.func
    if isinstance(func, ast.Attribute):
        return (
            func.attr == "mktemp"
            and isinstance(func.value, ast.Name)
            and func.value.id == "tempfile"
        )
    return False


def _is_open_call(node: ast.AST) -> bool:
    """True if node is open(...)."""
    if not isinstance(node, ast.Call):
        return False
    func = node.func
    return isinstance(func, ast.Name) and func.id == "open"


def _open_uses_temp_path(node: ast.Call) -> bool:
    """True if open() first arg is from get_path('temp') or tempfile.gettempdir()."""
    if not node.args:
        return False
    first = node.args[0]
    if not isinstance(first, ast.Call):
        return False
    f = first.func
    if isinstance(f, ast.Name) and f.id == "get_path" and first.args:
        arg0 = first.args[0]
        if isinstance(arg0, ast.Constant):
            return str(arg0.value) == "temp"
    if isinstance(f, ast.Attribute):
        if f.attr == "gettempdir" and isinstance(f.value, ast.Name):
            return f.value.id == "tempfile"
    return False


def _check_rule1(node: ast.AST) -> bool:
    """Rule 1: _register_audio_file (def or call)."""
    if isinstance(node, ast.FunctionDef) and node.name == "_register_audio_file":
        return True
    if isinstance(node, ast.Call):
        return isinstance(node.func, ast.Name) and node.func.id == "_register_audio_file"
    return False


def _check_rule2(node: ast.AST) -> bool:
    """Rule 2: _audio_storage."""
    return isinstance(node, ast.Name) and node.id == "_audio_storage"


def _check_rule3(node: ast.AST) -> bool:
    """Rule 3: sf.write(path, ...)."""
    if not _is_soundfile_write(node) or not isinstance(node, ast.Call):
        return False
    first_arg = node.args[0] if node.args else None
    return first_arg is None or not _is_bytesio_call(first_arg)


def _check_rule4(node: ast.AST) -> bool:
    """Rule 4: tempfile.mktemp(."""
    return _is_tempfile_mktemp(node)


def _check_rule6(node: ast.AST) -> bool:
    """Rule 6: record_artifact_provenance_and_usage (call)."""
    if not isinstance(node, ast.Call):
        return False
    func = node.func
    if isinstance(func, ast.Name):
        return func.id == "record_artifact_provenance_and_usage"
    if isinstance(func, ast.Attribute):
        return func.attr == "record_artifact_provenance_and_usage"
    return False


def _check_rule7(node: ast.AST) -> bool:
    """Rule 7: import backend.audio.processing.audio_artifact_registry."""
    if isinstance(node, ast.Import):
        return any(
            alias.name == "backend.audio.processing.audio_artifact_registry"
            or alias.name.startswith("backend.audio.processing.audio_artifact_registry.")
            for alias in node.names
        )
    if isinstance(node, ast.ImportFrom) and node.module:
        return (
            node.module == "backend.audio.processing.audio_artifact_registry"
            or node.module.startswith("backend.audio.processing.audio_artifact_registry.")
        )
    return False


def _check_rule8(node: ast.AST) -> bool:
    """Rule 8: import backend.audio.processing.content_addressed_audio_cache."""
    if isinstance(node, ast.Import):
        return any(
            alias.name == "backend.audio.processing.content_addressed_audio_cache"
            or alias.name.startswith("backend.audio.processing.content_addressed_audio_cache.")
            for alias in node.names
        )
    if isinstance(node, ast.ImportFrom) and node.module:
        return (
            node.module == "backend.audio.processing.content_addressed_audio_cache"
            or node.module.startswith("backend.audio.processing.content_addressed_audio_cache.")
        )
    return False


def _check_rule5_with(node: ast.With, lines: list[str]) -> list[tuple[int, str, str]]:
    """Rule 5: open(...).write(...) for output."""
    for item in node.items:
        if not _is_open_call(item.context_expr):
            continue
        call = item.context_expr
        if not isinstance(call, ast.Call) or len(call.args) < 2:
            continue
        mode = call.args[1].value if isinstance(call.args[1], ast.Constant) else None
        if mode not in ("wb", "w") or _open_uses_temp_path(call):
            continue
        for stmt in ast.walk(node):
            if (
                isinstance(stmt, ast.Call)
                and isinstance(stmt.func, ast.Attribute)
                and stmt.func.attr == "write"
            ):
                ln = getattr(stmt, "lineno", node.lineno)
                return [(ln, R5, _get_snippet(lines, ln))]
    return []


def audit_file_ast(path: Path) -> list[tuple[int, str, str]]:
    """
    AST-based audit. Returns [(line_num, rule_id, snippet), ...].
    """
    violations: list[tuple[int, str, str]] = []
    try:
        content = path.read_text(encoding="utf-8")
        tree = ast.parse(content)
        lines = content.split("\n")
    except (SyntaxError, OSError):
        return violations

    for node in ast.walk(tree):
        line_num = getattr(node, "lineno", None)
        if line_num is None:
            continue
        snippet = _get_snippet(lines, line_num)

        if _check_rule1(node):
            violations.append((line_num, R1, snippet))
        if _check_rule2(node):
            violations.append((line_num, R2, snippet))
        if _check_rule3(node):
            violations.append((line_num, R3, snippet))
        if _check_rule4(node):
            violations.append((line_num, R4, snippet))
        if _check_rule6(node):
            violations.append((line_num, R6, snippet))
        if _check_rule7(node):
            violations.append((line_num, R7, snippet))
        if _check_rule8(node):
            violations.append((line_num, R8, snippet))
        if isinstance(node, ast.With):
            violations.extend(_check_rule5_with(node, lines))

    return violations


def main() -> int:
    route_files = get_route_files()
    all_violations: list[tuple[Path, int, str, str]] = []

    for path in sorted(route_files):
        for line_num, rule_id, snippet in audit_file_ast(path):
            all_violations.append((path, line_num, rule_id, snippet))

    if all_violations:
        for path, line_num, rule_id, snippet in all_violations:
            rel = path.relative_to(ROOT)
            print(f"{rel}:{line_num}:{rule_id}: {snippet}")
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
