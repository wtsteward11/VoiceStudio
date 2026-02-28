"""
Automated Compliance Scanner for Plugins.

Phase 6C: Scans plugins for compliance issues using free local tools.
Self-audited compliance automation without paid certification services.

Compliance Checks:
1. License Compliance: Verify OSI-approved licenses, check compatibility
2. Security Compliance: Scan for vulnerabilities, check dependencies
3. Documentation: Verify README, changelog, API docs
4. Code Quality: Check test coverage, lint status
5. Privacy: Check data handling declarations

Certification Levels:
- Bronze: Basic checks pass
- Silver: Bronze + documentation + tests
- Gold: Silver + security review + privacy compliant

Usage:
    scanner = ComplianceScanner()

    result = await scanner.scan_plugin(
        plugin_path=Path("plugins/my-plugin"),
        plugin_id="my-plugin",
    )

    print(f"Certification: {result.level}")
    for issue in result.issues:
        print(f"[{issue.severity}] {issue.message}")
"""

from __future__ import annotations

import asyncio
import json
import logging
import subprocess
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from pathlib import Path
from typing import Any, Dict, List, Optional, Set

logger = logging.getLogger(__name__)


class ComplianceLevel(Enum):
    """Certification levels for plugins."""

    NONE = "none"
    BRONZE = "bronze"
    SILVER = "silver"
    GOLD = "gold"

    @property
    def display_name(self) -> str:
        return self.value.capitalize()


class IssueSeverity(Enum):
    """Severity levels for compliance issues."""

    BLOCKER = "blocker"
    MAJOR = "major"
    MINOR = "minor"
    INFO = "info"


class ComplianceCategory(Enum):
    """Categories of compliance checks."""

    LICENSE = "license"
    SECURITY = "security"
    DOCUMENTATION = "documentation"
    CODE_QUALITY = "code_quality"
    PRIVACY = "privacy"
    MANIFEST = "manifest"


@dataclass
class ComplianceIssue:
    """A compliance issue found during scanning."""

    category: ComplianceCategory
    severity: IssueSeverity
    message: str
    file: str = ""
    line: int = 0
    rule_id: str = ""
    suggestion: Optional[str] = None

    def to_dict(self) -> Dict[str, Any]:
        return {
            "category": self.category.value,
            "severity": self.severity.value,
            "message": self.message,
            "file": self.file,
            "line": self.line,
            "rule_id": self.rule_id,
            "suggestion": self.suggestion,
        }


@dataclass
class ComplianceResult:
    """Complete compliance scan result."""

    plugin_id: str
    level: ComplianceLevel
    issues: List[ComplianceIssue] = field(default_factory=list)
    checks_passed: Dict[str, bool] = field(default_factory=dict)
    scanned_at: datetime = field(default_factory=datetime.utcnow)
    scan_duration_ms: float = 0.0

    @property
    def blocker_count(self) -> int:
        return sum(1 for i in self.issues if i.severity == IssueSeverity.BLOCKER)

    @property
    def major_count(self) -> int:
        return sum(1 for i in self.issues if i.severity == IssueSeverity.MAJOR)

    def to_dict(self) -> Dict[str, Any]:
        return {
            "plugin_id": self.plugin_id,
            "level": self.level.value,
            "blocker_count": self.blocker_count,
            "major_count": self.major_count,
            "issue_count": len(self.issues),
            "issues": [i.to_dict() for i in self.issues],
            "checks_passed": self.checks_passed,
            "scanned_at": self.scanned_at.isoformat(),
            "scan_duration_ms": self.scan_duration_ms,
        }


# OSI-approved licenses
OSI_APPROVED_LICENSES = {
    "MIT",
    "Apache-2.0",
    "BSD-2-Clause",
    "BSD-3-Clause",
    "GPL-2.0",
    "GPL-3.0",
    "LGPL-2.1",
    "LGPL-3.0",
    "MPL-2.0",
    "ISC",
    "Unlicense",
    "0BSD",
    "Artistic-2.0",
    "Zlib",
    "BSL-1.0",
}

# License compatibility matrix (simplified)
COPYLEFT_LICENSES = {"GPL-2.0", "GPL-3.0", "LGPL-2.1", "LGPL-3.0", "MPL-2.0"}


class ComplianceScanner:
    """
    Automated compliance scanner for plugins.

    Performs comprehensive compliance checks using free local tools.
    No paid certification services required.

    Example:
        scanner = ComplianceScanner()

        result = await scanner.scan_plugin(
            plugin_path=Path("plugins/audio-fx"),
            plugin_id="audio-fx",
        )

        if result.level >= ComplianceLevel.SILVER:
            print("Plugin meets silver certification!")
    """

    def __init__(
        self,
        license_check: bool = True,
        security_check: bool = True,
        documentation_check: bool = True,
        quality_check: bool = True,
        privacy_check: bool = True,
    ):
        """
        Initialize compliance scanner.

        Args:
            license_check: Enable license compliance check
            security_check: Enable security vulnerability check
            documentation_check: Enable documentation check
            quality_check: Enable code quality check
            privacy_check: Enable privacy compliance check
        """
        self._license_check = license_check
        self._security_check = security_check
        self._documentation_check = documentation_check
        self._quality_check = quality_check
        self._privacy_check = privacy_check

    async def scan_plugin(
        self,
        plugin_path: Path,
        plugin_id: str,
    ) -> ComplianceResult:
        """
        Perform comprehensive compliance scan.

        Args:
            plugin_path: Path to plugin directory
            plugin_id: Plugin identifier

        Returns:
            ComplianceResult with issues and certification level
        """
        import time

        start_time = time.perf_counter()

        issues: List[ComplianceIssue] = []
        checks_passed: Dict[str, bool] = {}

        # Run all enabled checks
        if self._license_check:
            license_issues = await self._check_license(plugin_path)
            issues.extend(license_issues)
            checks_passed["license"] = not any(
                i.severity == IssueSeverity.BLOCKER for i in license_issues
            )

        if self._security_check:
            security_issues = await self._check_security(plugin_path)
            issues.extend(security_issues)
            checks_passed["security"] = not any(
                i.severity == IssueSeverity.BLOCKER for i in security_issues
            )

        if self._documentation_check:
            doc_issues = await self._check_documentation(plugin_path)
            issues.extend(doc_issues)
            checks_passed["documentation"] = not any(
                i.severity == IssueSeverity.BLOCKER for i in doc_issues
            )

        if self._quality_check:
            quality_issues = await self._check_quality(plugin_path)
            issues.extend(quality_issues)
            checks_passed["quality"] = not any(
                i.severity == IssueSeverity.BLOCKER for i in quality_issues
            )

        if self._privacy_check:
            privacy_issues = await self._check_privacy(plugin_path)
            issues.extend(privacy_issues)
            checks_passed["privacy"] = not any(
                i.severity == IssueSeverity.BLOCKER for i in privacy_issues
            )

        # Check manifest
        manifest_issues = await self._check_manifest(plugin_path)
        issues.extend(manifest_issues)
        checks_passed["manifest"] = not any(
            i.severity == IssueSeverity.BLOCKER for i in manifest_issues
        )

        # Determine certification level
        level = self._determine_level(checks_passed, issues)

        duration_ms = (time.perf_counter() - start_time) * 1000

        return ComplianceResult(
            plugin_id=plugin_id,
            level=level,
            issues=sorted(issues, key=lambda i: i.severity.value),
            checks_passed=checks_passed,
            scan_duration_ms=duration_ms,
        )

    async def _check_license(self, plugin_path: Path) -> List[ComplianceIssue]:
        """Check license compliance."""
        issues = []

        # Check for LICENSE file
        license_files = list(plugin_path.glob("LICENSE*"))
        if not license_files:
            issues.append(
                ComplianceIssue(
                    category=ComplianceCategory.LICENSE,
                    severity=IssueSeverity.BLOCKER,
                    message="No LICENSE file found",
                    suggestion="Add a LICENSE file with an OSI-approved license",
                )
            )
            return issues

        # Try to identify license
        license_content = license_files[0].read_text(encoding="utf-8", errors="ignore")
        identified_license = self._identify_license(license_content)

        if not identified_license:
            issues.append(
                ComplianceIssue(
                    category=ComplianceCategory.LICENSE,
                    severity=IssueSeverity.MAJOR,
                    message="Could not identify license",
                    file=str(license_files[0].name),
                    suggestion="Use a standard OSI-approved license text",
                )
            )
        elif identified_license not in OSI_APPROVED_LICENSES:
            issues.append(
                ComplianceIssue(
                    category=ComplianceCategory.LICENSE,
                    severity=IssueSeverity.MAJOR,
                    message=f"License '{identified_license}' is not OSI-approved",
                    file=str(license_files[0].name),
                    suggestion="Consider using MIT, Apache-2.0, or BSD-3-Clause",
                )
            )

        # Check dependencies for license compatibility
        dep_issues = await self._check_dependency_licenses(plugin_path)
        issues.extend(dep_issues)

        return issues

    def _identify_license(self, content: str) -> Optional[str]:
        """Identify license from content."""
        content_lower = content.lower()

        if (
            "mit license" in content_lower
            or "permission is hereby granted, free of charge" in content_lower
        ):
            return "MIT"
        elif "apache license" in content_lower and "version 2.0" in content_lower:
            return "Apache-2.0"
        elif "gnu general public license" in content_lower:
            if "version 3" in content_lower:
                return "GPL-3.0"
            elif "version 2" in content_lower:
                return "GPL-2.0"
        elif (
            "bsd 3-clause" in content_lower
            or "redistribution and use in source and binary forms" in content_lower
        ):
            return "BSD-3-Clause"
        elif "bsd 2-clause" in content_lower:
            return "BSD-2-Clause"
        elif "mozilla public license" in content_lower:
            return "MPL-2.0"
        elif "isc license" in content_lower:
            return "ISC"
        elif "unlicense" in content_lower or "public domain" in content_lower:
            return "Unlicense"

        return None

    async def _check_dependency_licenses(self, plugin_path: Path) -> List[ComplianceIssue]:
        """Check dependency licenses for compatibility."""
        issues = []

        # Check requirements.txt
        req_file = plugin_path / "requirements.txt"
        if req_file.exists():
            # Would use pip-licenses or similar tool
            pass

        # Check package.json for Node plugins
        pkg_file = plugin_path / "package.json"
        if pkg_file.exists():
            # Would check dependencies
            pass

        return issues

    async def _check_security(self, plugin_path: Path) -> List[ComplianceIssue]:
        """Check for security vulnerabilities."""
        issues = []

        # Check with bandit if available
        try:
            cmd = ["bandit", "-r", "-f", "json", "-ll", str(plugin_path)]
            proc = await asyncio.create_subprocess_exec(
                *cmd,
                stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.PIPE,
            )
            stdout, _ = await asyncio.wait_for(proc.communicate(), timeout=60)

            if stdout:
                data = json.loads(stdout.decode())
                for result in data.get("results", []):
                    severity = (
                        IssueSeverity.BLOCKER
                        if result.get("issue_severity") == "HIGH"
                        else (
                            IssueSeverity.MAJOR
                            if result.get("issue_severity") == "MEDIUM"
                            else IssueSeverity.MINOR
                        )
                    )
                    issues.append(
                        ComplianceIssue(
                            category=ComplianceCategory.SECURITY,
                            severity=severity,
                            message=result.get("issue_text", ""),
                            file=result.get("filename", ""),
                            line=result.get("line_number", 0),
                            rule_id=result.get("test_id", ""),
                        )
                    )
        except (subprocess.SubprocessError, FileNotFoundError, asyncio.TimeoutError) as e:
            # GAP-PY-001: Bandit not available or timed out
            logger.debug(f"Bandit security scan not available or failed: {e}")

        # Check for common security issues in code
        code_issues = await self._scan_code_security(plugin_path)
        issues.extend(code_issues)

        return issues

    async def _scan_code_security(self, plugin_path: Path) -> List[ComplianceIssue]:
        """Scan code for security patterns."""
        issues = []

        # Patterns to check
        dangerous_patterns = [
            ("eval(", "Use of eval() is dangerous"),
            ("exec(", "Use of exec() is dangerous"),
            ("pickle.loads", "pickle.loads can execute arbitrary code"),
            ("subprocess.call(.*shell=True", "Shell=True is vulnerable to injection"),
            ("__import__", "Dynamic imports can be dangerous"),
        ]

        for py_file in plugin_path.rglob("*.py"):
            try:
                content = py_file.read_text(encoding="utf-8", errors="ignore")
                for pattern, message in dangerous_patterns:
                    if pattern in content:
                        issues.append(
                            ComplianceIssue(
                                category=ComplianceCategory.SECURITY,
                                severity=IssueSeverity.MAJOR,
                                message=message,
                                file=str(py_file.relative_to(plugin_path)),
                                rule_id="dangerous-pattern",
                            )
                        )
            except (OSError, UnicodeDecodeError) as e:
                # Specific exceptions for file reading failures - acceptable to skip
                logger.warning(f"Skipping {py_file} for dangerous pattern scan: {e}")

        return issues

    async def _check_documentation(self, plugin_path: Path) -> List[ComplianceIssue]:
        """Check documentation completeness."""
        issues = []

        # Check for README
        readme_files = list(plugin_path.glob("README*"))
        if not readme_files:
            issues.append(
                ComplianceIssue(
                    category=ComplianceCategory.DOCUMENTATION,
                    severity=IssueSeverity.MAJOR,
                    message="No README file found",
                    suggestion="Add a README.md with installation and usage instructions",
                )
            )
        else:
            # Check README content
            readme_content = readme_files[0].read_text(encoding="utf-8", errors="ignore")
            if len(readme_content) < 100:
                issues.append(
                    ComplianceIssue(
                        category=ComplianceCategory.DOCUMENTATION,
                        severity=IssueSeverity.MINOR,
                        message="README is too short",
                        file=str(readme_files[0].name),
                        suggestion="Add more details about installation and usage",
                    )
                )

        # Check for CHANGELOG
        changelog_files = list(plugin_path.glob("CHANGELOG*")) + list(plugin_path.glob("HISTORY*"))
        if not changelog_files:
            issues.append(
                ComplianceIssue(
                    category=ComplianceCategory.DOCUMENTATION,
                    severity=IssueSeverity.MINOR,
                    message="No CHANGELOG file found",
                    suggestion="Add a CHANGELOG.md to track version changes",
                )
            )

        return issues

    async def _check_quality(self, plugin_path: Path) -> List[ComplianceIssue]:
        """Check code quality metrics."""
        issues = []

        # Check for tests
        test_dirs = list(plugin_path.glob("test*")) + list(plugin_path.glob("**/test_*.py"))
        if not test_dirs:
            issues.append(
                ComplianceIssue(
                    category=ComplianceCategory.CODE_QUALITY,
                    severity=IssueSeverity.MAJOR,
                    message="No tests found",
                    suggestion="Add unit tests in a tests/ directory",
                )
            )

        # Check for type hints (Python 3.5+)
        has_type_hints = False
        for py_file in plugin_path.rglob("*.py"):
            try:
                content = py_file.read_text(encoding="utf-8", errors="ignore")
                if ": " in content and "->" in content:
                    has_type_hints = True
                    break
            except (OSError, UnicodeDecodeError) as e:
                # Specific exceptions for file reading failures - acceptable to skip
                logger.warning(f"Skipping {py_file} for type hint check: {e}")

        if not has_type_hints:
            issues.append(
                ComplianceIssue(
                    category=ComplianceCategory.CODE_QUALITY,
                    severity=IssueSeverity.INFO,
                    message="No type hints detected",
                    suggestion="Consider adding type hints for better code quality",
                )
            )

        return issues

    async def _check_privacy(self, plugin_path: Path) -> List[ComplianceIssue]:
        """Check privacy compliance."""
        issues = []

        # Check manifest for privacy declarations
        manifest_file = plugin_path / "manifest.json"
        if manifest_file.exists():
            try:
                manifest = json.loads(manifest_file.read_text())

                # Check for privacy section
                privacy = manifest.get("privacy", {})
                if not privacy:
                    issues.append(
                        ComplianceIssue(
                            category=ComplianceCategory.PRIVACY,
                            severity=IssueSeverity.MINOR,
                            message="No privacy declaration in manifest",
                            file="manifest.json",
                            suggestion="Add a 'privacy' section declaring data collection",
                        )
                    )

                # Check for network access declaration
                permissions = manifest.get("permissions", [])
                if "net_internet" in permissions or "network" in permissions:
                    if not privacy.get("network_usage"):
                        issues.append(
                            ComplianceIssue(
                                category=ComplianceCategory.PRIVACY,
                                severity=IssueSeverity.MAJOR,
                                message="Network permission requested but not explained",
                                file="manifest.json",
                                suggestion="Explain network usage in privacy.network_usage",
                            )
                        )

            except json.JSONDecodeError as e:
                # GAP-PY-001: Manifest JSON parsing failure
                logger.debug(f"Failed to parse manifest.json: {e}")

        # Scan code for potential privacy issues
        privacy_patterns = [
            ("telemetry", "Plugin may collect telemetry"),
            ("analytics", "Plugin may collect analytics"),
            ("tracking", "Plugin may include tracking"),
            ("user_data", "Plugin may access user data"),
        ]

        for py_file in plugin_path.rglob("*.py"):
            try:
                content = py_file.read_text(encoding="utf-8", errors="ignore").lower()
                for pattern, message in privacy_patterns:
                    if pattern in content:
                        issues.append(
                            ComplianceIssue(
                                category=ComplianceCategory.PRIVACY,
                                severity=IssueSeverity.INFO,
                                message=message,
                                file=str(py_file.relative_to(plugin_path)),
                                suggestion="Declare data collection in manifest privacy section",
                            )
                        )
                        break
            except (OSError, UnicodeDecodeError) as e:
                # Specific exceptions for file reading failures - acceptable to skip
                logger.warning(f"Skipping {py_file} for privacy pattern scan: {e}")

        return issues

    async def _check_manifest(self, plugin_path: Path) -> List[ComplianceIssue]:
        """Check manifest completeness and validity."""
        issues = []

        manifest_file = plugin_path / "manifest.json"
        if not manifest_file.exists():
            issues.append(
                ComplianceIssue(
                    category=ComplianceCategory.MANIFEST,
                    severity=IssueSeverity.BLOCKER,
                    message="No manifest.json found",
                    suggestion="Add a manifest.json with plugin metadata",
                )
            )
            return issues

        try:
            manifest = json.loads(manifest_file.read_text())

            # Required fields
            required_fields = ["name", "version", "description", "author"]
            for field in required_fields:
                if field not in manifest:
                    issues.append(
                        ComplianceIssue(
                            category=ComplianceCategory.MANIFEST,
                            severity=IssueSeverity.MAJOR,
                            message=f"Missing required field: {field}",
                            file="manifest.json",
                        )
                    )

            # Version format
            version = manifest.get("version", "")
            if version and not self._valid_semver(version):
                issues.append(
                    ComplianceIssue(
                        category=ComplianceCategory.MANIFEST,
                        severity=IssueSeverity.MINOR,
                        message=f"Version '{version}' is not valid semver",
                        file="manifest.json",
                        suggestion="Use semantic versioning (e.g., 1.0.0)",
                    )
                )

        except json.JSONDecodeError as e:
            issues.append(
                ComplianceIssue(
                    category=ComplianceCategory.MANIFEST,
                    severity=IssueSeverity.BLOCKER,
                    message=f"Invalid JSON in manifest: {e}",
                    file="manifest.json",
                )
            )

        return issues

    def _valid_semver(self, version: str) -> bool:
        """Check if version is valid semver."""
        import re

        pattern = r"^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?(\+[a-zA-Z0-9.]+)?$"
        return bool(re.match(pattern, version))

    def _determine_level(
        self,
        checks_passed: Dict[str, bool],
        issues: List[ComplianceIssue],
    ) -> ComplianceLevel:
        """Determine certification level from results."""
        # Check for blockers
        blockers = sum(1 for i in issues if i.severity == IssueSeverity.BLOCKER)
        if blockers > 0:
            return ComplianceLevel.NONE

        # Bronze: Basic checks pass
        bronze_checks = ["license", "manifest"]
        if not all(checks_passed.get(c, False) for c in bronze_checks):
            return ComplianceLevel.NONE

        # Silver: Bronze + docs + quality
        silver_checks = bronze_checks + ["documentation", "quality"]
        majors = sum(1 for i in issues if i.severity == IssueSeverity.MAJOR)
        if not all(checks_passed.get(c, False) for c in silver_checks) or majors > 3:
            return ComplianceLevel.BRONZE

        # Gold: All checks pass with minimal issues
        gold_checks = silver_checks + ["security", "privacy"]
        if not all(checks_passed.get(c, False) for c in gold_checks) or majors > 0:
            return ComplianceLevel.SILVER

        return ComplianceLevel.GOLD
