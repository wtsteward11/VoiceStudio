# VoiceStudio Plugin Sandbox
# Phase 1: Backend sandboxing for plugin execution

import asyncio
import logging
import os
import subprocess
import sys
import tempfile
import threading
import time
from contextlib import contextmanager
from dataclasses import dataclass, field
from enum import Enum
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional, Set

# resource module is Unix-only; provide fallback for Windows
try:
    import resource

    HAS_RESOURCE_MODULE = True
except ImportError:
    resource = None  # type: ignore
    HAS_RESOURCE_MODULE = False

# psutil for Windows resource monitoring (optional)
try:
    import psutil

    HAS_PSUTIL = True
except ImportError:
    psutil = None  # type: ignore
    HAS_PSUTIL = False

logger = logging.getLogger(__name__)


class SandboxViolation(Exception):
    """Raised when a plugin violates sandbox restrictions."""

    pass


class ResourceLimitExceeded(SandboxViolation):
    """Raised when a plugin exceeds resource limits."""

    pass


class PermissionViolation(SandboxViolation):
    """Raised when a plugin attempts an unpermitted operation."""

    pass


class SandboxState(str, Enum):
    """State of a plugin sandbox."""

    IDLE = "idle"
    RUNNING = "running"
    SUSPENDED = "suspended"
    TERMINATED = "terminated"
    ERROR = "error"


@dataclass
class ResourceLimits:
    """Resource limits for sandboxed plugin execution."""

    max_memory_mb: int = 512
    max_cpu_seconds: float = 30.0
    max_cpu_percent: float = 100.0  # Windows: terminate if CPU exceeds this (0-100)
    max_file_size_mb: int = 100
    max_open_files: int = 64
    max_processes: int = 4
    max_network_connections: int = 10
    execution_timeout_seconds: float = 60.0


@dataclass
class SandboxPermissions:
    """Permissions granted to a sandboxed plugin."""

    plugin_id: str
    granted_permissions: Set[str] = field(default_factory=set)
    allowed_paths: List[Path] = field(default_factory=list)
    allowed_hosts: List[str] = field(default_factory=list)
    allowed_ports: List[int] = field(default_factory=list)

    def has_permission(self, permission: str) -> bool:
        """Check if permission is granted."""
        return permission in self.granted_permissions

    def can_access_path(self, path: Path) -> bool:
        """Check if path access is allowed. Target must be the allowed dir or under it."""
        resolved = path.resolve()
        return any(
            resolved == allowed or allowed in resolved.parents for allowed in self.allowed_paths
        )

    def can_access_network(self, host: str, port: int) -> bool:
        """Check if network access is allowed."""
        if not (self.has_permission("network.http") or self.has_permission("network.websocket")):
            return False
        if self.allowed_hosts and host not in self.allowed_hosts:
            return False
        return not (self.allowed_ports and port not in self.allowed_ports)


@dataclass
class SandboxMetrics:
    """Metrics collected during sandbox execution."""

    start_time: float = 0.0
    end_time: float = 0.0
    cpu_time: float = 0.0
    peak_memory_mb: float = 0.0
    file_operations: int = 0
    network_operations: int = 0
    violations: List[str] = field(default_factory=list)

    @property
    def wall_time(self) -> float:
        """Total wall clock time."""
        if self.end_time > 0 and self.start_time > 0:
            return self.end_time - self.start_time
        return 0.0


class PluginSandbox:
    """
    Sandbox for executing plugin code with resource limits and permission checks.

    Uses a combination of:
    - Resource limits (memory, CPU, file descriptors)
    - Path restrictions
    - Permission-based capability checks
    """

    def __init__(
        self,
        plugin_id: str,
        permissions: SandboxPermissions,
        limits: Optional[ResourceLimits] = None,
    ):
        self.plugin_id = plugin_id
        self.permissions = permissions
        self.limits = limits or ResourceLimits()
        self.state = SandboxState.IDLE
        self.metrics = SandboxMetrics()
        self._lock = threading.Lock()
        self._process: Optional[subprocess.Popen] = None
        self._temp_dir: Optional[Path] = None

    def _create_temp_workspace(self) -> Path:
        """Create isolated temporary workspace for plugin."""
        self._temp_dir = Path(tempfile.mkdtemp(prefix=f"vs_plugin_{self.plugin_id}_"))
        logger.debug(f"Created sandbox workspace: {self._temp_dir}")
        return self._temp_dir

    def _cleanup_temp_workspace(self) -> None:
        """Clean up temporary workspace."""
        if self._temp_dir and self._temp_dir.exists():
            import shutil

            try:
                shutil.rmtree(self._temp_dir)
                logger.debug(f"Cleaned up sandbox workspace: {self._temp_dir}")
            except Exception as e:
                logger.warning(f"Failed to cleanup sandbox workspace: {e}")

    def _apply_resource_limits(self) -> None:
        """Apply resource limits using OS mechanisms (Unix only)."""
        if not HAS_RESOURCE_MODULE or sys.platform == "win32":
            # Windows doesn't support resource module
            # Limits enforced via monitoring instead
            logger.debug(
                f"Resource limits for plugin {self.plugin_id} will be "
                "enforced via monitoring (resource module unavailable)"
            )
            return

        try:
            # Memory limit
            memory_bytes = self.limits.max_memory_mb * 1024 * 1024
            resource.setrlimit(resource.RLIMIT_AS, (memory_bytes, memory_bytes))  # type: ignore

            # CPU time limit
            cpu_seconds = int(self.limits.max_cpu_seconds)
            resource.setrlimit(resource.RLIMIT_CPU, (cpu_seconds, cpu_seconds))  # type: ignore

            # File size limit
            file_bytes = self.limits.max_file_size_mb * 1024 * 1024
            resource.setrlimit(resource.RLIMIT_FSIZE, (file_bytes, file_bytes))  # type: ignore

            # Open files limit
            resource.setrlimit(  # type: ignore
                resource.RLIMIT_NOFILE,  # type: ignore
                (self.limits.max_open_files, self.limits.max_open_files),
            )

            # Process limit
            resource.setrlimit(  # type: ignore
                resource.RLIMIT_NPROC,  # type: ignore
                (self.limits.max_processes, self.limits.max_processes),
            )

            logger.debug(f"Applied resource limits for plugin {self.plugin_id}")

        except (ValueError, OSError) as e:
            # OSError covers resource.error on Unix
            logger.warning(f"Could not apply some resource limits: {e}")

    def check_file_permission(self, path: Path, operation: str) -> None:
        """
        Check if file operation is permitted.

        Args:
            path: Target path
            operation: 'read', 'write', 'execute', 'delete'

        Raises:
            PermissionViolation: If operation is not permitted
        """
        permission_map = {
            "read": "filesystem.read",
            "write": "filesystem.write",
            "execute": "filesystem.execute",
            "delete": "filesystem.write",  # Delete requires write
        }

        required_permission = permission_map.get(operation)
        if not required_permission:
            raise PermissionViolation(f"Unknown file operation: {operation}")

        if not self.permissions.has_permission(required_permission):
            violation = f"Plugin {self.plugin_id} denied {operation} on {path}: missing {required_permission}"
            self.metrics.violations.append(violation)
            raise PermissionViolation(violation)

        if not self.permissions.can_access_path(path):
            violation = f"Plugin {self.plugin_id} denied {operation} on {path}: path not allowed"
            self.metrics.violations.append(violation)
            raise PermissionViolation(violation)

        self.metrics.file_operations += 1

    def check_network_permission(self, host: str, port: int) -> None:
        """
        Check if network operation is permitted.

        Raises:
            PermissionViolation: If operation is not permitted
        """
        if not self.permissions.can_access_network(host, port):
            violation = f"Plugin {self.plugin_id} denied network access to {host}:{port}"
            self.metrics.violations.append(violation)
            raise PermissionViolation(violation)

        self.metrics.network_operations += 1

    @contextmanager
    def execute_context(self):
        """
        Context manager for sandboxed execution.

        Applies resource limits and tracks metrics.
        """
        with self._lock:
            if self.state != SandboxState.IDLE:
                raise SandboxViolation(f"Sandbox in invalid state: {self.state}")
            self.state = SandboxState.RUNNING

        self.metrics = SandboxMetrics()
        self.metrics.start_time = time.time()

        workspace = self._create_temp_workspace()

        try:
            yield workspace
        except Exception as e:
            self.state = SandboxState.ERROR
            raise
        finally:
            self.metrics.end_time = time.time()
            self._cleanup_temp_workspace()

            with self._lock:
                if self.state == SandboxState.RUNNING:
                    self.state = SandboxState.IDLE

    async def execute_async(self, func: Callable[..., Any], *args: Any, **kwargs: Any) -> Any:
        """
        Execute a function in the sandbox asynchronously.

        Args:
            func: Function to execute
            *args: Positional arguments
            **kwargs: Keyword arguments

        Returns:
            Function result

        Raises:
            SandboxViolation: On security or resource limit violations
            asyncio.TimeoutError: If execution exceeds timeout
        """
        loop = asyncio.get_event_loop()

        with self.execute_context():
            try:
                # Run with timeout
                result = await asyncio.wait_for(
                    loop.run_in_executor(None, lambda: func(*args, **kwargs)),
                    timeout=self.limits.execution_timeout_seconds,
                )
                return result

            except asyncio.TimeoutError:
                violation = f"Plugin {self.plugin_id} exceeded execution timeout"
                self.metrics.violations.append(violation)
                raise ResourceLimitExceeded(violation)

    def execute_subprocess(
        self,
        command: List[str],
        cwd: Optional[Path] = None,
        env: Optional[Dict[str, str]] = None,
        capture_output: bool = True,
    ) -> subprocess.CompletedProcess:
        """
        Execute a subprocess in the sandbox.

        Args:
            command: Command and arguments
            cwd: Working directory
            env: Environment variables
            capture_output: Whether to capture stdout/stderr

        Returns:
            CompletedProcess result
        """
        if not self.permissions.has_permission("system.process"):
            raise PermissionViolation(
                f"Plugin {self.plugin_id} denied process spawn: missing system.process"
            )

        with self.execute_context() as workspace:
            # Use sandbox workspace as default cwd
            work_dir = cwd or workspace

            # Create restricted environment
            sandbox_env = {
                "HOME": str(workspace),
                "TEMP": str(workspace),
                "TMP": str(workspace),
                "VOICESTUDIO_PLUGIN_ID": self.plugin_id,
                "VOICESTUDIO_SANDBOX": "1",
            }

            if env:
                # Only allow safe environment variables
                safe_vars = {"PATH", "PYTHONPATH", "LANG", "LC_ALL"}
                for key, value in env.items():
                    if key in safe_vars:
                        sandbox_env[key] = value

            try:
                use_windows_monitor = sys.platform == "win32" and HAS_PSUTIL
                if use_windows_monitor:
                    kwargs = {
                        "cwd": work_dir,
                        "env": sandbox_env,
                        "stdout": subprocess.PIPE if capture_output else None,
                        "stderr": subprocess.PIPE if capture_output else None,
                    }
                    proc = subprocess.Popen(command, **kwargs)
                    self._process = proc
                    monitor = threading.Thread(
                        target=self._monitor_windows_process,
                        args=(proc,),
                        daemon=True,
                    )
                    monitor.start()
                    try:
                        stdout_bytes, stderr_bytes = proc.communicate(
                            timeout=self.limits.execution_timeout_seconds
                        )
                    except subprocess.TimeoutExpired:
                        proc.kill()
                        stdout_bytes, stderr_bytes = proc.communicate()
                        raise
                    finally:
                        self._process = None
                    result = subprocess.CompletedProcess(
                        args=command,
                        returncode=proc.returncode or 0,
                        stdout=stdout_bytes,
                        stderr=stderr_bytes,
                    )
                    return result
                # Unix: use subprocess.run with preexec_fn for resource limits
                preexec = self._apply_resource_limits if sys.platform != "win32" else None
                result = subprocess.run(
                    command,
                    cwd=work_dir,
                    env=sandbox_env,
                    capture_output=capture_output,
                    timeout=self.limits.execution_timeout_seconds,
                    preexec_fn=preexec,
                )
                return result

            except subprocess.TimeoutExpired as e:
                violation = f"Plugin {self.plugin_id} subprocess timed out"
                self.metrics.violations.append(violation)
                raise ResourceLimitExceeded(violation) from e

    def _monitor_windows_process(self, proc: subprocess.Popen) -> None:
        """Monitor subprocess resource usage on Windows using psutil; terminate on limit breach."""
        if not HAS_PSUTIL or proc.pid is None:
            return
        try:
            ps_proc = psutil.Process(proc.pid)
            while proc.poll() is None:
                try:
                    mem_mb = ps_proc.memory_info().rss / (1024 * 1024)
                    if mem_mb > self.limits.max_memory_mb:
                        proc.terminate()
                        self.metrics.violations.append(
                            f"Memory limit exceeded: {mem_mb:.1f}MB > {self.limits.max_memory_mb}MB"
                        )
                        break
                    cpu_pct = ps_proc.cpu_percent(interval=0.1)
                    if cpu_pct > self.limits.max_cpu_percent:
                        proc.terminate()
                        self.metrics.violations.append(
                            f"CPU limit exceeded: {cpu_pct:.1f}% > {self.limits.max_cpu_percent}%"
                        )
                        break
                except (psutil.NoSuchProcess, psutil.AccessDenied):
                    break
                time.sleep(0.5)
        except Exception as e:
            logger.warning("Windows process monitor error: %s", e)

    def terminate(self) -> None:
        """Forcefully terminate any running sandbox processes."""
        with self._lock:
            if self._process and self._process.poll() is None:
                self._process.terminate()
                try:
                    self._process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    self._process.kill()

            self.state = SandboxState.TERMINATED

        self._cleanup_temp_workspace()

    def get_metrics(self) -> SandboxMetrics:
        """Get execution metrics."""
        return self.metrics

    def get_violations(self) -> List[str]:
        """Get list of security violations."""
        return self.metrics.violations.copy()


class SandboxManager:
    """
    Manages sandbox instances for multiple plugins.
    """

    def __init__(self):
        self._sandboxes: Dict[str, PluginSandbox] = {}
        self._lock = threading.Lock()
        self._default_limits = ResourceLimits()

    def set_default_limits(self, limits: ResourceLimits) -> None:
        """Set default resource limits for new sandboxes."""
        self._default_limits = limits

    def create_sandbox(
        self,
        plugin_id: str,
        permissions: SandboxPermissions,
        limits: Optional[ResourceLimits] = None,
    ) -> PluginSandbox:
        """
        Create or get existing sandbox for a plugin.

        Args:
            plugin_id: Plugin identifier
            permissions: Permissions for the sandbox
            limits: Resource limits (uses defaults if None)

        Returns:
            PluginSandbox instance
        """
        with self._lock:
            if plugin_id in self._sandboxes:
                # Update permissions on existing sandbox
                existing = self._sandboxes[plugin_id]
                existing.permissions = permissions
                return existing

            sandbox = PluginSandbox(
                plugin_id=plugin_id, permissions=permissions, limits=limits or self._default_limits
            )
            self._sandboxes[plugin_id] = sandbox
            return sandbox

    def get_sandbox(self, plugin_id: str) -> Optional[PluginSandbox]:
        """Get existing sandbox for a plugin."""
        return self._sandboxes.get(plugin_id)

    def destroy_sandbox(self, plugin_id: str) -> None:
        """Destroy and cleanup a plugin's sandbox."""
        with self._lock:
            sandbox = self._sandboxes.pop(plugin_id, None)
            if sandbox:
                sandbox.terminate()

    def destroy_all(self) -> None:
        """Destroy all sandboxes."""
        with self._lock:
            for sandbox in self._sandboxes.values():
                sandbox.terminate()
            self._sandboxes.clear()

    def get_all_metrics(self) -> Dict[str, SandboxMetrics]:
        """Get metrics for all sandboxes."""
        return {plugin_id: sandbox.get_metrics() for plugin_id, sandbox in self._sandboxes.items()}

    def get_all_violations(self) -> Dict[str, List[str]]:
        """Get violations for all sandboxes."""
        return {
            plugin_id: sandbox.get_violations()
            for plugin_id, sandbox in self._sandboxes.items()
            if sandbox.get_violations()
        }


# Global sandbox manager instance
_sandbox_manager: Optional[SandboxManager] = None


def get_sandbox_manager() -> SandboxManager:
    """Get the global sandbox manager instance."""
    global _sandbox_manager
    if _sandbox_manager is None:
        _sandbox_manager = SandboxManager()
    return _sandbox_manager
