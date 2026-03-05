"""
Engine Lifecycle Management
State machine and lifecycle management for engine processes with Governor awareness
"""

from __future__ import annotations

import logging
import threading
import time
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from pathlib import Path
from typing import Any

from .port_manager import PortManager, get_port_manager
from .resource_manager import ResourceManager, get_resource_manager

HAS_VENV_FAMILIES = False
VenvFamily: Any = None
VenvFamilyManager: Any = None
get_venv_manager: Any = None
ENGINE_TO_FAMILY: dict[str, Any] = {}

try:
    from .venv_family_manager import (
        ENGINE_TO_FAMILY,
        VenvFamily,
        VenvFamilyManager,
        get_venv_manager,
    )

    HAS_VENV_FAMILIES = True
# ALLOWED: bare except - optional dependency, import failure acceptable
except ImportError:
    pass

logger = logging.getLogger(__name__)

HAS_RUNTIME_ENGINE = False
RuntimeEngine: Any = None

try:
    from .runtime_engine_enhanced import EnhancedRuntimeEngine

    RuntimeEngine = EnhancedRuntimeEngine
    HAS_RUNTIME_ENGINE = True
except ImportError:
    try:
        from .runtime_engine import RuntimeEngine as _FallbackRuntimeEngine

        RuntimeEngine = _FallbackRuntimeEngine
        HAS_RUNTIME_ENGINE = True
    except ImportError:
        logger.debug("RuntimeEngine not available. Process management will be limited.")


class EngineState(Enum):
    """Engine lifecycle states."""

    STOPPED = "stopped"
    STARTING = "starting"
    HEALTHY = "healthy"
    BUSY = "busy"
    DRAINING = "draining"
    ERROR = "error"


@dataclass
class EngineInstance:
    """Engine instance with lifecycle state."""

    engine_id: str
    manifest: dict[str, Any]
    state: EngineState = EngineState.STOPPED
    state_changed_at: datetime = field(default_factory=datetime.now)
    process: Any | None = None
    port: int | None = None
    pid: int | None = None
    health_check_failures: int = 0
    max_health_failures: int = 3
    idle_timeout_seconds: float | None = None
    last_activity: datetime | None = None
    job_lease: str | None = None  # Current job ID holding lease
    drain_requested: bool = False

    def set_state(self, new_state: EngineState):
        """Set new state and update timestamp."""
        old_state = self.state
        self.state = new_state
        self.state_changed_at = datetime.now()
        logger.info(f"Engine {self.engine_id} state: {old_state.name} -> {new_state.name}")

    def update_activity(self):
        """Update last activity timestamp."""
        self.last_activity = datetime.now()

    def is_idle(self) -> bool:
        """Check if engine is idle (exceeds timeout)."""
        if self.idle_timeout_seconds is None:
            return False  # No timeout configured

        if self.last_activity is None:
            return True  # Never had activity

        idle_seconds = (datetime.now() - self.last_activity).total_seconds()
        return idle_seconds > self.idle_timeout_seconds


class EngineLifecycleManager:
    """
    Manages engine lifecycle with state machine and Governor awareness.

    Features:
    - State machine (stopped → starting → healthy → busy → draining → stopped)
    - Port allocation and management
    - Health checks with failure tracking
    - Job leases to prevent premature shutdown
    - Graceful draining
    - Idle timeout management
    - Engine pooling (fast engines vs heavy engines)
    """

    def __init__(
        self,
        workspace_root: str = ".",
        audit_log_dir: str | None = None,
        port_manager: PortManager | None = None,
        resource_manager: ResourceManager | None = None,
    ):
        """
        Initialize engine lifecycle manager.

        Args:
            workspace_root: Workspace root directory
            workspace_root: Root directory for engines
            port_manager: Port manager instance (or None for global)
            resource_manager: Resource manager instance (or None for global)
        """
        self.workspace_root = workspace_root
        self.port_manager = port_manager or get_port_manager()
        self.resource_manager = resource_manager or get_resource_manager()

        # Venv family manager for engine isolation
        self.venv_manager = get_venv_manager() if HAS_VENV_FAMILIES else None

        # Audit log directory
        if audit_log_dir:
            self.audit_log_dir = Path(audit_log_dir)
        else:
            self.audit_log_dir = Path(workspace_root) / "runtime" / "audit_logs"
        self.audit_log_dir.mkdir(parents=True, exist_ok=True)

        # Engine instances
        self.engines: dict[str, EngineInstance] = {}

        # Engine pools (for fast engines)
        self.engine_pools: dict[str, list[EngineInstance]] = {}
        self.pool_sizes: dict[str, int] = {}  # Max pool size per engine type

        # Singletons (for heavy engines like XTTS, ComfyUI)
        self.singletons: dict[str, EngineInstance] = {}

        # Threading
        self.lock = threading.Lock()
        self.running = True

        # Lifecycle monitoring thread
        self.monitor_thread: threading.Thread | None = None
        self._start_monitor()

    def _start_monitor(self):
        """Start lifecycle monitoring thread."""

        def monitor_loop():
            while self.running:
                try:
                    self._monitor_engines()
                    time.sleep(5)  # Check every 5 seconds
                except Exception as e:
                    logger.error(f"Error in lifecycle monitor: {e}")
                    time.sleep(5)

        self.monitor_thread = threading.Thread(target=monitor_loop, daemon=True)
        self.monitor_thread.start()

    def _monitor_engines(self):
        """Monitor engines for state transitions."""
        with self.lock:
            for engine in list(self.engines.values()):
                # Check idle timeout
                if engine.state == EngineState.HEALTHY and engine.is_idle():
                    logger.info(f"Engine {engine.engine_id} is idle, transitioning to draining")
                    self._request_drain(engine.engine_id)

                # Check health for healthy/busy engines
                if engine.state in [EngineState.HEALTHY, EngineState.BUSY]:
                    if not self._check_health(engine):
                        engine.health_check_failures += 1
                        if engine.health_check_failures >= engine.max_health_failures:
                            logger.error(
                                f"Engine {engine.engine_id} health check failed {engine.health_check_failures} times"
                            )
                            engine.set_state(EngineState.ERROR)
                            # Could trigger restart logic here

    def register_engine(
        self,
        engine_id: str,
        manifest: dict[str, Any],
        pool_size: int | None = None,
        is_singleton: bool = False,
        idle_timeout_seconds: float | None = None,
    ):
        """
        Register an engine with lifecycle management.

        Args:
            engine_id: Engine identifier
            manifest: Engine manifest
            pool_size: Pool size for fast engines (None = singleton)
            is_singleton: Force singleton mode (for heavy engines)
            idle_timeout_seconds: Idle timeout in seconds
        """
        with self.lock:
            engine = EngineInstance(
                engine_id=engine_id,
                manifest=manifest,
                idle_timeout_seconds=idle_timeout_seconds,
            )

            if is_singleton or pool_size is None:
                self.singletons[engine_id] = engine
            else:
                # Pooled engine
                if engine_id not in self.engine_pools:
                    self.engine_pools[engine_id] = []
                    self.pool_sizes[engine_id] = pool_size
                # Don't create instance yet, create on demand

            self.engines[engine_id] = engine
            logger.info(
                f"Registered engine {engine_id} (singleton={is_singleton}, pool_size={pool_size})"
            )

    def acquire_engine(
        self, engine_id: str, job_id: str | None = None, auto_start: bool = True
    ) -> EngineInstance | None:
        """
        Acquire an engine instance for a job.

        Args:
            engine_id: Engine identifier
            job_id: Job ID (for lease tracking)
            auto_start: Automatically start if not running

        Returns:
            Engine instance or None if unavailable
        """
        with self.lock:
            # Check if singleton
            if engine_id in self.singletons:
                engine = self.singletons[engine_id]

                # Check if already has a lease
                if engine.job_lease is not None and engine.job_lease != job_id:
                    logger.warning(f"Engine {engine_id} is leased to job {engine.job_lease}")
                    return None

                # Start if needed
                if auto_start and engine.state == EngineState.STOPPED:
                    if not self._start_engine(engine):
                        return None

                # Acquire lease
                engine.job_lease = job_id
                engine.set_state(EngineState.BUSY)
                engine.update_activity()

                return engine

            # Pooled engine - get from pool or create new
            elif engine_id in self.engine_pools:
                pool = self.engine_pools[engine_id]
                pool_size = self.pool_sizes.get(engine_id, 1)

                # Find available engine in pool
                for engine in pool:
                    if engine.state == EngineState.HEALTHY and engine.job_lease is None:
                        engine.job_lease = job_id
                        engine.set_state(EngineState.BUSY)
                        engine.update_activity()
                        return engine

                # Create new engine if pool not full
                if len(pool) < pool_size:
                    engine = EngineInstance(
                        engine_id=f"{engine_id}_{len(pool)}",
                        manifest=self.engines[engine_id].manifest,
                    )
                    pool.append(engine)

                    if auto_start and not self._start_engine(engine):
                        return None

                    engine.job_lease = job_id
                    engine.set_state(EngineState.BUSY)
                    engine.update_activity()
                    return engine

                # Pool full, wait or return None
                logger.warning(f"Engine pool {engine_id} is full")
                return None

            logger.error(f"Engine {engine_id} not registered")
            return None

    def release_engine(self, engine_id: str, job_id: str):
        """
        Release an engine instance after job completion.

        Args:
            engine_id: Engine identifier
            job_id: Job ID that held the lease
        """
        with self.lock:
            engine = self.engines.get(engine_id)
            if not engine:
                # Check pools
                for pool in self.engine_pools.values():
                    for inst in pool:
                        if inst.engine_id == engine_id:
                            engine = inst
                            break
                    if engine:
                        break

            if not engine:
                logger.warning(f"Engine {engine_id} not found for release")
                return

            if engine.job_lease != job_id:
                logger.warning(
                    f"Engine {engine_id} lease mismatch (expected {job_id}, got {engine.job_lease})"
                )
                return

            engine.job_lease = None
            engine.update_activity()

            # Check if drain was requested
            if engine.drain_requested:
                engine.set_state(EngineState.DRAINING)
                self._stop_engine(engine)
            else:
                engine.set_state(EngineState.HEALTHY)

    def _get_venv_family(self, engine: EngineInstance) -> VenvFamily | None:
        """
        Get the venv family for an engine.

        Checks manifest for venv_family field, then falls back to ENGINE_TO_FAMILY mapping.
        """
        if not HAS_VENV_FAMILIES:
            return None

        # Check manifest for explicit venv_family
        manifest = engine.manifest
        venv_family_str = manifest.get("venv_family")
        if venv_family_str:
            try:
                return VenvFamily(venv_family_str)
            except ValueError:
                logger.warning(
                    f"Unknown venv_family '{venv_family_str}' for engine {engine.engine_id}"
                )

        # Fall back to ENGINE_TO_FAMILY mapping
        return ENGINE_TO_FAMILY.get(engine.engine_id)

    def _ensure_venv_ready(self, engine: EngineInstance) -> bool:
        """
        Ensure the venv family for an engine is ready.

        Returns True if ready (or no venv needed), False on failure.
        """
        family = self._get_venv_family(engine)
        if not family:
            # No venv family specified, use default venv
            return True

        if not self.venv_manager:
            logger.warning(
                f"Venv family {family.value} required for {engine.engine_id} but VenvFamilyManager not available"
            )
            return True  # Continue with default venv

        try:
            # Check if venv exists
            if self.venv_manager.is_venv_created(family):
                logger.debug(f"Venv {family.value} already exists for {engine.engine_id}")
                return True

            # Create venv (lazy creation)
            logger.info(f"Creating venv {family.value} for engine {engine.engine_id}")
            if not self.venv_manager.create_venv(family):
                logger.error(f"Failed to create venv {family.value} for {engine.engine_id}")
                return False

            # Note: Requirements installation is deferred to first actual use
            # since it can take a long time
            return True

        except Exception as e:
            logger.error(f"Error preparing venv for {engine.engine_id}: {e}")
            return False

    def _get_venv_python(self, engine: EngineInstance) -> Path | None:
        """
        Get the Python executable for an engine's venv family.

        Returns None if engine should use the default Python.
        """
        family = self._get_venv_family(engine)
        if not family or not self.venv_manager:
            return None

        if not self.venv_manager.is_venv_created(family):
            return None

        result: Path | None = self.venv_manager.get_python_executable(family)
        return result

    def _start_engine(self, engine: EngineInstance) -> bool:
        """Start an engine instance."""
        if engine.state != EngineState.STOPPED:
            logger.warning(f"Engine {engine.engine_id} is not in STOPPED state")
            return False

        engine.set_state(EngineState.STARTING)

        try:
            # Allocate port
            port = self.port_manager.allocate_port(engine.engine_id, pid=engine.pid)
            if not port:
                logger.error(f"Failed to allocate port for {engine.engine_id}")
                engine.set_state(EngineState.ERROR)
                return False

            engine.port = port

            # Ensure venv family is ready (TD-015: venv isolation)
            if not self._ensure_venv_ready(engine):
                logger.error(f"Failed to prepare venv for {engine.engine_id}")
                engine.set_state(EngineState.ERROR)
                self.port_manager.release_port(engine.engine_id)
                return False

            # Get venv Python executable if applicable
            venv_python = self._get_venv_python(engine)
            if venv_python:
                logger.info(f"Engine {engine.engine_id} will use venv: {venv_python}")

            # Start actual process using RuntimeEngine if available
            if HAS_RUNTIME_ENGINE and RuntimeEngine is not None:
                try:
                    # Check if manifest has runtime entry configuration
                    manifest = engine.manifest
                    entry = manifest.get("entry", {})

                    if entry.get("kind") == "python" or entry.get("kind") == "executable":
                        # Create RuntimeEngine instance
                        runtime_engine = RuntimeEngine(manifest, self.workspace_root)

                        # Start the process
                        if runtime_engine.start():
                            engine.process = runtime_engine
                            engine.pid = (
                                runtime_engine.process.pid if runtime_engine.process else None
                            )
                            logger.info(f"Started RuntimeEngine process for {engine.engine_id}")
                        else:
                            logger.error(f"Failed to start RuntimeEngine for {engine.engine_id}")
                            engine.set_state(EngineState.ERROR)
                            self.port_manager.release_port(engine.engine_id)
                            return False
                    else:
                        # No runtime entry, engine runs in-process
                        logger.debug(
                            f"Engine {engine.engine_id} has no runtime entry, running in-process"
                        )
                        engine.process = None
                        engine.pid = None
                except Exception as e:
                    logger.warning(
                        f"Failed to start RuntimeEngine for {engine.engine_id}: {e}. Using in-process mode."
                    )
                    engine.process = None
                    engine.pid = None
            else:
                # RuntimeEngine not available, engine runs in-process
                logger.debug(
                    f"RuntimeEngine not available for {engine.engine_id}, running in-process"
                )
                engine.process = None
                engine.pid = None

            # Health check
            if self._check_health(engine):
                engine.set_state(EngineState.HEALTHY)
                engine.update_activity()
                logger.info(f"Engine {engine.engine_id} started successfully on port {port}")
                return True
            else:
                engine.set_state(EngineState.ERROR)
                self.port_manager.release_port(engine.engine_id)
                return False

        except Exception as e:
            logger.error(f"Failed to start engine {engine.engine_id}: {e}")
            engine.set_state(EngineState.ERROR)
            if engine.port:
                self.port_manager.release_port(engine.engine_id)
            return False

    def _stop_engine(self, engine: EngineInstance):
        """Stop an engine instance."""
        if engine.state == EngineState.STOPPED:
            return

        engine.set_state(EngineState.DRAINING)

        try:
            # Stop actual process if RuntimeEngine is managing it
            if engine.process is not None:
                if HAS_RUNTIME_ENGINE and isinstance(engine.process, RuntimeEngine):
                    try:
                        engine.process.stop()
                        logger.info(f"Stopped RuntimeEngine process for {engine.engine_id}")
                    except Exception as e:
                        logger.warning(f"Error stopping RuntimeEngine for {engine.engine_id}: {e}")
                elif hasattr(engine.process, "terminate"):
                    # Direct subprocess.Popen instance
                    try:
                        engine.process.terminate()
                        engine.process.wait(timeout=10)
                    except Exception as e:
                        logger.warning(f"Error terminating process for {engine.engine_id}: {e}")
                        try:
                            engine.process.kill()
                            engine.process.wait()
                        except Exception as kill_err:
                            # GAP-PY-001: Best effort kill, process may already be dead
                            logger.debug(f"Failed to force kill {engine.engine_id}: {kill_err}")

            # Release port
            if engine.port:
                self.port_manager.release_port(engine.engine_id)
                engine.port = None

            engine.process = None
            engine.pid = None
            engine.set_state(EngineState.STOPPED)
            engine.drain_requested = False
            logger.info(f"Engine {engine.engine_id} stopped")

        except Exception as e:
            logger.error(f"Error stopping engine {engine.engine_id}: {e}")
            engine.set_state(EngineState.ERROR)

    def _check_health(self, engine: EngineInstance) -> bool:
        """
        Check engine health based on manifest configuration.

        Supports:
        - HTTP health checks (checks health endpoint URL)
        - TCP health checks (checks if port is listening)
        - Process health checks (checks if process is running)
        - No health check (assumes healthy if process running)
        """
        # If engine is in error state, it's not healthy
        if engine.state == EngineState.ERROR:
            return False

        manifest = engine.manifest
        health_config = manifest.get("health", {})

        # If no health check configured, check if process is running
        if not health_config:
            if engine.process is not None:
                if HAS_RUNTIME_ENGINE and isinstance(engine.process, RuntimeEngine):
                    healthy: bool = engine.process.is_healthy()
                    return healthy
                elif hasattr(engine.process, "poll"):
                    return engine.process.poll() is None
            return True

        health_kind = health_config.get("kind", "process")

        # HTTP health check
        if health_kind == "http":
            url = health_config.get("url")
            if url:
                try:
                    import requests

                    # Replace port placeholder if present
                    if "{port}" in url and engine.port:
                        url = url.replace("{port}", str(engine.port))
                    response = requests.get(url, timeout=2)
                    return response.status_code == 200
                except Exception as e:
                    logger.debug(f"HTTP health check failed for {engine.engine_id}: {e}")
                    return False

        # TCP health check
        elif health_kind == "tcp":
            port = engine.port or health_config.get("port")
            if port:
                try:
                    import socket

                    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                    sock.settimeout(1)
                    result = sock.connect_ex(("127.0.0.1", port))
                    sock.close()
                    return result == 0
                except Exception as e:
                    logger.debug(f"TCP health check failed for {engine.engine_id}: {e}")
                    return False

        # Process health check (default)
        elif health_kind == "process" or health_kind is None:
            if engine.process is not None:
                if HAS_RUNTIME_ENGINE and isinstance(engine.process, RuntimeEngine):
                    running: bool = engine.process.is_running() and engine.process.is_healthy()
                    return running
                elif hasattr(engine.process, "poll"):
                    return engine.process.poll() is None
            return True

        # Unknown health check kind, fallback to process check
        logger.warning(
            f"Unknown health check kind '{health_kind}' for {engine.engine_id}, using process check"
        )
        if engine.process is not None and hasattr(engine.process, "poll"):
            return engine.process.poll() is None
        return True

    def _request_drain(self, engine_id: str):
        """Request graceful drain of an engine."""
        with self.lock:
            engine = self.engines.get(engine_id)
            if not engine:
                return

            if engine.job_lease is not None:
                # Mark for drain after job completes
                engine.drain_requested = True
                logger.info(
                    f"Engine {engine_id} drain requested (will drain after job {engine.job_lease} completes)"
                )
            else:
                # Drain immediately
                engine.set_state(EngineState.DRAINING)
                self._stop_engine(engine)

    def kill_all(self, audit_log: bool = True) -> dict[str, bool]:
        """
        Panic switch: kill all engine processes.

        Args:
            audit_log: Log to audit trail

        Returns:
            Dictionary mapping engine_id to success status
        """
        results = {}

        with self.lock:
            logger.warning("KILL ALL ENGINES requested - panic switch activated")

            if audit_log:
                # Write to audit log
                self._write_audit_log(
                    event_type="panic_switch",
                    message="PANIC SWITCH: All engines killed by user",
                    details={
                        "engine_count": len(self.engines),
                        "engine_ids": list(self.engines.keys()),
                        "timestamp": datetime.now().isoformat(),
                    },
                )
                logger.critical("PANIC SWITCH: All engines killed by user")

            for engine_id, engine in list(self.engines.items()):
                try:
                    self._stop_engine(engine)
                    results[engine_id] = True
                except Exception as e:
                    logger.error(f"Failed to kill engine {engine_id}: {e}")
                    results[engine_id] = False

            # Clear pools
            for pool in self.engine_pools.values():
                for engine in pool:
                    try:
                        self._stop_engine(engine)
                    except Exception as e:
                        # GAP-PY-001: Best effort pool cleanup
                        logger.debug(f"Failed to stop pooled engine: {e}")

        return results

    def _write_audit_log(
        self, event_type: str, message: str, details: dict[str, Any] | None = None
    ):
        """
        Write event to audit log file.

        Args:
            event_type: Type of event (e.g., "panic_switch", "engine_start", "engine_stop")
            message: Event message
            details: Additional event details
        """
        try:
            import json

            audit_entry = {
                "timestamp": datetime.now().isoformat(),
                "event_type": event_type,
                "message": message,
                "details": details or {},
            }

            # Write to daily audit log file
            audit_file = self.audit_log_dir / f"audit_{datetime.now().strftime('%Y%m%d')}.jsonl"
            with open(audit_file, "a", encoding="utf-8") as f:
                f.write(json.dumps(audit_entry) + "\n")
        except Exception as e:
            # Don't fail if audit logging fails, just log the error
            logger.warning(f"Failed to write audit log: {e}")

    def get_engine_state(self, engine_id: str) -> EngineState | None:
        """Get current state of an engine."""
        with self.lock:
            engine = self.engines.get(engine_id)
            if engine:
                return engine.state
            return None

    def get_all_states(self) -> dict[str, dict[str, Any]]:
        """Get state of all engines."""
        with self.lock:
            states = {}
            for engine_id, engine in self.engines.items():
                states[engine_id] = {
                    "state": engine.state.name,
                    "port": engine.port,
                    "job_lease": engine.job_lease,
                    "last_activity": (
                        engine.last_activity.isoformat() if engine.last_activity else None
                    ),
                    "health_check_failures": engine.health_check_failures,
                }
            return states


# Global lifecycle manager instance
_lifecycle_manager: EngineLifecycleManager | None = None


def get_lifecycle_manager(workspace_root: str = ".") -> EngineLifecycleManager:
    """Get or create global lifecycle manager instance."""
    global _lifecycle_manager
    if _lifecycle_manager is None:
        _lifecycle_manager = EngineLifecycleManager(workspace_root)
    return _lifecycle_manager
