"""
Tests for Plugin Manifest v4 schema additions (Phase 4).

Tests the new distribution, catalog, and trust fields added in Phase 4.
"""

import pytest

from backend.services.plugin_schema_validator import (
    PluginSchemaValidator,
    validate_plugin_manifest,
)


class TestManifestV4Fields:
    """Test Phase 4 manifest schema additions."""

    @pytest.fixture
    def validator(self):
        """Create a validator instance."""
        return PluginSchemaValidator()

    @pytest.fixture
    def base_manifest(self):
        """Minimal valid manifest for testing."""
        return {
            "name": "test_plugin_v4",
            "version": "1.0.0",
            "author": "Test Author",
            "plugin_type": "backend_only",
            "category": "utilities",
            "entry_points": {"backend": "plugin.register"},
        }

    def test_schema_version_field(self, validator, base_manifest):
        """Test schema_version field validation."""
        # Test valid v4 schema version
        manifest = {**base_manifest, "schema_version": "4.0.0"}
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

        # Test valid v3 schema version (backwards compatibility)
        manifest = {**base_manifest, "schema_version": "3.0.0"}
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_id_field(self, validator, base_manifest):
        """Test globally unique plugin ID field."""
        # Valid ID format (reverse-domain style)
        manifest = {**base_manifest, "id": "com.voicestudio.test_plugin"}
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

        # Valid simple ID
        manifest = {**base_manifest, "id": "test_plugin_123"}
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_distribution_section(self, validator, base_manifest):
        """Test distribution section validation."""
        manifest = {
            **base_manifest,
            "distribution": {
                "package_format": "vspkg",
                "download_url": "https://plugins.voicestudio.app/test.vspkg",
                "checksum": {"algorithm": "sha256", "value": "abc123def456"},
                "size_bytes": 1024000,
                "release_date": "2025-01-01T00:00:00Z",
                "release_channel": "stable",
                "changelog_url": "https://plugins.voicestudio.app/changelog",
                "min_installer_version": "1.0.0",
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_distribution_release_channels(self, validator, base_manifest):
        """Test all valid release channels."""
        for channel in ["stable", "beta", "alpha", "nightly"]:
            manifest = {
                **base_manifest,
                "distribution": {"release_channel": channel},
            }
            is_valid, errors = validator.validate(manifest)
            assert is_valid, f"Channel '{channel}' should be valid, got: {errors}"

    def test_catalog_section(self, validator, base_manifest):
        """Test catalog section validation."""
        manifest = {
            **base_manifest,
            "catalog": {
                "category": "voice_synthesis",
                "subcategory": "neural-tts",
                "featured": True,
                "popularity_score": 85.5,
                "download_count": 10000,
                "rating": {"average": 4.5, "count": 250},
                "screenshots": [
                    {"url": "https://example.com/screen1.png", "caption": "Main view"},
                    {"url": "https://example.com/screen2.png", "type": "banner"},
                ],
                "demo_video_url": "https://youtube.com/watch?v=123",
                "pricing": {"model": "free"},
                "supported_languages": ["en", "es-ES", "fr"],
                "keywords": ["tts", "synthesis", "voice"],
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_catalog_categories(self, validator, base_manifest):
        """Test all valid catalog categories."""
        categories = [
            "audio_effects",
            "voice_synthesis",
            "speech_recognition",
            "voice_conversion",
            "audio_analysis",
            "video_processing",
            "integrations",
            "utilities",
            "developer_tools",
            "themes_ui",
            "other",
        ]
        for category in categories:
            manifest = {
                **base_manifest,
                "catalog": {"category": category},
            }
            is_valid, errors = validator.validate(manifest)
            assert is_valid, f"Category '{category}' should be valid, got: {errors}"

    def test_catalog_pricing_models(self, validator, base_manifest):
        """Test all valid pricing models."""
        models = ["free", "freemium", "paid", "subscription", "donation"]
        for model in models:
            manifest = {
                **base_manifest,
                "catalog": {"pricing": {"model": model}},
            }
            is_valid, errors = validator.validate(manifest)
            assert is_valid, f"Pricing model '{model}' should be valid, got: {errors}"

    def test_trust_section(self, validator, base_manifest):
        """Test trust section validation."""
        manifest = {
            **base_manifest,
            "trust": {
                "publisher_id": "voicestudio-official",
                "publisher_name": "VoiceStudio Team",
                "publisher_verified": True,
                "code_signed": True,
                "signature_info": {
                    "algorithm": "ed25519",
                    "signer_id": "voicestudio-official",
                    "signed_at": "2025-01-01T00:00:00Z",
                },
                "security_audit": {
                    "audited": True,
                    "auditor": "Security Firm",
                    "audit_date": "2025-01-01",
                    "audit_version": "1.0.0",
                },
                "trust_level": "official",
                "policy_compliance": ["offline_capable", "no_telemetry", "open_source"],
                "content_rating": "everyone",
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_trust_levels(self, validator, base_manifest):
        """Test all valid trust levels."""
        levels = ["unknown", "community", "verified", "official", "certified"]
        for level in levels:
            manifest = {
                **base_manifest,
                "trust": {"trust_level": level},
            }
            is_valid, errors = validator.validate(manifest)
            assert is_valid, f"Trust level '{level}' should be valid, got: {errors}"

    def test_trust_policy_compliance(self, validator, base_manifest):
        """Test policy compliance values."""
        policies = [
            "no_telemetry",
            "offline_capable",
            "open_source",
            "privacy_preserving",
            "gdpr_compliant",
            "accessibility_compliant",
        ]
        manifest = {
            **base_manifest,
            "trust": {"policy_compliance": policies},
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"


class TestSecurityPermissionsV4:
    """Test Phase 4 enhanced security permissions."""

    @pytest.fixture
    def validator(self):
        """Create a validator instance."""
        return PluginSchemaValidator()

    @pytest.fixture
    def base_manifest(self):
        """Minimal valid manifest for testing."""
        return {
            "name": "test_plugin_security",
            "version": "1.0.0",
            "author": "Test Author",
            "plugin_type": "backend_only",
            "category": "utilities",
            "entry_points": {"backend": "plugin.register"},
        }

    def test_filesystem_permissions(self, validator, base_manifest):
        """Test filesystem permission declarations."""
        manifest = {
            **base_manifest,
            "security": {
                "permissions": {
                    "filesystem": {
                        "read": ["/data/**", "/config/*.json"],
                        "write": ["/output/**"],
                        "scope": "plugin_data",
                    }
                }
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_network_permissions(self, validator, base_manifest):
        """Test network permission declarations."""
        manifest = {
            **base_manifest,
            "security": {
                "permissions": {
                    "network": {
                        "enabled": True,
                        "allowed_hosts": ["api.example.com", "*.voicestudio.app"],
                        "allowed_ports": [443, 8080, "8000-9000"],
                        "protocols": ["https", "websocket"],
                    }
                }
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_audio_permissions(self, validator, base_manifest):
        """Test audio permission declarations."""
        manifest = {
            **base_manifest,
            "security": {
                "permissions": {
                    "audio": {
                        "capture": True,
                        "playback": True,
                        "process": True,
                    }
                }
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_gpu_permissions(self, validator, base_manifest):
        """Test GPU permission declarations."""
        manifest = {
            **base_manifest,
            "security": {
                "permissions": {
                    "gpu": {
                        "enabled": True,
                        "vram_limit_mb": 4096,
                        "device_selection": "primary",
                    }
                }
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_host_api_permissions(self, validator, base_manifest):
        """Test host API permission declarations."""
        manifest = {
            **base_manifest,
            "security": {
                "permissions": {
                    "host_api": {
                        "allowed_apis": [
                            "audio.playback",
                            "audio.process",
                            "ui.notifications",
                            "engine.invoke",
                        ],
                        "denied_apis": ["settings.write"],
                    }
                }
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_subprocess_permissions(self, validator, base_manifest):
        """Test subprocess permission declarations."""
        manifest = {
            **base_manifest,
            "security": {
                "permissions": {
                    "subprocess": {
                        "enabled": True,
                        "spawn_children": False,
                        "allowed_executables": ["ffmpeg", "ffprobe"],
                    }
                }
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_isolation_mode_subprocess(self, validator, base_manifest):
        """Test new subprocess isolation mode."""
        manifest = {
            **base_manifest,
            "security": {"isolation_mode": "subprocess"},
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_combined_permissions(self, validator, base_manifest):
        """Test combined permissions declaration."""
        manifest = {
            **base_manifest,
            "security": {
                "isolation_mode": "subprocess",
                "permissions": {
                    "filesystem": {"scope": "plugin_data"},
                    "network": {"enabled": False},
                    "audio": {"capture": False, "playback": True},
                    "gpu": {"enabled": True},
                    "host_api": {"allowed_apis": ["audio.playback"]},
                    "subprocess": {"enabled": True, "spawn_children": False},
                },
            },
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"


class TestManifestV4BackwardCompatibility:
    """Test v4 schema backward compatibility with v3 manifests."""

    @pytest.fixture
    def validator(self):
        """Create a validator instance."""
        return PluginSchemaValidator()

    def test_v3_manifest_still_valid(self, validator):
        """V3-style manifest (with required v4 fields) should still validate."""
        # Note: category is required in v4 schema but all other v3 fields are supported
        v3_manifest = {
            "name": "legacy_plugin",
            "version": "1.0.0",
            "author": "Test Author",
            "plugin_type": "backend_only",
            "category": "utilities",
            "entry_points": {"backend": "plugin.register"},
            "permissions": ["filesystem.read", "network.http"],
            "security": {
                "isolation_mode": "in_process",
                "sandbox_config": {
                    "allow_network": True,
                    "allow_filesystem": True,
                },
            },
        }
        is_valid, errors = validator.validate(v3_manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"

    def test_mixed_v3_v4_manifest(self, validator):
        """Manifest with both v3 and v4 fields should validate."""
        manifest = {
            "name": "mixed_plugin",
            "version": "1.0.0",
            "author": "Test Author",
            "plugin_type": "backend_only",
            "category": "utilities",
            "schema_version": "4.0.0",
            "id": "com.example.mixed_plugin",
            "entry_points": {"backend": "plugin.register"},
            # V3 style permissions (still supported)
            "permissions": ["filesystem.read"],
            # V4 style security
            "security": {
                "isolation_mode": "subprocess",
                "permissions": {
                    "filesystem": {"scope": "plugin_data"},
                },
            },
            # V4 distribution
            "distribution": {"package_format": "vspkg"},
            # V4 catalog (category now at top level, but catalog can have other fields)
            "catalog": {"featured": False},
            # V4 trust
            "trust": {"trust_level": "community"},
        }
        is_valid, errors = validator.validate(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"


class TestManifestV4ValidationFunction:
    """Test the validate_plugin_manifest convenience function with v4 fields."""

    def test_full_v4_manifest(self):
        """Test complete v4 manifest through convenience function."""
        manifest = {
            "schema_version": "4.0.0",
            "id": "com.voicestudio.complete_v4_plugin",
            "name": "complete_v4_plugin",
            "version": "1.0.0",
            "author": "VoiceStudio",
            "description": "A complete v4 plugin",
            "plugin_type": "backend_only",
            "category": "voice_synthesis",
            "entry_points": {"backend": "main.register"},
            "distribution": {
                "package_format": "vspkg",
                "release_channel": "stable",
            },
            "catalog": {
                "keywords": ["synthesis", "tts"],
            },
            "trust": {
                "publisher_id": "voicestudio_official",
                "trust_level": "official",
            },
            "security": {
                "isolation_mode": "subprocess",
                "permissions": {
                    "audio": {"playback": True, "process": True},
                    "gpu": {"enabled": True},
                },
            },
        }
        is_valid, errors = validate_plugin_manifest(manifest)
        assert is_valid, f"Expected valid, got errors: {errors}"
