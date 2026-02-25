"""
Manifest Service — schema validation, manifest normalization, and version compatibility.

Extracted from plugin_service.py monolith.
Handles plugin discovery from the filesystem, manifest parsing (unified + legacy),
and application version compatibility checking.
"""

from __future__ import annotations

import json
import logging
import re
from pathlib import Path
from typing import Any

from backend.plugins.gallery.plugin_schema_validator import (
    validate_plugin_manifest_file,
)
from backend.plugins.plugin_service import (
    APP_VERSION,
    PluginInfo,
    PluginManifest,
    PluginState,
    PluginType,
)

logger = logging.getLogger(__name__)


def parse_version(version_str: str) -> tuple[int, ...]:
    """Parse a version string into a tuple of integers for comparison."""
    match = re.match(r"^(\d+)\.(\d+)\.(\d+)", version_str)
    if match:
        return tuple(int(x) for x in match.groups())
    return (0, 0, 0)


def is_version_compatible(app_version: str, min_required: str) -> bool:
    """Check if app version meets minimum required version."""
    return parse_version(app_version) >= parse_version(min_required)


class ManifestService:
    """Schema validation, manifest normalization, and version compatibility checking.

    Responsible for:
    - Discovering plugin directories and validating their manifests
    - Converting both unified-schema (manifest.json) and legacy (plugin.json) formats
    - Checking plugin/app version compatibility
    """

    def __init__(
        self,
        plugins_dir: Path,
        app_version: str = APP_VERSION,
    ) -> None:
        self._plugins_dir = plugins_dir
        self._app_version = app_version

    @property
    def plugins_dir(self) -> Path:
        """Return the plugins directory path."""
        return self._plugins_dir

    @property
    def app_version(self) -> str:
        """Return the current application version."""
        return self._app_version

    async def discover_plugins(
        self,
        settings: dict[str, dict[str, Any]],
    ) -> list[PluginInfo]:
        """Discover available plugins by scanning the plugins directory.

        Each subdirectory is checked for manifest.json (unified) or plugin.json (legacy).
        Valid manifests are parsed, version-checked, and returned as PluginInfo instances.

        Args:
            settings: Per-plugin settings dict keyed by plugin_id.

        Returns:
            List of newly discovered PluginInfo instances.
        """
        discovered: list[PluginInfo] = []

        if not self._plugins_dir.exists():
            return discovered

        for plugin_path in self._plugins_dir.iterdir():
            if not plugin_path.is_dir():
                continue

            manifest_path = plugin_path / "manifest.json"
            using_unified_schema = True
            if not manifest_path.exists():
                manifest_path = plugin_path / "plugin.json"
                using_unified_schema = False
                if not manifest_path.exists():
                    continue

            try:
                if using_unified_schema:
                    is_valid, errors, manifest_data = validate_plugin_manifest_file(manifest_path)
                    if not is_valid:
                        logger.warning(
                            f"Plugin manifest validation failed for {plugin_path.name}: {errors}"
                        )
                        continue
                else:
                    with open(manifest_path) as f:
                        manifest_data = json.load(f)
                    logger.info(
                        f"Plugin {plugin_path.name} uses legacy plugin.json format - "
                        "consider migrating to manifest.json with unified schema"
                    )

                if manifest_data is None:
                    continue

                manifest = self._convert_manifest_data(manifest_data, using_unified_schema)

                if not self.check_version_compatibility(manifest):
                    logger.warning(
                        f"Plugin {manifest.name} requires app version {manifest.min_app_version}, "
                        f"current version is {self._app_version} - skipping"
                    )
                    continue

                plugin_info = PluginInfo(
                    manifest=manifest,
                    state=PluginState.DISCOVERED,
                    path=plugin_path,
                    settings=settings.get(manifest.plugin_id, {}),
                )

                discovered.append(plugin_info)
                logger.info(f"Discovered plugin: {manifest.name} ({manifest.plugin_id})")

            except Exception as e:
                logger.warning(f"Failed to load plugin from {plugin_path}: {e}")

        return discovered

    def _convert_manifest_data(
        self, data: dict[str, Any], using_unified_schema: bool
    ) -> PluginManifest:
        """Convert manifest data to internal PluginManifest.

        Handles both unified schema (manifest.json) and legacy (plugin.json) formats.

        Args:
            data: Raw manifest dictionary.
            using_unified_schema: True when parsing manifest.json (unified), False for plugin.json.

        Returns:
            Populated PluginManifest instance.
        """
        if using_unified_schema:
            capabilities = data.get("capabilities", {})
            if capabilities.get("engines"):
                plugin_type = PluginType.ENGINE
            elif capabilities.get("effects"):
                plugin_type = PluginType.PROCESSOR
            elif capabilities.get("export_formats"):
                plugin_type = PluginType.EXPORTER
            elif capabilities.get("import_formats"):
                plugin_type = PluginType.IMPORTER
            elif capabilities.get("ui_panels"):
                plugin_type = PluginType.UI_PANEL
            else:
                plugin_type = PluginType.TOOL

            entry_points = data.get("entry_points", {})
            entry_point = entry_points.get("backend", "")

            dependencies = data.get("dependencies", {})
            python_deps = dependencies.get("python", [])
            plugin_deps = dependencies.get("plugins", [])

            return PluginManifest(
                plugin_id=data.get("name", ""),
                name=data.get("display_name") or data.get("name", ""),
                version=data.get("version", "0.0.0"),
                description=data.get("description", ""),
                author=data.get("author", ""),
                plugin_type=plugin_type,
                entry_point=entry_point,
                dependencies=python_deps + plugin_deps,
                min_app_version=data.get("min_app_version", "1.0.0"),
                permissions=data.get("permissions", []),
                settings_schema=data.get("settings_schema", {}),
            )
        else:
            return PluginManifest.from_dict(data)

    def check_version_compatibility(self, manifest: PluginManifest) -> bool:
        """Check if a plugin is compatible with the current app version.

        Args:
            manifest: Plugin manifest to check.

        Returns:
            True if the app version satisfies the plugin's min_app_version.
        """
        return is_version_compatible(self._app_version, manifest.min_app_version)

    def reload_manifest(self, plugin_info: PluginInfo) -> PluginManifest | None:
        """Re-read and validate a manifest from disk for hot-reload scenarios.

        Args:
            plugin_info: Existing plugin info with the path to re-scan.

        Returns:
            New PluginManifest on success, None on validation/compatibility failure.
            Sets plugin_info.state to ERROR and populates error_message on failure.
        """
        manifest_path = plugin_info.path / "manifest.json"
        using_unified_schema = True
        if not manifest_path.exists():
            manifest_path = plugin_info.path / "plugin.json"
            using_unified_schema = False

        if not manifest_path.exists():
            return None

        try:
            if using_unified_schema:
                is_valid, errors, manifest_data = validate_plugin_manifest_file(manifest_path)
                if not is_valid:
                    logger.error(f"Manifest validation failed on reload: {errors}")
                    plugin_info.state = PluginState.ERROR
                    plugin_info.error_message = f"Validation failed: {errors}"
                    return None
            else:
                with open(manifest_path) as f:
                    manifest_data = json.load(f)

            if manifest_data is None:
                return None

            new_manifest = self._convert_manifest_data(manifest_data, using_unified_schema)

            if not self.check_version_compatibility(new_manifest):
                logger.error(
                    f"Reloaded plugin {new_manifest.name} requires app version "
                    f"{new_manifest.min_app_version}, current is {self._app_version}"
                )
                plugin_info.state = PluginState.ERROR
                plugin_info.error_message = "Incompatible version"
                return None

            return new_manifest
        except Exception as e:
            logger.error(f"Failed to reload manifest from {plugin_info.path}: {e}")
            plugin_info.state = PluginState.ERROR
            plugin_info.error_message = str(e)
            return None
