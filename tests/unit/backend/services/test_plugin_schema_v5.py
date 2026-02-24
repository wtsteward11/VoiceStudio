"""
Tests for Plugin Manifest Schema v5 Extensions.

Phase 5C: Tests for supply chain, provenance, enterprise, and certification fields.
"""

import json
from pathlib import Path

import pytest

from backend.services.plugin_schema_validator import (
    PluginSchemaValidator,
    validate_plugin_manifest,
)

# Base valid manifest for testing v5 extensions
# Using security.permissions instead of legacy permissions array
BASE_VALID_MANIFEST = {
    "name": "test_plugin",
    "display_name": "Test Plugin",
    "version": "1.0.0",
    "author": "Test Author",
    "description": "A test plugin",
    "plugin_type": "backend_only",
    "category": "utilities",
    "entry_points": {"backend": "plugin.register"},
    "capabilities": {"backend_routes": True},
    "security": {
        "permissions": {
            "filesystem": {"read": ["$PLUGIN_DATA/*"], "scope": "plugin_data"},
            "network": {"enabled": False},
        }
    },
}


class TestSchemaVersionField:
    """Tests for schema_version field updates."""

    @pytest.fixture
    def validator(self):
        return PluginSchemaValidator()

    def test_schema_version_5_accepted(self, validator):
        """Test that schema_version 5.0.0 is accepted."""
        manifest = {**BASE_VALID_MANIFEST, "schema_version": "5.0.0"}
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_schema_version_4_still_accepted(self, validator):
        """Test that schema_version 4.0.0 is still accepted (backward compatible)."""
        manifest = {**BASE_VALID_MANIFEST, "schema_version": "4.0.0"}
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_schema_version_invalid(self, validator):
        """Test that invalid schema_version is rejected."""
        manifest = {**BASE_VALID_MANIFEST, "schema_version": "7.0.0"}  # Not in enum
        is_valid, errors = validator.validate(manifest)
        assert not is_valid
        assert any("schema_version" in e.lower() for e in errors)


class TestSupplyChainSection:
    """Tests for the supply_chain section."""

    @pytest.fixture
    def validator(self):
        return PluginSchemaValidator()

    def test_supply_chain_empty_valid(self, validator):
        """Test that empty supply_chain section is valid."""
        manifest = {**BASE_VALID_MANIFEST, "supply_chain": {}}
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_supply_chain_sbom_valid(self, validator):
        """Test valid SBOM configuration."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "supply_chain": {
                "sbom": {
                    "format": "cyclonedx-1.5",
                    "generated_at": "2026-02-17T12:00:00Z",
                    "tool": "VoiceStudio SBOM Generator",
                    "tool_version": "1.0.0",
                    "component_count": 15,
                    "sbom_path": "sbom.json",
                }
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_supply_chain_sbom_invalid_format(self, validator):
        """Test invalid SBOM format is rejected."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "supply_chain": {"sbom": {"format": "invalid-format"}},  # Not in enum
        }
        is_valid, errors = validator.validate(manifest)
        assert not is_valid
        assert any("format" in e.lower() for e in errors)

    def test_supply_chain_vulnerability_scan_valid(self, validator):
        """Test valid vulnerability scan configuration."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "supply_chain": {
                "vulnerability_scan": {
                    "scanned_at": "2026-02-17T12:00:00Z",
                    "scanner": "pip-audit",
                    "scanner_version": "2.5.0",
                    "passed": True,
                    "summary": {"critical": 0, "high": 0, "medium": 2, "low": 5, "unknown": 0},
                    "report_path": "vuln-report.json",
                }
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_supply_chain_vulnerability_scan_invalid_scanner(self, validator):
        """Test invalid scanner is rejected."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "supply_chain": {"vulnerability_scan": {"scanner": "unknown-scanner"}},  # Not in enum
        }
        is_valid, errors = validator.validate(manifest)
        assert not is_valid

    def test_supply_chain_license_compliance_valid(self, validator):
        """Test valid license compliance configuration."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "supply_chain": {
                "license_compliance": {
                    "checked_at": "2026-02-17T12:00:00Z",
                    "passed": True,
                    "plugin_license": "MIT",
                    "license_category": "permissive",
                    "dependency_count": 10,
                    "incompatible_count": 0,
                    "copyleft_count": 0,
                    "unknown_count": 1,
                    "report_path": "license-report.json",
                }
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_supply_chain_license_invalid_category(self, validator):
        """Test invalid license category is rejected."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "supply_chain": {
                "license_compliance": {"license_category": "invalid_category"}  # Not in enum
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert not is_valid

    def test_supply_chain_dependency_graph_valid(self, validator):
        """Test valid dependency graph configuration."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "supply_chain": {
                "dependency_graph": {
                    "direct_count": 5,
                    "transitive_count": 20,
                    "max_depth": 4,
                    "pinned": True,
                    "lockfile_path": "requirements.lock",
                }
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_supply_chain_full_valid(self, validator):
        """Test complete supply_chain section."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "supply_chain": {
                "sbom": {
                    "format": "cyclonedx-1.5",
                    "generated_at": "2026-02-17T12:00:00Z",
                    "component_count": 15,
                },
                "vulnerability_scan": {
                    "scanned_at": "2026-02-17T12:00:00Z",
                    "scanner": "grype",
                    "passed": True,
                    "summary": {"critical": 0, "high": 0, "medium": 0, "low": 0},
                },
                "license_compliance": {
                    "passed": True,
                    "plugin_license": "MIT",
                    "license_category": "permissive",
                },
                "dependency_graph": {"direct_count": 3, "transitive_count": 10, "pinned": True},
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"


class TestProvenanceSection:
    """Tests for the provenance section."""

    @pytest.fixture
    def validator(self):
        return PluginSchemaValidator()

    def test_provenance_empty_valid(self, validator):
        """Test that empty provenance section is valid."""
        manifest = {**BASE_VALID_MANIFEST, "provenance": {}}
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_provenance_basic_valid(self, validator):
        """Test basic provenance configuration."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "provenance": {
                "spec_version": "v1",
                "build_type": "release",
                "built_at": "2026-02-17T12:00:00Z",
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_provenance_invalid_build_type(self, validator):
        """Test invalid build_type is rejected."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "provenance": {"build_type": "invalid_type"},  # Not in enum
        }
        is_valid, errors = validator.validate(manifest)
        assert not is_valid

    def test_provenance_builder_valid(self, validator):
        """Test valid builder configuration."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "provenance": {
                "builder": {
                    "os": "Windows-10",
                    "python_version": "3.9.13",
                    "hostname": "build-server",
                    "ci_system": "github-actions",
                    "ci_job_url": "https://github.com/org/repo/actions/runs/123",
                }
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_provenance_source_valid(self, validator):
        """Test valid source configuration."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "provenance": {
                "source": {
                    "repository": "https://github.com/org/repo",
                    "commit": "abc123def456abc123def456abc123def456abc1",
                    "branch": "main",
                    "tag": "v1.0.0",
                    "dirty": False,
                }
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_provenance_inputs_valid(self, validator):
        """Test valid inputs configuration."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "provenance": {
                "inputs": [
                    {
                        "name": "plugin.py",
                        "digest": {
                            "sha256": "abc123def456abc123def456abc123def456abc123def456abc123def456abc1"
                        },
                    }
                ]
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_provenance_full_valid(self, validator):
        """Test complete provenance section."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "provenance": {
                "spec_version": "v1",
                "build_type": "ci",
                "built_at": "2026-02-17T12:00:00Z",
                "builder": {
                    "os": "Linux-5.4",
                    "python_version": "3.9.13",
                    "ci_system": "github-actions",
                },
                "source": {
                    "repository": "https://github.com/org/repo",
                    "commit": "abcdef1234567890abcdef1234567890abcdef12",
                    "branch": "main",
                    "dirty": False,
                },
                "reproducible": True,
                "provenance_path": "provenance.json",
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"


class TestEnterpriseSection:
    """Tests for the enterprise section."""

    @pytest.fixture
    def validator(self):
        return PluginSchemaValidator()

    def test_enterprise_empty_valid(self, validator):
        """Test that empty enterprise section is valid."""
        manifest = {**BASE_VALID_MANIFEST, "enterprise": {}}
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_enterprise_deployment_valid(self, validator):
        """Test valid deployment configuration."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "enterprise": {
                "deployment": {
                    "environments": ["development", "staging", "production"],
                    "requires_approval": True,
                    "rollout_strategy": "canary",
                    "rollback_supported": True,
                    "config_profiles": [
                        {
                            "name": "dev",
                            "description": "Development settings",
                            "environment": "development",
                        },
                        {
                            "name": "prod",
                            "description": "Production settings",
                            "environment": "production",
                        },
                    ],
                }
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_enterprise_sla_valid(self, validator):
        """Test valid SLA configuration."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "enterprise": {
                "sla": {
                    "availability_percent": 99.9,
                    "max_latency_ms": 100,
                    "max_memory_mb": 512,
                    "support_tier": "premium",
                    "response_time_hours": 4,
                    "maintenance_window": "0 3 * * 0",
                }
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_enterprise_compliance_valid(self, validator):
        """Test valid compliance configuration."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "enterprise": {
                "compliance": {
                    "certifications": ["soc2-type2", "gdpr", "iso27001"],
                    "data_residency": ["local-only", "us", "eu"],
                    "audit_logging": True,
                    "encryption_at_rest": True,
                    "encryption_in_transit": True,
                }
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_enterprise_telemetry_valid(self, validator):
        """Test valid telemetry configuration."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "enterprise": {
                "telemetry": {
                    "metrics_enabled": True,
                    "metrics_endpoint": "/metrics",
                    "metrics_format": "prometheus",
                    "tracing_enabled": True,
                    "health_endpoint": "/health",
                }
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_enterprise_full_valid(self, validator):
        """Test complete enterprise section."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "enterprise": {
                "deployment": {
                    "environments": ["production"],
                    "requires_approval": True,
                    "rollout_strategy": "staged",
                },
                "sla": {"availability_percent": 99.95, "support_tier": "enterprise"},
                "compliance": {
                    "certifications": ["soc2-type2"],
                    "data_residency": ["local-only"],
                    "audit_logging": True,
                },
                "telemetry": {"metrics_enabled": True, "metrics_format": "opentelemetry"},
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"


class TestCertificationSection:
    """Tests for the certification section."""

    @pytest.fixture
    def validator(self):
        return PluginSchemaValidator()

    def test_certification_empty_valid(self, validator):
        """Test that empty certification section is valid."""
        manifest = {**BASE_VALID_MANIFEST, "certification": {}}
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_certification_basic_valid(self, validator):
        """Test basic certification configuration."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "certification": {
                "certified": True,
                "certification_level": "standard",
                "certified_at": "2026-02-17T12:00:00Z",
                "expires_at": "2027-02-17T12:00:00Z",
                "certifier": "VoiceStudio Certification Authority",
                "certificate_id": "cert-12345",
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_certification_invalid_level(self, validator):
        """Test invalid certification level is rejected."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "certification": {"certification_level": "invalid_level"},  # Not in enum
        }
        is_valid, errors = validator.validate(manifest)
        assert not is_valid

    def test_certification_quality_gates_valid(self, validator):
        """Test valid quality gates configuration."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "certification": {
                "quality_gates": {
                    "manifest_valid": True,
                    "signature_valid": True,
                    "sbom_present": True,
                    "vulnerabilities_passed": True,
                    "licenses_compatible": True,
                    "provenance_verified": True,
                    "tests_passed": True,
                    "performance_acceptable": True,
                    "security_review_passed": True,
                }
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_certification_metrics_valid(self, validator):
        """Test valid metrics configuration."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "certification": {
                "metrics": {
                    "test_coverage_percent": 85.5,
                    "cyclomatic_complexity": 5.2,
                    "documentation_coverage_percent": 90.0,
                    "api_stability_score": 95.0,
                }
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_certification_requirements_valid(self, validator):
        """Test valid requirements configuration."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "certification": {
                "requirements": [
                    {
                        "id": "REQ-001",
                        "name": "No critical vulnerabilities",
                        "passed": True,
                        "evidence_path": "reports/vuln-scan.json",
                    },
                    {"id": "REQ-002", "name": "All tests pass", "passed": True},
                ]
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_certification_full_valid(self, validator):
        """Test complete certification section."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "certification": {
                "certified": True,
                "certification_level": "enterprise",
                "certified_at": "2026-02-17T12:00:00Z",
                "expires_at": "2027-02-17T12:00:00Z",
                "certifier": "VoiceStudio",
                "certificate_id": "VS-CERT-2026-001",
                "quality_gates": {
                    "manifest_valid": True,
                    "signature_valid": True,
                    "sbom_present": True,
                    "vulnerabilities_passed": True,
                    "licenses_compatible": True,
                    "provenance_verified": True,
                    "tests_passed": True,
                    "performance_acceptable": True,
                    "security_review_passed": True,
                },
                "metrics": {
                    "test_coverage_percent": 95.0,
                    "cyclomatic_complexity": 3.5,
                    "documentation_coverage_percent": 100.0,
                    "api_stability_score": 98.0,
                },
                "requirements": [
                    {"id": "REQ-001", "name": "No vulnerabilities", "passed": True},
                    {"id": "REQ-002", "name": "License compliance", "passed": True},
                ],
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"


class TestFullV5Manifest:
    """Tests for complete v5 manifests with all sections."""

    @pytest.fixture
    def validator(self):
        return PluginSchemaValidator()

    def test_full_v5_manifest_valid(self, validator):
        """Test a complete v5 manifest with all new sections."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "schema_version": "5.0.0",
            "supply_chain": {
                "sbom": {
                    "format": "cyclonedx-1.5",
                    "generated_at": "2026-02-17T12:00:00Z",
                    "component_count": 25,
                },
                "vulnerability_scan": {
                    "scanned_at": "2026-02-17T12:00:00Z",
                    "scanner": "pip-audit",
                    "passed": True,
                    "summary": {"critical": 0, "high": 0, "medium": 0, "low": 0},
                },
                "license_compliance": {
                    "passed": True,
                    "plugin_license": "Apache-2.0",
                    "license_category": "permissive",
                },
            },
            "provenance": {
                "spec_version": "v1",
                "build_type": "release",
                "built_at": "2026-02-17T12:00:00Z",
                "source": {
                    "repository": "https://github.com/org/plugin",
                    "commit": "1234567890abcdef1234567890abcdef12345678",
                    "branch": "main",
                    "tag": "v1.0.0",
                },
                "reproducible": True,
            },
            "enterprise": {
                "deployment": {"environments": ["production"], "requires_approval": True},
                "sla": {"availability_percent": 99.9, "support_tier": "premium"},
                "compliance": {"certifications": ["gdpr"], "data_residency": ["local-only"]},
            },
            "certification": {
                "certified": True,
                "certification_level": "premium",
                "certified_at": "2026-02-17T12:00:00Z",
                "quality_gates": {
                    "manifest_valid": True,
                    "signature_valid": True,
                    "sbom_present": True,
                    "vulnerabilities_passed": True,
                    "licenses_compatible": True,
                },
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_v5_backward_compatible_with_v4(self, validator):
        """Test that v5 schema accepts v4 manifests (no new sections)."""
        manifest = {
            **BASE_VALID_MANIFEST,
            "schema_version": "4.0.0",
            # No supply_chain, provenance, enterprise, or certification
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid (backward compatible), got errors: {errors}"

    def test_v5_manifest_additional_properties_rejected(self, validator):
        """Test that unknown top-level properties are rejected."""
        manifest = {**BASE_VALID_MANIFEST, "unknown_field": "should fail"}
        is_valid, errors = validator.validate(manifest)
        assert not is_valid
        assert any("additional" in e.lower() or "unknown" in e.lower() for e in errors)


class TestSchemaFileValidity:
    """Tests to ensure the schema file itself is valid JSON Schema."""

    def test_schema_file_is_valid_json(self):
        """Test that the schema file is valid JSON."""
        schema_path = Path("shared/schemas/plugin-manifest.schema.json")
        with open(schema_path) as f:
            schema = json.load(f)
        assert isinstance(schema, dict)

    def test_schema_has_required_fields(self):
        """Test that the schema has required JSON Schema fields."""
        schema_path = Path("shared/schemas/plugin-manifest.schema.json")
        with open(schema_path) as f:
            schema = json.load(f)

        assert "$schema" in schema
        assert "title" in schema
        assert "type" in schema
        assert "properties" in schema

    def test_schema_version_enum_includes_v5(self):
        """Test that schema_version enum includes 5.0.0."""
        schema_path = Path("shared/schemas/plugin-manifest.schema.json")
        with open(schema_path) as f:
            schema = json.load(f)

        schema_version_def = schema["properties"]["schema_version"]
        assert "5.0.0" in schema_version_def["enum"]

    def test_new_sections_exist(self):
        """Test that new v5 sections are defined in schema."""
        schema_path = Path("shared/schemas/plugin-manifest.schema.json")
        with open(schema_path) as f:
            schema = json.load(f)

        properties = schema["properties"]
        assert "supply_chain" in properties
        assert "provenance" in properties
        assert "enterprise" in properties
        assert "certification" in properties
