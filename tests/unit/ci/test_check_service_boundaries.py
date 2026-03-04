"""
Unit tests for check_service_boundaries.py (Phase 2.1, 2.2).

Verifies the checker detects service->api imports and does not flag
valid service code or allowlisted imports.
"""

from __future__ import annotations

import importlib.util
import tempfile
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[3]


def _load_checker():
    """Load check_service_boundaries module without running main."""
    spec = importlib.util.spec_from_file_location(
        "check_service_boundaries",
        ROOT / "scripts" / "ci" / "check_service_boundaries.py",
    )
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def _audit_content(content: str) -> list[tuple[int, str, str]]:
    """Run audit_file on content; return violations."""
    mod = _load_checker()
    with tempfile.NamedTemporaryFile(mode="w", suffix=".py", delete=False) as f:
        f.write(content)
        f.flush()
        path = Path(f.name)
    try:
        return mod.audit_file(path)
    finally:
        path.unlink(missing_ok=True)


class TestCheckServiceBoundariesViolations:
    """Violations must be detected."""

    def test_from_backend_api_routes_detected(self):
        """from backend.api.routes._persistent_store must be flagged."""
        content = "from backend.api.routes._persistent_store import PersistentStore\n"
        violations = _audit_content(content)
        assert len(violations) >= 1
        assert any("api" in v[1] for v in violations)

    def test_from_backend_api_ws_detected(self):
        """from backend.api.ws import realtime must be flagged."""
        content = "from backend.api.ws import realtime\n"
        violations = _audit_content(content)
        assert len(violations) >= 1
        assert any("api" in v[1] for v in violations)

    def test_import_backend_api_routes_detected(self):
        """import backend.api.routes must be flagged."""
        content = "import backend.api.routes\n"
        violations = _audit_content(content)
        assert len(violations) >= 1
        assert any("api" in v[1] for v in violations)

    def test_from_backend_api_ml_optimization_detected(self):
        """from backend.api.ml_optimization must be flagged."""
        content = "from backend.api.ml_optimization import HyperparameterOptimizer\n"
        violations = _audit_content(content)
        assert len(violations) >= 1
        assert any("api" in v[1] for v in violations)

    def test_from_backend_api_utils_training_quality_detected(self):
        """from backend.api.utils.training_quality must be flagged."""
        content = "from backend.api.utils.training_quality import calculate_quality_score_from_loss\n"
        violations = _audit_content(content)
        assert len(violations) >= 1
        assert any("api" in v[1] for v in violations)


class TestCheckServiceBoundariesClean:
    """Valid service code must NOT be flagged."""

    def test_backend_services_import_not_flagged(self):
        """from backend.services.persistent_store is allowed."""
        content = "from backend.services.persistent_store import PersistentStore\n"
        violations = _audit_content(content)
        assert len(violations) == 0

    def test_backend_config_import_not_flagged(self):
        """from backend.config is allowed."""
        content = "from backend.config.path_config import get_path\n"
        violations = _audit_content(content)
        assert len(violations) == 0

    def test_no_route_ws_imports_not_flagged(self):
        """Service with no api imports is clean."""
        content = """
from backend.services.persistent_store import PersistentStore
from backend.config.path_config import get_path

def do_thing():
    pass
"""
        violations = _audit_content(content)
        assert len(violations) == 0

    def test_allowed_api_import_not_flagged(self):
        """Allowlisted backend.api.models_additional must NOT be flagged."""
        content = "from backend.api.models_additional import QualityMetrics\n"
        violations = _audit_content(content)
        assert len(violations) == 0

    def test_backend_services_ml_optimization_not_flagged(self):
        """from backend.services.ml_optimization is allowed."""
        content = "from backend.services.ml_optimization import HyperparameterOptimizer\n"
        violations = _audit_content(content)
        assert len(violations) == 0
