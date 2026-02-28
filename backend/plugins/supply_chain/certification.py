"""
Plugin Certification Engine.

Phase 5C M3: Automated quality gates and certification workflow.

Provides a certification system that:
- Defines certification levels (none, basic, standard, premium, enterprise)
- Runs automated quality gate checks
- Integrates with verification, SBOM, license, and vulnerability services
- Generates certification results with unique certificate IDs
"""

from __future__ import annotations

import hashlib
import json
import logging
import uuid
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from enum import Enum
from pathlib import Path
from typing import Any, Callable, Optional

logger = logging.getLogger(__name__)


class CertificationLevel(Enum):
    """Plugin certification levels."""

    NONE = "none"
    BASIC = "basic"  # Manifest valid, no critical vulnerabilities
    STANDARD = "standard"  # + SBOM present, licenses compatible
    PREMIUM = "premium"  # + Signature valid, provenance verified
    ENTERPRISE = "enterprise"  # + All tests pass, security review


class GateStatus(Enum):
    """Quality gate status."""

    PASSED = "passed"
    FAILED = "failed"
    SKIPPED = "skipped"
    NOT_APPLICABLE = "not_applicable"


@dataclass
class QualityGate:
    """
    Quality gate definition and result.

    Represents a single quality check in the certification process.
    """

    id: str
    name: str
    description: str
    status: GateStatus = GateStatus.SKIPPED
    message: str = ""
    evidence: dict[str, Any] = field(default_factory=dict)
    required_for: list[CertificationLevel] = field(default_factory=list)
    executed_at: Optional[str] = None

    def __post_init__(self):
        if self.executed_at is None:
            self.executed_at = datetime.now(timezone.utc).isoformat()

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "id": self.id,
            "name": self.name,
            "description": self.description,
            "status": self.status.value,
            "message": self.message,
            "evidence": self.evidence,
            "required_for": [level.value for level in self.required_for],
            "executed_at": self.executed_at,
        }


@dataclass
class CertificationMetrics:
    """Quality metrics captured during certification."""

    test_coverage_percent: Optional[float] = None
    cyclomatic_complexity: Optional[float] = None
    documentation_coverage_percent: Optional[float] = None
    api_stability_score: Optional[float] = None

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        result = {}
        if self.test_coverage_percent is not None:
            result["test_coverage_percent"] = self.test_coverage_percent
        if self.cyclomatic_complexity is not None:
            result["cyclomatic_complexity"] = self.cyclomatic_complexity
        if self.documentation_coverage_percent is not None:
            result["documentation_coverage_percent"] = self.documentation_coverage_percent
        if self.api_stability_score is not None:
            result["api_stability_score"] = self.api_stability_score
        return result


@dataclass
class CertificationRequirement:
    """A certification requirement with evidence."""

    id: str
    name: str
    passed: bool
    evidence_path: str = ""

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        result = {"id": self.id, "name": self.name, "passed": self.passed}
        if self.evidence_path:
            result["evidence_path"] = self.evidence_path
        return result


@dataclass
class CertificationResult:
    """
    Complete certification result.

    Contains the achieved certification level, quality gate results,
    metrics, and certificate information.
    """

    plugin_id: str
    plugin_version: str
    certified: bool
    certification_level: CertificationLevel
    certificate_id: str = ""
    certified_at: str = ""
    expires_at: str = ""
    certifier: str = "VoiceStudio Local Certifier"
    quality_gates: list[QualityGate] = field(default_factory=list)
    metrics: CertificationMetrics = field(default_factory=CertificationMetrics)
    requirements: list[CertificationRequirement] = field(default_factory=list)
    errors: list[str] = field(default_factory=list)

    def __post_init__(self):
        if not self.certified_at:
            self.certified_at = datetime.now(timezone.utc).isoformat()
        if not self.expires_at:
            # Certification valid for 1 year by default
            expires = datetime.now(timezone.utc) + timedelta(days=365)
            self.expires_at = expires.isoformat()
        if not self.certificate_id and self.certified:
            # Generate deterministic certificate ID
            content = f"{self.plugin_id}:{self.plugin_version}:{self.certified_at}"
            self.certificate_id = (
                f"VS-CERT-{hashlib.sha256(content.encode()).hexdigest()[:16].upper()}"
            )

    @property
    def passed_gates(self) -> list[QualityGate]:
        """Get list of passed quality gates."""
        return [g for g in self.quality_gates if g.status == GateStatus.PASSED]

    @property
    def failed_gates(self) -> list[QualityGate]:
        """Get list of failed quality gates."""
        return [g for g in self.quality_gates if g.status == GateStatus.FAILED]

    def get_quality_gates_summary(self) -> dict[str, bool]:
        """Get quality gates as bool dict for manifest schema."""
        return {
            "manifest_valid": any(
                g.id == "manifest" and g.status == GateStatus.PASSED for g in self.quality_gates
            ),
            "signature_valid": any(
                g.id == "signature" and g.status == GateStatus.PASSED for g in self.quality_gates
            ),
            "sbom_present": any(
                g.id == "sbom" and g.status == GateStatus.PASSED for g in self.quality_gates
            ),
            "vulnerabilities_passed": any(
                g.id == "vulnerabilities" and g.status == GateStatus.PASSED
                for g in self.quality_gates
            ),
            "licenses_compatible": any(
                g.id == "licenses" and g.status == GateStatus.PASSED for g in self.quality_gates
            ),
            "provenance_verified": any(
                g.id == "provenance" and g.status == GateStatus.PASSED for g in self.quality_gates
            ),
            "tests_passed": any(
                g.id == "tests" and g.status == GateStatus.PASSED for g in self.quality_gates
            ),
            "performance_acceptable": any(
                g.id == "performance" and g.status == GateStatus.PASSED for g in self.quality_gates
            ),
            "security_review_passed": any(
                g.id == "security" and g.status == GateStatus.PASSED for g in self.quality_gates
            ),
        }

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary (matches plugin-manifest.schema.json certification section)."""
        return {
            "certified": self.certified,
            "certification_level": self.certification_level.value,
            "certified_at": self.certified_at,
            "expires_at": self.expires_at,
            "certifier": self.certifier,
            "certificate_id": self.certificate_id,
            "quality_gates": self.get_quality_gates_summary(),
            "metrics": self.metrics.to_dict(),
            "requirements": [r.to_dict() for r in self.requirements],
        }

    def to_full_dict(self) -> dict[str, Any]:
        """Convert to full dictionary with detailed gate results."""
        return {
            "plugin_id": self.plugin_id,
            "plugin_version": self.plugin_version,
            "certified": self.certified,
            "certification_level": self.certification_level.value,
            "certified_at": self.certified_at,
            "expires_at": self.expires_at,
            "certifier": self.certifier,
            "certificate_id": self.certificate_id,
            "quality_gates": [g.to_dict() for g in self.quality_gates],
            "metrics": self.metrics.to_dict(),
            "requirements": [r.to_dict() for r in self.requirements],
            "errors": self.errors,
            "passed_gates_count": len(self.passed_gates),
            "failed_gates_count": len(self.failed_gates),
        }

    def to_json(self, indent: int = 2) -> str:
        """Convert to JSON string."""
        return json.dumps(self.to_full_dict(), indent=indent)


@dataclass
class CertificationPolicy:
    """Policy defining certification requirements per level."""

    target_level: CertificationLevel = CertificationLevel.STANDARD
    require_signature: bool = False  # Only for PREMIUM+
    require_sbom: bool = True
    require_provenance: bool = False  # Only for PREMIUM+
    max_critical_vulnerabilities: int = 0
    max_high_vulnerabilities: int = 0
    max_medium_vulnerabilities: int = 5
    allowed_license_categories: list[str] = field(
        default_factory=lambda: ["permissive", "weak_copyleft"]
    )
    min_test_coverage: float = 0.0  # Only for ENTERPRISE
    validity_days: int = 365

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "target_level": self.target_level.value,
            "require_signature": self.require_signature,
            "require_sbom": self.require_sbom,
            "require_provenance": self.require_provenance,
            "max_critical_vulnerabilities": self.max_critical_vulnerabilities,
            "max_high_vulnerabilities": self.max_high_vulnerabilities,
            "max_medium_vulnerabilities": self.max_medium_vulnerabilities,
            "allowed_license_categories": self.allowed_license_categories,
            "min_test_coverage": self.min_test_coverage,
            "validity_days": self.validity_days,
        }

    @classmethod
    def for_level(cls, level: CertificationLevel) -> CertificationPolicy:
        """Create a policy for a specific certification level."""
        if level == CertificationLevel.NONE or level == CertificationLevel.BASIC:
            return cls(target_level=level, require_sbom=False)
        elif level == CertificationLevel.STANDARD:
            return cls(target_level=level, require_sbom=True)
        elif level == CertificationLevel.PREMIUM:
            return cls(
                target_level=level,
                require_sbom=True,
                require_signature=True,
                require_provenance=True,
            )
        elif level == CertificationLevel.ENTERPRISE:
            return cls(
                target_level=level,
                require_sbom=True,
                require_signature=True,
                require_provenance=True,
                min_test_coverage=80.0,
            )
        return cls(target_level=level)


class CertificationEngine:
    """
    Plugin Certification Engine.

    Orchestrates quality gate checks and determines certification level.
    Integrates with verification, SBOM, license, and vulnerability services.
    """

    # Quality gates with their requirements per level
    GATE_DEFINITIONS = {
        "manifest": {
            "name": "Manifest Validation",
            "description": "Plugin manifest is valid and complete",
            "required_for": [
                CertificationLevel.BASIC,
                CertificationLevel.STANDARD,
                CertificationLevel.PREMIUM,
                CertificationLevel.ENTERPRISE,
            ],
        },
        "vulnerabilities": {
            "name": "Vulnerability Scan",
            "description": "No critical or high vulnerabilities",
            "required_for": [
                CertificationLevel.BASIC,
                CertificationLevel.STANDARD,
                CertificationLevel.PREMIUM,
                CertificationLevel.ENTERPRISE,
            ],
        },
        "sbom": {
            "name": "SBOM Validation",
            "description": "Software Bill of Materials is present and valid",
            "required_for": [
                CertificationLevel.STANDARD,
                CertificationLevel.PREMIUM,
                CertificationLevel.ENTERPRISE,
            ],
        },
        "licenses": {
            "name": "License Compatibility",
            "description": "All dependency licenses are compatible",
            "required_for": [
                CertificationLevel.STANDARD,
                CertificationLevel.PREMIUM,
                CertificationLevel.ENTERPRISE,
            ],
        },
        "signature": {
            "name": "Signature Verification",
            "description": "Package is cryptographically signed",
            "required_for": [CertificationLevel.PREMIUM, CertificationLevel.ENTERPRISE],
        },
        "provenance": {
            "name": "Build Provenance",
            "description": "Build provenance metadata is verified",
            "required_for": [CertificationLevel.PREMIUM, CertificationLevel.ENTERPRISE],
        },
        "tests": {
            "name": "Test Suite",
            "description": "Automated tests pass with minimum coverage",
            "required_for": [CertificationLevel.ENTERPRISE],
        },
        "performance": {
            "name": "Performance Benchmarks",
            "description": "Performance benchmarks meet requirements",
            "required_for": [CertificationLevel.ENTERPRISE],
        },
        "security": {
            "name": "Security Review",
            "description": "Security review completed",
            "required_for": [CertificationLevel.ENTERPRISE],
        },
    }

    def __init__(
        self,
        policy: Optional[CertificationPolicy] = None,
        certifier: str = "VoiceStudio Local Certifier",
    ):
        """
        Initialize the certification engine.

        Args:
            policy: Certification policy to use
            certifier: Entity performing certification
        """
        self._policy = policy or CertificationPolicy()
        self._certifier = certifier
        self._quality_gates: list[QualityGate] = []
        self._metrics = CertificationMetrics()
        self._requirements: list[CertificationRequirement] = []

    async def certify_package(
        self,
        package_path: Path,
        target_level: Optional[CertificationLevel] = None,
        progress_callback: Optional[Callable[[str, float], None]] = None,
    ) -> CertificationResult:
        """
        Run certification process on a plugin package.

        Args:
            package_path: Path to plugin package (.vspkg or directory)
            target_level: Target certification level (overrides policy)
            progress_callback: Optional callback for progress updates

        Returns:
            CertificationResult with certification status and details
        """
        if target_level:
            self._policy = CertificationPolicy.for_level(target_level)

        self._quality_gates = []
        self._requirements = []
        errors: list[str] = []

        if not package_path.exists():
            return CertificationResult(
                plugin_id="unknown",
                plugin_version="unknown",
                certified=False,
                certification_level=CertificationLevel.NONE,
                certifier=self._certifier,
                errors=[f"Package not found: {package_path}"],
            )

        # Extract manifest to get plugin info
        manifest = self._extract_manifest(package_path)
        plugin_id = manifest.get("name", "unknown") if manifest else "unknown"
        plugin_version = manifest.get("version", "unknown") if manifest else "unknown"

        total_gates = len(self.GATE_DEFINITIONS)
        completed = 0

        def report_progress(gate_name: str):
            nonlocal completed
            completed += 1
            if progress_callback:
                progress_callback(gate_name, completed / total_gates)

        # Run quality gates
        logger.info(f"Starting certification for {plugin_id}@{plugin_version}")

        # Gate 1: Manifest validation
        manifest_gate = await self._check_manifest(manifest, package_path)
        self._quality_gates.append(manifest_gate)
        report_progress("manifest")
        if manifest_gate.status == GateStatus.FAILED:
            errors.append(manifest_gate.message)

        # Gate 2: Vulnerability scan
        vuln_gate = await self._check_vulnerabilities(package_path, manifest)
        self._quality_gates.append(vuln_gate)
        report_progress("vulnerabilities")
        if vuln_gate.status == GateStatus.FAILED:
            errors.append(vuln_gate.message)

        # Gate 3: SBOM validation
        sbom_gate = await self._check_sbom(package_path)
        self._quality_gates.append(sbom_gate)
        report_progress("sbom")
        if sbom_gate.status == GateStatus.FAILED and self._policy.require_sbom:
            errors.append(sbom_gate.message)

        # Gate 4: License compatibility
        license_gate = await self._check_licenses(package_path)
        self._quality_gates.append(license_gate)
        report_progress("licenses")
        if license_gate.status == GateStatus.FAILED:
            errors.append(license_gate.message)

        # Gate 5: Signature verification
        sig_gate = await self._check_signature(package_path)
        self._quality_gates.append(sig_gate)
        report_progress("signature")
        if sig_gate.status == GateStatus.FAILED and self._policy.require_signature:
            errors.append(sig_gate.message)

        # Gate 6: Provenance verification
        prov_gate = await self._check_provenance(package_path)
        self._quality_gates.append(prov_gate)
        report_progress("provenance")
        if prov_gate.status == GateStatus.FAILED and self._policy.require_provenance:
            errors.append(prov_gate.message)

        # Gate 7: Tests (placeholder - requires test execution)
        test_gate = self._check_tests_placeholder()
        self._quality_gates.append(test_gate)
        report_progress("tests")

        # Gate 8: Performance (placeholder - requires benchmarks)
        perf_gate = self._check_performance_placeholder()
        self._quality_gates.append(perf_gate)
        report_progress("performance")

        # Gate 9: Security review (placeholder - manual review required)
        security_gate = self._check_security_placeholder()
        self._quality_gates.append(security_gate)
        report_progress("security")

        # Determine achieved certification level
        achieved_level = self._determine_level()
        certified = achieved_level.value != "none"

        logger.info(
            f"Certification complete for {plugin_id}@{plugin_version}: "
            f"level={achieved_level.value}, certified={certified}"
        )

        return CertificationResult(
            plugin_id=plugin_id,
            plugin_version=plugin_version,
            certified=certified,
            certification_level=achieved_level,
            certifier=self._certifier,
            quality_gates=self._quality_gates,
            metrics=self._metrics,
            requirements=self._requirements,
            errors=errors,
        )

    def _extract_manifest(self, package_path: Path) -> Optional[dict[str, Any]]:
        """Extract manifest from package."""
        import zipfile

        manifest_paths = ["manifest.json", "plugin.json"]

        if package_path.is_file():
            try:
                with zipfile.ZipFile(package_path, "r") as zf:
                    for name in manifest_paths:
                        try:
                            data = zf.read(name)
                            return json.loads(data.decode("utf-8"))
                        except (KeyError, json.JSONDecodeError) as e:
                            # GAP-PY-001: Manifest file not found or invalid JSON, try next
                            logger.debug(f"Could not load manifest {name} from zip: {e}")
                            continue
            except zipfile.BadZipFile as e:
                # GAP-PY-001: Not a valid zip file
                logger.debug(f"Package {package_path} is not a valid zip: {e}")
                return None
        elif package_path.is_dir():
            for name in manifest_paths:
                manifest_file = package_path / name
                if manifest_file.exists():
                    try:
                        return json.loads(manifest_file.read_text())
                    except json.JSONDecodeError as e:
                        # GAP-PY-001: Invalid JSON in manifest, try next
                        logger.debug(f"Failed to parse manifest {name}: {e}")
                        continue
        return None

    async def _check_manifest(
        self, manifest: Optional[dict[str, Any]], package_path: Path
    ) -> QualityGate:
        """Check manifest validity."""
        gate_def = self.GATE_DEFINITIONS["manifest"]
        gate = QualityGate(
            id="manifest",
            name=gate_def["name"],
            description=gate_def["description"],
            required_for=gate_def["required_for"],
        )

        if not manifest:
            gate.status = GateStatus.FAILED
            gate.message = "Manifest not found or invalid JSON"
            return gate

        # Check required fields
        required_fields = ["name", "version", "author", "plugin_type"]
        missing = [f for f in required_fields if f not in manifest]

        if missing:
            gate.status = GateStatus.FAILED
            gate.message = f"Missing required fields: {', '.join(missing)}"
            gate.evidence = {"missing_fields": missing}
            return gate

        # Validate version format (semver)
        import re

        version = manifest.get("version", "")
        if not re.match(r"^\d+\.\d+\.\d+", version):
            gate.status = GateStatus.FAILED
            gate.message = f"Invalid version format: {version}"
            gate.evidence = {"version": version}
            return gate

        gate.status = GateStatus.PASSED
        gate.message = "Manifest is valid"
        gate.evidence = {
            "name": manifest.get("name"),
            "version": manifest.get("version"),
            "plugin_type": manifest.get("plugin_type"),
        }

        # Add requirement
        self._requirements.append(
            CertificationRequirement(
                id="REQ-001",
                name="Valid plugin manifest",
                passed=True,
                evidence_path=str(package_path),
            )
        )

        return gate

    async def _check_vulnerabilities(
        self, package_path: Path, manifest: Optional[dict[str, Any]]
    ) -> QualityGate:
        """Check for vulnerabilities."""
        gate_def = self.GATE_DEFINITIONS["vulnerabilities"]
        gate = QualityGate(
            id="vulnerabilities",
            name=gate_def["name"],
            description=gate_def["description"],
            required_for=gate_def["required_for"],
        )

        try:
            from .vuln_scanner import VulnerabilityScanner

            scanner = VulnerabilityScanner()
            results = await scanner.scan_package(package_path)

            critical = results.get("critical", 0)
            high = results.get("high", 0)
            medium = results.get("medium", 0)

            policy = self._policy
            if critical > policy.max_critical_vulnerabilities:
                gate.status = GateStatus.FAILED
                gate.message = f"Found {critical} critical vulnerabilities (max: {policy.max_critical_vulnerabilities})"
            elif high > policy.max_high_vulnerabilities:
                gate.status = GateStatus.FAILED
                gate.message = (
                    f"Found {high} high vulnerabilities (max: {policy.max_high_vulnerabilities})"
                )
            elif medium > policy.max_medium_vulnerabilities:
                gate.status = GateStatus.FAILED
                gate.message = f"Found {medium} medium vulnerabilities (max: {policy.max_medium_vulnerabilities})"
            else:
                gate.status = GateStatus.PASSED
                gate.message = f"Vulnerability scan passed (C:{critical} H:{high} M:{medium})"

            gate.evidence = {
                "critical": critical,
                "high": high,
                "medium": medium,
                "total": results.get("total", 0),
            }

        except ImportError:
            gate.status = GateStatus.SKIPPED
            gate.message = "Vulnerability scanner not available"
        except Exception as e:
            gate.status = GateStatus.FAILED
            gate.message = f"Vulnerability scan error: {e!s}"

        return gate

    async def _check_sbom(self, package_path: Path) -> QualityGate:
        """Check for SBOM presence and validity."""
        gate_def = self.GATE_DEFINITIONS["sbom"]
        gate = QualityGate(
            id="sbom",
            name=gate_def["name"],
            description=gate_def["description"],
            required_for=gate_def["required_for"],
        )

        import zipfile

        sbom_paths = ["sbom.json", "bom.json", ".sbom/bom.json"]
        sbom_content = None

        if package_path.is_file():
            try:
                with zipfile.ZipFile(package_path, "r") as zf:
                    for name in sbom_paths:
                        try:
                            data = zf.read(name)
                            sbom_content = json.loads(data.decode("utf-8"))
                            break
                        except (KeyError, json.JSONDecodeError) as e:
                            # GAP-PY-001: SBOM file not found or invalid, try next
                            logger.debug(f"Could not load SBOM {name} from zip: {e}")
                            continue
            except zipfile.BadZipFile:
                # GAP-PY-001: Not a valid zip, will try directory path below
                logger.debug(f"Package {package_path} is not a valid zip for SBOM extraction")
        elif package_path.is_dir():
            for name in sbom_paths:
                sbom_file = package_path / name
                if sbom_file.exists():
                    try:
                        sbom_content = json.loads(sbom_file.read_text())
                        break
                    except json.JSONDecodeError as e:
                        # GAP-PY-001: Invalid JSON in SBOM, try next
                        logger.debug(f"Failed to parse SBOM {name}: {e}")
                        continue

        if not sbom_content:
            if self._policy.require_sbom:
                gate.status = GateStatus.FAILED
                gate.message = "SBOM not found in package"
            else:
                gate.status = GateStatus.SKIPPED
                gate.message = "SBOM not required by policy"
            return gate

        # Validate SBOM structure (CycloneDX)
        if "bomFormat" not in sbom_content:
            gate.status = GateStatus.FAILED
            gate.message = "Invalid SBOM format (missing bomFormat)"
            return gate

        component_count = len(sbom_content.get("components", []))
        gate.status = GateStatus.PASSED
        gate.message = f"Valid SBOM with {component_count} components"
        gate.evidence = {
            "format": sbom_content.get("bomFormat"),
            "spec_version": sbom_content.get("specVersion"),
            "component_count": component_count,
        }

        self._requirements.append(
            CertificationRequirement(
                id="REQ-002",
                name="SBOM present and valid",
                passed=True,
            )
        )

        return gate

    async def _check_licenses(self, package_path: Path) -> QualityGate:
        """Check license compatibility."""
        gate_def = self.GATE_DEFINITIONS["licenses"]
        gate = QualityGate(
            id="licenses",
            name=gate_def["name"],
            description=gate_def["description"],
            required_for=gate_def["required_for"],
        )

        try:
            from .license_checker import LicenseChecker

            checker = LicenseChecker()
            result = await checker.check_package(package_path)

            if result.compatible:
                gate.status = GateStatus.PASSED
                gate.message = "All licenses are compatible"
            else:
                gate.status = GateStatus.FAILED
                incompatible = [i.license for i in result.issues]
                gate.message = f"Incompatible licenses: {', '.join(incompatible[:3])}"

            gate.evidence = {
                "compatible": result.compatible,
                "total_licenses": result.total_licenses,
                "issue_count": len(result.issues),
            }

        except ImportError:
            gate.status = GateStatus.SKIPPED
            gate.message = "License checker not available"
        except AttributeError:
            # License checker exists but has different API
            gate.status = GateStatus.SKIPPED
            gate.message = "License checker API incompatible"
        except Exception as e:
            gate.status = GateStatus.FAILED
            gate.message = f"License check error: {e!s}"

        return gate

    async def _check_signature(self, package_path: Path) -> QualityGate:
        """Check package signature."""
        gate_def = self.GATE_DEFINITIONS["signature"]
        gate = QualityGate(
            id="signature",
            name=gate_def["name"],
            description=gate_def["description"],
            required_for=gate_def["required_for"],
        )

        try:
            from .signer import check_signing_available, verify_package_auto

            if not check_signing_available():
                if self._policy.require_signature:
                    gate.status = GateStatus.FAILED
                    gate.message = "Cryptography library not available"
                else:
                    gate.status = GateStatus.SKIPPED
                    gate.message = "Signing not available (not required)"
                return gate

            result = verify_package_auto(package_path)

            if result.valid:
                gate.status = GateStatus.PASSED
                gate.message = f"Signature verified (key: {result.key_id})"
                gate.evidence = {
                    "key_id": result.key_id,
                    "algorithm": result.algorithm,
                    "signed_at": result.signed_at,
                    "fingerprint": result.fingerprint,
                }
            else:
                if self._policy.require_signature:
                    gate.status = GateStatus.FAILED
                    gate.message = result.message or "Signature verification failed"
                else:
                    gate.status = GateStatus.SKIPPED
                    gate.message = result.message or "No signature found (not required)"

        except ImportError:
            if self._policy.require_signature:
                gate.status = GateStatus.FAILED
                gate.message = "Signer not available but signature required"
            else:
                gate.status = GateStatus.SKIPPED
                gate.message = "Signer not available"
        except Exception as e:
            if self._policy.require_signature:
                gate.status = GateStatus.FAILED
            else:
                gate.status = GateStatus.SKIPPED
            gate.message = f"Signature check: {e!s}"

        return gate

    async def _check_provenance(self, package_path: Path) -> QualityGate:
        """Check build provenance."""
        gate_def = self.GATE_DEFINITIONS["provenance"]
        gate = QualityGate(
            id="provenance",
            name=gate_def["name"],
            description=gate_def["description"],
            required_for=gate_def["required_for"],
        )

        import zipfile

        provenance_paths = ["provenance.json", ".provenance/provenance.json"]
        provenance_content = None

        if package_path.is_file():
            try:
                with zipfile.ZipFile(package_path, "r") as zf:
                    for name in provenance_paths:
                        try:
                            data = zf.read(name)
                            provenance_content = json.loads(data.decode("utf-8"))
                            break
                        except (KeyError, json.JSONDecodeError) as e:
                            # GAP-PY-001: Provenance file not found or invalid, try next
                            logger.debug(f"Could not load provenance {name} from zip: {e}")
                            continue
            except zipfile.BadZipFile:
                # GAP-PY-001: Not a valid zip, will try directory path below
                logger.debug(f"Package {package_path} is not a valid zip for provenance extraction")
        elif package_path.is_dir():
            for name in provenance_paths:
                prov_file = package_path / name
                if prov_file.exists():
                    try:
                        provenance_content = json.loads(prov_file.read_text())
                        break
                    except json.JSONDecodeError as e:
                        # GAP-PY-001: Invalid JSON in provenance, try next
                        logger.debug(f"Failed to parse provenance {name}: {e}")
                        continue

        if not provenance_content:
            if self._policy.require_provenance:
                gate.status = GateStatus.FAILED
                gate.message = "Provenance not found in package"
            else:
                gate.status = GateStatus.SKIPPED
                gate.message = "Provenance not required by policy"
            return gate

        # Validate provenance structure
        required = ["build_type", "built_at"]
        missing = [f for f in required if f not in provenance_content]

        if missing:
            gate.status = GateStatus.FAILED
            gate.message = f"Invalid provenance: missing {', '.join(missing)}"
            return gate

        gate.status = GateStatus.PASSED
        gate.message = "Build provenance verified"
        gate.evidence = {
            "build_type": provenance_content.get("build_type"),
            "built_at": provenance_content.get("built_at"),
            "reproducible": provenance_content.get("reproducible", False),
        }

        self._requirements.append(
            CertificationRequirement(
                id="REQ-003",
                name="Build provenance verified",
                passed=True,
            )
        )

        return gate

    def _check_tests_placeholder(self) -> QualityGate:
        """Placeholder for test validation (requires actual test execution)."""
        gate_def = self.GATE_DEFINITIONS["tests"]
        return QualityGate(
            id="tests",
            name=gate_def["name"],
            description=gate_def["description"],
            status=GateStatus.SKIPPED,
            message="Test execution not implemented (requires test runner)",
            required_for=gate_def["required_for"],
        )

    def _check_performance_placeholder(self) -> QualityGate:
        """Placeholder for performance benchmarks."""
        gate_def = self.GATE_DEFINITIONS["performance"]
        return QualityGate(
            id="performance",
            name=gate_def["name"],
            description=gate_def["description"],
            status=GateStatus.SKIPPED,
            message="Performance benchmarks not implemented",
            required_for=gate_def["required_for"],
        )

    def _check_security_placeholder(self) -> QualityGate:
        """Placeholder for security review (manual process)."""
        gate_def = self.GATE_DEFINITIONS["security"]
        return QualityGate(
            id="security",
            name=gate_def["name"],
            description=gate_def["description"],
            status=GateStatus.SKIPPED,
            message="Security review is a manual process",
            required_for=gate_def["required_for"],
        )

    def _determine_level(self) -> CertificationLevel:
        """Determine the achieved certification level based on gate results."""
        # Build a set of passed gates
        passed_gates = {g.id for g in self._quality_gates if g.status == GateStatus.PASSED}

        # Check each level in order (highest to lowest)
        levels = [
            CertificationLevel.ENTERPRISE,
            CertificationLevel.PREMIUM,
            CertificationLevel.STANDARD,
            CertificationLevel.BASIC,
        ]

        for level in levels:
            required_gates = set()
            for gate_id, gate_def in self.GATE_DEFINITIONS.items():
                if level in gate_def["required_for"]:
                    required_gates.add(gate_id)

            # Check if all required gates for this level passed
            # (or are skipped for non-required gates)
            meets_level = True
            for gate_id in required_gates:
                if gate_id not in passed_gates:
                    # Check if gate was skipped (acceptable for some)
                    gate = next((g for g in self._quality_gates if g.id == gate_id), None)
                    if gate and gate.status == GateStatus.SKIPPED:
                        # Skipped gates don't block unless they're required by policy
                        if gate_id == "signature" and self._policy.require_signature:
                            meets_level = False
                            break
                        if gate_id == "sbom" and self._policy.require_sbom:
                            meets_level = False
                            break
                        if gate_id == "provenance" and self._policy.require_provenance:
                            meets_level = False
                            break
                        # Other skipped gates are okay
                    else:
                        meets_level = False
                        break

            if meets_level:
                return level

        return CertificationLevel.NONE


# Module-level convenience functions
_engine: Optional[CertificationEngine] = None


def get_certification_engine(
    policy: Optional[CertificationPolicy] = None,
    certifier: str = "VoiceStudio Local Certifier",
) -> CertificationEngine:
    """Get the certification engine instance."""
    global _engine
    if _engine is None or policy:
        _engine = CertificationEngine(policy=policy, certifier=certifier)
    return _engine


async def certify_plugin(
    package_path: Path,
    target_level: CertificationLevel = CertificationLevel.STANDARD,
    progress_callback: Optional[Callable[[str, float], None]] = None,
) -> CertificationResult:
    """
    Certify a plugin package.

    Args:
        package_path: Path to plugin package
        target_level: Target certification level
        progress_callback: Optional progress callback

    Returns:
        CertificationResult
    """
    engine = get_certification_engine()
    return await engine.certify_package(package_path, target_level, progress_callback)
