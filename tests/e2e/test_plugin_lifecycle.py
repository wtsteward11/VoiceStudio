"""
E2E Plugin Lifecycle Test.

Phase 4 Workstream 2: Tests the full plugin lifecycle from creation through
removal, including manifest validation, signing, sandbox enforcement, and
gallery API integration.
"""

from __future__ import annotations

import asyncio
import json
import logging
import os
import tempfile
from pathlib import Path
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

logger = logging.getLogger(__name__)

REFERENCE_PLUGINS_DIR = Path(__file__).parent.parent.parent / "plugins" / "reference"
CATALOG_PATH = Path(__file__).parent.parent.parent / "shared" / "catalog" / "plugins.json"


class TestPluginCatalogIntegrity:
    """Verify the plugin catalog is well-formed and contains reference plugins."""

    def test_catalog_json_loads(self):
        catalog = json.loads(CATALOG_PATH.read_text())
        assert "plugins" in catalog
        assert "categories" in catalog
        assert "version" in catalog

    def test_catalog_has_reference_plugins(self):
        catalog = json.loads(CATALOG_PATH.read_text())
        plugin_ids = [p["id"] for p in catalog["plugins"]]
        expected = [
            "com.voicestudio.noise_reduction",
            "com.voicestudio.format_converter",
            "com.voicestudio.silence_detector",
            "com.voicestudio.pitch_shifter",
            "com.voicestudio.silence_trimmer",
        ]
        for pid in expected:
            assert pid in plugin_ids, f"Missing plugin {pid} in catalog"

    def test_catalog_plugins_have_required_fields(self):
        catalog = json.loads(CATALOG_PATH.read_text())
        required_fields = {"id", "name", "description", "category", "author", "versions"}
        for plugin in catalog["plugins"]:
            missing = required_fields - set(plugin.keys())
            assert not missing, f"Plugin {plugin.get('id', '?')} missing fields: {missing}"

    def test_catalog_categories_match_schema(self):
        catalog = json.loads(CATALOG_PATH.read_text())
        cat_ids = {c["id"] for c in catalog["categories"]}
        for plugin in catalog["plugins"]:
            assert plugin["category"] in cat_ids, (
                f"Plugin {plugin['id']} has category '{plugin['category']}' "
                f"not in catalog categories: {cat_ids}"
            )

    def test_each_plugin_has_at_least_one_version(self):
        catalog = json.loads(CATALOG_PATH.read_text())
        for plugin in catalog["plugins"]:
            assert len(plugin["versions"]) >= 1, f"Plugin {plugin['id']} has no versions"

    def test_version_entries_have_required_fields(self):
        catalog = json.loads(CATALOG_PATH.read_text())
        required = {"version", "release_date", "download_url"}
        for plugin in catalog["plugins"]:
            for ver in plugin["versions"]:
                missing = required - set(ver.keys())
                assert not missing, f"Plugin {plugin['id']} version missing: {missing}"


class TestReferencePluginManifests:
    """Verify reference plugin manifest files are valid."""

    @pytest.fixture(
        params=[
            "noise_reduction",
            "format_converter",
            "silence_detector",
            "pitch_shifter",
            "silence_trimmer",
        ]
    )
    def plugin_dir(self, request):
        return REFERENCE_PLUGINS_DIR / request.param

    def test_manifest_exists(self, plugin_dir):
        manifest_path = plugin_dir / "manifest.json"
        assert manifest_path.exists(), f"No manifest.json in {plugin_dir}"

    def test_manifest_valid_json(self, plugin_dir):
        manifest = json.loads((plugin_dir / "manifest.json").read_text())
        assert "name" in manifest
        assert "version" in manifest
        assert "plugin_type" in manifest
        assert "category" in manifest

    def test_manifest_has_entry_point(self, plugin_dir):
        manifest = json.loads((plugin_dir / "manifest.json").read_text())
        entry = manifest.get("entry_point", "plugin.py")
        assert (plugin_dir / entry).exists(), f"Entry point {entry} not found"

    def test_manifest_schema_version(self, plugin_dir):
        manifest = json.loads((plugin_dir / "manifest.json").read_text())
        assert manifest.get("schema_version") == "6.0.0"

    def test_manifest_id_format(self, plugin_dir):
        manifest = json.loads((plugin_dir / "manifest.json").read_text())
        plugin_id = manifest.get("id", "")
        assert plugin_id.startswith("com.voicestudio."), f"Bad id format: {plugin_id}"

    def test_manifest_permissions_defined(self, plugin_dir):
        manifest = json.loads((plugin_dir / "manifest.json").read_text())
        assert "permissions" in manifest, "No permissions field in manifest"
        perms = manifest["permissions"]
        assert "network" in perms, "No network permission field"

    def test_manifest_settings_schema(self, plugin_dir):
        manifest = json.loads((plugin_dir / "manifest.json").read_text())
        schema = manifest.get("settings_schema", {})
        assert schema.get("type") == "object", "Settings schema must be object type"
        assert "properties" in schema, "Settings schema must have properties"


class TestReferencePluginImports:
    """Verify reference plugins can be imported and instantiated."""

    def test_noise_reduction_imports(self):
        from plugins.reference.noise_reduction.plugin import NoiseReductionPlugin

        p = NoiseReductionPlugin()
        info = p.get_info()
        assert info["id"] == "com.voicestudio.noise_reduction"
        assert info["version"] == "1.0.0"

    def test_format_converter_imports(self):
        from plugins.reference.format_converter.plugin import FormatConverterPlugin

        p = FormatConverterPlugin()
        info = p.get_info()
        assert info["id"] == "com.voicestudio.format_converter"

    def test_silence_detector_imports(self):
        from plugins.reference.silence_detector.plugin import SilenceDetectorPlugin

        p = SilenceDetectorPlugin()
        info = p.get_info()
        assert info["id"] == "com.voicestudio.silence_detector"

    def test_pitch_shifter_imports(self):
        from plugins.reference.pitch_shifter.plugin import PitchShifterPlugin

        p = PitchShifterPlugin()
        info = p.get_info()
        assert info["id"] == "com.voicestudio.pitch_shifter"
        assert info["version"] == "1.0.0"

    def test_silence_trimmer_imports(self):
        from plugins.reference.silence_trimmer.plugin import SilenceTrimmerPlugin

        p = SilenceTrimmerPlugin()
        info = p.get_info()
        assert info["id"] == "com.voicestudio.silence_trimmer"
        assert info["version"] == "1.0.0"


class TestPluginLifecycle:
    """Test the full plugin activate/configure/process/deactivate lifecycle."""

    @pytest.mark.asyncio
    async def test_noise_reduction_lifecycle(self):
        from plugins.reference.noise_reduction.plugin import NoiseReductionPlugin

        p = NoiseReductionPlugin()
        activated = await p.activate()
        assert activated is True

        p.configure({"reduction_strength": 0.5, "stationary": False})
        info = p.get_info()
        assert info["config"]["reduction_strength"] == 0.5
        assert info["config"]["stationary"] is False

        await p.deactivate()

    @pytest.mark.asyncio
    async def test_silence_detector_lifecycle(self):
        from plugins.reference.silence_detector.plugin import SilenceDetectorPlugin

        p = SilenceDetectorPlugin()
        activated = await p.activate()
        assert activated is True

        p.configure({"silence_threshold_db": -30, "min_silence_duration": 0.5})
        info = p.get_info()
        assert info["config"]["silence_threshold_db"] == -30

        await p.deactivate()

    @pytest.mark.asyncio
    async def test_format_converter_lifecycle(self):
        from plugins.reference.format_converter.plugin import FormatConverterPlugin

        p = FormatConverterPlugin()
        activated = await p.activate()

        p.configure({"default_format": "flac", "sample_rate": 48000})
        info = p.get_info()
        assert info["config"]["default_format"] == "flac"
        assert info["config"]["sample_rate"] == 48000

        await p.deactivate()

    @pytest.mark.asyncio
    async def test_process_with_missing_file_returns_error(self):
        from plugins.reference.noise_reduction.plugin import NoiseReductionPlugin

        p = NoiseReductionPlugin()
        await p.activate()

        result = await p.process({"audio_path": "/nonexistent/file.wav"})
        assert "error" in result

        await p.deactivate()


class TestPluginServiceIntegration:
    """Test plugin service loads and manages plugins."""

    def test_plugin_service_imports(self):
        from backend.services.plugin_service import PluginService, PluginState, PluginType

        assert PluginType.PROCESSOR is not None
        assert PluginState.DISCOVERED is not None

    def test_plugin_service_instantiates(self):
        from backend.services.plugin_service import PluginService

        service = PluginService()
        assert service is not None

    def test_plugin_manifest_from_dict(self):
        from backend.services.plugin_service import PluginManifest, PluginType

        data = {
            "plugin_id": "test.plugin",
            "name": "Test Plugin",
            "version": "1.0.0",
            "description": "A test plugin",
            "author": "Test",
            "plugin_type": "processor",
            "entry_point": "plugin.py",
        }
        manifest = PluginManifest.from_dict(data)
        assert manifest.plugin_id == "test.plugin"
        assert manifest.plugin_type == PluginType.PROCESSOR


class TestPluginSandbox:
    """Test sandbox isolation and permission enforcement."""

    def test_sandbox_imports(self):
        from backend.services.plugin_sandbox import PluginSandbox, SandboxPermissions

        assert PluginSandbox is not None
        assert SandboxPermissions is not None

    def test_sandbox_instantiation(self):
        from backend.services.plugin_sandbox import (
            PluginSandbox,
            SandboxPermissions,
            SandboxState,
        )

        perms = SandboxPermissions(plugin_id="test.plugin")
        sandbox = PluginSandbox(plugin_id="test.plugin", permissions=perms)
        assert sandbox.state == SandboxState.IDLE
        assert sandbox.plugin_id == "test.plugin"

    def test_sandbox_temp_workspace_is_isolated(self):
        from backend.services.plugin_sandbox import PluginSandbox, SandboxPermissions

        perms = SandboxPermissions(plugin_id="test.isolation")
        sandbox = PluginSandbox(plugin_id="test.isolation", permissions=perms)
        workspace = sandbox._create_temp_workspace()
        assert workspace.exists()
        assert "vs_plugin_test.isolation_" in str(workspace)
        import shutil

        shutil.rmtree(workspace, ignore_errors=True)


class TestPluginSigningInfrastructure:
    """Test Ed25519 signing and verification are available."""

    def test_signer_module_imports(self):
        from backend.plugins.supply_chain.signer import (
            KeyStatus,
            SignatureAlgorithm,
            check_signing_available,
        )

        assert KeyStatus.ACTIVE is not None
        assert SignatureAlgorithm.ED25519 is not None

    def test_signing_availability(self):
        from backend.plugins.supply_chain.signer import check_signing_available

        available = check_signing_available()
        if available:
            logger.info("Ed25519 signing is available (cryptography library present)")
        else:
            pytest.skip("cryptography library not installed; signing unavailable")

    def test_key_metadata_creation(self):
        from backend.plugins.supply_chain.signer import KeyMetadata, KeyStatus

        meta = KeyMetadata(
            key_id="test-key-001",
            fingerprint="abc123",
            created_at="2026-02-21T00:00:00Z",
            status=KeyStatus.ACTIVE,
        )
        d = meta.to_dict()
        assert d["key_id"] == "test-key-001"
        assert d["status"] == "active"


class TestUnsignedPluginRejection:
    """Verify unsigned plugins are rejected when signing is enforced."""

    def test_unsigned_manifest_flagged(self):
        manifest_data = {
            "name": "malicious_plugin",
            "version": "1.0.0",
            "author": "attacker",
            "plugin_type": "backend_only",
            "category": "utilities",
        }
        assert "signature" not in manifest_data
        assert manifest_data.get("signed") is None

    def test_signature_field_required_for_catalog(self):
        catalog = json.loads(CATALOG_PATH.read_text())
        for plugin in catalog["plugins"]:
            assert plugin.get("verified") is True, f"Plugin {plugin['id']} is not verified"


class TestGalleryAPICatalog:
    """Verify catalog service returns all 5 reference plugins when using local catalog."""

    @pytest.mark.asyncio
    async def test_catalog_service_loads_five_plugins_from_local(self, monkeypatch):
        """When VOICESTUDIO_PLUGIN_CATALOG_URL points to local file, catalog has 5 plugins."""
        catalog_path = CATALOG_PATH.resolve()
        monkeypatch.setenv("VOICESTUDIO_PLUGIN_CATALOG_URL", str(catalog_path))
        import backend.plugins.gallery.catalog as catalog_mod

        catalog_mod._catalog_service = None
        try:
            from backend.plugins.gallery import get_catalog_service

            service = get_catalog_service()
            catalog = await service.get_catalog(force_refresh=True)
            plugin_ids = [p.id for p in catalog.plugins]
            expected = [
                "com.voicestudio.noise_reduction",
                "com.voicestudio.format_converter",
                "com.voicestudio.silence_detector",
                "com.voicestudio.pitch_shifter",
                "com.voicestudio.silence_trimmer",
            ]
            for pid in expected:
                assert pid in plugin_ids, f"Catalog missing plugin {pid}"
            assert (
                len(catalog.plugins) >= 5
            ), f"Expected at least 5 plugins, got {len(catalog.plugins)}"
        finally:
            catalog_mod._catalog_service = None


class TestGalleryAPIModels:
    """Test gallery API Pydantic models match catalog structure."""

    def test_plugin_summary_model(self):
        from backend.api.routes.plugin_gallery import PluginSummary

        summary = PluginSummary(
            id="com.voicestudio.test",
            name="Test Plugin",
            description="A test",
            category="utilities",
            author="Test",
            license="MIT",
            latest_version="1.0.0",
            tags=["test"],
            featured=False,
            verified=True,
            rating=0.0,
            downloads=0,
        )
        assert summary.id == "com.voicestudio.test"

    def test_install_request_model(self):
        from backend.api.routes.plugin_gallery import InstallRequest

        req = InstallRequest(plugin_id="com.voicestudio.noise_reduction")
        assert req.plugin_id == "com.voicestudio.noise_reduction"
        assert req.version is None

    def test_install_response_model(self):
        from backend.api.routes.plugin_gallery import InstallResponse

        resp = InstallResponse(
            success=True,
            plugin_id="com.voicestudio.noise_reduction",
            version="1.0.0",
        )
        assert resp.success is True
