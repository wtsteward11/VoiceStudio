"""
Unit tests for check_route_boundaries.py (M8).

Verifies the checker detects CWD-relative path violations and does not flag
correct patterns (get_path, PathService, etc.).
"""

from __future__ import annotations

import importlib.util
import tempfile
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[3]


def _load_checker():
    """Load check_route_boundaries module without running main."""
    spec = importlib.util.spec_from_file_location(
        "check_route_boundaries",
        ROOT / "scripts" / "ci" / "check_route_boundaries.py",
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


class TestCheckRouteBoundariesViolations:
    """Violations must be detected."""

    def test_violation_profiles_reference_wav_detected(self):
        """f\"profiles/{pid}/reference.wav\" must be flagged."""
        content = 'x = f"profiles/{pid}/reference.wav"\n'
        violations = _audit_content(content)
        assert len(violations) >= 1
        descs = [v[1] for v in violations]
        assert any("profiles" in d or "reference" in d for d in descs)

    def test_projects_path_detected(self):
        """f\"projects/{x}/audio\" must be flagged."""
        content = 'd = f"projects/{x}/audio"\n'
        violations = _audit_content(content)
        assert len(violations) >= 1
        assert any("projects" in v[1] for v in violations)

    def test_models_path_detected(self):
        """\"models/speechbrain_speaker\" must be flagged."""
        content = 'savedir="models/speechbrain_speaker"\n'
        violations = _audit_content(content)
        assert len(violations) >= 1
        assert any("models" in v[1] for v in violations)

    def test_api_utils_import_detected(self):
        """from api.utils.X must be flagged (Phase 3 guardrail)."""
        content = 'from api.utils.quality_visualization import calculate_quality_heatmap\n'
        violations = _audit_content(content)
        assert len(violations) >= 1
        assert any("api.utils" in v[1] for v in violations)

    def test_backend_api_utils_import_detected(self):
        """from backend.api.utils.X must be flagged (Phase 3 guardrail)."""
        content = 'from backend.api.utils.text_analysis import analyze_text\n'
        violations = _audit_content(content)
        assert len(violations) >= 1
        assert any("api.utils" in v[1] for v in violations)


class TestCheckRouteBoundariesExempt:
    """Correct patterns must NOT be flagged."""

    def test_get_path_profiles_not_flagged(self):
        """get_path(\"profiles\") must not be flagged."""
        content = 'p = get_path("profiles")\n'
        violations = _audit_content(content)
        assert len(violations) == 0

    def test_get_projects_dir_not_flagged(self):
        """PathService.get_projects_dir() must not be flagged."""
        content = "d = PathService.get_projects_dir()\n"
        violations = _audit_content(content)
        assert len(violations) == 0

    def test_get_models_dir_not_flagged(self):
        """PathService.get_models_dir() must not be flagged."""
        content = "m = PathService.get_models_dir()\n"
        violations = _audit_content(content)
        assert len(violations) == 0

    def test_resolve_reference_audio_path_not_flagged(self):
        """resolve_reference_audio_path(pid) must not be flagged."""
        content = "p = resolve_reference_audio_path(profile_id)\n"
        violations = _audit_content(content)
        assert len(violations) == 0

    def test_backend_services_import_not_flagged(self):
        """from backend.services.X must not be flagged (correct pattern)."""
        content = "from backend.services.quality_visualization_service import calculate_quality_heatmap\n"
        violations = _audit_content(content)
        assert len(violations) == 0
