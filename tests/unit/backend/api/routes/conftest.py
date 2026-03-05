"""Shared fixtures for route tests.

Ensures sys.path includes the project root and clears the global
response cache before each test to prevent leaking between methods.
"""

import sys
from pathlib import Path

import pytest

_project_root = str(Path(__file__).parent.parent.parent.parent.parent.parent)
if _project_root not in sys.path:
    sys.path.insert(0, _project_root)


@pytest.fixture(autouse=True)
def clear_response_cache():
    """Clear API response cache before and after each test."""
    try:
        from backend.api.optimization import _response_cache

        _response_cache.clear()
    # ALLOWED: bare except - optional dependency, import failure acceptable
    except ImportError:
        pass
    yield
    try:
        from backend.api.optimization import _response_cache

        _response_cache.clear()
    # ALLOWED: bare except - optional dependency, import failure acceptable
    except ImportError:
        pass
