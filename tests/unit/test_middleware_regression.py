"""
Phase C-T1: Middleware regression tests.

Validates:
  1. RTL override in path returns 400 (not 500 crash).
  2. Multiple Unicode control characters in path are rejected.
  3. performance_profiling_middleware except block does NOT call call_next twice.
  4. request_size_limit_middleware except block does NOT call call_next twice.
"""

from __future__ import annotations

import ast
from pathlib import Path

import pytest
from fastapi.testclient import TestClient


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _get_middleware_setup_source() -> str:
    """Return the source code of middleware_setup.py."""
    path = Path(__file__).resolve().parents[2] / "backend" / "api" / "middleware_setup.py"
    return path.read_text(encoding="utf-8")


class _CallNextInExceptVisitor(ast.NodeVisitor):
    """AST visitor that collects ``await call_next(...)`` calls inside except handlers."""

    def __init__(self, target_func_name: str) -> None:
        self.target_func_name = target_func_name
        self._inside_target = False
        self.call_next_in_except: list[int] = []

    def visit_AsyncFunctionDef(self, node: ast.AsyncFunctionDef) -> None:
        if node.name == self.target_func_name:
            self._inside_target = True
            self.generic_visit(node)
            self._inside_target = False
        else:
            self.generic_visit(node)

    def visit_ExceptHandler(self, node: ast.ExceptHandler) -> None:
        if not self._inside_target:
            self.generic_visit(node)
            return
        for child in ast.walk(node):
            if isinstance(child, ast.Await) and isinstance(child.value, ast.Call):
                func = child.value.func
                if isinstance(func, ast.Name) and func.id == "call_next":
                    self.call_next_in_except.append(child.lineno)
        self.generic_visit(node)


# ---------------------------------------------------------------------------
# Test 1 & 2: HTTP-level Unicode path rejection
# ---------------------------------------------------------------------------

@pytest.fixture(scope="module")
def _test_app_client():
    """Build a minimal TestClient with InputValidationMiddleware."""
    from fastapi import FastAPI
    from backend.api.middleware.input_validation import InputValidationMiddleware

    app = FastAPI()

    @app.get("/{full_path:path}")
    async def catch_all(full_path: str):
        return {"path": full_path}

    app.add_middleware(InputValidationMiddleware, enabled=True)
    return TestClient(app, raise_server_exceptions=False)


def test_rtl_override_in_path_returns_400_not_crash(_test_app_client: TestClient):
    """RTL override U+202E in path must yield 400, never 500."""
    response = _test_app_client.get("/api/endpoints/metrics/test\u202e")
    assert response.status_code != 500, "RTL override caused a server crash"
    assert response.status_code == 400


SAFE_CONTROL_CHARS = [
    "\u202e",  # RTL override  (Cf)
    "\u200b",  # Zero-width space (Cf)
    "\u2028",  # Line separator (Zl)
    "\ufeff",  # BOM (Cf)
]


@pytest.mark.parametrize("char", SAFE_CONTROL_CHARS, ids=["RTL", "ZWS", "LSEP", "BOM"])
def test_unicode_control_chars_in_path_rejected(_test_app_client: TestClient, char: str):
    """Each forbidden Unicode control character must be rejected with 400."""
    response = _test_app_client.get(f"/api/test{char}path")
    assert response.status_code == 400, (
        f"Control char U+{ord(char):04X} was not rejected (got {response.status_code})"
    )
    body = response.json()
    assert body.get("error") == "INVALID_PATH"


def test_null_byte_in_path_rejected(_test_app_client: TestClient):
    """Null byte in path is rejected (by httpx at transport level, or by middleware)."""
    import httpx

    try:
        response = _test_app_client.get("/api/test\x00path")
        assert response.status_code == 400
    except httpx.InvalidURL:
        pass  # httpx rejects null bytes before they reach the server — acceptable


# ---------------------------------------------------------------------------
# Test 3 & 4: AST-based verification of call_next absence in except blocks
# ---------------------------------------------------------------------------

def test_performance_middleware_error_does_not_call_call_next_twice():
    """Statically verify performance_profiling_middleware except blocks lack call_next."""
    source = _get_middleware_setup_source()
    tree = ast.parse(source)
    visitor = _CallNextInExceptVisitor("performance_profiling_middleware")
    visitor.visit(tree)
    assert visitor.call_next_in_except == [], (
        f"call_next found in except handler at line(s): {visitor.call_next_in_except}"
    )


def test_request_size_middleware_error_does_not_call_call_next_twice():
    """Statically verify request_size_limit_middleware except blocks lack call_next."""
    source = _get_middleware_setup_source()
    tree = ast.parse(source)
    visitor = _CallNextInExceptVisitor("request_size_limit_middleware")
    visitor.visit(tree)
    assert visitor.call_next_in_except == [], (
        f"call_next found in except handler at line(s): {visitor.call_next_in_except}"
    )
