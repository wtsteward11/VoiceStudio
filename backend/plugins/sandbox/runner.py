"""
Plugin Subprocess Runner.

Phase 4 Enhancement: Manages plugin subprocess lifecycle including
spawning, monitoring, and termination.

Phase 5A Enhancement: Integrated resource monitoring with psutil for
CPU and memory limit enforcement.

Phase 5D Enhancement: Crash recovery with auto-restart, exponential backoff,
and state preservation.

The runner:
    - Spawns isolated Python subprocesses for plugins
    - Establishes IPC bridge communication
    - Monitors process health via heartbeat
    - Enforces resource limits via ResourceMonitor (Phase 5A)
    - Handles graceful and forced termination
    - Auto-restart on crash with exponential backoff (Phase 5D)
    - Preserves and restores plugin state (Phase 5D)
"""

from __future__ import annotations

import asyncio
import logging
import os
import signal
import sys
from dataclasses import dataclass, field
from enum import Enum
from pathlib import Path
from typing import Any, Awaitable, Callable, Dict, List, Optional

from .bridge import BridgeState, IPCBridge
from .crash_recovery import (
    BackoffConfig,
    CrashRecoveryManager,
    PluginState,
    RecoveryConfig,
    RestartPolicy,
    get_recovery_manager,
    remove_recovery_manager,
)
from .protocol import HostMethods, Notification, Request, Response
from .resource_monitor import (
    ResourceLimits,
    ResourceMonitor,
    ViolationAction,
    ViolationEvent,
    ViolationType,
    get_resource_monitor_registry,
)

logger = logging.getLogger(__name__)


class ProcessState(str, Enum):
    """States of a plugin subprocess."""

    NOT_STARTED = "not_started"
    STARTING = "starting"
    RUNNING = "running"
    INITIALIZING = "initializing"
    ACTIVE = "active"
    DEACTIVATING = "deactivating"
    STOPPING = "stopping"
    STOPPED = "stopped"
    CRASHED = "crashed"
    KILLED = "killed"


@dataclass
class RunnerConfig:
    """Configuration for plugin subprocess runner."""

    # Plugin info
    plugin_id: str
    plugin_path: Path
    entry_module: str

    # Process settings
    python_executable: str = field(default_factory=lambda: sys.executable)
    working_directory: Optional[Path] = None
    env_vars: Dict[str, str] = field(default_factory=dict)

    # Timeouts (milliseconds)
    startup_timeout_ms: int = 10000
    shutdown_timeout_ms: int = 5000
    heartbeat_interval_ms: int = 5000
    heartbeat_timeout_ms: int = 15000

    # Resource limits (Phase 5A - enforced via ResourceMonitor)
    max_memory_mb: Optional[int] = None
    max_cpu_percent: Optional[int] = None
    resource_check_interval_sec: float = 2.0
    memory_grace_period_sec: float = 5.0
    cpu_grace_period_sec: float = 10.0

    # Enable/disable resource monitoring
    enable_resource_monitoring: bool = True

    # Permissions (passed to subprocess)
    permissions: Dict[str, Any] = field(default_factory=dict)

    # Crash recovery settings (Phase 5D)
    restart_policy: str = "on_crash"  # never, always, on_crash, on_error
    max_restarts: int = 5
    restart_window_sec: float = 300.0  # 5 minutes
    backoff_initial_sec: float = 1.0
    backoff_max_sec: float = 300.0
    backoff_multiplier: float = 2.0
    enable_state_preservation: bool = True
    state_dir: Optional[Path] = None


@dataclass
class PluginRunner:
    """
    Manages a single plugin subprocess.

    Handles the complete lifecycle from spawning to termination,
    including IPC bridge setup and health monitoring.

    Phase 5A: Integrated resource monitoring via ResourceMonitor for
    enforcing CPU and memory limits with psutil.
    """

    config: RunnerConfig
    state: ProcessState = ProcessState.NOT_STARTED

    # Process handle
    _process: Optional[asyncio.subprocess.Process] = field(default=None, repr=False)
    _bridge: IPCBridge = field(default_factory=IPCBridge, repr=False)

    # Monitoring
    _heartbeat_task: Optional[asyncio.Task] = field(default=None, repr=False)
    _last_heartbeat: float = field(default=0.0, repr=False)

    # Resource monitoring (Phase 5A)
    _resource_monitor: Optional[ResourceMonitor] = field(default=None, repr=False)
    _resource_violations: List[ViolationEvent] = field(default_factory=list, repr=False)

    # Crash recovery (Phase 5D)
    _recovery_manager: Optional[CrashRecoveryManager] = field(default=None, repr=False)
    _pending_state: Optional[PluginState] = field(default=None, repr=False)

    # Callbacks
    _on_state_change: List = field(default_factory=list, repr=False)
    _on_crash: List = field(default_factory=list, repr=False)
    _on_resource_violation: List = field(default_factory=list, repr=False)
    _on_restart: List = field(default_factory=list, repr=False)

    def __post_init__(self):
        """Initialize internal state."""
        self._on_state_change = []
        self._on_crash = []
        self._on_resource_violation = []
        self._on_restart = []
        self._resource_violations = []
        self._init_crash_recovery()

    @property
    def bridge(self) -> IPCBridge:
        """Get the IPC bridge for this runner."""
        return self._bridge

    @property
    def pid(self) -> Optional[int]:
        """Get the subprocess PID if running."""
        if self._process:
            return self._process.pid
        return None

    @property
    def is_running(self) -> bool:
        """Check if the subprocess is running."""
        return self.state in (
            ProcessState.RUNNING,
            ProcessState.INITIALIZING,
            ProcessState.ACTIVE,
        )

    @property
    def resource_monitor(self) -> Optional[ResourceMonitor]:
        """Get the resource monitor for this runner (Phase 5A)."""
        return self._resource_monitor

    @property
    def resource_violations(self) -> List[ViolationEvent]:
        """Get list of resource violations that have occurred."""
        return self._resource_violations.copy()

    @property
    def recovery_manager(self) -> Optional[CrashRecoveryManager]:
        """Get the crash recovery manager (Phase 5D)."""
        return self._recovery_manager

    @property
    def crash_count(self) -> int:
        """Get total crash count (Phase 5D)."""
        if self._recovery_manager:
            return self._recovery_manager.crash_count
        return 0

    @property
    def can_auto_restart(self) -> bool:
        """Check if auto-restart is available (Phase 5D)."""
        if self._recovery_manager:
            return self._recovery_manager.can_restart
        return False

    def _init_crash_recovery(self) -> None:
        """Initialize crash recovery manager (Phase 5D)."""
        # Map string policy to enum
        policy_map = {
            "never": RestartPolicy.NEVER,
            "always": RestartPolicy.ALWAYS,
            "on_crash": RestartPolicy.ON_CRASH,
            "on_error": RestartPolicy.ON_ERROR,
        }
        restart_policy = policy_map.get(self.config.restart_policy, RestartPolicy.ON_CRASH)

        recovery_config = RecoveryConfig(
            restart_policy=restart_policy,
            max_restarts=self.config.max_restarts,
            restart_window_sec=self.config.restart_window_sec,
            backoff=BackoffConfig(
                initial_delay_sec=self.config.backoff_initial_sec,
                max_delay_sec=self.config.backoff_max_sec,
                multiplier=self.config.backoff_multiplier,
            ),
            preserve_state=self.config.enable_state_preservation,
            state_dir=self.config.state_dir,
        )

        self._recovery_manager = get_recovery_manager(
            plugin_id=self.config.plugin_id,
            config=recovery_config,
        )

        # Set up restart callback
        self._recovery_manager.set_restart_callback(self._do_restart)

    async def start(self, restore_state: bool = True) -> None:
        """
        Start the plugin subprocess.

        Spawns the subprocess, establishes IPC, and waits for
        the plugin to complete initialization.

        Args:
            restore_state: If True, attempt to restore preserved state (Phase 5D)

        Raises:
            RuntimeError: If already running or start fails
            TimeoutError: If startup times out
        """
        if self.state not in (
            ProcessState.NOT_STARTED,
            ProcessState.STOPPED,
            ProcessState.CRASHED,
            ProcessState.KILLED,
        ):
            raise RuntimeError(f"Cannot start in state {self.state}")

        self._set_state(ProcessState.STARTING)

        try:
            await self._spawn_subprocess()
            await self._establish_bridge()
            await self._initialize_plugin()

            self._set_state(ProcessState.ACTIVE)
            self._start_heartbeat()

            # Start resource monitoring if enabled and limits are configured (Phase 5A)
            await self._start_resource_monitoring()

            # Restore state if available (Phase 5D)
            if restore_state and self._recovery_manager:
                await self._restore_preserved_state()

            logger.info(f"Plugin subprocess started: {self.config.plugin_id} (PID: {self.pid})")

        except Exception as e:
            logger.error(f"Failed to start plugin subprocess: {e}")
            await self._cleanup()
            self._set_state(ProcessState.CRASHED)
            raise

    async def stop(self, force: bool = False) -> None:
        """
        Stop the plugin subprocess.

        Args:
            force: If True, kill immediately without graceful shutdown

        Raises:
            TimeoutError: If graceful shutdown times out
        """
        if not self.is_running:
            return

        self._set_state(ProcessState.STOPPING)
        self._stop_heartbeat()

        if force:
            await self._force_kill()
        else:
            await self._graceful_shutdown()

    async def invoke_capability(
        self,
        capability: str,
        params: Optional[Dict[str, Any]] = None,
        timeout_ms: Optional[int] = None,
    ) -> Any:
        """
        Invoke a plugin capability.

        Args:
            capability: The capability name
            params: Optional parameters
            timeout_ms: Optional timeout override

        Returns:
            The capability result

        Raises:
            RuntimeError: If plugin is not active
        """
        if self.state != ProcessState.ACTIVE:
            raise RuntimeError(f"Cannot invoke capability in state {self.state}")

        return await self._bridge.send_request(
            HostMethods.INVOKE_CAPABILITY,
            {"capability": capability, "params": params or {}},
            timeout_ms,
        )

    def on_state_change(self, callback) -> None:
        """Register a callback for state changes."""
        self._on_state_change.append(callback)

    def on_crash(self, callback) -> None:
        """Register a callback for crash events."""
        self._on_crash.append(callback)

    def on_resource_violation(self, callback) -> None:
        """Register a callback for resource violation events (Phase 5A)."""
        self._on_resource_violation.append(callback)

    async def _spawn_subprocess(self) -> None:
        """Spawn the plugin subprocess."""
        # Build the subprocess entry script
        entry_script = self._build_entry_script()

        # Build environment
        env = os.environ.copy()
        env.update(self.config.env_vars)
        env["VOICESTUDIO_PLUGIN_ID"] = self.config.plugin_id
        env["VOICESTUDIO_PLUGIN_PATH"] = str(self.config.plugin_path)
        env["VOICESTUDIO_SUBPROCESS"] = "1"

        # Working directory
        cwd = self.config.working_directory or self.config.plugin_path

        logger.debug(f"Spawning subprocess for {self.config.plugin_id}")

        self._process = await asyncio.create_subprocess_exec(
            self.config.python_executable,
            "-c",
            entry_script,
            stdin=asyncio.subprocess.PIPE,
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE,
            env=env,
            cwd=cwd,
        )

        self._set_state(ProcessState.RUNNING)

        # Start stderr reader
        asyncio.create_task(self._read_stderr())

    def _build_entry_script(self) -> str:
        """Build the Python script to run in the subprocess."""
        # Get the backend path for fallback import
        backend_root = Path(__file__).parent.parent.parent.parent

        # This script bootstraps the plugin in the subprocess
        return f"""
import sys
import os
import asyncio
import json

# Add plugin path to sys.path
plugin_path = os.environ.get("VOICESTUDIO_PLUGIN_PATH", "")
if plugin_path:
    sys.path.insert(0, plugin_path)

# Import the subprocess client module
# This will be provided by the SDK
try:
    from voicestudio_sdk.subprocess_client import SubprocessClient
    client = SubprocessClient()
    asyncio.run(client.run("{self.config.entry_module}"))
except ImportError:
    # Fallback: minimal bootstrap without SDK
    # Add backend root to path for fallback import
    backend_root = r"{backend_root}"
    if backend_root not in sys.path:
        sys.path.insert(0, backend_root)
    
    from backend.plugins.sandbox.subprocess_bootstrap import run_plugin_subprocess
    asyncio.run(run_plugin_subprocess("{self.config.entry_module}"))
"""

    async def _establish_bridge(self) -> None:
        """Establish IPC bridge with subprocess."""
        if not self._process or not self._process.stdin or not self._process.stdout:
            raise RuntimeError("Process streams not available")

        # Create stream reader/writer from process pipes
        reader = self._process.stdout
        writer = self._process.stdin

        # Connect bridge
        self._bridge.connect(reader, writer)
        await self._bridge.start_reading()

        logger.debug(f"IPC bridge established for {self.config.plugin_id}")

    async def _initialize_plugin(self) -> None:
        """Initialize the plugin in the subprocess."""
        self._set_state(ProcessState.INITIALIZING)

        try:
            # Send initialization request
            result = await self._bridge.send_request(
                HostMethods.INITIALIZE,
                {
                    "plugin_id": self.config.plugin_id,
                    "permissions": self.config.permissions,
                },
                timeout_ms=self.config.startup_timeout_ms,
            )

            logger.debug(f"Plugin initialized: {result}")

        except TimeoutError:
            raise TimeoutError(f"Plugin {self.config.plugin_id} initialization timed out")

    async def _graceful_shutdown(self) -> None:
        """Attempt graceful shutdown of the subprocess."""
        try:
            # Send shutdown request
            await self._bridge.send_request(
                HostMethods.SHUTDOWN,
                timeout_ms=self.config.shutdown_timeout_ms,
            )

            # Wait for process to exit
            await asyncio.wait_for(
                self._process.wait(),
                timeout=self.config.shutdown_timeout_ms / 1000.0,
            )

            self._set_state(ProcessState.STOPPED)

        except (TimeoutError, asyncio.TimeoutError):
            logger.warning(f"Graceful shutdown timed out for {self.config.plugin_id}, forcing")
            await self._force_kill()

        finally:
            await self._cleanup()

    async def _force_kill(self) -> None:
        """Force kill the subprocess."""
        if self._process and self._process.returncode is None:
            try:
                self._process.kill()
                await self._process.wait()
            except ProcessLookupError:
                # SAFETY: ProcessLookupError occurs when the process has already
                # terminated before we could kill it. This is expected during
                # force-kill scenarios and requires no further action.
                pass

        self._set_state(ProcessState.KILLED)
        await self._cleanup()

    async def _cleanup(self) -> None:
        """Clean up resources after subprocess ends."""
        self._stop_heartbeat()

        # Stop resource monitoring (Phase 5A)
        await self._stop_resource_monitoring()

        if self._bridge.state != BridgeState.DISCONNECTED:
            await self._bridge.close()

        self._process = None

    def _start_heartbeat(self) -> None:
        """Start heartbeat monitoring."""
        self._heartbeat_task = asyncio.create_task(self._heartbeat_loop())

    def _stop_heartbeat(self) -> None:
        """Stop heartbeat monitoring."""
        if self._heartbeat_task and not self._heartbeat_task.done():
            self._heartbeat_task.cancel()
            self._heartbeat_task = None

    async def _heartbeat_loop(self) -> None:
        """Monitor plugin health via heartbeat."""
        import time

        while self.is_running:
            try:
                await asyncio.sleep(self.config.heartbeat_interval_ms / 1000.0)

                # Send heartbeat
                await self._bridge.send_notification(
                    HostMethods.HEARTBEAT,
                    {"timestamp": time.time()},
                )
                self._last_heartbeat = time.time()

            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.warning(f"Heartbeat failed for {self.config.plugin_id}: {e}")

                # Check if process is still running
                if self._process and self._process.returncode is not None:
                    self._set_state(ProcessState.CRASHED)
                    self._fire_crash_callbacks()
                    break

    async def _read_stderr(self) -> None:
        """Read and log subprocess stderr."""
        if not self._process or not self._process.stderr:
            return

        while True:
            try:
                line = await self._process.stderr.readline()
                if not line:
                    break
                logger.debug(f"[{self.config.plugin_id}] {line.decode().strip()}")
            except asyncio.CancelledError:
                break  # Expected during shutdown
            except Exception as e:
                logger.debug(f"[{self.config.plugin_id}] stderr read error: {e}")
                break

    def _set_state(self, new_state: ProcessState) -> None:
        """Update state and fire callbacks."""
        old_state = self.state
        self.state = new_state
        logger.debug(f"Plugin {self.config.plugin_id}: {old_state} -> {new_state}")

        for callback in self._on_state_change:
            try:
                callback(self.config.plugin_id, old_state, new_state)
            except Exception as e:
                logger.error(f"Error in state change callback: {e}")

    def _fire_crash_callbacks(self) -> None:
        """Fire crash callbacks and trigger recovery (Phase 5D enhanced)."""
        for callback in self._on_crash:
            try:
                callback(self.config.plugin_id)
            except Exception as e:
                logger.error(f"Error in crash callback: {e}")

        # Trigger crash recovery (Phase 5D)
        if self._recovery_manager:
            exit_code = None
            if self._process and self._process.returncode is not None:
                exit_code = self._process.returncode

            asyncio.create_task(
                self._recovery_manager.on_crash(
                    exit_code=exit_code,
                    error_message=f"Plugin {self.config.plugin_id} crashed",
                )
            )

    # Phase 5A: Resource Monitoring Methods

    async def _start_resource_monitoring(self) -> None:
        """Start resource monitoring for the subprocess (Phase 5A)."""
        if not self.config.enable_resource_monitoring:
            logger.debug(f"Resource monitoring disabled for {self.config.plugin_id}")
            return

        if not self.config.max_memory_mb and not self.config.max_cpu_percent:
            logger.debug(
                f"No resource limits configured for {self.config.plugin_id}, "
                "skipping resource monitoring"
            )
            return

        if not self.pid:
            logger.warning(
                f"Cannot start resource monitoring for {self.config.plugin_id}: " "no PID available"
            )
            return

        # Build resource limits from config
        limits = ResourceLimits(
            max_memory_mb=self.config.max_memory_mb,
            max_cpu_percent=self.config.max_cpu_percent,
            check_interval_sec=self.config.resource_check_interval_sec,
            memory_grace_period_sec=self.config.memory_grace_period_sec,
            cpu_grace_period_sec=self.config.cpu_grace_period_sec,
        )

        # Create and start the monitor
        registry = get_resource_monitor_registry()
        self._resource_monitor = await registry.create_monitor(
            plugin_id=self.config.plugin_id,
            pid=self.pid,
            limits=limits,
            auto_start=True,
        )

        # Register violation callback
        self._resource_monitor.on_violation(self._handle_resource_violation)
        self._resource_monitor.on_terminate(self._handle_resource_termination)

        logger.info(
            f"Started resource monitoring for {self.config.plugin_id}: "
            f"memory={self.config.max_memory_mb}MB, cpu={self.config.max_cpu_percent}%"
        )

    async def _stop_resource_monitoring(self) -> None:
        """Stop resource monitoring for the subprocess (Phase 5A)."""
        if self._resource_monitor:
            await self._resource_monitor.stop()

            # Also remove from global registry
            registry = get_resource_monitor_registry()
            await registry.stop_monitor(self.config.plugin_id)

            self._resource_monitor = None

    async def _handle_resource_violation(self, event: ViolationEvent) -> None:
        """Handle a resource violation event (Phase 5A)."""
        self._resource_violations.append(event)

        # Fire callbacks
        for callback in self._on_resource_violation:
            try:
                if asyncio.iscoroutinefunction(callback):
                    await callback(self.config.plugin_id, event)
                else:
                    callback(self.config.plugin_id, event)
            except Exception as e:
                logger.error(f"Error in resource violation callback: {e}")

        # If the monitor terminated the process, update our state
        if event.action == ViolationAction.TERMINATE:
            if event.violation_type == ViolationType.PROCESS_GONE:
                # Process exited on its own, check if it was a crash
                if self._process and self._process.returncode is not None:
                    if self._process.returncode != 0:
                        self._set_state(ProcessState.CRASHED)
                        self._fire_crash_callbacks()
                    else:
                        self._set_state(ProcessState.STOPPED)
            else:
                # We killed it due to resource limits
                self._set_state(ProcessState.KILLED)

    async def _handle_resource_termination(self, plugin_id: str, pid: int) -> None:
        """Handle process termination by resource monitor (Phase 5A)."""
        logger.warning(f"Plugin {plugin_id} (PID: {pid}) was terminated by resource monitor")

        # Update state if we haven't already
        if self.state not in (ProcessState.STOPPED, ProcessState.KILLED, ProcessState.CRASHED):
            self._set_state(ProcessState.KILLED)

            # Clean up
            await self._cleanup()

    # Phase 5D: Crash Recovery Methods

    def on_restart(self, callback: Callable) -> None:
        """Register a callback for restart events (Phase 5D)."""
        self._on_restart.append(callback)

    async def preserve_state(
        self,
        invocation_context: Optional[Dict[str, Any]] = None,
        user_data: Optional[Dict[str, Any]] = None,
        capabilities_state: Optional[Dict[str, Any]] = None,
    ) -> None:
        """
        Preserve plugin state for crash recovery (Phase 5D).

        Args:
            invocation_context: Current invocation context
            user_data: User data to preserve
            capabilities_state: Capability states to preserve
        """
        if self._recovery_manager:
            await self._recovery_manager.preserve_state(
                invocation_context=invocation_context,
                user_data=user_data,
                capabilities_state=capabilities_state,
            )

    async def _restore_preserved_state(self) -> None:
        """Restore preserved state after restart (Phase 5D)."""
        if not self._recovery_manager:
            return

        state = await self._recovery_manager.restore_state()
        if not state:
            return

        try:
            # Send state restoration request to plugin
            await self._bridge.send_request(
                HostMethods.INVOKE_CAPABILITY,
                {
                    "capability": "__restore_state__",
                    "params": {
                        "invocation_context": state.invocation_context,
                        "user_data": state.user_data,
                        "capabilities_state": state.capabilities_state,
                    },
                },
                timeout_ms=5000,
            )
            logger.info(f"Restored state for {self.config.plugin_id}")

        except Exception as e:
            # State restoration is best-effort
            logger.warning(f"Failed to restore state for {self.config.plugin_id}: {e}")

    async def _do_restart(self) -> bool:
        """
        Execute a restart (called by recovery manager) (Phase 5D).

        Returns:
            True if restart succeeded, False otherwise
        """
        try:
            # Cancel any pending restart
            if self._recovery_manager:
                await self._recovery_manager.cancel_pending_restart()

            # Clean up current state
            await self._cleanup()

            # Reset state to allow restart
            self.state = ProcessState.NOT_STARTED

            # Fire restart callbacks
            self._fire_restart_callbacks()

            # Start the process again
            await self.start(restore_state=True)

            return True

        except Exception as e:
            logger.error(f"Restart failed for {self.config.plugin_id}: {e}")
            return False

    def _fire_restart_callbacks(self) -> None:
        """Fire restart callbacks (Phase 5D)."""
        for callback in self._on_restart:
            try:
                callback(self.config.plugin_id)
            except Exception as e:
                logger.error(f"Error in restart callback: {e}")

    async def trigger_crash_recovery(
        self,
        exit_code: Optional[int] = None,
        error_message: Optional[str] = None,
    ) -> bool:
        """
        Manually trigger crash recovery (Phase 5D).

        Args:
            exit_code: Process exit code
            error_message: Error message

        Returns:
            True if restart was initiated, False otherwise
        """
        if not self._recovery_manager:
            return False

        # Capture current state snapshot if available
        state_snapshot = None
        if self._recovery_manager.preserved_state:
            state_snapshot = self._recovery_manager.preserved_state.to_dict()

        return await self._recovery_manager.on_crash(
            exit_code=exit_code,
            error_message=error_message,
            state_snapshot=state_snapshot,
        )

    def get_recovery_stats(self) -> Dict[str, Any]:
        """Get crash recovery statistics (Phase 5D)."""
        if self._recovery_manager:
            return self._recovery_manager.get_stats()
        return {"enabled": False}
