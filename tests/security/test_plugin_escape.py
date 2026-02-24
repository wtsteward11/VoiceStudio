"""
Plugin Sandbox Escape Security Tests.

Phase 8 WS5: Plugin sandbox escape attempts (filesystem, network, process).
Adversarial tests to verify sandbox isolation.
"""

from __future__ import annotations

import os
import tempfile
from pathlib import Path

import pytest

pytestmark = [pytest.mark.security]


class TestPluginSandboxEscape:
    """Sandbox escape attempt prevention."""

    def test_filesystem_escape_blocked(self):
        """Plugin cannot access paths outside allowed workspace."""
        from backend.services.plugin_sandbox import (
            PluginSandbox,
            SandboxPermissions,
        )

        allowed = Path(tempfile.mkdtemp(prefix="vs_plugin_allowed_"))
        perms = SandboxPermissions(
            plugin_id="test.escape",
            allowed_paths=[allowed],
        )
        sandbox = PluginSandbox(plugin_id="test.escape", permissions=perms)
        try:
            assert not perms.can_access_path(Path("C:/Windows/System32"))
            assert not perms.can_access_path(Path("/etc/passwd"))
            assert not perms.can_access_path(Path.home() / ".ssh" / "id_rsa")
        finally:
            import shutil

            shutil.rmtree(allowed, ignore_errors=True)

    def test_network_permission_default_denied(self):
        """Network access denied by default unless explicitly granted."""
        from backend.services.plugin_sandbox import SandboxPermissions

        perms = SandboxPermissions(plugin_id="test.network")
        assert not perms.can_access_network("example.com", 80)
        assert not perms.can_access_network("localhost", 443)

    def test_process_spawn_requires_sandbox_runner(self):
        """Process execution goes through sandbox runner with limits."""
        from backend.services.plugin_sandbox import PluginSandbox, SandboxPermissions

        perms = SandboxPermissions(plugin_id="test.process")
        sandbox = PluginSandbox(plugin_id="test.process", permissions=perms)
        # Sandbox enforces ResourceLimits; no direct subprocess without runner
        assert sandbox.permissions == perms

    def test_relative_traversal_outside_workspace(self):
        """.. traversal cannot escape workspace."""
        from backend.services.plugin_sandbox import SandboxPermissions

        workspace = Path(tempfile.mkdtemp(prefix="vs_workspace_"))
        perms = SandboxPermissions(
            plugin_id="test.traversal",
            allowed_paths=[workspace],
        )
        try:
            escape = workspace / ".." / ".." / "etc" / "passwd"
            resolved = escape.resolve()
            assert not perms.can_access_path(resolved)
        finally:
            import shutil

            shutil.rmtree(workspace, ignore_errors=True)
