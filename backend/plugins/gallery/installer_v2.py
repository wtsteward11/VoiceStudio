"""
Enhanced Plugin Installer v2.

Phase 5C M7: Overhaul installer for .vspkg support, verification, and atomic install with rollback.

Features:
- Native .vspkg package format support
- Integrated verification (signature, SBOM, provenance)
- Atomic installation with automatic rollback on failure
- Dependency resolution before installation
- Transaction-based multi-plugin installation
- Backup and restore capabilities
"""

from __future__ import annotations

import hashlib
import json
import logging
import shutil
import tempfile
import zipfile
from collections.abc import Callable
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from pathlib import Path
from typing import Any

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

# GAP-PY-007: Import signature verification
try:
    from backend.plugins.supply_chain.signer import verify_package_auto

    SIGNER_AVAILABLE = True
except ImportError:
    SIGNER_AVAILABLE = False
    verify_package_auto = None

logger = logging.getLogger(__name__)


class TransactionState(Enum):
    """Transaction state."""

    PENDING = "pending"
    IN_PROGRESS = "in_progress"
    COMMITTED = "committed"
    ROLLED_BACK = "rolled_back"
    FAILED = "failed"


class InstallAction(Enum):
    """Type of installation action."""

    INSTALL = "install"
    UPDATE = "update"
    UNINSTALL = "uninstall"
    ROLLBACK = "rollback"


@dataclass
class BackupInfo:
    """Information about a plugin backup."""

    plugin_id: str
    version: str
    backup_path: str
    created_at: datetime
    registry_data: dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "plugin_id": self.plugin_id,
            "version": self.version,
            "backup_path": self.backup_path,
            "created_at": self.created_at.isoformat(),
            "registry_data": self.registry_data,
        }


@dataclass
class InstallTransaction:
    """Represents an installation transaction."""

    id: str
    actions: list[dict[str, Any]] = field(default_factory=list)
    state: TransactionState = TransactionState.PENDING
    backups: list[BackupInfo] = field(default_factory=list)
    started_at: datetime | None = None
    completed_at: datetime | None = None
    error: str | None = None

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "id": self.id,
            "actions": self.actions,
            "state": self.state.value,
            "backups": [b.to_dict() for b in self.backups],
            "started_at": self.started_at.isoformat() if self.started_at else None,
            "completed_at": self.completed_at.isoformat() if self.completed_at else None,
            "error": self.error,
        }


@dataclass
class PackageVerificationResult:
    """Result of package verification during installation."""

    valid: bool
    signature_valid: bool | None = None
    sbom_present: bool = False
    provenance_present: bool = False
    checksum_valid: bool = False
    manifest_valid: bool = False
    errors: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "valid": self.valid,
            "signature_valid": self.signature_valid,
            "sbom_present": self.sbom_present,
            "provenance_present": self.provenance_present,
            "checksum_valid": self.checksum_valid,
            "manifest_valid": self.manifest_valid,
            "errors": self.errors,
            "warnings": self.warnings,
        }


@dataclass
class AtomicInstallResult:
    """Result of an atomic installation operation."""

    success: bool
    plugin_id: str
    version: str
    install_path: str | None = None
    verification: PackageVerificationResult | None = None
    transaction_id: str | None = None
    rollback_available: bool = False
    error: str | None = None

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "success": self.success,
            "plugin_id": self.plugin_id,
            "version": self.version,
            "install_path": self.install_path,
            "verification": self.verification.to_dict() if self.verification else None,
            "transaction_id": self.transaction_id,
            "rollback_available": self.rollback_available,
            "error": self.error,
        }


class PluginInstallerV2:
    """
    Enhanced plugin installer with atomic operations and rollback support.

    Improvements over v1:
    - Native .vspkg support
    - Integrated verification chain
    - Atomic install with automatic rollback
    - Transaction-based multi-plugin operations
    - Backup and restore
    - Dependency resolution integration
    """

    def __init__(self, plugins_dir: Path | None = None):
        """
        Initialize the enhanced installer.

        Args:
            plugins_dir: Base directory for plugin installation
        """
        self._plugins_dir = plugins_dir or Path.home() / ".voicestudio" / "plugins"
        self._plugins_dir.mkdir(parents=True, exist_ok=True)

        self._registry_file = self._plugins_dir / "registry.json"
        self._backups_dir = self._plugins_dir / ".backups"
        self._staging_dir = self._plugins_dir / ".staging"
        self._transactions_dir = self._plugins_dir / ".transactions"

        # Create supporting directories
        self._backups_dir.mkdir(parents=True, exist_ok=True)
        self._staging_dir.mkdir(parents=True, exist_ok=True)
        self._transactions_dir.mkdir(parents=True, exist_ok=True)

        self._installed: dict[str, InstalledPlugin] = {}
        self._active_transactions: dict[str, InstallTransaction] = {}

        # Load existing installations
        self._load_registry()

    async def install_from_vspkg(
        self,
        package_path: Path,
        verify: bool = True,
        atomic: bool = True,
        progress_callback: Callable[[InstallProgress], None] | None = None,
    ) -> AtomicInstallResult:
        """
        Install a plugin from a .vspkg package.

        Args:
            package_path: Path to .vspkg file
            verify: Whether to verify the package
            atomic: Whether to use atomic installation
            progress_callback: Progress callback

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

        transaction_id = None
        backup = None
        staging_path = None

        try:
            if not package_path.exists():
                return AtomicInstallResult(
                    success=False,
                    plugin_id="",
                    version="",
                    error=f"Package not found: {package_path}",
                )

            report(InstallPhase.PREPARING, 0.0, "Extracting package metadata...")

            # Extract and validate package structure
            manifest = await self._extract_manifest(package_path)

            if not manifest:
                return AtomicInstallResult(
                    success=False,
                    plugin_id="",
                    version="",
                    error="Invalid package: No manifest found",
                )

            plugin_id = manifest.get("id", "")
            version = manifest.get("version", "")

            if not plugin_id or not version:
                return AtomicInstallResult(
                    success=False,
                    plugin_id=plugin_id,
                    version=version,
                    error="Invalid manifest: Missing id or version",
                )

            report(InstallPhase.VERIFYING, 0.1, "Verifying package integrity...")

            # Verify package
            verification: PackageVerificationResult | None = None
            if verify:
                verification = await self._verify_package(package_path)

                if not verification.valid:
                    return AtomicInstallResult(
                        success=False,
                        plugin_id=plugin_id,
                        version=version,
                        verification=verification,
                        error=f"Package verification failed: {', '.join(verification.errors)}",
                    )

            report(InstallPhase.PREPARING, 0.2, "Checking dependencies...")

            # Check dependencies
            dependencies = manifest.get("dependencies", {})
            dep_result = await self._check_dependencies(dependencies)

            if not dep_result.satisfied:
                return AtomicInstallResult(
                    success=False,
                    plugin_id=plugin_id,
                    version=version,
                    verification=verification,
                    error=f"Missing dependencies: {', '.join(dep_result.missing)}",
                )

            # Start atomic transaction
            if atomic:
                transaction_id = self._create_transaction()

            report(InstallPhase.PREPARING, 0.3, "Creating backup...")

            # Backup existing installation if present
            if plugin_id in self._installed:
                backup = await self._create_backup(plugin_id)
                if atomic and backup:
                    transaction = self._active_transactions.get(transaction_id or "")
                    if transaction:
                        transaction.backups.append(backup)

            report(InstallPhase.EXTRACTING, 0.4, "Extracting to staging...")

            # Extract to staging area first
            staging_path = await self._extract_to_staging(package_path, plugin_id, version)

            report(InstallPhase.CONFIGURING, 0.6, "Installing plugin...")

            # Determine final install location
            category = manifest.get("category", "other")
            install_path = self._plugins_dir / category / plugin_id

            # Remove existing installation
            if install_path.exists():
                shutil.rmtree(install_path)

            # Move from staging to final location
            install_path.parent.mkdir(parents=True, exist_ok=True)
            shutil.move(str(staging_path), str(install_path))
            staging_path = None  # Moved, don't clean up

            report(InstallPhase.CONFIGURING, 0.8, "Updating registry...")

            # Register installation
            installed = InstalledPlugin(
                id=plugin_id,
                version=version,
                installed_at=datetime.now(),
                install_path=str(install_path),
                state="enabled",
                config={},
            )
            self._installed[plugin_id] = installed
            self._save_registry()

            # Commit transaction
            if atomic and transaction_id:
                self._commit_transaction(transaction_id)

            report(InstallPhase.COMPLETE, 1.0, "Installation complete!")

            logger.info(f"Installed plugin {plugin_id} v{version} from .vspkg")

            return AtomicInstallResult(
                success=True,
                plugin_id=plugin_id,
                version=version,
                install_path=str(install_path),
                verification=verification,
                transaction_id=transaction_id,
                rollback_available=backup is not None,
            )

        except Exception as e:
            logger.error(f"Installation failed: {e}", exc_info=True)

            # Attempt rollback
            if atomic and transaction_id:
                await self._rollback_transaction(transaction_id)

            # Clean up staging
            if staging_path and staging_path.exists():
                shutil.rmtree(staging_path, ignore_errors=True)

            return AtomicInstallResult(
                success=False,
                plugin_id=manifest.get("id", "") if "manifest" in locals() else "",
                version=manifest.get("version", "") if "manifest" in locals() else "",
                transaction_id=transaction_id,
                rollback_available=backup is not None if "backup" in locals() else False,
                error=str(e),
            )

    async def install_plugin(
        self,
        plugin_id: str,
        version: str | None = None,
        verify: bool = True,
        atomic: bool = True,
        progress_callback: Callable[[InstallProgress], None] | None = None,
    ) -> AtomicInstallResult:
        """
        Install a plugin from the catalog.

        This is the enhanced version of the v1 install_plugin method.

        Args:
            plugin_id: Plugin identifier
            version: Specific version (None = latest)
            verify: Whether to verify the package
            atomic: Whether to use atomic installation
            progress_callback: Progress callback

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

        transaction_id = None
        backup = None
        download_path = None

        try:
            report(InstallPhase.PREPARING, 0.0, "Fetching plugin information...")

            # Get plugin from catalog
            catalog_service = get_catalog_service()
            plugin = await catalog_service.get_plugin_details(plugin_id)

            if not plugin:
                return AtomicInstallResult(
                    success=False,
                    plugin_id=plugin_id,
                    version=version or "",
                    error=f"Plugin '{plugin_id}' not found in catalog",
                )

            # Find version
            if version:
                plugin_version = next((v for v in plugin.versions if v.version == version), None)
            else:
                plugin_version = plugin.latest_version

            if not plugin_version:
                return AtomicInstallResult(
                    success=False,
                    plugin_id=plugin_id,
                    version=version or "latest",
                    error="Version not found",
                )

            # Start transaction
            if atomic:
                transaction_id = self._create_transaction()

            report(InstallPhase.DOWNLOADING, 0.1, "Downloading package...")

            # Download .vspkg
            download_path = self._staging_dir / f"{plugin_id}-{plugin_version.version}.vspkg"

            await self._download_file(
                plugin_version.download_url,
                download_path,
                plugin_version.size_bytes,
                lambda p: report(
                    InstallPhase.DOWNLOADING, 0.1 + p * 0.3, f"Downloading... {int(p*100)}%"
                ),
            )

            # Install from downloaded .vspkg
            result = await self.install_from_vspkg(
                download_path,
                verify=verify,
                atomic=atomic,
                progress_callback=progress_callback,
            )

            # Clean up download
            if download_path.exists():
                download_path.unlink(missing_ok=True)

            return result

        except Exception as e:
            logger.error(f"Installation failed: {e}", exc_info=True)

            # Clean up download
            if download_path and download_path.exists():
                download_path.unlink(missing_ok=True)

            # Rollback if needed
            if atomic and transaction_id:
                await self._rollback_transaction(transaction_id)

            return AtomicInstallResult(
                success=False,
                plugin_id=plugin_id,
                version=version or "",
                transaction_id=transaction_id,
                error=str(e),
            )

    async def uninstall_plugin(
        self,
        plugin_id: str,
        create_backup: bool = True,
    ) -> AtomicInstallResult:
        """
        Uninstall a plugin with optional backup.

        Args:
            plugin_id: Plugin identifier
            create_backup: Whether to backup before removal

        Returns:
            Uninstallation result
        """
        installed = self._installed.get(plugin_id)

        if not installed:
            return AtomicInstallResult(
                success=False,
                plugin_id=plugin_id,
                version="",
                error=f"Plugin '{plugin_id}' is not installed",
            )

        try:
            version = installed.version
            backup = None

            # Create backup
            if create_backup:
                backup = await self._create_backup(plugin_id)

            # Remove files
            install_path = Path(installed.install_path)
            if install_path.exists():
                shutil.rmtree(install_path)

            # Update registry
            del self._installed[plugin_id]
            self._save_registry()

            logger.info(f"Uninstalled plugin: {plugin_id}")

            return AtomicInstallResult(
                success=True,
                plugin_id=plugin_id,
                version=version,
                rollback_available=backup is not None,
            )

        except Exception as e:
            logger.error(f"Uninstallation failed: {e}", exc_info=True)
            return AtomicInstallResult(
                success=False,
                plugin_id=plugin_id,
                version=installed.version,
                error=str(e),
            )

    async def rollback_plugin(self, plugin_id: str) -> AtomicInstallResult:
        """
        Rollback a plugin to its previous version from backup.

        Args:
            plugin_id: Plugin identifier

        Returns:
            Rollback result
        """
        # Find latest backup
        backup = self._find_latest_backup(plugin_id)

        if not backup:
            return AtomicInstallResult(
                success=False,
                plugin_id=plugin_id,
                version="",
                error=f"No backup found for plugin '{plugin_id}'",
            )

        try:
            backup_path = Path(backup.backup_path)

            if not backup_path.exists():
                return AtomicInstallResult(
                    success=False,
                    plugin_id=plugin_id,
                    version=backup.version,
                    error="Backup files not found",
                )

            # Remove current installation
            current = self._installed.get(plugin_id)
            if current:
                current_path = Path(current.install_path)
                if current_path.exists():
                    shutil.rmtree(current_path)

            # Restore from backup
            install_path = Path(backup.registry_data.get("install_path", ""))
            install_path.parent.mkdir(parents=True, exist_ok=True)

            shutil.copytree(backup_path, install_path)

            # Restore registry entry
            self._installed[plugin_id] = InstalledPlugin(
                id=plugin_id,
                version=backup.version,
                installed_at=datetime.fromisoformat(
                    backup.registry_data.get("installed_at", datetime.now().isoformat())
                ),
                install_path=str(install_path),
                state=backup.registry_data.get("state", "enabled"),
                config=backup.registry_data.get("config", {}),
            )
            self._save_registry()

            logger.info(f"Rolled back plugin {plugin_id} to version {backup.version}")

            return AtomicInstallResult(
                success=True,
                plugin_id=plugin_id,
                version=backup.version,
                install_path=str(install_path),
            )

        except Exception as e:
            logger.error(f"Rollback failed: {e}", exc_info=True)
            return AtomicInstallResult(
                success=False,
                plugin_id=plugin_id,
                version=backup.version,
                error=str(e),
            )

    async def update_plugin(
        self,
        plugin_id: str,
        target_version: str | None = None,
        verify: bool = True,
        progress_callback: Callable[[InstallProgress], None] | None = None,
    ) -> AtomicInstallResult:
        """
        Update a plugin atomically with automatic rollback on failure.

        Args:
            plugin_id: Plugin identifier
            target_version: Target version (None = latest)
            verify: Whether to verify
            progress_callback: Progress callback

        Returns:
            Update result
        """
        current = self._installed.get(plugin_id)

        if not current:
            return AtomicInstallResult(
                success=False,
                plugin_id=plugin_id,
                version=target_version or "",
                error=f"Plugin '{plugin_id}' is not installed",
            )

        # Create backup of current version
        backup = await self._create_backup(plugin_id)

        # Install new version (will remove old one atomically)
        result = await self.install_plugin(
            plugin_id,
            version=target_version,
            verify=verify,
            atomic=True,
            progress_callback=progress_callback,
        )

        if not result.success and backup:
            # Rollback to previous version
            rollback_result = await self.rollback_plugin(plugin_id)
            if rollback_result.success:
                result.error = f"{result.error}. Rolled back to version {backup.version}."

        result.rollback_available = backup is not None
        return result

    def get_installed_plugins(self) -> list[InstalledPlugin]:
        """Get all installed plugins."""
        return list(self._installed.values())

    def get_installed_plugin(self, plugin_id: str) -> InstalledPlugin | None:
        """Get a specific installed plugin."""
        return self._installed.get(plugin_id)

    def is_installed(self, plugin_id: str) -> bool:
        """Check if a plugin is installed."""
        return plugin_id in self._installed

    def get_backups(self, plugin_id: str) -> list[BackupInfo]:
        """Get all backups for a plugin."""
        backups = []
        plugin_backup_dir = self._backups_dir / plugin_id

        if not plugin_backup_dir.exists():
            return backups

        for version_dir in plugin_backup_dir.iterdir():
            if version_dir.is_dir():
                meta_file = version_dir / "backup.json"
                if meta_file.exists():
                    try:
                        data = json.loads(meta_file.read_text())
                        backups.append(
                            BackupInfo(
                                plugin_id=data["plugin_id"],
                                version=data["version"],
                                backup_path=data["backup_path"],
                                created_at=datetime.fromisoformat(data["created_at"]),
                                registry_data=data.get("registry_data", {}),
                            )
                        )
                    except Exception as e:
                        logger.warning(f"Failed to load backup metadata: {e}")

        return sorted(backups, key=lambda b: b.created_at, reverse=True)

    def cleanup_old_backups(self, plugin_id: str, keep: int = 3) -> int:
        """
        Clean up old backups, keeping only the most recent ones.

        Args:
            plugin_id: Plugin identifier
            keep: Number of backups to keep

        Returns:
            Number of backups removed
        """
        backups = self.get_backups(plugin_id)
        removed = 0

        for backup in backups[keep:]:
            try:
                backup_path = Path(backup.backup_path)
                if backup_path.exists():
                    shutil.rmtree(backup_path)
                    removed += 1
            except Exception as e:
                logger.warning(f"Failed to remove backup: {e}")

        return removed

    # Private methods

    async def _extract_manifest(self, package_path: Path) -> dict[str, Any] | None:
        """Extract manifest from .vspkg package."""
        try:
            with zipfile.ZipFile(package_path, "r") as zf:
                # Look for manifest in standard locations
                for manifest_name in ["plugin.json", "manifest.json", "voicestudio-plugin.json"]:
                    try:
                        manifest_data = zf.read(manifest_name)
                        return json.loads(manifest_data)
                    except KeyError:
                        continue

            return None
        except Exception as e:
            logger.error(f"Failed to extract manifest: {e}")
            return None

    async def _verify_package(self, package_path: Path) -> PackageVerificationResult:
        """Verify a .vspkg package."""
        errors = []
        warnings = []

        # Verify checksum (basic file integrity)
        checksum_valid = await self._verify_file_integrity(package_path)

        # Check for signature
        signature_valid = None
        try:
            with zipfile.ZipFile(package_path, "r") as zf:
                if "signature.json" in zf.namelist() or "SIGNATURE" in zf.namelist():
                    # Signature present - verify it
                    signature_valid = await self._verify_signature(package_path)
                    if not signature_valid:
                        errors.append("Invalid package signature")
                else:
                    warnings.append("Package is not signed")
        except Exception as e:
            errors.append(f"Signature check failed: {e}")

        # Check for SBOM
        sbom_present = False
        try:
            with zipfile.ZipFile(package_path, "r") as zf:
                sbom_present = any(
                    "sbom" in name.lower() or "bom" in name.lower() for name in zf.namelist()
                )
                if not sbom_present:
                    warnings.append("No SBOM included in package")
        except Exception as e:
            logger.warning(f"SBOM check failed: {e}")

        # Check for provenance
        provenance_present = False
        try:
            with zipfile.ZipFile(package_path, "r") as zf:
                provenance_present = any("provenance" in name.lower() for name in zf.namelist())
                if not provenance_present:
                    warnings.append("No provenance information in package")
        except Exception as e:
            logger.warning(f"Provenance check failed: {e}")

        # Validate manifest
        manifest = await self._extract_manifest(package_path)
        manifest_valid = manifest is not None and "id" in manifest and "version" in manifest

        if not manifest_valid:
            errors.append("Invalid or missing manifest")

        # Determine overall validity
        # Package is valid if: checksum OK, manifest OK, and no critical errors
        valid = checksum_valid and manifest_valid and len(errors) == 0

        return PackageVerificationResult(
            valid=valid,
            signature_valid=signature_valid,
            sbom_present=sbom_present,
            provenance_present=provenance_present,
            checksum_valid=checksum_valid,
            manifest_valid=manifest_valid,
            errors=errors,
            warnings=warnings,
        )

    async def _verify_file_integrity(self, file_path: Path) -> bool:
        """Verify basic file integrity."""
        try:
            # Just ensure the file is a valid zip
            with zipfile.ZipFile(file_path, "r") as zf:
                # Test all files in archive
                return zf.testzip() is None
        except Exception:
            return False

    async def _verify_signature(self, package_path: Path) -> bool:
        """
        Verify package signature.

        GAP-PY-007: Integrated with signer.py for actual signature verification.
        Falls back to signature file existence check if signer is unavailable.
        """
        # GAP-PY-007: Use actual signature verification when available
        if SIGNER_AVAILABLE and verify_package_auto is not None:
            try:
                result = verify_package_auto(package_path)
                if result.valid:
                    logger.debug(
                        f"Signature verification passed for {package_path.name}: "
                        f"key_id={result.key_id}, algorithm={result.algorithm}"
                    )
                    return True
                else:
                    logger.warning(
                        f"Signature verification failed for {package_path.name}: "
                        f"{result.message}"
                    )
                    return False
            except Exception as e:
                logger.warning(f"Signature verification error for {package_path.name}: {e}")
                # Fall through to legacy check

        # Legacy fallback: check if signature file exists
        try:
            with zipfile.ZipFile(package_path, "r") as zf:
                has_sig = "signature.json" in zf.namelist() or "SIGNATURE" in zf.namelist()
                if has_sig:
                    logger.debug(
                        f"Signature file found in {package_path.name} "
                        "(signer module not available for verification)"
                    )
                return has_sig
        except Exception:
            return False

    async def _check_dependencies(self, dependencies: dict[str, str]) -> DependencyCheckResult:
        """Check if dependencies are satisfied."""
        missing = []
        incompatible = []
        details = {}

        for dep_name, version_spec in dependencies.items():
            if version_spec.startswith("optional:"):
                details[dep_name] = {"status": "optional", "installed": False}
                continue

            # Check plugin dependencies
            if dep_name.startswith("plugin:"):
                plugin_id = dep_name[7:]
                if plugin_id in self._installed:
                    details[dep_name] = {
                        "status": "satisfied",
                        "version": self._installed[plugin_id].version,
                    }
                else:
                    missing.append(dep_name)
                    details[dep_name] = {"status": "missing"}
            # Check system dependencies
            elif dep_name == "python":
                import sys

                current = (
                    f"{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}"
                )
                details[dep_name] = {"status": "satisfied", "version": current}
            elif dep_name == "pytorch" or dep_name == "torch":
                try:
                    import torch

                    details[dep_name] = {"status": "satisfied", "version": torch.__version__}
                except ImportError:
                    missing.append(dep_name)
                    details[dep_name] = {"status": "missing"}
            else:
                details[dep_name] = {"status": "unknown", "assumed": True}

        return DependencyCheckResult(
            satisfied=len(missing) == 0 and len(incompatible) == 0,
            missing=missing,
            incompatible=incompatible,
            details=details,
        )

    async def _extract_to_staging(
        self,
        package_path: Path,
        plugin_id: str,
        version: str,
    ) -> Path:
        """Extract package to staging area."""
        staging_path = self._staging_dir / f"{plugin_id}-{version}"

        # Clean existing staging
        if staging_path.exists():
            shutil.rmtree(staging_path)

        staging_path.mkdir(parents=True, exist_ok=True)

        with zipfile.ZipFile(package_path, "r") as zf:
            zf.extractall(staging_path)

        return staging_path

    async def _create_backup(self, plugin_id: str) -> BackupInfo | None:
        """Create a backup of an installed plugin."""
        installed = self._installed.get(plugin_id)

        if not installed:
            return None

        try:
            install_path = Path(installed.install_path)

            if not install_path.exists():
                return None

            # Create backup directory
            backup_dir = self._backups_dir / plugin_id / installed.version
            backup_dir.mkdir(parents=True, exist_ok=True)

            # Copy plugin files
            backup_files_dir = backup_dir / "files"
            if backup_files_dir.exists():
                shutil.rmtree(backup_files_dir)
            shutil.copytree(install_path, backup_files_dir)

            # Save metadata
            backup = BackupInfo(
                plugin_id=plugin_id,
                version=installed.version,
                backup_path=str(backup_files_dir),
                created_at=datetime.now(),
                registry_data=installed.to_dict(),
            )

            (backup_dir / "backup.json").write_text(json.dumps(backup.to_dict(), indent=2))

            logger.info(f"Created backup for {plugin_id} v{installed.version}")

            return backup

        except Exception as e:
            logger.error(f"Failed to create backup: {e}")
            return None

    def _find_latest_backup(self, plugin_id: str) -> BackupInfo | None:
        """Find the most recent backup for a plugin."""
        backups = self.get_backups(plugin_id)
        return backups[0] if backups else None

    def _create_transaction(self) -> str:
        """Create a new installation transaction."""
        import uuid

        transaction_id = str(uuid.uuid4())

        transaction = InstallTransaction(
            id=transaction_id,
            state=TransactionState.IN_PROGRESS,
            started_at=datetime.now(),
        )

        self._active_transactions[transaction_id] = transaction

        return transaction_id

    def _commit_transaction(self, transaction_id: str) -> None:
        """Commit a transaction."""
        transaction = self._active_transactions.get(transaction_id)

        if transaction:
            transaction.state = TransactionState.COMMITTED
            transaction.completed_at = datetime.now()

            # Save transaction record
            self._save_transaction(transaction)

    async def _rollback_transaction(self, transaction_id: str) -> bool:
        """Rollback a transaction."""
        transaction = self._active_transactions.get(transaction_id)

        if not transaction:
            return False

        try:
            # Restore all backups
            for backup in transaction.backups:
                backup_path = Path(backup.backup_path)

                if backup_path.exists():
                    install_path = Path(backup.registry_data.get("install_path", ""))

                    # Remove failed installation
                    if install_path.exists():
                        shutil.rmtree(install_path, ignore_errors=True)

                    # Restore backup
                    shutil.copytree(backup_path, install_path)

                    # Restore registry
                    self._installed[backup.plugin_id] = InstalledPlugin(
                        id=backup.plugin_id,
                        version=backup.version,
                        installed_at=datetime.fromisoformat(
                            backup.registry_data.get("installed_at", datetime.now().isoformat())
                        ),
                        install_path=str(install_path),
                        state=backup.registry_data.get("state", "enabled"),
                        config=backup.registry_data.get("config", {}),
                    )

            self._save_registry()

            transaction.state = TransactionState.ROLLED_BACK
            transaction.completed_at = datetime.now()
            self._save_transaction(transaction)

            logger.info(f"Rolled back transaction {transaction_id}")
            return True

        except Exception as e:
            logger.error(f"Transaction rollback failed: {e}")
            transaction.state = TransactionState.FAILED
            transaction.error = str(e)
            return False

    def _save_transaction(self, transaction: InstallTransaction) -> None:
        """Save transaction record."""
        try:
            trans_file = self._transactions_dir / f"{transaction.id}.json"
            trans_file.write_text(json.dumps(transaction.to_dict(), indent=2))
        except Exception as e:
            logger.error(f"Failed to save transaction: {e}")

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
                "version": "2.0.0",
                "updated_at": datetime.now().isoformat(),
                "plugins": {
                    plugin_id: plugin.to_dict() for plugin_id, plugin in self._installed.items()
                },
            }
            self._registry_file.write_text(json.dumps(data, indent=2))
        except Exception as e:
            logger.error(f"Failed to save plugin registry: {e}")


# Singleton instance
_installer_v2: PluginInstallerV2 | None = None


def get_installer_v2() -> PluginInstallerV2:
    """Get or create the installer v2 singleton."""
    global _installer_v2
    if _installer_v2 is None:
        _installer_v2 = PluginInstallerV2()
    return _installer_v2


# Convenience functions


async def install_from_vspkg(
    package_path: Path,
    verify: bool = True,
    atomic: bool = True,
) -> AtomicInstallResult:
    """Install a plugin from a .vspkg package."""
    return await get_installer_v2().install_from_vspkg(package_path, verify, atomic)


async def install_plugin_atomic(
    plugin_id: str,
    version: str | None = None,
    verify: bool = True,
) -> AtomicInstallResult:
    """Install a plugin atomically."""
    return await get_installer_v2().install_plugin(plugin_id, version, verify, atomic=True)


async def rollback_plugin(plugin_id: str) -> AtomicInstallResult:
    """Rollback a plugin to its previous version."""
    return await get_installer_v2().rollback_plugin(plugin_id)
