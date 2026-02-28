"""
Plugin Schema Validator

Phase 1: Unified manifest validation for VoiceStudio plugins.
Validates plugin manifests against the unified JSON schema and performs
semantic validation for cross-field consistency.
"""

from __future__ import annotations

import json
import logging
import re
from pathlib import Path
from typing import Any

try:
    import jsonschema
    from jsonschema import Draft7Validator, ValidationError

    JSONSCHEMA_AVAILABLE = True
except ImportError:
    JSONSCHEMA_AVAILABLE = False
    Draft7Validator = None
    ValidationError = Exception

logger = logging.getLogger(__name__)

# Path to unified schema (relative to repo root)
SCHEMA_PATH = (
    Path(__file__).parent.parent.parent / "shared" / "schemas" / "plugin-manifest.schema.json"
)

# Semver regex pattern
SEMVER_PATTERN = re.compile(
    r"^(?P<major>\d+)\.(?P<minor>\d+)\.(?P<patch>\d+)"
    r"(-(?P<prerelease>[a-zA-Z0-9.]+))?"
    r"(\+(?P<build>[a-zA-Z0-9.]+))?$"
)


class PluginSchemaValidator:
    """
    Validates plugin manifests against the unified schema.

    Performs both JSON Schema validation and semantic validation
    for cross-field consistency checks.
    """

    def __init__(self, schema_path: Path | str | None = None):
        """
        Initialize validator with schema.

        Args:
            schema_path: Path to JSON schema file. Uses default if None.
        """
        self._schema: dict[str, Any] | None = None
        self._validator: Draft7Validator | None = None
        self._schema_path = Path(schema_path) if schema_path else SCHEMA_PATH
        self._load_schema()

    def _load_schema(self) -> None:
        """Load the JSON schema from file."""
        if not JSONSCHEMA_AVAILABLE:
            logger.warning("jsonschema package not available - validation disabled")
            return

        if not self._schema_path.exists():
            logger.error(f"Schema file not found: {self._schema_path}")
            return

        try:
            with open(self._schema_path, encoding="utf-8") as f:
                self._schema = json.load(f)
            self._validator = Draft7Validator(self._schema)
            logger.debug(f"Loaded plugin schema from {self._schema_path}")
        except Exception as e:
            logger.error(f"Failed to load plugin schema: {e}")
            self._schema = None
            self._validator = None

    def validate(self, manifest: dict[str, Any]) -> tuple[bool, list[str]]:
        """
        Validate a plugin manifest.

        Performs:
        1. JSON Schema validation against the unified schema
        2. Semantic validation for cross-field consistency

        Args:
            manifest: Plugin manifest dictionary

        Returns:
            Tuple of (is_valid, list of error messages)
        """
        errors: list[str] = []

        # Schema validation
        schema_errors = self._validate_schema(manifest)
        errors.extend(schema_errors)

        # Semantic validation (only if schema passes basic structure)
        if not schema_errors or self._has_required_fields(manifest):
            semantic_errors = self._validate_semantics(manifest)
            errors.extend(semantic_errors)

        is_valid = len(errors) == 0
        return is_valid, errors

    def _validate_schema(self, manifest: dict[str, Any]) -> list[str]:
        """Validate manifest against JSON schema."""
        errors: list[str] = []

        if not JSONSCHEMA_AVAILABLE:
            logger.warning("Skipping schema validation - jsonschema not available")
            return errors

        if self._validator is None:
            errors.append("Schema validator not initialized - schema file may be missing")
            return errors

        for error in self._validator.iter_errors(manifest):
            # Format error message with path
            path = ".".join(str(p) for p in error.absolute_path) if error.absolute_path else "root"
            errors.append(f"[{path}] {error.message}")

        return errors

    def _has_required_fields(self, manifest: dict[str, Any]) -> bool:
        """Check if manifest has minimum required fields for semantic validation."""
        return all(field in manifest for field in ["name", "version", "plugin_type"])

    def _validate_semantics(self, manifest: dict[str, Any]) -> list[str]:
        """
        Perform semantic validation for cross-field consistency.

        Checks:
        - plugin_type matches entry_points configuration
        - Capabilities are consistent with plugin_type
        - Version format is valid semver
        - Dependency formats are valid
        - GAP-ARCH-002: Deprecation warnings for legacy fields
        """
        errors: list[str] = []

        # GAP-ARCH-002: Check for deprecated permissions array
        self._check_deprecated_permissions(manifest, errors)

        plugin_type = manifest.get("plugin_type", "")
        entry_points = manifest.get("entry_points", {})
        capabilities = manifest.get("capabilities", {})

        # Check entry_points vs plugin_type consistency
        has_backend = "backend" in entry_points
        has_frontend = "frontend" in entry_points

        if plugin_type == "full_stack":
            if not has_backend:
                errors.append("full_stack plugin requires entry_points.backend")
            if not has_frontend:
                errors.append("full_stack plugin requires entry_points.frontend")
        elif plugin_type == "backend_only":
            if not has_backend:
                errors.append("backend_only plugin requires entry_points.backend")
            if has_frontend:
                errors.append("backend_only plugin should not have entry_points.frontend")
        elif plugin_type == "frontend_only":
            if not has_frontend:
                errors.append("frontend_only plugin requires entry_points.frontend")
            if has_backend:
                errors.append("frontend_only plugin should not have entry_points.backend")

        # Check capabilities vs plugin_type consistency
        if capabilities.get("backend_routes") and plugin_type == "frontend_only":
            errors.append("frontend_only plugin cannot have backend_routes capability")

        if capabilities.get("ui_panels") and plugin_type == "backend_only":
            errors.append("backend_only plugin cannot have ui_panels capability")

        # Validate version format (stricter than schema regex)
        version = manifest.get("version", "")
        if version and not self._is_valid_semver(version):
            errors.append(f"Invalid semver format: {version}")

        # Validate min_app_version format
        min_app_version = manifest.get("min_app_version", "")
        if min_app_version and not self._is_valid_semver(min_app_version):
            errors.append(f"Invalid min_app_version format: {min_app_version}")

        # Validate min_api_version format
        min_api_version = manifest.get("min_api_version", "")
        if min_api_version and not self._is_valid_semver(min_api_version):
            errors.append(f"Invalid min_api_version format: {min_api_version}")

        return errors

    def _is_valid_semver(self, version: str) -> bool:
        """Check if version string is valid semver."""
        return bool(SEMVER_PATTERN.match(version))

    def _check_deprecated_permissions(self, manifest: dict[str, Any], errors: list[str]) -> None:
        """
        GAP-ARCH-002: Check for deprecated permissions array and emit warning.

        The legacy top-level 'permissions' array is deprecated since v6.
        Use 'security.permissions' object instead.

        Note: Deprecation warnings are logged but do NOT cause validation failure
        to maintain backward compatibility with v3/v4/v5 manifests.

        Args:
            manifest: Plugin manifest dictionary
            errors: List to append error messages to (not used for deprecation warnings)
        """
        if "permissions" in manifest:
            plugin_name = manifest.get("name", "unknown")
            warning_msg = (
                "[DEPRECATED] permissions: The top-level 'permissions' array is "
                "deprecated since schema v6. Migrate to 'security.permissions' object. "
                f"Plugin: {plugin_name}"
            )
            # Log warning but do NOT add to errors - deprecation should warn, not fail
            logger.warning(warning_msg)

            # Provide migration guidance if security section exists
            security = manifest.get("security", {})
            if not security.get("permissions"):
                guidance_msg = (
                    "[DEPRECATED] Suggestion: Add 'security.permissions' object with "
                    "granular permissions (filesystem.read, network.outbound, etc.) "
                    "to replace the legacy array."
                )
                logger.info(guidance_msg)

    def validate_file(self, path: Path | str) -> tuple[bool, list[str], dict[str, Any] | None]:
        """
        Validate a plugin manifest file.

        Args:
            path: Path to manifest.json file

        Returns:
            Tuple of (is_valid, error messages, parsed manifest or None)
        """
        path = Path(path)
        errors: list[str] = []
        manifest: dict[str, Any] | None = None

        if not path.exists():
            errors.append(f"Manifest file not found: {path}")
            return False, errors, None

        try:
            with open(path, encoding="utf-8") as f:
                manifest = json.load(f)
        except json.JSONDecodeError as e:
            errors.append(f"Invalid JSON in manifest: {e}")
            return False, errors, None
        except Exception as e:
            errors.append(f"Failed to read manifest: {e}")
            return False, errors, None

        is_valid, validation_errors = self.validate(manifest)
        errors.extend(validation_errors)

        return is_valid, errors, manifest if is_valid else None


# Singleton instance
_validator: PluginSchemaValidator | None = None


def get_validator() -> PluginSchemaValidator:
    """Get the singleton validator instance."""
    global _validator
    if _validator is None:
        _validator = PluginSchemaValidator()
    return _validator


def validate_plugin_manifest(manifest: dict[str, Any]) -> tuple[bool, list[str]]:
    """
    Convenience function to validate a plugin manifest.

    Args:
        manifest: Plugin manifest dictionary

    Returns:
        Tuple of (is_valid, list of error messages)
    """
    return get_validator().validate(manifest)


def validate_plugin_manifest_file(
    path: Path | str,
) -> tuple[bool, list[str], dict[str, Any] | None]:
    """
    Convenience function to validate a plugin manifest file.

    Args:
        path: Path to manifest.json file

    Returns:
        Tuple of (is_valid, error messages, parsed manifest or None)
    """
    return get_validator().validate_file(path)
