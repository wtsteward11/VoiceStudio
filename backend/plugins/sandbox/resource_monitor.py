"""
Plugin Resource Monitor.

Phase 5A Enhancement: Monitors and enforces resource limits (CPU, memory)
for plugin subprocesses using psutil.

The monitor:
    - Tracks CPU and memory usage of plugin processes
    - Enforces configurable limits with grace periods
    - Provides violation callbacks for automated responses
    - Supports soft (warning) and hard (termination) limits
"""

from __future__ import annotations

import asyncio
import logging
import time
from dataclasses import dataclass, field
from enum import Enum
from typing import Any, Awaitable, Callable, Dict, List, Optional

try:
    import psutil

    PSUTIL_AVAILABLE = True
except ImportError:
    PSUTIL_AVAILABLE = False
    psutil = None  # type: ignore

logger = logging.getLogger(__name__)


class ViolationType(str, Enum):
    """Types of resource limit violations."""

    MEMORY_SOFT = "memory_soft"  # Exceeded soft limit (warning)
    MEMORY_HARD = "memory_hard"  # Exceeded hard limit (terminate)
    CPU_SOFT = "cpu_soft"  # Exceeded soft limit (warning)
    CPU_HARD = "cpu_hard"  # Exceeded hard limit (terminate)
    PROCESS_GONE = "process_gone"  # Process no longer exists


class ViolationAction(str, Enum):
    """Actions to take on violation."""

    WARN = "warn"  # Log warning, continue monitoring
    THROTTLE = "throttle"  # Attempt to reduce priority (future)
    TERMINATE = "terminate"  # Kill the process


@dataclass
class ResourceLimits:
    """
    Resource limits for a plugin subprocess.

    All limits are optional. If not set, that resource is not monitored.
    """

    # Memory limits (MB)
    max_memory_mb: Optional[int] = None
    soft_memory_mb: Optional[int] = None  # Warning threshold

    # CPU limits (percentage)
    max_cpu_percent: Optional[int] = None
    soft_cpu_percent: Optional[int] = None  # Warning threshold

    # Grace periods (seconds)
    memory_grace_period_sec: float = 5.0  # Time before hard limit triggers
    cpu_grace_period_sec: float = 10.0  # CPU can spike briefly

    # Monitoring interval
    check_interval_sec: float = 2.0

    def __post_init__(self):
        # Set soft limits to 80% of hard limits if not specified
        if self.max_memory_mb and not self.soft_memory_mb:
            self.soft_memory_mb = int(self.max_memory_mb * 0.8)
        if self.max_cpu_percent and not self.soft_cpu_percent:
            self.soft_cpu_percent = int(self.max_cpu_percent * 0.8)


@dataclass
class ResourceSnapshot:
    """A snapshot of resource usage at a point in time."""

    timestamp: float
    memory_mb: float
    memory_percent: float
    cpu_percent: float
    num_threads: int
    status: str  # Process status string

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary."""
        return {
            "timestamp": self.timestamp,
            "memory_mb": round(self.memory_mb, 2),
            "memory_percent": round(self.memory_percent, 2),
            "cpu_percent": round(self.cpu_percent, 2),
            "num_threads": self.num_threads,
            "status": self.status,
        }


@dataclass
class ViolationEvent:
    """Record of a resource limit violation."""

    violation_type: ViolationType
    action: ViolationAction
    timestamp: float
    current_value: float
    limit_value: float
    grace_remaining_sec: float
    plugin_id: str
    pid: int

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary."""
        return {
            "violation_type": self.violation_type.value,
            "action": self.action.value,
            "timestamp": self.timestamp,
            "current_value": round(self.current_value, 2),
            "limit_value": self.limit_value,
            "grace_remaining_sec": round(self.grace_remaining_sec, 2),
            "plugin_id": self.plugin_id,
            "pid": self.pid,
        }


# Type alias for violation callback
ViolationCallback = Callable[[ViolationEvent], Awaitable[None]]


@dataclass
class ResourceMonitor:
    """
    Monitors resource usage of a plugin subprocess.

    Tracks CPU and memory usage, enforces limits, and triggers
    callbacks when violations occur.
    """

    plugin_id: str
    pid: int
    limits: ResourceLimits

    # State
    _running: bool = field(default=False, repr=False)
    _monitor_task: Optional[asyncio.Task] = field(default=None, repr=False)
    _process: Any = field(default=None, repr=False)  # psutil.Process

    # Violation tracking
    _memory_violation_start: Optional[float] = field(default=None, repr=False)
    _cpu_violation_start: Optional[float] = field(default=None, repr=False)
    _violations: List[ViolationEvent] = field(default_factory=list, repr=False)

    # Recent snapshots for averaging
    _snapshots: List[ResourceSnapshot] = field(default_factory=list, repr=False)
    _max_snapshots: int = field(default=30, repr=False)  # ~1 minute of history

    # Callbacks
    _on_violation: List[ViolationCallback] = field(default_factory=list, repr=False)
    _on_terminate: List[Callable[[str, int], Awaitable[None]]] = field(
        default_factory=list, repr=False
    )

    def __post_init__(self):
        """Initialize psutil process handle."""
        if not PSUTIL_AVAILABLE:
            logger.warning(
                f"psutil not available, resource monitoring disabled for {self.plugin_id}"
            )
            return

        try:
            self._process = psutil.Process(self.pid)
        except psutil.NoSuchProcess:
            logger.error(f"Process {self.pid} does not exist")
            self._process = None

    @property
    def is_monitoring(self) -> bool:
        """Check if monitoring is active."""
        return self._running and self._monitor_task is not None

    @property
    def violations(self) -> List[ViolationEvent]:
        """Get list of recorded violations."""
        return self._violations.copy()

    @property
    def recent_snapshots(self) -> List[ResourceSnapshot]:
        """Get recent resource snapshots."""
        return self._snapshots.copy()

    @property
    def average_cpu_percent(self) -> float:
        """Get average CPU usage from recent snapshots."""
        if not self._snapshots:
            return 0.0
        return sum(s.cpu_percent for s in self._snapshots) / len(self._snapshots)

    @property
    def peak_memory_mb(self) -> float:
        """Get peak memory usage from recent snapshots."""
        if not self._snapshots:
            return 0.0
        return max(s.memory_mb for s in self._snapshots)

    def on_violation(self, callback: ViolationCallback) -> None:
        """Register a callback for resource violations."""
        self._on_violation.append(callback)

    def on_terminate(self, callback: Callable[[str, int], Awaitable[None]]) -> None:
        """Register a callback for termination events."""
        self._on_terminate.append(callback)

    async def start(self) -> None:
        """Start resource monitoring."""
        if not PSUTIL_AVAILABLE:
            logger.warning("Cannot start monitoring: psutil not available")
            return

        if not self._process:
            logger.warning(f"Cannot start monitoring: process {self.pid} not found")
            return

        if self._running:
            return

        self._running = True
        self._monitor_task = asyncio.create_task(self._monitor_loop())
        logger.info(f"Started resource monitoring for {self.plugin_id} (PID: {self.pid})")

    async def stop(self) -> None:
        """Stop resource monitoring."""
        self._running = False

        if self._monitor_task and not self._monitor_task.done():
            self._monitor_task.cancel()
            try:
                await self._monitor_task
            except asyncio.CancelledError:
                pass
            self._monitor_task = None

        logger.info(f"Stopped resource monitoring for {self.plugin_id}")

    def get_current_usage(self) -> Optional[ResourceSnapshot]:
        """Get current resource usage snapshot."""
        if not PSUTIL_AVAILABLE or not self._process:
            return None

        try:
            with self._process.oneshot():
                mem_info = self._process.memory_info()
                mem_percent = self._process.memory_percent()
                cpu_percent = self._process.cpu_percent()
                num_threads = self._process.num_threads()
                status = self._process.status()

            return ResourceSnapshot(
                timestamp=time.time(),
                memory_mb=mem_info.rss / (1024 * 1024),
                memory_percent=mem_percent,
                cpu_percent=cpu_percent,
                num_threads=num_threads,
                status=status,
            )
        except psutil.NoSuchProcess:
            return None
        except Exception as e:
            logger.warning(f"Error getting resource usage: {e}")
            return None

    async def _monitor_loop(self) -> None:
        """Main monitoring loop."""
        # Initial CPU measurement (needs two calls to get accurate reading)
        if self._process:
            try:
                self._process.cpu_percent()
            except Exception as e:
                # Process may have exited, will be detected in main loop
                logger.debug(f"Initial CPU measurement failed: {e}")

        await asyncio.sleep(0.1)  # Brief pause for CPU measurement

        while self._running:
            try:
                await self._check_resources()
                await asyncio.sleep(self.limits.check_interval_sec)
            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"Error in resource monitor loop: {e}")
                await asyncio.sleep(self.limits.check_interval_sec)

    async def _check_resources(self) -> None:
        """Check current resource usage against limits."""
        snapshot = self.get_current_usage()

        if snapshot is None:
            # Process is gone
            await self._handle_process_gone()
            return

        # Store snapshot
        self._snapshots.append(snapshot)
        if len(self._snapshots) > self._max_snapshots:
            self._snapshots.pop(0)

        # Check memory limits
        if self.limits.max_memory_mb:
            await self._check_memory(snapshot)

        # Check CPU limits (use average to avoid false positives from spikes)
        if self.limits.max_cpu_percent:
            await self._check_cpu(snapshot)

    async def _check_memory(self, snapshot: ResourceSnapshot) -> None:
        """Check memory usage against limits."""
        current_mb = snapshot.memory_mb
        now = time.time()

        # Check soft limit (warning)
        if (
            self.limits.soft_memory_mb
            and current_mb > self.limits.soft_memory_mb
            and current_mb <= self.limits.max_memory_mb
        ):
            await self._handle_violation(
                ViolationType.MEMORY_SOFT,
                ViolationAction.WARN,
                current_mb,
                self.limits.soft_memory_mb,
                0,
            )

        # Check hard limit
        if current_mb > self.limits.max_memory_mb:
            if self._memory_violation_start is None:
                self._memory_violation_start = now
                grace_remaining = self.limits.memory_grace_period_sec
            else:
                elapsed = now - self._memory_violation_start
                grace_remaining = max(0, self.limits.memory_grace_period_sec - elapsed)

            if grace_remaining <= 0:
                # Grace period expired, terminate
                await self._handle_violation(
                    ViolationType.MEMORY_HARD,
                    ViolationAction.TERMINATE,
                    current_mb,
                    self.limits.max_memory_mb,
                    grace_remaining,
                )
                await self._terminate_process("memory limit exceeded")
            else:
                # Still in grace period, warn
                await self._handle_violation(
                    ViolationType.MEMORY_HARD,
                    ViolationAction.WARN,
                    current_mb,
                    self.limits.max_memory_mb,
                    grace_remaining,
                )
        else:
            # Under limit, reset violation tracker
            self._memory_violation_start = None

    async def _check_cpu(self, snapshot: ResourceSnapshot) -> None:
        """Check CPU usage against limits."""
        # Use average to smooth out spikes
        current_cpu = (
            self.average_cpu_percent if len(self._snapshots) >= 3 else snapshot.cpu_percent
        )
        now = time.time()

        # Check soft limit (warning)
        if (
            self.limits.soft_cpu_percent
            and current_cpu > self.limits.soft_cpu_percent
            and current_cpu <= self.limits.max_cpu_percent
        ):
            await self._handle_violation(
                ViolationType.CPU_SOFT,
                ViolationAction.WARN,
                current_cpu,
                self.limits.soft_cpu_percent,
                0,
            )

        # Check hard limit
        if current_cpu > self.limits.max_cpu_percent:
            if self._cpu_violation_start is None:
                self._cpu_violation_start = now
                grace_remaining = self.limits.cpu_grace_period_sec
            else:
                elapsed = now - self._cpu_violation_start
                grace_remaining = max(0, self.limits.cpu_grace_period_sec - elapsed)

            if grace_remaining <= 0:
                # Grace period expired, terminate
                await self._handle_violation(
                    ViolationType.CPU_HARD,
                    ViolationAction.TERMINATE,
                    current_cpu,
                    self.limits.max_cpu_percent,
                    grace_remaining,
                )
                await self._terminate_process("CPU limit exceeded")
            else:
                # Still in grace period, warn
                await self._handle_violation(
                    ViolationType.CPU_HARD,
                    ViolationAction.WARN,
                    current_cpu,
                    self.limits.max_cpu_percent,
                    grace_remaining,
                )
        else:
            # Under limit, reset violation tracker
            self._cpu_violation_start = None

    async def _handle_violation(
        self,
        violation_type: ViolationType,
        action: ViolationAction,
        current_value: float,
        limit_value: float,
        grace_remaining: float,
    ) -> None:
        """Handle a resource limit violation."""
        event = ViolationEvent(
            violation_type=violation_type,
            action=action,
            timestamp=time.time(),
            current_value=current_value,
            limit_value=limit_value,
            grace_remaining_sec=grace_remaining,
            plugin_id=self.plugin_id,
            pid=self.pid,
        )

        self._violations.append(event)

        # Log the violation
        if action == ViolationAction.TERMINATE:
            logger.error(
                f"Resource violation TERMINATE: {self.plugin_id} "
                f"{violation_type.value}={current_value:.1f} "
                f"(limit={limit_value})"
            )
        elif action == ViolationAction.WARN:
            logger.warning(
                f"Resource violation WARNING: {self.plugin_id} "
                f"{violation_type.value}={current_value:.1f} "
                f"(limit={limit_value}, grace={grace_remaining:.1f}s)"
            )

        # Fire callbacks
        for callback in self._on_violation:
            try:
                await callback(event)
            except Exception as e:
                logger.error(f"Error in violation callback: {e}")

    async def _handle_process_gone(self) -> None:
        """Handle the case where the process no longer exists."""
        event = ViolationEvent(
            violation_type=ViolationType.PROCESS_GONE,
            action=ViolationAction.TERMINATE,
            timestamp=time.time(),
            current_value=0,
            limit_value=0,
            grace_remaining_sec=0,
            plugin_id=self.plugin_id,
            pid=self.pid,
        )

        self._violations.append(event)
        logger.info(f"Process {self.pid} for {self.plugin_id} is no longer running")

        self._running = False

        for callback in self._on_violation:
            try:
                await callback(event)
            except Exception as e:
                logger.error(f"Error in violation callback: {e}")

    async def _terminate_process(self, reason: str) -> None:
        """Terminate the monitored process."""
        logger.warning(f"Terminating {self.plugin_id} (PID: {self.pid}): {reason}")

        if not self._process:
            return

        try:
            # Try graceful termination first
            self._process.terminate()

            # Wait briefly for process to exit
            try:
                self._process.wait(timeout=2.0)
            except psutil.TimeoutExpired:
                # Force kill if still running
                self._process.kill()
                self._process.wait(timeout=1.0)

            logger.info(f"Successfully terminated {self.plugin_id}")

        except psutil.NoSuchProcess:
            # Already gone
            pass
        except Exception as e:
            logger.error(f"Error terminating process: {e}")

        self._running = False

        # Fire terminate callbacks
        for callback in self._on_terminate:
            try:
                await callback(self.plugin_id, self.pid)
            except Exception as e:
                logger.error(f"Error in terminate callback: {e}")


class ResourceMonitorRegistry:
    """
    Registry for managing multiple resource monitors.

    Provides a central point for creating, tracking, and cleaning up
    resource monitors for plugin subprocesses.
    """

    def __init__(self):
        self._monitors: Dict[str, ResourceMonitor] = {}
        self._lock = asyncio.Lock()

    @property
    def monitors(self) -> Dict[str, ResourceMonitor]:
        """Get all active monitors."""
        return self._monitors.copy()

    async def create_monitor(
        self,
        plugin_id: str,
        pid: int,
        limits: ResourceLimits,
        auto_start: bool = True,
    ) -> ResourceMonitor:
        """
        Create a new resource monitor for a plugin.

        Args:
            plugin_id: The plugin identifier
            pid: The process ID to monitor
            limits: Resource limits to enforce
            auto_start: Whether to start monitoring immediately

        Returns:
            The created ResourceMonitor
        """
        async with self._lock:
            # Stop any existing monitor for this plugin
            if plugin_id in self._monitors:
                await self._monitors[plugin_id].stop()

            monitor = ResourceMonitor(
                plugin_id=plugin_id,
                pid=pid,
                limits=limits,
            )

            self._monitors[plugin_id] = monitor

            if auto_start:
                await monitor.start()

            return monitor

    async def get_monitor(self, plugin_id: str) -> Optional[ResourceMonitor]:
        """Get the monitor for a plugin."""
        return self._monitors.get(plugin_id)

    async def stop_monitor(self, plugin_id: str) -> None:
        """Stop and remove a monitor."""
        async with self._lock:
            if plugin_id in self._monitors:
                await self._monitors[plugin_id].stop()
                del self._monitors[plugin_id]

    async def stop_all(self) -> None:
        """Stop all monitors."""
        async with self._lock:
            for monitor in self._monitors.values():
                await monitor.stop()
            self._monitors.clear()

    def get_all_violations(self) -> Dict[str, List[ViolationEvent]]:
        """Get all violations from all monitors."""
        return {plugin_id: monitor.violations for plugin_id, monitor in self._monitors.items()}

    def get_all_snapshots(self) -> Dict[str, List[ResourceSnapshot]]:
        """Get recent snapshots from all monitors."""
        return {
            plugin_id: monitor.recent_snapshots for plugin_id, monitor in self._monitors.items()
        }


# Global registry instance
_global_registry: Optional[ResourceMonitorRegistry] = None


def get_resource_monitor_registry() -> ResourceMonitorRegistry:
    """Get the global resource monitor registry."""
    global _global_registry
    if _global_registry is None:
        _global_registry = ResourceMonitorRegistry()
    return _global_registry
