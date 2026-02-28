"""
Plugin Install Service (v1 - DEPRECATED).

D.1 Enhancement: Handles plugin download, verification, and installation.

.. deprecated:: 1.0.2
    Use :mod:`backend.plugins.gallery.installer_v2` instead.
    This module provides basic installation without atomic rollback.
    installer_v2 adds .vspkg support, signature verification, and
    atomic installation with rollback. See ADR-042.
"""

from __future__ import annotations

import warnings

warnings.warn(
    "backend.plugins.gallery.installer is deprecated. "
    "Use backend.plugins.gallery.installer_v2 instead. See ADR-042.",
    DeprecationWarning,
    stacklevel=2,
)

import hashlib
import json
import logging
import shutil
import zipfile
from collections.abc import Callable
from datetime import datetime
from pathlib import Path

import aiohttp

from .catalog import get_catalog_service
from .models import (
    DependencyCheckResult,
    InstalledPlugin,
    InstallPhase,
    InstallProgress,
    InstallResult,
    PluginVersion,
    UpdateInfo,
)

logger = logging.getLogger(__name__)


class PluginInstallService:
    """
    Service for installing, updating, and removing plugins.

    Features:
    - Download with progress tracking
    - Checksum verification
    - Dependency checking
    - Install/uninstall management
    - Update detection
    """

    def __init__(self, plugins_dir: Path | None = None):
        """
        Initialize install service.

        Args:
            plugins_dir: Directory for installed plugins
        """
        self._plugins_dir = plugins_dir or Path.home() / ".voicestudio" / "plugins"
        self._plugins_dir.mkdir(parents=True, exist_ok=True)

        self._registry_file = self._plugins_dir / "registry.json"
        self._installed: dict[str, InstalledPlugin] = {}

        # Load existing installations
        self._load_registry()

    async def install_plugin(
        self,
        plugin_id: str,
        version: str | None = None,
        progress_callback: Callable[[InstallProgress], None] | None = None,
    ) -> InstallResult:
        """
        Install a plugin.

        Args:
            plugin_id: Plugin identifier
            version: Specific version (default: latest)
            progress_callback: Callback for progress updates

        Returns:
            Installation result
        """

        def report(phase: InstallPhase, progress: float, message: str = ""):
            if progress_callback:
                progress_callback(
                    InstallProgress(
                        phase=phase,
                        progress=progress,
                        message=message,
                    )
                )

        try:
            report(InstallPhase.PREPARING, 0.0, "Fetching plugin information...")

            # Get plugin details from catalog
            catalog_service = get_catalog_service()
            plugin = await catalog_service.get_plugin_details(plugin_id)

            if not plugin:
                return InstallResult(
                    success=False,
                    plugin_id=plugin_id,
                    version="",
                    error=f"Plugin '{plugin_id}' not found in catalog",
                )

            # Find version
            if version:
                plugin_version = next((v for v in plugin.versions if v.version == version), None)
            else:
                plugin_version = plugin.latest_version

            if not plugin_version:
                return InstallResult(
                    success=False,
                    plugin_id=plugin_id,
                    version=version or "latest",
                    error="Version not found",
                )

            report(InstallPhase.PREPARING, 0.1, "Checking dependencies...")

            # Check dependencies
            dep_result = await self._check_dependencies(plugin_version)
            if not dep_result.satisfied:
                return InstallResult(
                    success=False,
                    plugin_id=plugin_id,
                    version=plugin_version.version,
                    error=f"Missing dependencies: {', '.join(dep_result.missing)}",
                )

            # Create install directory
            install_path = self._plugins_dir / plugin.category / plugin_id
            install_path.mkdir(parents=True, exist_ok=True)

            report(InstallPhase.DOWNLOADING, 0.2, "Downloading plugin...")

            # Download plugin
            download_path = (
                self._plugins_dir / "downloads" / f"{plugin_id}-{plugin_version.version}.zip"
            )
            download_path.parent.mkdir(parents=True, exist_ok=True)

            await self._download_file(
                plugin_version.download_url,
                download_path,
                plugin_version.size_bytes,
                lambda p: report(
                    InstallPhase.DOWNLOADING, 0.2 + p * 0.4, f"Downloading... {int(p*100)}%"
                ),
            )

            report(InstallPhase.VERIFYING, 0.6, "Verifying checksum...")

            # Verify checksum
            if not await self._verify_checksum(download_path, plugin_version.checksum_sha256):
                download_path.unlink(missing_ok=True)
                return InstallResult(
                    success=False,
                    plugin_id=plugin_id,
                    version=plugin_version.version,
                    error="Checksum verification failed",
                )

            report(InstallPhase.EXTRACTING, 0.7, "Extracting files...")

            # Extract
            await self._extract_plugin(download_path, install_path)
            download_path.unlink(missing_ok=True)

            report(InstallPhase.CONFIGURING, 0.9, "Configuring plugin...")

            # Register installed plugin
            installed = InstalledPlugin(
                id=plugin_id,
                version=plugin_version.version,
                installed_at=datetime.now(),
                install_path=str(install_path),
                state="enabled",
                config={},
            )
            self._installed[plugin_id] = installed
            self._save_registry()

            report(InstallPhase.COMPLETE, 1.0, "Installation complete!")

            logger.info(f"Installed plugin {plugin_id} v{plugin_version.version}")

            return InstallResult(
                success=True,
                plugin_id=plugin_id,
                version=plugin_version.version,
                install_path=str(install_path),
            )

        except Exception as e:
            logger.error(f"Plugin installation failed: {e}", exc_info=True)
            return InstallResult(
                success=False,
                plugin_id=plugin_id,
                version=version or "",
                error=str(e),
            )

    async def uninstall_plugin(self, plugin_id: str) -> bool:
        """
        Uninstall a plugin.

        Args:
            plugin_id: Plugin identifier

        Returns:
            True if successful
        """
        installed = self._installed.get(plugin_id)
        if not installed:
            logger.warning(f"Plugin not installed: {plugin_id}")
            return False

        try:
            # Remove files
            install_path = Path(installed.install_path)
            if install_path.exists():
                shutil.rmtree(install_path)

            # Update registry
            del self._installed[plugin_id]
            self._save_registry()

            logger.info(f"Uninstalled plugin: {plugin_id}")
            return True

        except Exception as e:
            logger.error(f"Failed to uninstall plugin: {e}")
            return False

    def enable_plugin(self, plugin_id: str) -> bool:
        """Enable a disabled plugin."""
        installed = self._installed.get(plugin_id)
        if not installed:
            return False

        installed.state = "enabled"
        self._save_registry()
        logger.info(f"Enabled plugin: {plugin_id}")
        return True

    def disable_plugin(self, plugin_id: str) -> bool:
        """Disable a plugin."""
        installed = self._installed.get(plugin_id)
        if not installed:
            return False

        installed.state = "disabled"
        self._save_registry()
        logger.info(f"Disabled plugin: {plugin_id}")
        return True

    def get_installed_plugins(self) -> list[InstalledPlugin]:
        """Get all installed plugins."""
        return list(self._installed.values())

    def get_installed_plugin(self, plugin_id: str) -> InstalledPlugin | None:
        """Get a specific installed plugin."""
        return self._installed.get(plugin_id)

    def is_installed(self, plugin_id: str) -> bool:
        """Check if a plugin is installed."""
        return plugin_id in self._installed

    async def check_for_updates(self) -> list[UpdateInfo]:
        """
        Check all installed plugins for updates.

        Returns:
            List of available updates
        """
        updates = []
        catalog_service = get_catalog_service()

        for plugin_id, installed in self._installed.items():
            plugin = await catalog_service.get_plugin_details(plugin_id)
            if not plugin or not plugin.latest_version:
                continue

            if self._is_newer_version(plugin.latest_version.version, installed.version):
                updates.append(
                    UpdateInfo(
                        plugin_id=plugin_id,
                        current_version=installed.version,
                        available_version=plugin.latest_version.version,
                        changelog=plugin.latest_version.changelog,
                    )
                )

        return updates

    async def update_plugin(
        self,
        plugin_id: str,
        target_version: str | None = None,
        progress_callback: Callable[[InstallProgress], None] | None = None,
    ) -> InstallResult:
        """
        Update a plugin to a new version.

        Args:
            plugin_id: Plugin identifier
            target_version: Target version (default: latest)
            progress_callback: Progress callback

        Returns:
            Update result
        """
        # Uninstall old version
        if not await self.uninstall_plugin(plugin_id):
            return InstallResult(
                success=False,
                plugin_id=plugin_id,
                version=target_version or "",
                error="Failed to remove old version",
            )

        # Install new version
        return await self.install_plugin(plugin_id, target_version, progress_callback)

    async def _download_file(
        self,
        url: str,
        destination: Path,
        expected_size: int,
        progress_callback: Callable[[float], None] | None = None,
    ) -> None:
        """Download a file with progress tracking."""
        async with aiohttp.ClientSession() as session, session.get(url) as response:
            response.raise_for_status()

            total_size = int(response.headers.get("content-length", expected_size))
            downloaded = 0

            with open(destination, "wb") as f:
                async for chunk in response.content.iter_chunked(8192):
                    f.write(chunk)
                    downloaded += len(chunk)
                    if progress_callback and total_size > 0:
                        progress_callback(downloaded / total_size)

    async def _verify_checksum(self, file_path: Path, expected_hash: str) -> bool:
        """Verify file checksum."""
        if not expected_hash:
            logger.warning("No checksum provided, skipping verification")
            return True

        sha256 = hashlib.sha256()
        with open(file_path, "rb") as f:
            for chunk in iter(lambda: f.read(8192), b""):
                sha256.update(chunk)

        actual_hash = sha256.hexdigest()
        return actual_hash.lower() == expected_hash.lower()

    async def _extract_plugin(self, archive_path: Path, destination: Path) -> None:
        """Extract plugin archive."""
        with zipfile.ZipFile(archive_path, "r") as zf:
            zf.extractall(destination)

    async def _check_dependencies(self, version: PluginVersion) -> DependencyCheckResult:
        """Check if dependencies are satisfied."""
        missing = []
        incompatible = []
        details = {}

        for dep_name, version_spec in version.dependencies.items():
            # Skip optional dependencies for now
            if version_spec.startswith("optional:"):
                details[dep_name] = {"status": "optional", "installed": False}
                continue

            # Check common dependencies
            if dep_name == "python":
                import sys

                current = (
                    f"{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}"
                )
                # Simplified version check
                details[dep_name] = {"status": "satisfied", "version": current}
            elif dep_name == "pytorch":
                try:
                    import torch

                    details[dep_name] = {"status": "satisfied", "version": torch.__version__}
                except ImportError:
                    missing.append(dep_name)
                    details[dep_name] = {"status": "missing"}
            else:
                # Unknown dependency - assume satisfied
                details[dep_name] = {"status": "unknown", "assumed": True}

        return DependencyCheckResult(
            satisfied=len(missing) == 0 and len(incompatible) == 0,
            missing=missing,
            incompatible=incompatible,
            details=details,
        )

    def _is_newer_version(self, available: str, current: str) -> bool:
        """Check if available version is newer than current."""
        try:
            from packaging import version

            return version.parse(available) > version.parse(current)
        except Exception:
            # Fallback to string comparison
            return available > current

    def _load_registry(self) -> None:
        """Load installed plugins registry."""
        try:
            if not self._registry_file.exists():
                return

            data = json.loads(self._registry_file.read_text())

            for plugin_id, plugin_data in data.get("plugins", {}).items():
                self._installed[plugin_id] = InstalledPlugin(
                    id=plugin_id,
                    version=plugin_data.get("version", ""),
                    installed_at=datetime.fromisoformat(
                        plugin_data.get("installed_at", "2000-01-01")
                    ),
                    install_path=plugin_data.get("install_path", ""),
                    state=plugin_data.get("state", "enabled"),
                    config=plugin_data.get("config", {}),
                )

            logger.info(f"Loaded {len(self._installed)} installed plugins")
        except Exception as e:
            logger.warning(f"Failed to load plugin registry: {e}")

    def _save_registry(self) -> None:
        """Save installed plugins registry."""
        try:
            data = {
                "version": "1.0.0",
                "updated_at": datetime.now().isoformat(),
                "plugins": {
                    plugin_id: plugin.to_dict() for plugin_id, plugin in self._installed.items()
                },
            }
            self._registry_file.write_text(json.dumps(data, indent=2))
        except Exception as e:
            logger.error(f"Failed to save plugin registry: {e}")


# Global service instance
_install_service: PluginInstallService | None = None


def get_install_service() -> PluginInstallService:
    """Get or create the global install service."""
    global _install_service
    if _install_service is None:
        _install_service = PluginInstallService()
    return _install_service
