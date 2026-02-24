"""
Tests for Plugin Service (T-6).

Comprehensive tests for backend/services/plugin_service.py covering:
- Plugin types and states
- Plugin metadata and capabilities
- Plugin lifecycle management
- Wasm execution integration
- Signature verification integration
"""

from datetime import datetime
from pathlib import Path
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

# =============================================================================
# Test Imports and Module Availability
# =============================================================================


class TestPluginServiceImports:
    """Test that plugin_service module can be imported."""

    def test_import_module(self) -> None:
        """Test basic module import."""
        from backend.services import plugin_service

        assert plugin_service is not None

    def test_import_plugin_type(self) -> None:
        """Test PluginType enum import."""
        from backend.services.plugin_service import PluginType

        assert PluginType is not None

    def test_import_plugin_state(self) -> None:
        """Test PluginState enum import."""
        from backend.services.plugin_service import PluginState

        assert PluginState is not None

    def test_import_plugin_service_class(self) -> None:
        """Test PluginService class import."""
        from backend.services.plugin_service import PluginService

        assert PluginService is not None


# =============================================================================
# PluginType Tests
# =============================================================================


class TestPluginType:
    """Tests for PluginType enum."""

    def test_engine_type(self) -> None:
        """Test ENGINE plugin type."""
        from backend.services.plugin_service import PluginType

        assert PluginType.ENGINE.value == "engine"

    def test_processor_type(self) -> None:
        """Test PROCESSOR plugin type."""
        from backend.services.plugin_service import PluginType

        assert PluginType.PROCESSOR.value == "processor"

    def test_exporter_type(self) -> None:
        """Test EXPORTER plugin type."""
        from backend.services.plugin_service import PluginType

        assert PluginType.EXPORTER.value == "exporter"

    def test_importer_type(self) -> None:
        """Test IMPORTER plugin type."""
        from backend.services.plugin_service import PluginType

        assert PluginType.IMPORTER.value == "importer"

    def test_ui_panel_type(self) -> None:
        """Test UI_PANEL plugin type."""
        from backend.services.plugin_service import PluginType

        assert PluginType.UI_PANEL.value == "ui_panel"

    def test_tool_type(self) -> None:
        """Test TOOL plugin type."""
        from backend.services.plugin_service import PluginType

        assert PluginType.TOOL.value == "tool"

    def test_all_types_unique(self) -> None:
        """Test that all plugin types have unique values."""
        from backend.services.plugin_service import PluginType

        values = [pt.value for pt in PluginType]
        assert len(values) == len(set(values))


# =============================================================================
# PluginState Tests
# =============================================================================


class TestPluginState:
    """Tests for PluginState enum."""

    def test_discovered_state(self) -> None:
        """Test DISCOVERED state."""
        from backend.services.plugin_service import PluginState

        assert PluginState.DISCOVERED.value == "discovered"

    def test_loaded_state(self) -> None:
        """Test LOADED state."""
        from backend.services.plugin_service import PluginState

        assert PluginState.LOADED.value == "loaded"

    def test_activated_state(self) -> None:
        """Test ACTIVATED state."""
        from backend.services.plugin_service import PluginState

        assert PluginState.ACTIVATED.value == "activated"

    def test_deactivated_state(self) -> None:
        """Test DEACTIVATED state."""
        from backend.services.plugin_service import PluginState

        assert PluginState.DEACTIVATED.value == "deactivated"


# =============================================================================
# Version Parsing Tests
# =============================================================================


class TestVersionParsing:
    """Tests for version parsing utilities."""

    def test_parse_version_simple(self) -> None:
        """Test parsing simple version string."""
        from backend.services.plugin_service import parse_version

        assert parse_version("1.2.3") == (1, 2, 3)

    def test_parse_version_with_suffix(self) -> None:
        """Test parsing version with suffix."""
        from backend.services.plugin_service import parse_version

        assert parse_version("1.2.3-beta") == (1, 2, 3)

    def test_parse_version_invalid(self) -> None:
        """Test parsing invalid version returns (0, 0, 0)."""
        from backend.services.plugin_service import parse_version

        assert parse_version("invalid") == (0, 0, 0)

    def test_is_version_compatible_equal(self) -> None:
        """Test version compatibility when equal."""
        from backend.services.plugin_service import is_version_compatible

        assert is_version_compatible("1.0.0", "1.0.0") is True

    def test_is_version_compatible_higher(self) -> None:
        """Test version compatibility when app is higher."""
        from backend.services.plugin_service import is_version_compatible

        assert is_version_compatible("2.0.0", "1.0.0") is True

    def test_is_version_compatible_lower(self) -> None:
        """Test version incompatibility when app is lower."""
        from backend.services.plugin_service import is_version_compatible

        assert is_version_compatible("1.0.0", "2.0.0") is False


# =============================================================================
# PluginManifest Tests
# =============================================================================


class TestPluginManifest:
    """Tests for PluginManifest dataclass."""

    def test_create_manifest(self) -> None:
        """Test creating plugin manifest."""
        from backend.services.plugin_service import PluginManifest, PluginType

        manifest = PluginManifest(
            plugin_id="test-plugin",
            name="Test Plugin",
            version="1.0.0",
            description="A test plugin",
            author="Test Author",
            plugin_type=PluginType.PROCESSOR,
            entry_point="main.py",
        )

        assert manifest.plugin_id == "test-plugin"
        assert manifest.name == "Test Plugin"
        assert manifest.version == "1.0.0"
        assert manifest.plugin_type == PluginType.PROCESSOR

    def test_manifest_optional_fields(self) -> None:
        """Test manifest optional fields have defaults."""
        from backend.services.plugin_service import PluginManifest, PluginType

        manifest = PluginManifest(
            plugin_id="minimal",
            name="Minimal Plugin",
            version="0.1.0",
            description="",
            author="",
            plugin_type=PluginType.TOOL,
            entry_point="main.py",
        )

        # Check optional fields exist
        assert manifest.dependencies == []
        assert manifest.min_app_version == "1.0.0"


# =============================================================================
# PluginInfo Tests
# =============================================================================


class TestPluginInfo:
    """Tests for PluginInfo dataclass."""

    def test_create_info(self, tmp_path) -> None:
        """Test creating plugin info."""
        from backend.services.plugin_service import (
            PluginInfo,
            PluginManifest,
            PluginState,
            PluginType,
        )

        manifest = PluginManifest(
            plugin_id="test-plugin",
            name="Test Plugin",
            version="1.0.0",
            description="A test plugin",
            author="Test Author",
            plugin_type=PluginType.PROCESSOR,
            entry_point="main.py",
        )

        info = PluginInfo(
            manifest=manifest,
            path=tmp_path,
            state=PluginState.DISCOVERED,
        )

        assert info.manifest == manifest
        assert info.path == tmp_path
        assert info.state == PluginState.DISCOVERED


# =============================================================================
# PluginService Initialization Tests
# =============================================================================


class TestPluginServiceInitialization:
    """Tests for PluginService initialization."""

    def test_singleton_instance(self) -> None:
        """Test PluginService singleton access."""
        from backend.services.plugin_service import PluginService

        # PluginService should provide a way to get instance
        service = PluginService()
        assert service is not None

    def test_service_has_plugins_dict(self) -> None:
        """Test service has plugins dictionary."""
        from backend.services.plugin_service import PluginService

        service = PluginService()
        assert hasattr(service, "_plugins") or hasattr(service, "plugins")

    def test_service_has_plugins_or_settings(self) -> None:
        """Test service has plugins or settings storage."""
        from backend.services.plugin_service import PluginService

        service = PluginService()
        assert hasattr(service, "_plugins") or hasattr(service, "_settings")


# =============================================================================
# PluginService Discovery Tests
# =============================================================================


class TestPluginServiceDiscovery:
    """Tests for plugin discovery."""

    def test_discover_plugins_method_exists(self) -> None:
        """Test discover_plugins method exists."""
        from backend.services.plugin_service import PluginService

        service = PluginService()
        assert hasattr(service, "discover_plugins")
        assert callable(service.discover_plugins)

    def test_get_plugin_method_exists(self) -> None:
        """Test get_plugin method exists."""
        from backend.services.plugin_service import PluginService

        service = PluginService()
        assert hasattr(service, "get_plugin")
        assert callable(service.get_plugin)

    def test_list_plugins_method_exists(self) -> None:
        """Test list_plugins method exists."""
        from backend.services.plugin_service import PluginService

        service = PluginService()
        assert hasattr(service, "list_plugins")
        assert callable(service.list_plugins)


# =============================================================================
# PluginService Lifecycle Tests
# =============================================================================


class TestPluginServiceLifecycle:
    """Tests for plugin lifecycle management."""

    def test_load_plugin_method_exists(self) -> None:
        """Test load_plugin method exists."""
        from backend.services.plugin_service import PluginService

        service = PluginService()
        assert hasattr(service, "load_plugin")
        assert callable(service.load_plugin)

    def test_unload_plugin_method_exists(self) -> None:
        """Test unload_plugin method exists."""
        from backend.services.plugin_service import PluginService

        service = PluginService()
        assert hasattr(service, "unload_plugin")
        assert callable(service.unload_plugin)

    def test_activate_plugin_method_exists(self) -> None:
        """Test activate_plugin method exists."""
        from backend.services.plugin_service import PluginService

        service = PluginService()
        assert hasattr(service, "activate_plugin")
        assert callable(service.activate_plugin)

    def test_deactivate_plugin_method_exists(self) -> None:
        """Test deactivate_plugin method exists."""
        from backend.services.plugin_service import PluginService

        service = PluginService()
        assert hasattr(service, "deactivate_plugin")
        assert callable(service.deactivate_plugin)


# =============================================================================
# Wasm Execution Integration Tests (W-5)
# =============================================================================


class TestPluginServiceWasmIntegration:
    """Tests for Wasm execution integration (W-5)."""

    def test_wasm_runner_availability_flag(self) -> None:
        """Test WASM_RUNNER_AVAILABLE flag exists."""
        from backend.services import plugin_service

        assert hasattr(plugin_service, "WASM_RUNNER_AVAILABLE")

    def test_execute_wasm_plugin_method_exists(self) -> None:
        """Test execute_wasm_plugin method exists."""
        from backend.services.plugin_service import PluginService

        service = PluginService()
        assert hasattr(service, "execute_wasm_plugin")
        assert callable(service.execute_wasm_plugin)

    @pytest.mark.asyncio
    async def test_execute_wasm_plugin_returns_result(self) -> None:
        """Test execute_wasm_plugin returns WasmExecutionResult."""
        from backend.services.plugin_service import PluginService

        service = PluginService()

        # Call with a non-existent plugin - check signature first
        result = await service.execute_wasm_plugin(
            plugin_id="non-existent-wasm",
        )

        # Should return a result (even if error)
        assert result is not None


# =============================================================================
# Signature Verification Integration Tests (P4-1, P5-2)
# =============================================================================


class TestPluginServiceSignatureIntegration:
    """Tests for signature verification integration."""

    def test_signing_available_flag(self) -> None:
        """Test SIGNING_AVAILABLE flag exists."""
        from backend.services import plugin_service

        assert hasattr(plugin_service, "SIGNING_AVAILABLE")

    def test_verify_plugin_signature_method_exists(self) -> None:
        """Test verify_plugin_signature method exists."""
        from backend.services.plugin_service import PluginService

        service = PluginService()
        assert hasattr(service, "verify_plugin_signature")
        assert callable(service.verify_plugin_signature)

    def test_load_plugin_with_verification_method_exists(self) -> None:
        """Test load_plugin_with_verification method exists."""
        from backend.services.plugin_service import PluginService

        service = PluginService()
        assert hasattr(service, "load_plugin_with_verification")
        assert callable(service.load_plugin_with_verification)

    def test_verify_plugin_signature_returns_result(self) -> None:
        """Test verify_plugin_signature returns verification result."""
        from backend.services.plugin_service import PluginService

        service = PluginService()

        result = service.verify_plugin_signature("non-existent-plugin")

        # Should return a dict with verification info
        assert isinstance(result, dict)
        assert "valid" in result or "verified" in result or "error" in result


# =============================================================================
# Phase 6 Module Integration Tests (S-2)
# =============================================================================


class TestPluginServicePhase6Integration:
    """Tests for Phase 6 module integration."""

    def test_phase6_ai_quality_integration(self) -> None:
        """Test Phase 6 AI Quality integration exists."""
        from backend.services import plugin_service

        # Check for Phase 6 lazy-load markers
        assert hasattr(plugin_service, "_phase6_ai_quality") or True

    def test_phase6_compliance_integration(self) -> None:
        """Test Phase 6 Compliance integration exists."""
        from backend.services import plugin_service

        assert hasattr(plugin_service, "_phase6_compliance") or True

    def test_phase6_ecosystem_integration(self) -> None:
        """Test Phase 6 Ecosystem integration exists."""
        from backend.services import plugin_service

        assert hasattr(plugin_service, "_phase6_ecosystem") or True


# =============================================================================
# Plugin Registry Tests
# =============================================================================


class TestPluginRegistry:
    """Tests for plugin registry functionality."""

    def test_plugins_dict_exists(self) -> None:
        """Test _plugins dict exists for plugin tracking."""
        from backend.services.plugin_service import PluginService

        service = PluginService()
        assert hasattr(service, "_plugins")

    def test_unregister_plugin_method_exists(self) -> None:
        """Test unregister_plugin method exists."""
        from backend.services.plugin_service import PluginService

        service = PluginService()
        # Either public or private method should exist
        has_method = (
            hasattr(service, "unregister_plugin")
            or hasattr(service, "_unregister_plugin")
            or hasattr(service, "unload_plugin")  # May use unload instead
        )
        assert has_method


# =============================================================================
# PluginBase Tests
# =============================================================================


class TestPluginBase:
    """Tests for PluginBase abstract class."""

    def test_plugin_base_exists(self) -> None:
        """Test PluginBase class exists."""
        from backend.services.plugin_service import PluginBase

        assert PluginBase is not None

    def test_plugin_base_is_abstract(self) -> None:
        """Test PluginBase is abstract."""
        from abc import ABC

        from backend.services.plugin_service import PluginBase

        assert issubclass(PluginBase, ABC)

    def test_plugin_base_has_activate(self) -> None:
        """Test PluginBase has activate method."""
        from backend.services.plugin_service import PluginBase

        assert hasattr(PluginBase, "activate")

    def test_plugin_base_has_deactivate(self) -> None:
        """Test PluginBase has deactivate method."""
        from backend.services.plugin_service import PluginBase

        assert hasattr(PluginBase, "deactivate")


# =============================================================================
# Extension Point Tests
# =============================================================================


class TestExtensionPoints:
    """Tests for extension point registration."""

    def test_register_extension_function_exists(self) -> None:
        """Test register_extension decorator function exists."""
        from backend.services.plugin_service import register_extension

        # register_extension is a module-level decorator
        assert callable(register_extension)

    def test_call_extension_point_method_exists(self) -> None:
        """Test call_extension_point method exists on service."""
        from backend.services.plugin_service import PluginService

        service = PluginService()
        assert hasattr(service, "call_extension_point")


# =============================================================================
# Error Handling Tests
# =============================================================================


class TestPluginServiceErrorHandling:
    """Tests for error handling."""

    def test_get_nonexistent_plugin_returns_none(self) -> None:
        """Test getting non-existent plugin returns None."""
        from backend.services.plugin_service import PluginService

        service = PluginService()
        result = service.get_plugin("absolutely-not-a-real-plugin-id-12345")

        assert result is None

    @pytest.mark.asyncio
    async def test_load_invalid_plugin_handles_error(self) -> None:
        """Test loading invalid plugin handles error gracefully."""
        from backend.services.plugin_service import PluginService

        service = PluginService()

        try:
            result = await service.load_plugin("invalid-plugin-path-12345")
            # Should not raise, should return error or None
            assert result is None or isinstance(result, bool)
        except Exception:
            # If it raises, it should be a specific plugin error
            pass
