"""
Feature Catalog Appendix CI Drift Check.

Validates that docs/governance/FEATURE_CATALOG_MASTER.appendix.json:
- Exists and is valid JSON
- Contains all required keys per FCM-011
- Is not stale (generated_at_utc within STALE_DAYS)

Fails when the appendix is missing, malformed, or stale.
Run after any material change to panel registry, route registry, engine manifests,
or feature-gate posture. Regenerate the appendix to fix.

FCM-009 task 6: Add CI check for stale catalog versus generated appendix.
"""

from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path

import pytest

# Pytest markers
pytestmark = [
    pytest.mark.ci,
]

ROOT = Path(__file__).resolve().parent.parent.parent
APPENDIX_PATH = ROOT / "docs" / "governance" / "FEATURE_CATALOG_MASTER.appendix.json"
MARKDOWN_PATH = ROOT / "docs" / "governance" / "FEATURE_CATALOG_MASTER.md"

# Fail if appendix generated_at is older than this many days
STALE_DAYS = 90

# Required top-level keys per FCM-011
REQUIRED_KEYS = [
    "schema_version",
    "catalog_id",
    "generated_at_utc",
    "source_scope",
    "canonical_ui",
    "api_surface",
    "engine_surface",
    "plugin_surface",
    "snapshot_comparison",
    "verification_status",
    "known_risks",
    "next_20_tasks_ordered",
]


class TestFeatureCatalogAppendixExists:
    """Appendix file must exist."""

    def test_appendix_file_exists(self) -> None:
        """Appendix JSON file must exist at docs/governance/FEATURE_CATALOG_MASTER.appendix.json."""
        assert APPENDIX_PATH.exists(), (
            f"Feature catalog appendix not found: {APPENDIX_PATH}. "
            "Regenerate per FEATURE_CATALOG_MASTER.md FCM-011."
        )


class TestFeatureCatalogAppendixSchema:
    """Appendix must have valid schema and required keys."""

    @pytest.fixture
    def appendix(self) -> dict:
        """Load appendix JSON. Fails if missing or invalid."""
        assert APPENDIX_PATH.exists(), f"Appendix not found: {APPENDIX_PATH}"
        with open(APPENDIX_PATH, encoding="utf-8") as f:
            return json.load(f)

    def test_appendix_is_valid_json(self, appendix: dict) -> None:
        """Appendix must be parseable JSON."""
        assert isinstance(appendix, dict), "Appendix must be a JSON object"

    def test_appendix_has_all_required_keys(self, appendix: dict) -> None:
        """Appendix must contain all required keys per FCM-011."""
        missing = [k for k in REQUIRED_KEYS if k not in appendix]
        assert not missing, (
            f"Appendix missing required keys: {missing}. "
            "Regenerate per FEATURE_CATALOG_MASTER.md FCM-011."
        )

    def test_appendix_schema_version(self, appendix: dict) -> None:
        """Appendix schema_version must be 1.0.0."""
        assert appendix.get("schema_version") == "1.0.0", (
            f"Expected schema_version 1.0.0, got {appendix.get('schema_version')}"
        )

    def test_appendix_catalog_id(self, appendix: dict) -> None:
        """Appendix catalog_id must be FEATURE_CATALOG_MASTER."""
        assert appendix.get("catalog_id") == "FEATURE_CATALOG_MASTER", (
            f"Expected catalog_id FEATURE_CATALOG_MASTER, got {appendix.get('catalog_id')}"
        )


class TestFeatureCatalogAppendixStaleness:
    """Appendix must not be stale (generated within STALE_DAYS)."""

    @pytest.fixture
    def appendix(self) -> dict:
        """Load appendix JSON."""
        with open(APPENDIX_PATH, encoding="utf-8") as f:
            return json.load(f)

    def test_appendix_not_stale(self, appendix: dict) -> None:
        """Appendix generated_at_utc must be within STALE_DAYS of now."""
        raw = appendix.get("generated_at_utc", "")
        assert raw, "generated_at_utc is required"
        try:
            # Parse ISO format with timezone
            gen_dt = datetime.fromisoformat(raw.replace("Z", "+00:00"))
            if gen_dt.tzinfo is None:
                gen_dt = gen_dt.replace(tzinfo=timezone.utc)
        except (ValueError, TypeError) as e:
            pytest.fail(f"Invalid generated_at_utc '{raw}': {e}")
        now = datetime.now(timezone.utc)
        age_days = (now - gen_dt).total_seconds() / 86400
        assert age_days <= STALE_DAYS, (
            f"Appendix is stale: generated {age_days:.0f} days ago "
            f"(max {STALE_DAYS} days). Regenerate docs/governance/FEATURE_CATALOG_MASTER.appendix.json."
        )


class TestFeatureCatalogMarkdownExists:
    """Canonical markdown must exist alongside appendix."""

    def test_markdown_exists(self) -> None:
        """FEATURE_CATALOG_MASTER.md must exist."""
        assert MARKDOWN_PATH.exists(), (
            f"Feature catalog markdown not found: {MARKDOWN_PATH}"
        )
