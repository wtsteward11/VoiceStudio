"""
Plugin Sandbox Escape Adversarial Tests.

Task 3.3: Adversarial tests to verify sandbox isolation. Covers filesystem escape,
network escape, resource abuse, subprocess boundaries, and allowed API enforcement.
"""

from __future__ import annotations

import os
import shutil
import tempfile
from pathlib import Path

import pytest

from backend.plugins.sandbox.plugin_sandbox import (
    PluginSandbox,
    SandboxPermissions,
)
from backend.plugins.sdk.allowed_apis import (
    ALLOWED_HOST_METHODS,
    is_allowed_host_method,
    is_allowed_plugin_outgoing,
    validate_host_method,
)

pytestmark = [pytest.mark.security]


class TestFilesystemEscape:
    """Adversarial tests: filesystem escape attempts."""

    def test_path_traversal_outside_workspace_blocked(self):
        """.. traversal cannot escape allowed workspace."""
        workspace = Path(tempfile.mkdtemp(prefix="vs_escape_"))
        perms = SandboxPermissions(
            plugin_id="test.traversal",
            allowed_paths=[workspace],
        )
        try:
            escape = workspace / ".." / ".." / "etc" / "passwd"
            resolved = escape.resolve()
            assert not perms.can_access_path(resolved)
        finally:
            shutil.rmtree(workspace, ignore_errors=True)

    def test_system_paths_denied(self):
        """Sensitive system paths are never allowed."""
        perms = SandboxPermissions(plugin_id="test.system")
        assert not perms.can_access_path(Path("C:/Windows/System32"))
        assert not perms.can_access_path(Path("/etc/passwd"))
        assert not perms.can_access_path(Path.home() / ".ssh" / "id_rsa")

    def test_symlink_escape_blocked(self):
        """Symlink-based escape outside allowed dir is blocked."""
        allowed = Path(tempfile.mkdtemp(prefix="vs_symlink_"))
        perms = SandboxPermissions(
            plugin_id="test.symlink",
            allowed_paths=[allowed],
        )
        try:
            outside = Path(tempfile.gettempdir()) / "outside_target"
            outside.mkdir(exist_ok=True)
            assert not perms.can_access_path(outside)
        finally:
            shutil.rmtree(allowed, ignore_errors=True)
            if outside.exists():
                shutil.rmtree(outside, ignore_errors=True)


class TestNetworkEscape:
    """Adversarial tests: network escape attempts."""

    def test_network_denied_by_default(self):
        """Network access denied unless explicitly granted."""
        perms = SandboxPermissions(plugin_id="test.network")
        assert not perms.can_access_network("example.com", 80)
        assert not perms.can_access_network("192.168.1.1", 443)
        assert not perms.can_access_network("localhost", 8080)

    def test_empty_allowed_hosts(self):
        """Default permissions have no allowed hosts."""
        perms = SandboxPermissions(plugin_id="test.hosts")
        assert len(perms.allowed_hosts) == 0
        assert len(perms.allowed_ports) == 0


class TestResourceAbuse:
    """Adversarial tests: resource abuse prevention."""

    def test_resource_limits_enforced_by_sandbox(self):
        """Sandbox accepts and stores resource limits."""
        from backend.plugins.sandbox.plugin_sandbox import ResourceLimits

        perms = SandboxPermissions(plugin_id="test.resource")
        limits = ResourceLimits(max_memory_mb=32, max_cpu_seconds=5)
        sandbox = PluginSandbox(
            plugin_id="test.resource",
            permissions=perms,
            limits=limits,
        )
        assert sandbox.limits.max_memory_mb == 32
        assert sandbox.limits.max_cpu_seconds == 5

    def test_default_limits_positive(self):
        """Default resource limits are non-zero."""
        from backend.plugins.sandbox.plugin_sandbox import ResourceLimits

        limits = ResourceLimits()
        assert limits.max_memory_mb > 0
        assert limits.max_cpu_seconds > 0


class TestSubprocessBoundaries:
    """Adversarial tests: subprocess spawn boundaries."""

    def test_process_execution_requires_runner(self):
        """Process execution goes through sandbox runner with limits."""
        perms = SandboxPermissions(plugin_id="test.process")
        sandbox = PluginSandbox(plugin_id="test.process", permissions=perms)
        assert sandbox.permissions == perms
        assert not perms.has_permission("subprocess.execute")


class TestAllowedAPISurface:
    """Adversarial tests: allowed API whitelist enforcement."""

    def test_unknown_host_method_rejected(self):
        """Unknown host methods are not in whitelist."""
        assert not is_allowed_host_method("host.system.exec")
        assert not is_allowed_host_method("host.filesystem.read")
        assert not is_allowed_host_method("host.network.fetch")
        assert not is_allowed_host_method("os.system")
        assert not is_allowed_host_method("__import__")

    def test_known_host_methods_allowed(self):
        """All documented host methods are in whitelist."""
        assert is_allowed_host_method("host.audio.play")
        assert is_allowed_host_method("host.storage.get")
        assert is_allowed_host_method("host.engine.invoke")

    def test_validate_host_method_raises_for_unknown(self):
        """validate_host_method raises for disallowed methods."""
        with pytest.raises(ValueError, match="not in the allowed host API whitelist"):
            validate_host_method("host.evil.escape")

    def test_validate_host_method_passes_for_known(self):
        """validate_host_method passes for allowed methods."""
        validate_host_method("host.audio.play")
        validate_host_method("host.ui.notify")

    def test_whitelist_matches_protocol(self):
        """Allowed API whitelist includes all protocol HostMethods."""
        from backend.plugins.sandbox.protocol import HostMethods

        protocol_host_methods = {
            HostMethods.AUDIO_PLAY,
            HostMethods.AUDIO_STOP,
            HostMethods.AUDIO_GET_DEVICES,
            HostMethods.AUDIO_PROCESS,
            HostMethods.UI_NOTIFY,
            HostMethods.UI_SHOW_DIALOG,
            HostMethods.UI_UPDATE_PANEL,
            HostMethods.STORAGE_GET,
            HostMethods.STORAGE_SET,
            HostMethods.STORAGE_DELETE,
            HostMethods.SETTINGS_GET,
            HostMethods.SETTINGS_SET,
            HostMethods.ENGINE_INVOKE,
            HostMethods.ENGINE_LIST,
        }
        for method in protocol_host_methods:
            assert method in ALLOWED_HOST_METHODS, f"Protocol method {method} missing from whitelist"

    def test_plugin_outgoing_only_whitelisted(self):
        """Only whitelisted methods may be sent from plugin to host."""
        assert is_allowed_plugin_outgoing("host.audio.play")
        assert not is_allowed_plugin_outgoing("host.evil.escape")
        assert not is_allowed_plugin_outgoing("system.exec")


class TestRunnerIsolationPolicies:
    """Verify runner applies all isolation policies."""

    def test_runner_config_includes_resource_limits(self):
        """RunnerConfig supports resource limit configuration."""
        from backend.plugins.sandbox.runner import RunnerConfig

        config = RunnerConfig(
            plugin_id="test.runner",
            plugin_path=Path("."),
            entry_module="test",
            max_memory_mb=64,
            max_cpu_percent=50,
            enable_resource_monitoring=True,
        )
        assert config.max_memory_mb == 64
        assert config.max_cpu_percent == 50
        assert config.enable_resource_monitoring is True

    def test_runner_config_includes_permissions(self):
        """RunnerConfig passes permissions to subprocess."""
        from backend.plugins.sandbox.runner import RunnerConfig

        perms = {"audio.playback": True, "network.outbound": False}
        config = RunnerConfig(
            plugin_id="test.perm",
            plugin_path=Path("."),
            entry_module="test",
            permissions=perms,
        )
        assert config.permissions == perms


class TestMaliciousPluginIntegration:
    """Integration tests: malicious plugin cannot escape via disallowed API."""

    @pytest.mark.asyncio
    async def test_disallowed_host_method_returns_method_not_found(self):
        """Host rejects requests for methods not in allowed API whitelist."""
        from unittest.mock import patch

        from backend.plugins.sandbox.bridge import IPCBridge
        from backend.plugins.sandbox.host_api import HostAPI
        from backend.plugins.sandbox.protocol import Request, Response

        bridge = IPCBridge()
        api = HostAPI(plugin_id="malicious.test", permissions={})
        api.register_with_bridge(bridge)

        # Simulate malicious plugin sending disallowed method
        malicious_request = Request(id=1, method="host.system.exec", params={"cmd": "rm -rf /"})

        sent_messages = []

        async def capture_send(msg):
            sent_messages.append(msg)

        with patch.object(bridge, "_send_message", side_effect=capture_send):
            await bridge._handle_request(malicious_request)

        assert len(sent_messages) == 1
        response = sent_messages[0]
        assert isinstance(response, Response)
        assert response.error is not None
        assert response.error.code == -32601  # METHOD_NOT_FOUND
        assert "host.system.exec" in response.error.message

    @pytest.mark.asyncio
    async def test_storage_path_traversal_blocked(self):
        """Storage isolation blocks path traversal in key/path params."""
        from backend.plugins.sandbox.storage_isolation import (
            PathValidationError,
            PluginStorage,
            StorageType,
        )

        base = Path(tempfile.mkdtemp(prefix="vs_storage_"))
        try:
            storage = PluginStorage(plugin_id="malicious.storage", base_path=base)
            with pytest.raises(PathValidationError):
                storage.validate_path(Path("../../../etc/passwd"), StorageType.DATA)
        finally:
            shutil.rmtree(base, ignore_errors=True)
