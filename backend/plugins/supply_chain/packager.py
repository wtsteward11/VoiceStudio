"""
Plugin Packager with SBOM Integration.

Creates .vspkg plugin packages with embedded Software Bill of Materials (SBOM)
for supply chain security compliance.

Phase 5B M1: Integrates CycloneDX SBOM generation into the pack command.
"""

from __future__ import annotations

import hashlib
import json
import logging
import os
import shutil
import tarfile
import tempfile
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional

from .sbom import SBOM, SBOMFormat, SBOMGenerator, generate_sbom

logger = logging.getLogger(__name__)


class PackageFormat(Enum):
    """Supported package formats."""

    VSPKG = "vspkg"  # VoiceStudio native package (tar.gz with metadata)
    ZIP = "zip"  # Standard ZIP archive


class PackagePhase(Enum):
    """Phases of the packaging process."""

    PREPARING = "preparing"
    VALIDATING = "validating"
    GENERATING_SBOM = "generating_sbom"
    PACKAGING = "packaging"
    SIGNING = "signing"
    FINALIZING = "finalizing"
    COMPLETE = "complete"


@dataclass
class PackageProgress:
    """Progress information for packaging operations."""

    phase: PackagePhase
    progress: float  # 0.0 to 1.0
    message: str = ""
    current_file: Optional[str] = None


@dataclass
class PackageManifest:
    """Metadata about a packaged plugin."""

    plugin_id: str
    plugin_name: str
    version: str
    created_at: str
    package_format: str
    checksum_sha256: str
    size_bytes: int
    files_count: int
    has_sbom: bool
    sbom_format: Optional[str] = None
    signature: Optional[str] = None
    provenance: Optional[Dict[str, Any]] = None

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary."""
        return {
            "plugin_id": self.plugin_id,
            "plugin_name": self.plugin_name,
            "version": self.version,
            "created_at": self.created_at,
            "package_format": self.package_format,
            "checksum_sha256": self.checksum_sha256,
            "size_bytes": self.size_bytes,
            "files_count": self.files_count,
            "has_sbom": self.has_sbom,
            "sbom_format": self.sbom_format,
            "signature": self.signature,
            "provenance": self.provenance,
        }

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> PackageManifest:
        """Create from dictionary."""
        return cls(
            plugin_id=data.get("plugin_id", ""),
            plugin_name=data.get("plugin_name", ""),
            version=data.get("version", ""),
            created_at=data.get("created_at", ""),
            package_format=data.get("package_format", ""),
            checksum_sha256=data.get("checksum_sha256", ""),
            size_bytes=data.get("size_bytes", 0),
            files_count=data.get("files_count", 0),
            has_sbom=data.get("has_sbom", False),
            sbom_format=data.get("sbom_format"),
            signature=data.get("signature"),
            provenance=data.get("provenance"),
        )


@dataclass
class PackageResult:
    """Result of a packaging operation."""

    success: bool
    package_path: Optional[str] = None
    manifest: Optional[PackageManifest] = None
    sbom: Optional[SBOM] = None
    error: Optional[str] = None
    warnings: List[str] = field(default_factory=list)


@dataclass
class PackageConfig:
    """Configuration for packaging a plugin."""

    plugin_path: Path
    output_dir: Path
    plugin_name: Optional[str] = None
    plugin_version: Optional[str] = None
    package_format: PackageFormat = PackageFormat.VSPKG
    include_sbom: bool = True
    sbom_format: SBOMFormat = SBOMFormat.JSON
    include_transitive_deps: bool = True
    sign_package: bool = False
    signing_key: Optional[str] = None
    exclude_patterns: List[str] = field(
        default_factory=lambda: [
            "__pycache__",
            "*.pyc",
            "*.pyo",
            ".git",
            ".gitignore",
            "*.egg-info",
            ".pytest_cache",
            ".tox",
            ".venv",
            "venv",
            "*.log",
        ]
    )


class PluginPackager:
    """
    Plugin packaging service with SBOM integration.

    Creates distributable plugin packages with:
    - CycloneDX SBOM for dependency tracking
    - Package manifest with checksums
    - Optional digital signatures
    - Provenance metadata
    """

    def __init__(self, config: PackageConfig):
        """
        Initialize packager.

        Args:
            config: Packaging configuration
        """
        self.config = config
        self._validate_config()

    def _validate_config(self) -> None:
        """Validate configuration."""
        if not self.config.plugin_path.exists():
            raise ValueError(f"Plugin path does not exist: {self.config.plugin_path}")

        if not self.config.plugin_path.is_dir():
            raise ValueError(f"Plugin path is not a directory: {self.config.plugin_path}")

        # Ensure output directory exists
        self.config.output_dir.mkdir(parents=True, exist_ok=True)

    def _read_plugin_metadata(self) -> Dict[str, Any]:
        """Read plugin metadata from manifest or pyproject.toml."""
        # Try plugin.json manifest
        manifest_path = self.config.plugin_path / "plugin.json"
        if manifest_path.exists():
            try:
                return json.loads(manifest_path.read_text(encoding="utf-8"))
            except json.JSONDecodeError as e:
                logger.warning(f"Invalid plugin.json: {e}")

        # Try pyproject.toml
        pyproject_path = self.config.plugin_path / "pyproject.toml"
        if pyproject_path.exists():
            try:
                import tomllib
            except ImportError:
                try:
                    import tomli as tomllib  # type: ignore
                except ImportError:
                    logger.warning("TOML parser not available")
                    return {}

            try:
                content = pyproject_path.read_text(encoding="utf-8")
                data = tomllib.loads(content)
                project = data.get("project", {})
                return {
                    "name": project.get("name", ""),
                    "version": project.get("version", ""),
                    "description": project.get("description", ""),
                }
            except Exception as e:
                logger.warning(f"Failed to parse pyproject.toml: {e}")

        return {}

    def _get_files_to_package(self) -> List[Path]:
        """Get list of files to include in package."""
        files = []
        exclude_patterns = self.config.exclude_patterns

        for path in self.config.plugin_path.rglob("*"):
            if path.is_file():
                # Check exclusion patterns
                relative = path.relative_to(self.config.plugin_path)
                excluded = False

                for pattern in exclude_patterns:
                    if "*" in pattern:
                        # Glob pattern
                        import fnmatch

                        if any(fnmatch.fnmatch(part, pattern) for part in relative.parts):
                            excluded = True
                            break
                    else:
                        # Exact match
                        if pattern in relative.parts:
                            excluded = True
                            break

                if not excluded:
                    files.append(path)

        return files

    def _calculate_checksum(self, file_path: Path) -> str:
        """Calculate SHA-256 checksum of a file."""
        sha256 = hashlib.sha256()
        with open(file_path, "rb") as f:
            for chunk in iter(lambda: f.read(8192), b""):
                sha256.update(chunk)
        return sha256.hexdigest()

    def pack(
        self,
        progress_callback: Optional[Callable[[PackageProgress], None]] = None,
    ) -> PackageResult:
        """
        Create a plugin package.

        Args:
            progress_callback: Optional callback for progress updates

        Returns:
            PackageResult with package details
        """

        def report(
            phase: PackagePhase, progress: float, message: str = "", current_file: str | None = None
        ):
            if progress_callback:
                progress_callback(
                    PackageProgress(
                        phase=phase,
                        progress=progress,
                        message=message,
                        current_file=current_file,
                    )
                )

        warnings = []

        try:
            report(PackagePhase.PREPARING, 0.0, "Preparing to package plugin...")

            # Read plugin metadata
            metadata = self._read_plugin_metadata()
            plugin_name = (
                self.config.plugin_name or metadata.get("name") or self.config.plugin_path.name
            )
            plugin_version = self.config.plugin_version or metadata.get("version") or "0.0.0"
            plugin_id = metadata.get("id") or plugin_name.lower().replace(" ", "-").replace(
                "_", "-"
            )

            report(PackagePhase.VALIDATING, 0.1, "Validating plugin structure...")

            # Get files to package
            files_to_package = self._get_files_to_package()
            if not files_to_package:
                return PackageResult(
                    success=False,
                    error="No files to package",
                )

            logger.info(f"Found {len(files_to_package)} files to package")

            # Generate SBOM if requested
            sbom: Optional[SBOM] = None
            if self.config.include_sbom:
                report(
                    PackagePhase.GENERATING_SBOM, 0.2, "Generating Software Bill of Materials..."
                )

                try:
                    generator = SBOMGenerator(
                        plugin_path=self.config.plugin_path,
                        plugin_name=plugin_name,
                        plugin_version=plugin_version,
                    )
                    sbom = generator.generate(
                        include_transitive=self.config.include_transitive_deps,
                    )
                    logger.info(f"Generated SBOM with {len(sbom.components)} components")
                except Exception as e:
                    logger.warning(f"Failed to generate SBOM: {e}")
                    warnings.append(f"SBOM generation failed: {e}")

            report(PackagePhase.PACKAGING, 0.4, "Creating package archive...")

            # Create package in temp directory
            with tempfile.TemporaryDirectory() as temp_dir:
                temp_path = Path(temp_dir)
                package_dir = temp_path / "package"
                package_dir.mkdir()

                # Copy plugin files
                for i, file_path in enumerate(files_to_package):
                    relative = file_path.relative_to(self.config.plugin_path)
                    dest_path = package_dir / relative
                    dest_path.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(file_path, dest_path)

                    if progress_callback and len(files_to_package) > 0:
                        progress = 0.4 + (i / len(files_to_package)) * 0.3
                        report(PackagePhase.PACKAGING, progress, "Copying files...", str(relative))

                # Add SBOM if generated
                if sbom:
                    sbom_filename = f"sbom.{self.config.sbom_format.value}"
                    sbom_path = package_dir / ".vspkg" / sbom_filename
                    sbom_path.parent.mkdir(parents=True, exist_ok=True)
                    sbom.save(sbom_path, self.config.sbom_format)
                    logger.info(f"Saved SBOM to {sbom_path}")

                # Create package manifest
                report(PackagePhase.FINALIZING, 0.8, "Creating package manifest...")

                # Create archive
                package_filename = (
                    f"{plugin_id}-{plugin_version}.{self.config.package_format.value}"
                )
                archive_path = temp_path / package_filename

                if self.config.package_format == PackageFormat.VSPKG:
                    # Create tar.gz
                    with tarfile.open(archive_path, "w:gz") as tar:
                        for file_path in package_dir.rglob("*"):
                            if file_path.is_file():
                                arcname = file_path.relative_to(package_dir)
                                tar.add(file_path, arcname=arcname)
                else:
                    # Create ZIP
                    shutil.make_archive(
                        str(archive_path.with_suffix("")),
                        "zip",
                        package_dir,
                    )
                    archive_path = archive_path.with_suffix(".zip")

                # Calculate checksum
                checksum = self._calculate_checksum(archive_path)
                size_bytes = archive_path.stat().st_size

                # Create package manifest
                manifest = PackageManifest(
                    plugin_id=plugin_id,
                    plugin_name=plugin_name,
                    version=plugin_version,
                    created_at=datetime.now().isoformat(),
                    package_format=self.config.package_format.value,
                    checksum_sha256=checksum,
                    size_bytes=size_bytes,
                    files_count=len(files_to_package),
                    has_sbom=sbom is not None,
                    sbom_format=self.config.sbom_format.value if sbom else None,
                )

                # Sign if requested
                if self.config.sign_package and self.config.signing_key:
                    report(PackagePhase.SIGNING, 0.9, "Signing package...")
                    # TODO: Implement signing in Phase 5B M4
                    warnings.append("Package signing not yet implemented")

                # Move to output directory
                final_path = self.config.output_dir / package_filename
                shutil.move(str(archive_path), str(final_path))

                # Save manifest alongside package
                manifest_path = final_path.with_suffix(final_path.suffix + ".manifest.json")
                manifest_path.write_text(json.dumps(manifest.to_dict(), indent=2))

                report(PackagePhase.COMPLETE, 1.0, "Package created successfully!")

                logger.info(f"Created package: {final_path} ({size_bytes} bytes)")

                return PackageResult(
                    success=True,
                    package_path=str(final_path),
                    manifest=manifest,
                    sbom=sbom,
                    warnings=warnings,
                )

        except Exception as e:
            logger.error(f"Packaging failed: {e}", exc_info=True)
            return PackageResult(
                success=False,
                error=str(e),
                warnings=warnings,
            )


def pack_plugin(
    plugin_path: Path | str,
    output_dir: Path | str,
    plugin_name: Optional[str] = None,
    plugin_version: Optional[str] = None,
    include_sbom: bool = True,
    sbom_format: SBOMFormat = SBOMFormat.JSON,
    progress_callback: Optional[Callable[[PackageProgress], None]] = None,
) -> PackageResult:
    """
    Convenience function to pack a plugin.

    Args:
        plugin_path: Path to plugin directory
        output_dir: Output directory for package
        plugin_name: Optional plugin name
        plugin_version: Optional plugin version
        include_sbom: Whether to include SBOM
        sbom_format: SBOM format (JSON or XML)
        progress_callback: Optional progress callback

    Returns:
        PackageResult with package details
    """
    config = PackageConfig(
        plugin_path=Path(plugin_path),
        output_dir=Path(output_dir),
        plugin_name=plugin_name,
        plugin_version=plugin_version,
        include_sbom=include_sbom,
        sbom_format=sbom_format,
    )

    packager = PluginPackager(config)
    return packager.pack(progress_callback=progress_callback)


def extract_package_sbom(package_path: Path | str) -> Optional[SBOM]:
    """
    Extract SBOM from a .vspkg package.

    Args:
        package_path: Path to package file

    Returns:
        SBOM if found, None otherwise
    """
    package_path = Path(package_path)

    if not package_path.exists():
        raise FileNotFoundError(f"Package not found: {package_path}")

    try:
        with tarfile.open(package_path, "r:gz") as tar:
            # Look for SBOM in .vspkg directory
            for member in tar.getmembers():
                if member.name.startswith(".vspkg/sbom."):
                    f = tar.extractfile(member)
                    if f:
                        content = f.read().decode("utf-8")
                        if member.name.endswith(".json"):
                            from .sbom import SBOM

                            return SBOM.from_dict(json.loads(content))
                        # TODO: Add XML parsing if needed

        return None

    except Exception as e:
        logger.error(f"Failed to extract SBOM from package: {e}")
        return None


def extract_package_manifest(package_path: Path | str) -> Optional[PackageManifest]:
    """
    Extract or load package manifest.

    Args:
        package_path: Path to package file

    Returns:
        PackageManifest if found
    """
    package_path = Path(package_path)

    # First try .manifest.json file
    manifest_path = package_path.with_suffix(package_path.suffix + ".manifest.json")
    if manifest_path.exists():
        try:
            data = json.loads(manifest_path.read_text())
            return PackageManifest.from_dict(data)
        except Exception as e:
            logger.warning(f"Failed to load manifest file: {e}")

    # Try extracting from package
    try:
        with tarfile.open(package_path, "r:gz") as tar:
            for member in tar.getmembers():
                if member.name == ".vspkg/manifest.json":
                    f = tar.extractfile(member)
                    if f:
                        content = f.read().decode("utf-8")
                        return PackageManifest.from_dict(json.loads(content))
    except Exception as e:
        logger.warning(f"Failed to extract manifest from package: {e}")

    return None
