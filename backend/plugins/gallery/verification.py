"""
Plugin Verification Service.

Phase 5B Enhancement: Integrates signature and SBOM verification into
the gallery installer. Provides:
    - Cryptographic signature verification
    - SBOM validation and extraction
    - License compliance checking
    - Vulnerability scanning pre-install
    - Build provenance validation

All verification is local-first with no external service dependencies.
"""

from __future__ import annotations

import json
import logging
import zipfile
from dataclasses import dataclass, field
from datetime import datetime, timezone
from enum import Enum
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional

logger = logging.getLogger(__name__)


# =============================================================================
# Enums
# =============================================================================


class VerificationLevel(Enum):
    """Level of verification to perform."""

    NONE = "none"  # Skip verification (not recommended)
    BASIC = "basic"  # Checksum only
    STANDARD = "standard"  # Checksum + signature
    STRICT = "strict"  # Checksum + signature + SBOM + license + vuln scan
    ENTERPRISE = "enterprise"  # All checks + provenance + audit logging


class VerificationStatus(Enum):
    """Status of a verification check."""

    PASSED = "passed"
    FAILED = "failed"
    SKIPPED = "skipped"
    WARNING = "warning"  # Passed but with concerns


# =============================================================================
# Data Models
# =============================================================================


@dataclass
class VerificationCheck:
    """Result of a single verification check."""

    name: str
    status: VerificationStatus
    message: str = ""
    details: Dict[str, Any] = field(default_factory=dict)
    duration_ms: float = 0.0

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary."""
        return {
            "name": self.name,
            "status": self.status.value,
            "message": self.message,
            "details": self.details,
            "duration_ms": self.duration_ms,
        }


@dataclass
class VerificationResult:
    """Complete verification result for a package."""

    package_path: str
    plugin_id: str
    version: str
    level: VerificationLevel
    checks: List[VerificationCheck] = field(default_factory=list)
    passed: bool = False
    verified_at: str = ""
    warnings: List[str] = field(default_factory=list)

    # Extracted data
    sbom: Optional[Dict[str, Any]] = None
    provenance: Optional[Dict[str, Any]] = None
    manifest: Optional[Dict[str, Any]] = None
    signature: Optional[Dict[str, Any]] = None

    def __post_init__(self):
        """Initialize timestamp if not set."""
        if not self.verified_at:
            self.verified_at = datetime.now(timezone.utc).isoformat()

    @property
    def failed_checks(self) -> List[VerificationCheck]:
        """Get failed checks."""
        return [c for c in self.checks if c.status == VerificationStatus.FAILED]

    @property
    def warning_checks(self) -> List[VerificationCheck]:
        """Get warning checks."""
        return [c for c in self.checks if c.status == VerificationStatus.WARNING]

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary."""
        return {
            "package_path": self.package_path,
            "plugin_id": self.plugin_id,
            "version": self.version,
            "level": self.level.value,
            "checks": [c.to_dict() for c in self.checks],
            "passed": self.passed,
            "verified_at": self.verified_at,
            "warnings": self.warnings,
            "has_sbom": self.sbom is not None,
            "has_provenance": self.provenance is not None,
            "has_signature": self.signature is not None,
        }

    def to_json(self, indent: int = 2) -> str:
        """Convert to JSON string."""
        return json.dumps(self.to_dict(), indent=indent)


@dataclass
class VerificationPolicy:
    """Policy for verification requirements."""

    level: VerificationLevel = VerificationLevel.STANDARD
    require_signature: bool = True
    require_sbom: bool = False
    require_provenance: bool = False
    allowed_signers: Optional[List[str]] = None  # Key IDs
    blocked_licenses: Optional[List[str]] = None
    max_critical_vulns: int = 0
    max_high_vulns: int = 5
    check_license_compatibility: bool = True
    project_license: str = "MIT"  # For compatibility checking

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary."""
        return {
            "level": self.level.value,
            "require_signature": self.require_signature,
            "require_sbom": self.require_sbom,
            "require_provenance": self.require_provenance,
            "allowed_signers": self.allowed_signers,
            "blocked_licenses": self.blocked_licenses,
            "max_critical_vulns": self.max_critical_vulns,
            "max_high_vulns": self.max_high_vulns,
            "check_license_compatibility": self.check_license_compatibility,
            "project_license": self.project_license,
        }

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> VerificationPolicy:
        """Create from dictionary."""
        return cls(
            level=VerificationLevel(data.get("level", "standard")),
            require_signature=data.get("require_signature", True),
            require_sbom=data.get("require_sbom", False),
            require_provenance=data.get("require_provenance", False),
            allowed_signers=data.get("allowed_signers"),
            blocked_licenses=data.get("blocked_licenses"),
            max_critical_vulns=data.get("max_critical_vulns", 0),
            max_high_vulns=data.get("max_high_vulns", 5),
            check_license_compatibility=data.get("check_license_compatibility", True),
            project_license=data.get("project_license", "MIT"),
        )


# =============================================================================
# Verification Service
# =============================================================================


class PluginVerificationService:
    """
    Service for verifying plugin packages before installation.

    Integrates with supply chain security tools:
        - Signature verification via signer.py
        - SBOM validation via sbom.py
        - License checking via license_checker.py
        - Vulnerability scanning via vuln_scanner.py
        - Provenance validation via provenance.py

    Example:
        >>> service = PluginVerificationService()
        >>> result = await service.verify_package(
        ...     Path("plugin.vspkg"),
        ...     policy=VerificationPolicy(level=VerificationLevel.STRICT),
        ... )
        >>> if result.passed:
        ...     print("Package verified!")
    """

    def __init__(
        self,
        keystore_path: Optional[Path] = None,
        policy: Optional[VerificationPolicy] = None,
    ):
        """
        Initialize verification service.

        Args:
            keystore_path: Path to keystore for signature verification
            policy: Default verification policy
        """
        self._keystore_path = keystore_path
        self._default_policy = policy or VerificationPolicy()
        self._keystore = None

        # Try to load signer module
        self._signer_available = False
        try:
            from backend.plugins.supply_chain.signer import (
                check_signing_available,
                load_keystore,
            )

            self._signer_available = check_signing_available()
            if self._signer_available and keystore_path and keystore_path.exists():
                self._keystore = load_keystore(keystore_path)
        except ImportError:
            logger.debug("Signer module not available")

    async def verify_package(
        self,
        package_path: Path,
        policy: Optional[VerificationPolicy] = None,
        progress_callback: Optional[Callable[[str, float], None]] = None,
    ) -> VerificationResult:
        """
        Verify a plugin package.

        Args:
            package_path: Path to the package file (.vspkg or .zip)
            policy: Verification policy (uses default if not provided)
            progress_callback: Optional callback for progress updates

        Returns:
            VerificationResult with check details
        """
        import time

        policy = policy or self._default_policy

        # Initialize result
        result = VerificationResult(
            package_path=str(package_path),
            plugin_id="",
            version="",
            level=policy.level,
        )

        def report(message: str, progress: float):
            if progress_callback:
                progress_callback(message, progress)

        try:
            report("Starting verification...", 0.0)

            # Verify package exists
            if not package_path.exists():
                result.checks.append(
                    VerificationCheck(
                        name="file_exists",
                        status=VerificationStatus.FAILED,
                        message=f"Package file not found: {package_path}",
                    )
                )
                return result

            result.checks.append(
                VerificationCheck(
                    name="file_exists",
                    status=VerificationStatus.PASSED,
                    message="Package file exists",
                )
            )

            report("Extracting package metadata...", 0.1)

            # Extract manifest
            start = time.time()
            manifest = self._extract_manifest(package_path)
            duration = (time.time() - start) * 1000

            if manifest:
                result.manifest = manifest
                result.plugin_id = manifest.get("id", "")
                result.version = manifest.get("version", "")
                result.checks.append(
                    VerificationCheck(
                        name="manifest_valid",
                        status=VerificationStatus.PASSED,
                        message="Plugin manifest is valid",
                        details={"id": result.plugin_id, "version": result.version},
                        duration_ms=duration,
                    )
                )
            else:
                result.checks.append(
                    VerificationCheck(
                        name="manifest_valid",
                        status=VerificationStatus.FAILED,
                        message="Could not extract plugin manifest",
                        duration_ms=duration,
                    )
                )

            # Level NONE stops here
            if policy.level == VerificationLevel.NONE:
                result.passed = len(result.failed_checks) == 0
                return result

            report("Verifying checksum...", 0.2)

            # Basic verification: checksum
            start = time.time()
            checksum_result = self._verify_checksum(package_path)
            duration = (time.time() - start) * 1000
            result.checks.append(
                VerificationCheck(
                    name="checksum",
                    status=(
                        VerificationStatus.PASSED if checksum_result else VerificationStatus.WARNING
                    ),
                    message=(
                        "Checksum computed" if checksum_result else "No expected checksum to verify"
                    ),
                    details={"sha256": checksum_result} if checksum_result else {},
                    duration_ms=duration,
                )
            )

            # Level BASIC stops here
            if policy.level == VerificationLevel.BASIC:
                result.passed = len(result.failed_checks) == 0
                return result

            report("Verifying signature...", 0.3)

            # Standard verification: signature
            start = time.time()
            sig_check = await self._verify_signature(package_path, policy)
            sig_check.duration_ms = (time.time() - start) * 1000
            result.checks.append(sig_check)

            if sig_check.status == VerificationStatus.PASSED:
                # Extract signature details
                result.signature = sig_check.details.get("signature")

            # Level STANDARD stops here
            if policy.level == VerificationLevel.STANDARD:
                result.passed = len(result.failed_checks) == 0
                return result

            report("Validating SBOM...", 0.5)

            # Strict verification: SBOM
            start = time.time()
            sbom_check = self._verify_sbom(package_path, policy)
            sbom_check.duration_ms = (time.time() - start) * 1000
            result.checks.append(sbom_check)

            if sbom_check.status in (VerificationStatus.PASSED, VerificationStatus.WARNING):
                result.sbom = sbom_check.details.get("sbom")

            report("Checking licenses...", 0.6)

            # License check
            if policy.check_license_compatibility and result.sbom:
                start = time.time()
                license_check = self._check_licenses(result.sbom, policy)
                license_check.duration_ms = (time.time() - start) * 1000
                result.checks.append(license_check)

                if license_check.status == VerificationStatus.WARNING:
                    result.warnings.extend(license_check.details.get("warnings", []))

            report("Scanning for vulnerabilities...", 0.7)

            # Vulnerability scan
            if result.sbom:
                start = time.time()
                vuln_check = await self._scan_vulnerabilities(package_path, result.sbom, policy)
                vuln_check.duration_ms = (time.time() - start) * 1000
                result.checks.append(vuln_check)

            # Level STRICT stops here
            if policy.level == VerificationLevel.STRICT:
                result.passed = len(result.failed_checks) == 0
                return result

            report("Validating provenance...", 0.85)

            # Enterprise verification: provenance
            start = time.time()
            prov_check = self._verify_provenance(package_path, policy)
            prov_check.duration_ms = (time.time() - start) * 1000
            result.checks.append(prov_check)

            if prov_check.status in (VerificationStatus.PASSED, VerificationStatus.WARNING):
                result.provenance = prov_check.details.get("provenance")

            report("Logging audit event...", 0.95)

            # Audit logging for enterprise
            await self._log_verification_audit(result)

            report("Verification complete", 1.0)

            # Final result
            result.passed = len(result.failed_checks) == 0
            return result

        except Exception as e:
            logger.error(f"Verification failed: {e}", exc_info=True)
            result.checks.append(
                VerificationCheck(
                    name="unexpected_error",
                    status=VerificationStatus.FAILED,
                    message=f"Unexpected error: {e}",
                )
            )
            return result

    def _extract_manifest(self, package_path: Path) -> Optional[Dict[str, Any]]:
        """Extract plugin manifest from package."""
        try:
            with zipfile.ZipFile(package_path, "r") as zf:
                # Look for manifest
                manifest_names = ["manifest.json", "plugin.json", "plugin-manifest.json"]
                for name in manifest_names:
                    try:
                        content = zf.read(name)
                        return json.loads(content.decode("utf-8"))
                    except KeyError:
                        continue
            return None
        except Exception as e:
            logger.warning(f"Failed to extract manifest: {e}")
            return None

    def _verify_checksum(self, package_path: Path) -> Optional[str]:
        """Compute and return package checksum."""
        import hashlib

        try:
            sha256 = hashlib.sha256()
            with open(package_path, "rb") as f:
                for chunk in iter(lambda: f.read(8192), b""):
                    sha256.update(chunk)
            return sha256.hexdigest()
        except Exception as e:
            logger.warning(f"Failed to compute checksum: {e}")
            return None

    async def _verify_signature(
        self,
        package_path: Path,
        policy: VerificationPolicy,
    ) -> VerificationCheck:
        """Verify package signature."""
        try:
            # Check if signer is available
            if not self._signer_available:
                if policy.require_signature:
                    return VerificationCheck(
                        name="signature",
                        status=VerificationStatus.FAILED,
                        message="Signature verification required but cryptography library not available",
                    )
                return VerificationCheck(
                    name="signature",
                    status=VerificationStatus.SKIPPED,
                    message="Signature verification skipped (cryptography not available)",
                )

            # Look for signature file
            sig_path = package_path.with_suffix(package_path.suffix + ".sig")
            if not sig_path.exists():
                # Check inside package
                try:
                    with zipfile.ZipFile(package_path, "r") as zf:
                        sig_content = zf.read("signature.json")
                        sig_data = json.loads(sig_content.decode("utf-8"))
                except (KeyError, zipfile.BadZipFile):
                    sig_data = None

                if not sig_data:
                    if policy.require_signature:
                        return VerificationCheck(
                            name="signature",
                            status=VerificationStatus.FAILED,
                            message="Signature required but not found",
                        )
                    return VerificationCheck(
                        name="signature",
                        status=VerificationStatus.SKIPPED,
                        message="No signature found",
                    )
            else:
                sig_data = json.loads(sig_path.read_text())

            # Import signer
            from backend.plugins.supply_chain.signer import Signature, verify_package

            # Reconstruct signature
            signature = Signature(
                key_id=sig_data.get("key_id", ""),
                signature_id=sig_data.get("signature_id", ""),
                signature=sig_data.get("signature", ""),
                signed_at=sig_data.get("signed_at", ""),
                package_digest=sig_data.get("package_digest", {}),
            )

            # Check allowed signers
            if policy.allowed_signers and signature.key_id not in policy.allowed_signers:
                return VerificationCheck(
                    name="signature",
                    status=VerificationStatus.FAILED,
                    message=f"Signer '{signature.key_id}' not in allowed list",
                    details={"key_id": signature.key_id},
                )

            # Verify signature
            if self._keystore:
                is_valid = verify_package(package_path, signature, keystore=self._keystore)
            else:
                # Can't verify without keystore
                return VerificationCheck(
                    name="signature",
                    status=VerificationStatus.WARNING,
                    message="Signature present but no keystore configured for verification",
                    details={"signature": sig_data},
                )

            if is_valid:
                return VerificationCheck(
                    name="signature",
                    status=VerificationStatus.PASSED,
                    message="Signature verified successfully",
                    details={
                        "key_id": signature.key_id,
                        "signed_at": signature.signed_at,
                        "signature": sig_data,
                    },
                )
            else:
                return VerificationCheck(
                    name="signature",
                    status=VerificationStatus.FAILED,
                    message="Signature verification failed",
                    details={"key_id": signature.key_id},
                )

        except Exception as e:
            logger.warning(f"Signature verification error: {e}")
            return VerificationCheck(
                name="signature",
                status=VerificationStatus.FAILED,
                message=f"Signature verification error: {e}",
            )

    def _verify_sbom(
        self,
        package_path: Path,
        policy: VerificationPolicy,
    ) -> VerificationCheck:
        """Extract and validate SBOM from package."""
        try:
            sbom_data = None

            # Look for SBOM in package
            with zipfile.ZipFile(package_path, "r") as zf:
                sbom_names = ["sbom.json", "bom.json", "cyclonedx.json"]
                for name in sbom_names:
                    try:
                        content = zf.read(name)
                        sbom_data = json.loads(content.decode("utf-8"))
                        break
                    except KeyError:
                        continue

            if not sbom_data:
                if policy.require_sbom:
                    return VerificationCheck(
                        name="sbom",
                        status=VerificationStatus.FAILED,
                        message="SBOM required but not found in package",
                    )
                return VerificationCheck(
                    name="sbom",
                    status=VerificationStatus.SKIPPED,
                    message="No SBOM found in package",
                )

            # Validate SBOM structure
            if "bomFormat" not in sbom_data:
                return VerificationCheck(
                    name="sbom",
                    status=VerificationStatus.WARNING,
                    message="SBOM found but format not recognized",
                    details={"sbom": sbom_data},
                )

            component_count = len(sbom_data.get("components", []))

            return VerificationCheck(
                name="sbom",
                status=VerificationStatus.PASSED,
                message=f"SBOM valid with {component_count} components",
                details={
                    "format": sbom_data.get("bomFormat"),
                    "spec_version": sbom_data.get("specVersion"),
                    "component_count": component_count,
                    "sbom": sbom_data,
                },
            )

        except Exception as e:
            logger.warning(f"SBOM verification error: {e}")
            return VerificationCheck(
                name="sbom",
                status=VerificationStatus.FAILED,
                message=f"SBOM verification error: {e}",
            )

    def _check_licenses(
        self,
        sbom: Dict[str, Any],
        policy: VerificationPolicy,
    ) -> VerificationCheck:
        """Check license compatibility from SBOM."""
        try:
            from backend.plugins.supply_chain.license_checker import (
                DependencyLicense,
                LicenseChecker,
            )

            # Build dependency list from SBOM
            deps = []
            for comp in sbom.get("components", []):
                licenses = comp.get("licenses", [])
                license_id = "UNKNOWN"

                if licenses:
                    lic = licenses[0]
                    if isinstance(lic, dict):
                        license_obj = lic.get("license", {})
                        license_id = license_obj.get("id", license_obj.get("name", "UNKNOWN"))

                deps.append(
                    DependencyLicense(
                        package_name=comp.get("name", "unknown"),
                        package_version=comp.get("version", ""),
                        license_id=license_id,
                        detected_from="sbom",
                    )
                )

            # Check licenses
            checker = LicenseChecker(
                blocked_licenses=set(policy.blocked_licenses or []),
            )

            result = checker.check_plugin(
                plugin_id="package",
                plugin_license=policy.project_license,
                dependencies=deps,
            )

            if not result.passed:
                return VerificationCheck(
                    name="license",
                    status=VerificationStatus.FAILED,
                    message=f"License check failed: {len(result.issues)} issues",
                    details={
                        "issues": [i.to_dict() for i in result.issues],
                        "warnings": result.warnings,
                    },
                )

            if result.warnings:
                return VerificationCheck(
                    name="license",
                    status=VerificationStatus.WARNING,
                    message=f"License check passed with {len(result.warnings)} warnings",
                    details={"warnings": result.warnings},
                )

            return VerificationCheck(
                name="license",
                status=VerificationStatus.PASSED,
                message=f"All {len(deps)} dependencies have compatible licenses",
            )

        except ImportError:
            return VerificationCheck(
                name="license",
                status=VerificationStatus.SKIPPED,
                message="License checker not available",
            )
        except Exception as e:
            logger.warning(f"License check error: {e}")
            return VerificationCheck(
                name="license",
                status=VerificationStatus.WARNING,
                message=f"License check error: {e}",
            )

    async def _scan_vulnerabilities(
        self,
        package_path: Path,
        sbom: Dict[str, Any],
        policy: VerificationPolicy,
    ) -> VerificationCheck:
        """Scan SBOM for vulnerabilities."""
        try:
            from backend.plugins.supply_chain.vuln_scanner import (
                VulnerabilityScanner,
                check_scanner_availability,
            )

            if not check_scanner_availability():
                return VerificationCheck(
                    name="vulnerability",
                    status=VerificationStatus.SKIPPED,
                    message="No vulnerability scanner available (pip-audit or grype)",
                )

            # Save SBOM temporarily for scanning
            import tempfile

            with tempfile.NamedTemporaryFile(mode="w", suffix=".json", delete=False) as f:
                json.dump(sbom, f)
                sbom_path = Path(f.name)

            try:
                scanner = VulnerabilityScanner()
                scan_result = await scanner.scan_sbom(sbom_path)
            finally:
                sbom_path.unlink(missing_ok=True)

            # Count by severity
            counts = scan_result.severity_counts()
            critical = counts.get("CRITICAL", 0)
            high = counts.get("HIGH", 0)

            # Check against policy
            if critical > policy.max_critical_vulns:
                return VerificationCheck(
                    name="vulnerability",
                    status=VerificationStatus.FAILED,
                    message=f"Found {critical} critical vulnerabilities (max: {policy.max_critical_vulns})",
                    details={
                        "severity_counts": counts,
                        "vulnerabilities": [v.to_dict() for v in scan_result.vulnerabilities],
                    },
                )

            if high > policy.max_high_vulns:
                return VerificationCheck(
                    name="vulnerability",
                    status=VerificationStatus.FAILED,
                    message=f"Found {high} high severity vulnerabilities (max: {policy.max_high_vulns})",
                    details={
                        "severity_counts": counts,
                        "vulnerabilities": [v.to_dict() for v in scan_result.vulnerabilities],
                    },
                )

            total = len(scan_result.vulnerabilities)
            if total > 0:
                return VerificationCheck(
                    name="vulnerability",
                    status=VerificationStatus.WARNING,
                    message=f"Found {total} vulnerabilities (within policy limits)",
                    details={
                        "severity_counts": counts,
                        "vulnerabilities": [v.to_dict() for v in scan_result.vulnerabilities],
                    },
                )

            return VerificationCheck(
                name="vulnerability",
                status=VerificationStatus.PASSED,
                message="No vulnerabilities detected",
            )

        except ImportError:
            return VerificationCheck(
                name="vulnerability",
                status=VerificationStatus.SKIPPED,
                message="Vulnerability scanner not available",
            )
        except Exception as e:
            logger.warning(f"Vulnerability scan error: {e}")
            return VerificationCheck(
                name="vulnerability",
                status=VerificationStatus.WARNING,
                message=f"Vulnerability scan error: {e}",
            )

    def _verify_provenance(
        self,
        package_path: Path,
        policy: VerificationPolicy,
    ) -> VerificationCheck:
        """Extract and validate build provenance."""
        try:
            prov_data = None

            # Look for provenance in package
            with zipfile.ZipFile(package_path, "r") as zf:
                prov_names = ["provenance.json", "build-provenance.json"]
                for name in prov_names:
                    try:
                        content = zf.read(name)
                        prov_data = json.loads(content.decode("utf-8"))
                        break
                    except KeyError:
                        continue

            if not prov_data:
                if policy.require_provenance:
                    return VerificationCheck(
                        name="provenance",
                        status=VerificationStatus.FAILED,
                        message="Provenance required but not found",
                    )
                return VerificationCheck(
                    name="provenance",
                    status=VerificationStatus.SKIPPED,
                    message="No provenance data found",
                )

            # Validate provenance structure
            required_fields = ["version", "builder", "build_started_at"]
            missing = [f for f in required_fields if f not in prov_data]

            if missing:
                return VerificationCheck(
                    name="provenance",
                    status=VerificationStatus.WARNING,
                    message=f"Provenance missing fields: {', '.join(missing)}",
                    details={"provenance": prov_data},
                )

            return VerificationCheck(
                name="provenance",
                status=VerificationStatus.PASSED,
                message="Provenance data validated",
                details={
                    "version": prov_data.get("version"),
                    "build_type": prov_data.get("build_type"),
                    "builder": prov_data.get("builder", {}).get("id"),
                    "provenance": prov_data,
                },
            )

        except Exception as e:
            logger.warning(f"Provenance verification error: {e}")
            return VerificationCheck(
                name="provenance",
                status=VerificationStatus.WARNING,
                message=f"Provenance verification error: {e}",
            )

    async def _log_verification_audit(self, result: VerificationResult) -> None:
        """Log verification to audit system."""
        try:
            from backend.plugins.supply_chain.audit import (
                log_signature_verification,
            )

            sig_valid = any(
                c.name == "signature" and c.status == VerificationStatus.PASSED
                for c in result.checks
            )

            log_signature_verification(
                plugin_id=result.plugin_id or "unknown",
                success=sig_valid,
                key_id=result.signature.get("key_id") if result.signature else None,
                details={
                    "verification_level": result.level.value,
                    "checks_passed": len(
                        [c for c in result.checks if c.status == VerificationStatus.PASSED]
                    ),
                    "checks_failed": len(result.failed_checks),
                    "warnings": len(result.warnings),
                },
            )

        except ImportError:
            pass  # Audit not available
        except Exception as e:
            logger.debug(f"Failed to log audit event: {e}")


# =============================================================================
# Convenience Functions
# =============================================================================


async def verify_package(
    package_path: Path,
    level: VerificationLevel = VerificationLevel.STANDARD,
    **policy_kwargs,
) -> VerificationResult:
    """
    Verify a plugin package with specified level.

    Args:
        package_path: Path to package file
        level: Verification level
        **policy_kwargs: Additional policy options

    Returns:
        VerificationResult
    """
    policy = VerificationPolicy(level=level, **policy_kwargs)
    service = PluginVerificationService(policy=policy)
    return await service.verify_package(package_path, policy)


def get_verification_service(
    keystore_path: Optional[Path] = None,
    policy: Optional[VerificationPolicy] = None,
) -> PluginVerificationService:
    """
    Get a verification service instance.

    Args:
        keystore_path: Optional path to keystore
        policy: Optional verification policy

    Returns:
        PluginVerificationService instance
    """
    return PluginVerificationService(
        keystore_path=keystore_path,
        policy=policy,
    )
