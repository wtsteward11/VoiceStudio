"""
Tests for Plugin Sandbox

Phase 1: Tests for backend sandboxing and resource limits.
"""

import asyncio
import os
import subprocess
import sys
import tempfile
from pathlib import Path
from unittest.mock import MagicMock, patch

import pytest

from backend.services.plugin_sandbox import (
    PermissionViolation,
    PluginSandbox,
    ResourceLimitExceeded,
    ResourceLimits,
    SandboxManager,
    SandboxMetrics,
    SandboxPermissions,
    SandboxState,
    SandboxViolation,
    get_sandbox_manager,
)


def get_temp_path(subpath: str = "") -> Path:
    """Get platform-appropriate temporary path for tests."""
    base = Path(tempfile.gettempdir())
    if subpath:
        return base / subpath
    return base


class TestSandboxPermissions:
    """Tests for SandboxPermissions class."""

    def test_permissions_initialization(self):
        """Test SandboxPermissions initializes correctly."""
        perms = SandboxPermissions(
            plugin_id="test_plugin",
            granted_permissions={"filesystem.read", "network.http"},
            allowed_paths=[get_temp_path("test")],
            allowed_hosts=["example.com"],
            allowed_ports=[80, 443],
        )

        assert perms.plugin_id == "test_plugin"
        assert "filesystem.read" in perms.granted_permissions
        assert len(perms.allowed_paths) == 1
        assert len(perms.allowed_hosts) == 1

    def test_has_permission(self):
        """Test has_permission check."""
        perms = SandboxPermissions(
            plugin_id="test", granted_permissions={"filesystem.read", "audio.output"}
        )

        assert perms.has_permission("filesystem.read")
        assert perms.has_permission("audio.output")
        assert not perms.has_permission("filesystem.write")
        assert not perms.has_permission("network.http")

    def test_can_access_path_allowed(self):
        """Test path access when allowed."""
        allowed_path = get_temp_path("allowed")
        perms = SandboxPermissions(plugin_id="test", allowed_paths=[allowed_path])

        # Direct match
        assert perms.can_access_path(allowed_path)
        # Subdirectory
        assert perms.can_access_path(allowed_path / "subdir")

    def test_can_access_path_denied(self):
        """Test path access when not allowed."""
        allowed_path = get_temp_path("allowed")
        perms = SandboxPermissions(plugin_id="test", allowed_paths=[allowed_path])

        # Test sibling path (same parent, different folder)
        assert not perms.can_access_path(get_temp_path("other"))
        # Test completely different root on Windows, or /usr on Unix
        if sys.platform == "win32":
            assert not perms.can_access_path(Path("C:/Windows/System32"))
        else:
            assert not perms.can_access_path(Path("/usr/bin"))

    def test_can_access_network_with_permission(self):
        """Test network access with proper permissions."""
        perms = SandboxPermissions(
            plugin_id="test",
            granted_permissions={"network.http"},
            allowed_hosts=["api.example.com"],
            allowed_ports=[443],
        )

        assert perms.can_access_network("api.example.com", 443)
        assert not perms.can_access_network("api.example.com", 80)
        assert not perms.can_access_network("other.com", 443)

    def test_can_access_network_without_permission(self):
        """Test network access without outbound permission."""
        perms = SandboxPermissions(
            plugin_id="test",
            granted_permissions=set(),  # No network permission
            allowed_hosts=["api.example.com"],
            allowed_ports=[443],
        )

        assert not perms.can_access_network("api.example.com", 443)


class TestResourceLimits:
    """Tests for ResourceLimits class."""

    def test_default_limits(self):
        """Test default resource limits."""
        limits = ResourceLimits()

        assert limits.max_memory_mb == 512
        assert limits.max_cpu_seconds == 30.0
        assert limits.max_file_size_mb == 100
        assert limits.max_open_files == 64
        assert limits.max_processes == 4
        assert limits.execution_timeout_seconds == 60.0

    def test_custom_limits(self):
        """Test custom resource limits."""
        limits = ResourceLimits(
            max_memory_mb=256, max_cpu_seconds=10.0, execution_timeout_seconds=30.0
        )

        assert limits.max_memory_mb == 256
        assert limits.max_cpu_seconds == 10.0
        assert limits.execution_timeout_seconds == 30.0


class TestPluginSandbox:
    """Tests for PluginSandbox class."""

    @pytest.fixture
    def basic_permissions(self):
        """Create basic permissions for testing."""
        return SandboxPermissions(
            plugin_id="test_plugin",
            granted_permissions={"filesystem.read", "filesystem.write"},
            allowed_paths=[get_temp_path()],
        )

    @pytest.fixture
    def sandbox(self, basic_permissions):
        """Create a sandbox for testing."""
        return PluginSandbox(
            plugin_id="test_plugin",
            permissions=basic_permissions,
            limits=ResourceLimits(execution_timeout_seconds=5.0),
        )

    def test_sandbox_initialization(self, sandbox):
        """Test sandbox initializes correctly."""
        assert sandbox.plugin_id == "test_plugin"
        assert sandbox.state == SandboxState.IDLE
        assert sandbox.limits.execution_timeout_seconds == 5.0

    def test_check_file_permission_allowed(self, sandbox):
        """Test file permission check when allowed."""
        # Should not raise
        sandbox.check_file_permission(get_temp_path("test.txt"), "read")
        sandbox.check_file_permission(get_temp_path("test.txt"), "write")

    def test_check_file_permission_denied_path(self, sandbox):
        """Test file permission check when path not allowed."""
        # Use a path that's definitely outside temp dir
        denied_path = Path(os.path.expanduser("~")) / ".bashrc"
        with pytest.raises(PermissionViolation) as exc_info:
            sandbox.check_file_permission(denied_path, "read")

        assert "path not allowed" in str(exc_info.value)
        assert len(sandbox.get_violations()) == 1

    def test_check_file_permission_denied_path_above_allowed(self):
        """Test that path above allowed directory is denied (path traversal)."""
        allowed_dir = get_temp_path("allowed_subdir")
        allowed_dir.mkdir(parents=True, exist_ok=True)
        parent_of_allowed = allowed_dir.parent  # Path above allowed_dir
        perms = SandboxPermissions(
            plugin_id="test",
            granted_permissions={"filesystem.read", "filesystem.write"},
            allowed_paths=[allowed_dir],
        )
        sandbox = PluginSandbox("test", perms)
        with pytest.raises(PermissionViolation) as exc_info:
            sandbox.check_file_permission(parent_of_allowed, "read")
        assert "path not allowed" in str(exc_info.value)

    def test_check_file_permission_denied_operation(self):
        """Test file permission check when operation not permitted."""
        perms = SandboxPermissions(
            plugin_id="test",
            granted_permissions={"filesystem.read"},  # No write
            allowed_paths=[get_temp_path()],
        )
        sandbox = PluginSandbox("test", perms)

        with pytest.raises(PermissionViolation) as exc_info:
            sandbox.check_file_permission(get_temp_path("test.txt"), "write")

        assert "filesystem.write" in str(exc_info.value)

    def test_check_network_permission_denied(self, sandbox):
        """Test network permission check when not permitted."""
        with pytest.raises(PermissionViolation):
            sandbox.check_network_permission("example.com", 443)

    def test_execute_context_lifecycle(self, sandbox):
        """Test execute context manages state correctly."""
        assert sandbox.state == SandboxState.IDLE

        with sandbox.execute_context() as workspace:
            assert sandbox.state == SandboxState.RUNNING
            assert workspace.exists()
            assert workspace.is_dir()

        assert sandbox.state == SandboxState.IDLE

    def test_execute_context_error_state(self, sandbox):
        """Test execute context sets error state on exception."""
        with pytest.raises(ValueError):
            with sandbox.execute_context():
                assert sandbox.state == SandboxState.RUNNING
                raise ValueError("Test error")

        assert sandbox.state == SandboxState.ERROR

    def test_execute_context_invalid_state(self, sandbox):
        """Test execute context fails if not idle."""
        sandbox.state = SandboxState.RUNNING

        with pytest.raises(SandboxViolation) as exc_info:
            with sandbox.execute_context():
                pass

        assert "invalid state" in str(exc_info.value)

    @pytest.mark.asyncio
    async def test_execute_async_success(self, sandbox):
        """Test async execution succeeds."""

        def simple_func():
            return 42

        result = await sandbox.execute_async(simple_func)
        assert result == 42

    @pytest.mark.asyncio
    async def test_execute_async_timeout(self):
        """Test async execution timeout."""
        perms = SandboxPermissions(plugin_id="test")
        sandbox = PluginSandbox("test", perms, ResourceLimits(execution_timeout_seconds=0.1))

        def slow_func():
            import time

            time.sleep(1.0)
            return "done"

        with pytest.raises(ResourceLimitExceeded):
            await sandbox.execute_async(slow_func)

    def test_metrics_tracking(self, sandbox):
        """Test metrics are tracked during execution."""
        with sandbox.execute_context():
            # Simulate some operations
            sandbox.check_file_permission(get_temp_path("test.txt"), "read")
            sandbox.check_file_permission(get_temp_path("test2.txt"), "read")

        metrics = sandbox.get_metrics()
        assert metrics.file_operations == 2
        assert metrics.wall_time >= 0

    def test_terminate(self, sandbox):
        """Test sandbox termination."""
        sandbox.terminate()
        assert sandbox.state == SandboxState.TERMINATED

    def test_monitor_windows_process_finished_process(self):
        """Test _monitor_windows_process does not raise when process has already exited."""
        perms = SandboxPermissions(plugin_id="test", granted_permissions={"system.process"})
        sandbox = PluginSandbox("test", perms)
        proc = subprocess.Popen(
            [sys.executable, "-c", "pass"],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )
        proc.wait()
        sandbox._monitor_windows_process(proc)
        assert proc.poll() is not None

    def test_execute_subprocess_requires_system_process_permission(self):
        """Test execute_subprocess raises without system.process permission."""
        perms = SandboxPermissions(
            plugin_id="test",
            granted_permissions={"filesystem.read"},
            allowed_paths=[get_temp_path()],
        )
        sandbox = PluginSandbox("test", perms)
        with pytest.raises(PermissionViolation) as exc_info:
            sandbox.execute_subprocess([sys.executable, "-c", "pass"])
        assert "system.process" in str(exc_info.value)


class TestSandboxManager:
    """Tests for SandboxManager class."""

    @pytest.fixture
    def manager(self):
        """Create a sandbox manager for testing."""
        return SandboxManager()

    def test_create_sandbox(self, manager):
        """Test creating a sandbox."""
        perms = SandboxPermissions(plugin_id="plugin1")
        sandbox = manager.create_sandbox("plugin1", perms)

        assert sandbox is not None
        assert sandbox.plugin_id == "plugin1"

    def test_get_sandbox(self, manager):
        """Test getting an existing sandbox."""
        perms = SandboxPermissions(plugin_id="plugin1")
        manager.create_sandbox("plugin1", perms)

        sandbox = manager.get_sandbox("plugin1")
        assert sandbox is not None
        assert sandbox.plugin_id == "plugin1"

    def test_get_nonexistent_sandbox(self, manager):
        """Test getting a non-existent sandbox returns None."""
        sandbox = manager.get_sandbox("nonexistent")
        assert sandbox is None

    def test_destroy_sandbox(self, manager):
        """Test destroying a sandbox."""
        perms = SandboxPermissions(plugin_id="plugin1")
        manager.create_sandbox("plugin1", perms)

        manager.destroy_sandbox("plugin1")

        assert manager.get_sandbox("plugin1") is None

    def test_destroy_all(self, manager):
        """Test destroying all sandboxes."""
        perms1 = SandboxPermissions(plugin_id="plugin1")
        perms2 = SandboxPermissions(plugin_id="plugin2")
        manager.create_sandbox("plugin1", perms1)
        manager.create_sandbox("plugin2", perms2)

        manager.destroy_all()

        assert manager.get_sandbox("plugin1") is None
        assert manager.get_sandbox("plugin2") is None

    def test_set_default_limits(self, manager):
        """Test setting default limits."""
        limits = ResourceLimits(max_memory_mb=256)
        manager.set_default_limits(limits)

        perms = SandboxPermissions(plugin_id="plugin1")
        sandbox = manager.create_sandbox("plugin1", perms)

        assert sandbox.limits.max_memory_mb == 256

    def test_get_all_metrics(self, manager):
        """Test getting metrics for all sandboxes."""
        perms1 = SandboxPermissions(plugin_id="plugin1")
        perms2 = SandboxPermissions(plugin_id="plugin2")
        manager.create_sandbox("plugin1", perms1)
        manager.create_sandbox("plugin2", perms2)

        metrics = manager.get_all_metrics()

        assert "plugin1" in metrics
        assert "plugin2" in metrics


class TestGetSandboxManager:
    """Tests for global sandbox manager."""

    def test_get_sandbox_manager_singleton(self):
        """Test get_sandbox_manager returns singleton."""
        m1 = get_sandbox_manager()
        m2 = get_sandbox_manager()

        assert m1 is m2
