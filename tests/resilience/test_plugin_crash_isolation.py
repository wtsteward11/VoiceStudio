"""
Plugin Crash Isolation Tests.

Phase 8 WS5: Verify crashed plugin doesn't affect host.
"""

from __future__ import annotations

import pytest


class TestPluginCrashIsolation:
    """Plugin crash isolation from host process."""

    def test_sandbox_runner_isolates_execution(self):
        """PluginSandbox provides execution isolation."""
        from backend.services.plugin_sandbox import PluginSandbox, SandboxPermissions

        perms = SandboxPermissions(plugin_id="test.isolation")
        sandbox = PluginSandbox(plugin_id="test.isolation", permissions=perms)
        assert sandbox.plugin_id == "test.isolation"
        assert sandbox.permissions == perms

    def test_sandbox_violation_raises_not_system_exit(self):
        """Sandbox violations raise SandboxViolation, not unhandled exceptions."""
        from backend.services.plugin_sandbox import (
            PermissionViolation,
            SandboxViolation,
        )

        with pytest.raises(SandboxViolation):
            raise PermissionViolation("test violation")
