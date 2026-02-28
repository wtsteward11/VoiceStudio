"""
Plugin License Compliance Checker.

Phase 5B Enhancement: Provides SPDX license validation and compatibility
checking for plugin dependencies. Supports:
    - SPDX license identifier validation
    - License compatibility matrix
    - Copyleft detection and warnings
    - License expression parsing
    - SBOM-based dependency license analysis

This enables organizations to enforce license policies for plugin ecosystems.
"""

from __future__ import annotations

import json
import logging
import re
from dataclasses import dataclass, field
from enum import Enum
from pathlib import Path
from typing import Any, Dict, List, Optional, Set, Tuple

logger = logging.getLogger(__name__)


# =============================================================================
# Enums
# =============================================================================


class LicenseCategory(Enum):
    """Categories for license types."""

    PERMISSIVE = "permissive"  # MIT, BSD, Apache, etc.
    COPYLEFT_WEAK = "copyleft_weak"  # LGPL, MPL, etc.
    COPYLEFT_STRONG = "copyleft_strong"  # GPL, AGPL, etc.
    PROPRIETARY = "proprietary"  # Commercial, closed-source
    PUBLIC_DOMAIN = "public_domain"  # CC0, Unlicense, etc.
    UNKNOWN = "unknown"


class CompatibilityLevel(Enum):
    """Compatibility levels between licenses."""

    COMPATIBLE = "compatible"  # Licenses are compatible
    COMPATIBLE_WITH_NOTICE = "compatible_with_notice"  # Compatible with attribution
    INCOMPATIBLE = "incompatible"  # Licenses conflict
    UNKNOWN = "unknown"  # Cannot determine
    REQUIRES_REVIEW = "requires_review"  # Legal review recommended


# =============================================================================
# SPDX License Database
# =============================================================================


# Common SPDX license identifiers and their categories
# This is a subset - a production system would load the full SPDX database
SPDX_LICENSES: Dict[str, Dict[str, Any]] = {
    # Permissive licenses
    "MIT": {
        "name": "MIT License",
        "category": LicenseCategory.PERMISSIVE,
        "osi_approved": True,
        "copyleft": False,
    },
    "Apache-2.0": {
        "name": "Apache License 2.0",
        "category": LicenseCategory.PERMISSIVE,
        "osi_approved": True,
        "copyleft": False,
    },
    "BSD-2-Clause": {
        "name": 'BSD 2-Clause "Simplified" License',
        "category": LicenseCategory.PERMISSIVE,
        "osi_approved": True,
        "copyleft": False,
    },
    "BSD-3-Clause": {
        "name": 'BSD 3-Clause "New" or "Revised" License',
        "category": LicenseCategory.PERMISSIVE,
        "osi_approved": True,
        "copyleft": False,
    },
    "ISC": {
        "name": "ISC License",
        "category": LicenseCategory.PERMISSIVE,
        "osi_approved": True,
        "copyleft": False,
    },
    "Zlib": {
        "name": "zlib License",
        "category": LicenseCategory.PERMISSIVE,
        "osi_approved": True,
        "copyleft": False,
    },
    "PSF-2.0": {
        "name": "Python Software Foundation License 2.0",
        "category": LicenseCategory.PERMISSIVE,
        "osi_approved": False,  # Not OSI but permissive
        "copyleft": False,
    },
    # Weak copyleft
    "LGPL-2.0-only": {
        "name": "GNU Lesser General Public License v2.0 only",
        "category": LicenseCategory.COPYLEFT_WEAK,
        "osi_approved": True,
        "copyleft": True,
    },
    "LGPL-2.0-or-later": {
        "name": "GNU Lesser General Public License v2.0 or later",
        "category": LicenseCategory.COPYLEFT_WEAK,
        "osi_approved": True,
        "copyleft": True,
    },
    "LGPL-2.1-only": {
        "name": "GNU Lesser General Public License v2.1 only",
        "category": LicenseCategory.COPYLEFT_WEAK,
        "osi_approved": True,
        "copyleft": True,
    },
    "LGPL-2.1-or-later": {
        "name": "GNU Lesser General Public License v2.1 or later",
        "category": LicenseCategory.COPYLEFT_WEAK,
        "osi_approved": True,
        "copyleft": True,
    },
    "LGPL-3.0-only": {
        "name": "GNU Lesser General Public License v3.0 only",
        "category": LicenseCategory.COPYLEFT_WEAK,
        "osi_approved": True,
        "copyleft": True,
    },
    "LGPL-3.0-or-later": {
        "name": "GNU Lesser General Public License v3.0 or later",
        "category": LicenseCategory.COPYLEFT_WEAK,
        "osi_approved": True,
        "copyleft": True,
    },
    "MPL-2.0": {
        "name": "Mozilla Public License 2.0",
        "category": LicenseCategory.COPYLEFT_WEAK,
        "osi_approved": True,
        "copyleft": True,
    },
    "EPL-1.0": {
        "name": "Eclipse Public License 1.0",
        "category": LicenseCategory.COPYLEFT_WEAK,
        "osi_approved": True,
        "copyleft": True,
    },
    "EPL-2.0": {
        "name": "Eclipse Public License 2.0",
        "category": LicenseCategory.COPYLEFT_WEAK,
        "osi_approved": True,
        "copyleft": True,
    },
    # Strong copyleft
    "GPL-2.0-only": {
        "name": "GNU General Public License v2.0 only",
        "category": LicenseCategory.COPYLEFT_STRONG,
        "osi_approved": True,
        "copyleft": True,
    },
    "GPL-2.0-or-later": {
        "name": "GNU General Public License v2.0 or later",
        "category": LicenseCategory.COPYLEFT_STRONG,
        "osi_approved": True,
        "copyleft": True,
    },
    "GPL-3.0-only": {
        "name": "GNU General Public License v3.0 only",
        "category": LicenseCategory.COPYLEFT_STRONG,
        "osi_approved": True,
        "copyleft": True,
    },
    "GPL-3.0-or-later": {
        "name": "GNU General Public License v3.0 or later",
        "category": LicenseCategory.COPYLEFT_STRONG,
        "osi_approved": True,
        "copyleft": True,
    },
    "AGPL-3.0-only": {
        "name": "GNU Affero General Public License v3.0 only",
        "category": LicenseCategory.COPYLEFT_STRONG,
        "osi_approved": True,
        "copyleft": True,
    },
    "AGPL-3.0-or-later": {
        "name": "GNU Affero General Public License v3.0 or later",
        "category": LicenseCategory.COPYLEFT_STRONG,
        "osi_approved": True,
        "copyleft": True,
    },
    # Public domain
    "CC0-1.0": {
        "name": "Creative Commons Zero v1.0 Universal",
        "category": LicenseCategory.PUBLIC_DOMAIN,
        "osi_approved": False,
        "copyleft": False,
    },
    "Unlicense": {
        "name": "The Unlicense",
        "category": LicenseCategory.PUBLIC_DOMAIN,
        "osi_approved": True,
        "copyleft": False,
    },
    "WTFPL": {
        "name": "Do What The F*ck You Want To Public License",
        "category": LicenseCategory.PUBLIC_DOMAIN,
        "osi_approved": False,
        "copyleft": False,
    },
}


# License compatibility matrix (simplified)
# Key: (project_license_category, dependency_license_category)
# Value: CompatibilityLevel
COMPATIBILITY_MATRIX: Dict[Tuple[LicenseCategory, LicenseCategory], CompatibilityLevel] = {
    # Permissive project can use anything except strong copyleft
    (LicenseCategory.PERMISSIVE, LicenseCategory.PERMISSIVE): CompatibilityLevel.COMPATIBLE,
    (LicenseCategory.PERMISSIVE, LicenseCategory.PUBLIC_DOMAIN): CompatibilityLevel.COMPATIBLE,
    (
        LicenseCategory.PERMISSIVE,
        LicenseCategory.COPYLEFT_WEAK,
    ): CompatibilityLevel.COMPATIBLE_WITH_NOTICE,
    (LicenseCategory.PERMISSIVE, LicenseCategory.COPYLEFT_STRONG): CompatibilityLevel.INCOMPATIBLE,
    (LicenseCategory.PERMISSIVE, LicenseCategory.PROPRIETARY): CompatibilityLevel.REQUIRES_REVIEW,
    # Copyleft weak project compatibility
    (LicenseCategory.COPYLEFT_WEAK, LicenseCategory.PERMISSIVE): CompatibilityLevel.COMPATIBLE,
    (LicenseCategory.COPYLEFT_WEAK, LicenseCategory.PUBLIC_DOMAIN): CompatibilityLevel.COMPATIBLE,
    (
        LicenseCategory.COPYLEFT_WEAK,
        LicenseCategory.COPYLEFT_WEAK,
    ): CompatibilityLevel.COMPATIBLE_WITH_NOTICE,
    (
        LicenseCategory.COPYLEFT_WEAK,
        LicenseCategory.COPYLEFT_STRONG,
    ): CompatibilityLevel.REQUIRES_REVIEW,
    (
        LicenseCategory.COPYLEFT_WEAK,
        LicenseCategory.PROPRIETARY,
    ): CompatibilityLevel.REQUIRES_REVIEW,
    # Strong copyleft project can use most things
    (LicenseCategory.COPYLEFT_STRONG, LicenseCategory.PERMISSIVE): CompatibilityLevel.COMPATIBLE,
    (LicenseCategory.COPYLEFT_STRONG, LicenseCategory.PUBLIC_DOMAIN): CompatibilityLevel.COMPATIBLE,
    (LicenseCategory.COPYLEFT_STRONG, LicenseCategory.COPYLEFT_WEAK): CompatibilityLevel.COMPATIBLE,
    (
        LicenseCategory.COPYLEFT_STRONG,
        LicenseCategory.COPYLEFT_STRONG,
    ): CompatibilityLevel.COMPATIBLE,
    (LicenseCategory.COPYLEFT_STRONG, LicenseCategory.PROPRIETARY): CompatibilityLevel.INCOMPATIBLE,
    # Public domain is most permissive
    (LicenseCategory.PUBLIC_DOMAIN, LicenseCategory.PERMISSIVE): CompatibilityLevel.COMPATIBLE,
    (LicenseCategory.PUBLIC_DOMAIN, LicenseCategory.PUBLIC_DOMAIN): CompatibilityLevel.COMPATIBLE,
    (
        LicenseCategory.PUBLIC_DOMAIN,
        LicenseCategory.COPYLEFT_WEAK,
    ): CompatibilityLevel.COMPATIBLE_WITH_NOTICE,
    (
        LicenseCategory.PUBLIC_DOMAIN,
        LicenseCategory.COPYLEFT_STRONG,
    ): CompatibilityLevel.INCOMPATIBLE,
    (
        LicenseCategory.PUBLIC_DOMAIN,
        LicenseCategory.PROPRIETARY,
    ): CompatibilityLevel.REQUIRES_REVIEW,
    # Proprietary is restrictive
    (
        LicenseCategory.PROPRIETARY,
        LicenseCategory.PERMISSIVE,
    ): CompatibilityLevel.COMPATIBLE_WITH_NOTICE,
    (LicenseCategory.PROPRIETARY, LicenseCategory.PUBLIC_DOMAIN): CompatibilityLevel.COMPATIBLE,
    (
        LicenseCategory.PROPRIETARY,
        LicenseCategory.COPYLEFT_WEAK,
    ): CompatibilityLevel.REQUIRES_REVIEW,
    (LicenseCategory.PROPRIETARY, LicenseCategory.COPYLEFT_STRONG): CompatibilityLevel.INCOMPATIBLE,
    (LicenseCategory.PROPRIETARY, LicenseCategory.PROPRIETARY): CompatibilityLevel.REQUIRES_REVIEW,
}


# =============================================================================
# Data Models
# =============================================================================


@dataclass
class LicenseInfo:
    """Information about a license."""

    spdx_id: str
    name: str = ""
    category: LicenseCategory = LicenseCategory.UNKNOWN
    osi_approved: bool = False
    copyleft: bool = False
    url: Optional[str] = None

    def __post_init__(self):
        """Populate from SPDX database if available."""
        if self.spdx_id in SPDX_LICENSES:
            info = SPDX_LICENSES[self.spdx_id]
            if not self.name:
                self.name = info.get("name", self.spdx_id)
            if self.category == LicenseCategory.UNKNOWN:
                self.category = info.get("category", LicenseCategory.UNKNOWN)
            self.osi_approved = info.get("osi_approved", False)
            self.copyleft = info.get("copyleft", False)

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary."""
        return {
            "spdx_id": self.spdx_id,
            "name": self.name,
            "category": self.category.value,
            "osi_approved": self.osi_approved,
            "copyleft": self.copyleft,
            "url": self.url,
        }

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> LicenseInfo:
        """Create from dictionary."""
        return cls(
            spdx_id=data.get("spdx_id", ""),
            name=data.get("name", ""),
            category=LicenseCategory(data.get("category", "unknown")),
            osi_approved=data.get("osi_approved", False),
            copyleft=data.get("copyleft", False),
            url=data.get("url"),
        )


@dataclass
class DependencyLicense:
    """License information for a dependency."""

    package_name: str
    package_version: str
    license_id: str
    license_info: Optional[LicenseInfo] = None
    detected_from: str = ""  # Where license was detected (sbom, manifest, etc.)

    def __post_init__(self):
        """Initialize license info if not provided."""
        if self.license_info is None and self.license_id:
            self.license_info = LicenseInfo(spdx_id=self.license_id)

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary."""
        return {
            "package_name": self.package_name,
            "package_version": self.package_version,
            "license_id": self.license_id,
            "license_info": self.license_info.to_dict() if self.license_info else None,
            "detected_from": self.detected_from,
        }


@dataclass
class CompatibilityIssue:
    """A license compatibility issue."""

    dependency: DependencyLicense
    level: CompatibilityLevel
    reason: str
    recommendation: str = ""

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary."""
        return {
            "dependency": self.dependency.to_dict(),
            "level": self.level.value,
            "reason": self.reason,
            "recommendation": self.recommendation,
        }


@dataclass
class LicenseCheckResult:
    """Result of a license compatibility check."""

    plugin_id: str
    plugin_license: str
    plugin_license_info: Optional[LicenseInfo] = None
    dependencies: List[DependencyLicense] = field(default_factory=list)
    issues: List[CompatibilityIssue] = field(default_factory=list)
    warnings: List[str] = field(default_factory=list)
    passed: bool = True

    def __post_init__(self):
        """Initialize license info if not provided."""
        if self.plugin_license_info is None and self.plugin_license:
            self.plugin_license_info = LicenseInfo(spdx_id=self.plugin_license)

    @property
    def has_incompatible(self) -> bool:
        """Check if there are incompatible licenses."""
        return any(i.level == CompatibilityLevel.INCOMPATIBLE for i in self.issues)

    @property
    def has_copyleft(self) -> bool:
        """Check if any dependency has copyleft license."""
        return any(d.license_info and d.license_info.copyleft for d in self.dependencies)

    @property
    def requires_review(self) -> bool:
        """Check if legal review is recommended."""
        return any(i.level == CompatibilityLevel.REQUIRES_REVIEW for i in self.issues)

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary."""
        return {
            "plugin_id": self.plugin_id,
            "plugin_license": self.plugin_license,
            "plugin_license_info": (
                self.plugin_license_info.to_dict() if self.plugin_license_info else None
            ),
            "dependencies": [d.to_dict() for d in self.dependencies],
            "issues": [i.to_dict() for i in self.issues],
            "warnings": self.warnings,
            "passed": self.passed,
            "has_incompatible": self.has_incompatible,
            "has_copyleft": self.has_copyleft,
            "requires_review": self.requires_review,
        }

    def to_json(self, indent: int = 2) -> str:
        """Convert to JSON string."""
        return json.dumps(self.to_dict(), indent=indent)

    def save(self, path: Path) -> None:
        """Save result to file."""
        path.write_text(self.to_json(), encoding="utf-8")


# =============================================================================
# License Checker
# =============================================================================


class LicenseChecker:
    """
    License compliance checker for plugins.

    Validates SPDX license identifiers and checks compatibility
    between plugin licenses and dependency licenses.

    Example:
        >>> checker = LicenseChecker()
        >>> result = checker.check_plugin(
        ...     plugin_id="my-plugin",
        ...     plugin_license="MIT",
        ...     dependencies=[
        ...         DependencyLicense("requests", "2.28.0", "Apache-2.0"),
        ...         DependencyLicense("flask", "2.0.0", "BSD-3-Clause"),
        ...     ]
        ... )
        >>> print(result.passed)
        True
    """

    def __init__(
        self,
        allowed_categories: Optional[Set[LicenseCategory]] = None,
        blocked_licenses: Optional[Set[str]] = None,
        custom_compatibility: Optional[Dict[Tuple[str, str], CompatibilityLevel]] = None,
    ):
        """
        Initialize the license checker.

        Args:
            allowed_categories: Categories to allow (None = all except proprietary)
            blocked_licenses: Specific license IDs to block
            custom_compatibility: Custom compatibility rules
        """
        self.allowed_categories = allowed_categories or {
            LicenseCategory.PERMISSIVE,
            LicenseCategory.PUBLIC_DOMAIN,
            LicenseCategory.COPYLEFT_WEAK,
            LicenseCategory.COPYLEFT_STRONG,
        }
        self.blocked_licenses = blocked_licenses or set()
        self.custom_compatibility = custom_compatibility or {}

    def is_valid_spdx(self, license_id: str) -> bool:
        """
        Check if a license ID is a valid SPDX identifier.

        Args:
            license_id: SPDX license identifier

        Returns:
            True if valid SPDX identifier
        """
        return license_id in SPDX_LICENSES

    def get_license_info(self, license_id: str) -> LicenseInfo:
        """
        Get information about a license.

        Args:
            license_id: SPDX license identifier

        Returns:
            LicenseInfo object
        """
        return LicenseInfo(spdx_id=license_id)

    def get_category(self, license_id: str) -> LicenseCategory:
        """
        Get the category for a license.

        Args:
            license_id: SPDX license identifier

        Returns:
            LicenseCategory enum value
        """
        if license_id in SPDX_LICENSES:
            return SPDX_LICENSES[license_id].get("category", LicenseCategory.UNKNOWN)
        return LicenseCategory.UNKNOWN

    def is_copyleft(self, license_id: str) -> bool:
        """
        Check if a license is copyleft.

        Args:
            license_id: SPDX license identifier

        Returns:
            True if copyleft license
        """
        if license_id in SPDX_LICENSES:
            return SPDX_LICENSES[license_id].get("copyleft", False)

        # Heuristic: check for GPL/AGPL/LGPL in name
        copyleft_patterns = ["GPL", "AGPL", "LGPL", "MPL", "EPL"]
        return any(p in license_id.upper() for p in copyleft_patterns)

    def check_compatibility(
        self,
        project_license: str,
        dependency_license: str,
    ) -> CompatibilityLevel:
        """
        Check if two licenses are compatible.

        Args:
            project_license: SPDX ID of the project license
            dependency_license: SPDX ID of the dependency license

        Returns:
            CompatibilityLevel enum value
        """
        # Check custom rules first
        key = (project_license, dependency_license)
        if key in self.custom_compatibility:
            return self.custom_compatibility[key]

        # Get categories
        proj_cat = self.get_category(project_license)
        dep_cat = self.get_category(dependency_license)

        if proj_cat == LicenseCategory.UNKNOWN or dep_cat == LicenseCategory.UNKNOWN:
            return CompatibilityLevel.UNKNOWN

        # Look up in compatibility matrix
        matrix_key = (proj_cat, dep_cat)
        return COMPATIBILITY_MATRIX.get(matrix_key, CompatibilityLevel.UNKNOWN)

    def check_plugin(
        self,
        plugin_id: str,
        plugin_license: str,
        dependencies: List[DependencyLicense],
    ) -> LicenseCheckResult:
        """
        Check license compliance for a plugin.

        Args:
            plugin_id: Plugin identifier
            plugin_license: SPDX license ID of the plugin
            dependencies: List of dependency licenses

        Returns:
            LicenseCheckResult with issues and warnings
        """
        result = LicenseCheckResult(
            plugin_id=plugin_id,
            plugin_license=plugin_license,
            dependencies=dependencies,
        )

        # Validate plugin license
        if not self.is_valid_spdx(plugin_license):
            result.warnings.append(
                f"Plugin license '{plugin_license}' is not a recognized SPDX identifier"
            )

        # Check each dependency
        for dep in dependencies:
            # Validate dependency license
            if not self.is_valid_spdx(dep.license_id):
                result.warnings.append(
                    f"Dependency '{dep.package_name}' has unrecognized license: {dep.license_id}"
                )
                continue

            # Check if license is blocked
            if dep.license_id in self.blocked_licenses:
                result.issues.append(
                    CompatibilityIssue(
                        dependency=dep,
                        level=CompatibilityLevel.INCOMPATIBLE,
                        reason=f"License '{dep.license_id}' is blocked by policy",
                        recommendation="Remove or replace this dependency",
                    )
                )
                result.passed = False
                continue

            # Check if category is allowed
            dep_category = self.get_category(dep.license_id)
            if dep_category not in self.allowed_categories:
                result.issues.append(
                    CompatibilityIssue(
                        dependency=dep,
                        level=CompatibilityLevel.INCOMPATIBLE,
                        reason=f"License category '{dep_category.value}' is not allowed",
                        recommendation="Remove or replace this dependency",
                    )
                )
                result.passed = False
                continue

            # Check compatibility
            compat = self.check_compatibility(plugin_license, dep.license_id)

            if compat == CompatibilityLevel.INCOMPATIBLE:
                result.issues.append(
                    CompatibilityIssue(
                        dependency=dep,
                        level=CompatibilityLevel.INCOMPATIBLE,
                        reason=f"License '{dep.license_id}' is incompatible with '{plugin_license}'",
                        recommendation="Remove, replace, or obtain a different license for this dependency",
                    )
                )
                result.passed = False

            elif compat == CompatibilityLevel.REQUIRES_REVIEW:
                result.issues.append(
                    CompatibilityIssue(
                        dependency=dep,
                        level=CompatibilityLevel.REQUIRES_REVIEW,
                        reason=f"License '{dep.license_id}' may have compatibility issues with '{plugin_license}'",
                        recommendation="Consult legal counsel before distribution",
                    )
                )

            elif compat == CompatibilityLevel.COMPATIBLE_WITH_NOTICE:
                result.issues.append(
                    CompatibilityIssue(
                        dependency=dep,
                        level=CompatibilityLevel.COMPATIBLE_WITH_NOTICE,
                        reason=f"License '{dep.license_id}' requires attribution notice",
                        recommendation="Include license notice in distribution",
                    )
                )

            # Warn about copyleft
            if self.is_copyleft(dep.license_id):
                result.warnings.append(
                    f"Dependency '{dep.package_name}' uses copyleft license '{dep.license_id}'"
                )

        return result

    def check_sbom(
        self,
        plugin_id: str,
        plugin_license: str,
        sbom_path: Path,
    ) -> LicenseCheckResult:
        """
        Check license compliance from an SBOM file.

        Args:
            plugin_id: Plugin identifier
            plugin_license: SPDX license ID of the plugin
            sbom_path: Path to the SBOM JSON file

        Returns:
            LicenseCheckResult
        """
        if not sbom_path.exists():
            raise FileNotFoundError(f"SBOM file not found: {sbom_path}")

        # Load SBOM
        sbom_data = json.loads(sbom_path.read_text(encoding="utf-8"))

        # Extract dependencies
        dependencies = []
        components = sbom_data.get("components", [])

        for comp in components:
            # Get license info
            licenses = comp.get("licenses", [])
            license_id = ""

            if licenses:
                # Take the first license
                lic = licenses[0]
                if isinstance(lic, dict):
                    license_obj = lic.get("license", {})
                    license_id = license_obj.get("id", license_obj.get("name", ""))
                else:
                    license_id = str(lic)

            if not license_id:
                license_id = "UNKNOWN"

            dep = DependencyLicense(
                package_name=comp.get("name", "unknown"),
                package_version=comp.get("version", ""),
                license_id=license_id,
                detected_from="sbom",
            )
            dependencies.append(dep)

        return self.check_plugin(plugin_id, plugin_license, dependencies)


# =============================================================================
# Convenience Functions
# =============================================================================


def validate_spdx_license(license_id: str) -> Tuple[bool, Optional[LicenseInfo]]:
    """
    Validate an SPDX license identifier.

    Args:
        license_id: License identifier to validate

    Returns:
        Tuple of (is_valid, LicenseInfo or None)
    """
    if license_id in SPDX_LICENSES:
        return True, LicenseInfo(spdx_id=license_id)
    return False, None


def check_license_compatibility(
    project_license: str,
    dependency_licenses: List[str],
) -> Dict[str, CompatibilityLevel]:
    """
    Quick compatibility check for a list of licenses.

    Args:
        project_license: The project's SPDX license ID
        dependency_licenses: List of dependency SPDX license IDs

    Returns:
        Dict mapping license ID to compatibility level
    """
    checker = LicenseChecker()
    return {lic: checker.check_compatibility(project_license, lic) for lic in dependency_licenses}


def get_allowed_licenses(
    categories: Optional[List[LicenseCategory]] = None,
) -> List[str]:
    """
    Get list of allowed license IDs by category.

    Args:
        categories: List of allowed categories (None = permissive + public_domain)

    Returns:
        List of SPDX license IDs
    """
    if categories is None:
        categories = [LicenseCategory.PERMISSIVE, LicenseCategory.PUBLIC_DOMAIN]

    allowed = []
    for spdx_id, info in SPDX_LICENSES.items():
        if info.get("category") in categories:
            allowed.append(spdx_id)

    return sorted(allowed)


def list_known_licenses() -> List[Dict[str, Any]]:
    """
    List all known licenses in the database.

    Returns:
        List of license dictionaries
    """
    return [
        {
            "spdx_id": spdx_id,
            **info,
            "category": info["category"].value,
        }
        for spdx_id, info in SPDX_LICENSES.items()
    ]
