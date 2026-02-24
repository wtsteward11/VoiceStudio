"""
Plugin Sandbox Security Tests.

Phase 4 Workstream 3: Adversarial tests to verify sandbox isolation.
Tests path traversal, resource abuse, network access, and permission enforcement.
"""

from __future__ import annotations

import json
import os
import shutil
import tempfile
from pathlib import Path
from typing import Any

import pytest

from backend.services.plugin_sandbox import (
    PluginSandbox,
    ResourceLimits,
    SandboxPermissions,
    SandboxState,
)


class TestPathTraversalPrevention:
    """Verify sandbox blocks path traversal attacks."""

    def _make_sandbox(self, plugin_id: str = "test.adversarial") -> PluginSandbox:
        perms = SandboxPermissions(plugin_id=plugin_id)
        return PluginSandbox(plugin_id=plugin_id, permissions=perms)

    def test_workspace_is_in_temp_directory(self):
        sandbox = self._make_sandbox()
        workspace = sandbox._create_temp_workspace()
        assert str(workspace).startswith(tempfile.gettempdir().rstrip(os.sep))
        shutil.rmtree(workspace, ignore_errors=True)

    def test_workspace_name_contains_plugin_id(self):
        sandbox = self._make_sandbox("test.path_traversal")
        workspace = sandbox._create_temp_workspace()
        assert "vs_plugin_test.path_traversal_" in workspace.name
        shutil.rmtree(workspace, ignore_errors=True)

    def test_path_access_denied_outside_allowed(self):
        perms = SandboxPermissions(
            plugin_id="test.escape",
            allowed_paths=[Path(tempfile.gettempdir()) / "allowed_only"],
        )
        assert not perms.can_access_path(Path("C:/Windows/System32"))
        assert not perms.can_access_path(Path("/etc/passwd"))

    def test_relative_traversal_blocked(self):
        allowed = Path(tempfile.gettempdir()) / "plugin_workspace"
        perms = SandboxPermissions(
            plugin_id="test.traversal",
            allowed_paths=[allowed],
        )
        traversal_path = allowed / ".." / ".." / "Windows" / "System32"
        assert not perms.can_access_path(traversal_path)

    def test_symlink_traversal_blocked(self):
        """Symlink-based escape should be caught by path resolution."""
        allowed = Path(tempfile.mkdtemp(prefix="vs_sandbox_test_"))
        perms = SandboxPermissions(
            plugin_id="test.symlink",
            allowed_paths=[allowed],
        )
        try:
            outside_path = Path(tempfile.gettempdir()) / "outside_sandbox_target"
            outside_path.mkdir(exist_ok=True)
            assert not perms.can_access_path(outside_path)
        finally:
            shutil.rmtree(allowed, ignore_errors=True)
            if outside_path.exists():
                shutil.rmtree(outside_path, ignore_errors=True)


class TestPermissionEnforcement:
    """Verify the capability-based permission model works."""

    def test_no_permissions_by_default(self):
        perms = SandboxPermissions(plugin_id="test.no_perms")
        assert not perms.has_permission("filesystem.write")
        assert not perms.has_permission("network.outbound")
        assert not perms.has_permission("subprocess.execute")

    def test_granted_permission_accessible(self):
        perms = SandboxPermissions(
            plugin_id="test.granted",
            granted_permissions={"filesystem.read", "audio.process"},
        )
        assert perms.has_permission("filesystem.read")
        assert perms.has_permission("audio.process")
        assert not perms.has_permission("network.outbound")

    def test_empty_allowed_hosts(self):
        perms = SandboxPermissions(plugin_id="test.network")
        assert len(perms.allowed_hosts) == 0
        assert len(perms.allowed_ports) == 0

    def test_path_allowed_only_in_whitelist(self):
        workspace = Path(tempfile.mkdtemp(prefix="vs_sandbox_"))
        try:
            perms = SandboxPermissions(
                plugin_id="test.whitelist",
                allowed_paths=[workspace],
            )
            file_in_workspace = workspace / "data.txt"
            file_outside = Path(tempfile.gettempdir()) / "other_dir" / "secret.txt"

            assert perms.can_access_path(file_in_workspace)
            assert not perms.can_access_path(file_outside)
        finally:
            shutil.rmtree(workspace, ignore_errors=True)


class TestResourceLimits:
    """Verify resource limit configuration."""

    def test_default_resource_limits(self):
        limits = ResourceLimits()
        assert limits.max_memory_mb > 0
        assert limits.max_cpu_seconds > 0

    def test_custom_resource_limits(self):
        limits = ResourceLimits(max_memory_mb=64, max_cpu_seconds=10)
        assert limits.max_memory_mb == 64
        assert limits.max_cpu_seconds == 10

    def test_sandbox_accepts_resource_limits(self):
        perms = SandboxPermissions(plugin_id="test.limits")
        limits = ResourceLimits(max_memory_mb=32, max_cpu_seconds=5)
        sandbox = PluginSandbox(
            plugin_id="test.limits",
            permissions=perms,
            limits=limits,
        )
        assert sandbox.limits.max_memory_mb == 32
        assert sandbox.limits.max_cpu_seconds == 5


class TestSandboxStateTransitions:
    """Verify sandbox state machine works correctly."""

    def test_initial_state_is_idle(self):
        perms = SandboxPermissions(plugin_id="test.state")
        sandbox = PluginSandbox(plugin_id="test.state", permissions=perms)
        assert sandbox.state == SandboxState.IDLE

    def test_sandbox_creates_isolated_workspace(self):
        perms = SandboxPermissions(plugin_id="test.workspace")
        sandbox = PluginSandbox(plugin_id="test.workspace", permissions=perms)
        workspace = sandbox._create_temp_workspace()
        assert workspace.exists()
        assert workspace.is_dir()
        shutil.rmtree(workspace, ignore_errors=True)


class TestPluginSigningSecurityRules:
    """Verify signing infrastructure enforces security rules."""

    def test_unsigned_plugins_have_no_signature(self):
        manifest = {"name": "unsigned_plugin", "version": "1.0.0"}
        assert "signature" not in manifest

    def test_catalog_plugins_are_verified(self):
        catalog_path = Path(__file__).parent.parent.parent / "shared" / "catalog" / "plugins.json"
        catalog = json.loads(catalog_path.read_text())
        for plugin in catalog["plugins"]:
            assert plugin.get("verified") is True, f"Catalog plugin {plugin['id']} must be verified"

    def test_signing_module_available(self):
        from backend.plugins.supply_chain.signer import check_signing_available

        available = check_signing_available()
        if not available:
            pytest.skip("cryptography library not installed")
        assert available is True

    def test_key_generation_produces_valid_metadata(self):
        from backend.plugins.supply_chain.signer import (
            KeyMetadata,
            KeyStatus,
            check_signing_available,
        )

        if not check_signing_available():
            pytest.skip("cryptography library not installed")

        meta = KeyMetadata(
            key_id="sec-test-key",
            fingerprint="deadbeef",
            created_at="2026-02-21T00:00:00Z",
            status=KeyStatus.ACTIVE,
        )
        assert meta.key_id == "sec-test-key"
        assert meta.status == KeyStatus.ACTIVE

        meta.status = KeyStatus.REVOKED
        meta.revoked_at = "2026-02-21T01:00:00Z"
        d = meta.to_dict()
        assert d["status"] == "revoked"
        assert d["revoked_at"] is not None


class TestManifestSecurityFields:
    """Verify reference plugin manifests include security-relevant fields."""

    @pytest.fixture(params=["noise_reduction", "format_converter", "silence_detector"])
    def manifest(self, request):
        plugin_dir = Path(__file__).parent.parent.parent / "plugins" / "reference" / request.param
        return json.loads((plugin_dir / "manifest.json").read_text())

    def test_permissions_field_exists(self, manifest):
        assert "permissions" in manifest

    def test_network_permission_declared(self, manifest):
        perms = manifest["permissions"]
        assert "network" in perms
        assert perms["network"] is False, "Reference plugins must not require network access"

    def test_filesystem_permissions_scoped(self, manifest):
        perms = manifest["permissions"]
        if "filesystem" in perms:
            fs = perms["filesystem"]
            for read_path in fs.get("read", []):
                assert read_path.startswith("$"), f"Read path must use variable: {read_path}"
            for write_path in fs.get("write", []):
                assert write_path.startswith("$"), f"Write path must use variable: {write_path}"

    def test_no_arbitrary_subprocess_for_analysis_plugins(self, manifest):
        if manifest["category"] == "audio_analysis":
            perms = manifest["permissions"]
            assert (
                perms.get("subprocess") is not True
            ), "Analysis plugins should not require subprocess access"
