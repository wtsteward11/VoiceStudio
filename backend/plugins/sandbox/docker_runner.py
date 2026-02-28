"""
Docker-based Plugin Runner.

Phase 5A Enhancement: Implements PluginRunner interface using Docker containers
for stronger isolation than subprocess-based execution.

Docker provides:
    - Filesystem isolation via container root filesystem
    - Network isolation via container networking
    - Resource limits via cgroups (memory, CPU)
    - Process namespace isolation
    - User namespace isolation

Requirements:
    - Docker Engine or Docker Desktop installed
    - docker Python package (pip install docker)
    - Appropriate Docker permissions for the user

This is optional - the system falls back to subprocess runner
if Docker is not available.
"""

from __future__ import annotations

import asyncio
import io
import json
import logging
import os
import tarfile
import time
from dataclasses import dataclass, field
from enum import Enum
from pathlib import Path
from typing import Any, Awaitable, Callable, Dict, List, Optional

logger = logging.getLogger(__name__)

# Type alias for docker client
DockerClient = Any  # docker.DockerClient

# Try to import docker
try:
    import docker
    from docker.errors import ContainerError, DockerException, ImageNotFound, NotFound
    from docker.types import Mount, Ulimit

    DOCKER_AVAILABLE = True
except ImportError:
    docker = None
    DockerException = Exception
    NotFound = Exception
    ContainerError = Exception
    ImageNotFound = Exception
    Mount = None
    Ulimit = None
    DOCKER_AVAILABLE = False


class ContainerState(str, Enum):
    """States of a Docker container."""

    NOT_CREATED = "not_created"
    CREATING = "creating"
    STARTING = "starting"
    RUNNING = "running"
    INITIALIZING = "initializing"
    ACTIVE = "active"
    STOPPING = "stopping"
    STOPPED = "stopped"
    CRASHED = "crashed"
    REMOVED = "removed"


@dataclass
class DockerRunnerConfig:
    """Configuration for Docker-based plugin runner."""

    # Plugin info
    plugin_id: str
    plugin_path: Path
    entry_module: str

    # Docker settings
    image: str = "python:3.11-slim"
    container_name_prefix: str = "voicestudio-plugin-"
    network_mode: str = "none"  # Isolated by default
    auto_remove: bool = True  # Remove container on stop
    read_only_root: bool = True  # Read-only filesystem

    # Resource limits
    memory_limit_mb: Optional[int] = 512  # Memory limit in MB
    memory_swap_limit_mb: Optional[int] = None  # Swap limit (None = same as memory)
    cpu_limit: Optional[float] = 1.0  # CPU limit (1.0 = one full core)
    cpu_period: int = 100000  # CPU scheduler period (microseconds)
    pids_limit: int = 100  # Maximum number of processes

    # Timeouts (seconds)
    startup_timeout_sec: int = 30
    shutdown_timeout_sec: int = 10
    health_check_interval_sec: int = 5

    # Communication
    host_port_range_start: int = 49152  # Dynamic port range start
    use_unix_socket: bool = False  # Use Unix socket instead of TCP

    # Environment and permissions
    env_vars: Dict[str, str] = field(default_factory=dict)
    permissions: Dict[str, Any] = field(default_factory=dict)

    # Security options
    cap_drop: List[str] = field(default_factory=lambda: ["ALL"])  # Drop all capabilities
    security_opt: List[str] = field(default_factory=lambda: ["no-new-privileges:true"])

    # Working directory inside container
    container_workdir: str = "/plugin"

    def __post_init__(self):
        """Validate configuration."""
        if not self.plugin_id:
            raise ValueError("plugin_id is required")
        if not self.plugin_path or not self.plugin_path.exists():
            raise ValueError(f"plugin_path must exist: {self.plugin_path}")


@dataclass
class DockerRunner:
    """
    Manages a plugin running in a Docker container.

    Provides stronger isolation than subprocess-based execution
    at the cost of startup latency and resource overhead.

    Features:
        - Full filesystem isolation
        - Network isolation (no network by default)
        - Resource limits enforced by cgroups
        - Process namespace isolation
        - Read-only root filesystem option
        - Capability dropping

    Usage:
        runner = DockerRunner(config)
        await runner.start()
        result = await runner.invoke_capability("my_capability", {"param": "value"})
        await runner.stop()
    """

    config: DockerRunnerConfig
    state: ContainerState = ContainerState.NOT_CREATED

    # Docker handles
    _client: Optional[DockerClient] = field(default=None, repr=False)
    _container: Any = field(default=None, repr=False)
    _image_ready: bool = field(default=False, repr=False)

    # Communication
    _communication_task: Optional[asyncio.Task] = field(default=None, repr=False)
    _request_queue: asyncio.Queue = field(default_factory=asyncio.Queue, repr=False)
    _response_futures: Dict[str, asyncio.Future] = field(default_factory=dict, repr=False)
    _next_request_id: int = field(default=0, repr=False)

    # Health monitoring
    _health_task: Optional[asyncio.Task] = field(default=None, repr=False)
    _last_health_check: float = field(default=0.0, repr=False)

    # Callbacks
    _on_state_change: List[Callable[[ContainerState], None]] = field(
        default_factory=list, repr=False
    )
    _on_crash: List[Callable[[str], Awaitable[None]]] = field(default_factory=list, repr=False)

    def __post_init__(self):
        """Initialize internal state."""
        self._on_state_change = []
        self._on_crash = []
        self._response_futures = {}
        self._request_queue = asyncio.Queue()

        if not DOCKER_AVAILABLE:
            logger.warning("Docker package not installed. Install with: pip install docker")

    @staticmethod
    def is_available() -> bool:
        """Check if Docker is available on this system."""
        if not DOCKER_AVAILABLE:
            return False

        try:
            client = docker.from_env()
            client.ping()
            client.close()
            return True
        except Exception as e:
            logger.debug(f"Docker not available: {e}")
            return False

    @property
    def container_name(self) -> str:
        """Get the container name for this plugin."""
        # Sanitize plugin ID for Docker naming
        safe_id = self.config.plugin_id.replace(".", "-").replace("_", "-").lower()
        return f"{self.config.container_name_prefix}{safe_id}"

    @property
    def container_id(self) -> Optional[str]:
        """Get the Docker container ID if running."""
        if self._container:
            return self._container.short_id
        return None

    @property
    def is_running(self) -> bool:
        """Check if the container is running."""
        return self.state in (
            ContainerState.RUNNING,
            ContainerState.INITIALIZING,
            ContainerState.ACTIVE,
        )

    async def start(self) -> None:
        """
        Start the plugin in a Docker container.

        Creates the container, copies plugin files, and starts
        the plugin subprocess inside the container.

        Raises:
            RuntimeError: If already running or Docker unavailable
            TimeoutError: If startup times out
        """
        if self.state not in (
            ContainerState.NOT_CREATED,
            ContainerState.STOPPED,
            ContainerState.REMOVED,
        ):
            raise RuntimeError(f"Cannot start in state {self.state}")

        if not DOCKER_AVAILABLE:
            raise RuntimeError("Docker package not installed. Install with: pip install docker")

        self._set_state(ContainerState.CREATING)

        try:
            # Initialize Docker client
            self._client = docker.from_env()
            self._client.ping()  # Verify connection

            # Ensure image is available
            await self._ensure_image()

            # Create container
            await self._create_container()

            # Copy plugin files
            await self._copy_plugin_files()

            # Start container
            self._set_state(ContainerState.STARTING)
            await asyncio.to_thread(self._container.start)
            self._set_state(ContainerState.RUNNING)

            # Initialize plugin
            await self._initialize_plugin()

            # Start health monitoring
            self._start_health_monitoring()

            self._set_state(ContainerState.ACTIVE)
            logger.info(
                f"Plugin container started: {self.config.plugin_id} "
                f"(container: {self.container_id})"
            )

        except Exception as e:
            logger.error(f"Failed to start plugin container: {e}")
            await self._cleanup()
            self._set_state(ContainerState.CRASHED)
            raise

    async def stop(self, force: bool = False, timeout: Optional[int] = None) -> None:
        """
        Stop the plugin container.

        Args:
            force: If True, kill immediately without graceful shutdown
            timeout: Override shutdown timeout (seconds)
        """
        if not self.is_running:
            return

        self._set_state(ContainerState.STOPPING)
        self._stop_health_monitoring()

        timeout = timeout or self.config.shutdown_timeout_sec

        try:
            if force:
                await asyncio.to_thread(self._container.kill)
            else:
                # Graceful shutdown
                await asyncio.to_thread(self._container.stop, timeout=timeout)

        except NotFound:
            # Container already gone, nothing to stop
            logger.debug(f"Container already removed: {self.config.plugin_id}")
        except Exception as e:
            logger.warning(f"Error stopping container: {e}")
            # Force kill on error
            try:
                await asyncio.to_thread(self._container.kill)
            except Exception as kill_err:
                # Best-effort cleanup, already stopping
                logger.debug(f"Container kill during cleanup failed: {kill_err}")

        await self._cleanup()
        self._set_state(ContainerState.STOPPED)

        logger.info(f"Plugin container stopped: {self.config.plugin_id}")

    async def remove(self) -> None:
        """Remove the container and clean up resources."""
        await self.stop(force=True)

        if self._container:
            try:
                await asyncio.to_thread(self._container.remove, force=True)
            except NotFound:
                # Container already removed, nothing to clean up
                logger.debug(f"Container already removed: {self.config.plugin_id}")
            except Exception as e:
                logger.warning(f"Error removing container: {e}")

        self._container = None
        self._set_state(ContainerState.REMOVED)

    async def invoke_capability(
        self,
        capability: str,
        params: Optional[Dict[str, Any]] = None,
        timeout_sec: Optional[float] = None,
    ) -> Any:
        """
        Invoke a plugin capability.

        Args:
            capability: The capability name
            params: Optional parameters
            timeout_sec: Optional timeout in seconds

        Returns:
            The capability result

        Raises:
            RuntimeError: If plugin is not active
            TimeoutError: If invocation times out
        """
        if self.state != ContainerState.ACTIVE:
            raise RuntimeError(f"Cannot invoke capability in state {self.state}")

        request_id = self._get_next_request_id()
        request = {
            "jsonrpc": "2.0",
            "id": request_id,
            "method": "plugin.invokeCapability",
            "params": {"capability": capability, "params": params or {}},
        }

        return await self._send_request(request, timeout_sec)

    async def get_logs(self, tail: int = 100) -> str:
        """Get container logs."""
        if not self._container:
            return ""

        try:
            logs = await asyncio.to_thread(self._container.logs, tail=tail, decode=True)
            return logs
        except Exception as e:
            logger.warning(f"Failed to get container logs: {e}")
            return ""

    async def get_stats(self) -> Dict[str, Any]:
        """Get container resource usage statistics."""
        if not self._container:
            return {}

        try:
            stats = await asyncio.to_thread(self._container.stats, stream=False, decode=True)
            return self._parse_stats(stats)
        except Exception as e:
            logger.warning(f"Failed to get container stats: {e}")
            return {}

    def on_state_change(self, callback: Callable[[ContainerState], None]) -> None:
        """Register a callback for state changes."""
        self._on_state_change.append(callback)

    def on_crash(self, callback: Callable[[str], Awaitable[None]]) -> None:
        """Register a callback for crash events."""
        self._on_crash.append(callback)

    # =========================================================================
    # Private methods
    # =========================================================================

    def _set_state(self, new_state: ContainerState) -> None:
        """Update state and notify callbacks."""
        old_state = self.state
        self.state = new_state

        if old_state != new_state:
            logger.debug(
                f"Container state change: {self.config.plugin_id} "
                f"{old_state.value} -> {new_state.value}"
            )
            for callback in self._on_state_change:
                try:
                    callback(new_state)
                except Exception as e:
                    logger.warning(f"State change callback error: {e}")

    async def _ensure_image(self) -> None:
        """Ensure the Docker image is available."""
        if self._image_ready:
            return

        try:
            # Check if image exists locally
            await asyncio.to_thread(self._client.images.get, self.config.image)
            self._image_ready = True
            logger.debug(f"Image found: {self.config.image}")

        except ImageNotFound:
            # Pull the image
            logger.info(f"Pulling image: {self.config.image}")
            await asyncio.to_thread(self._client.images.pull, self.config.image)
            self._image_ready = True
            logger.info(f"Image pulled: {self.config.image}")

    async def _create_container(self) -> None:
        """Create the Docker container."""
        # Build environment variables
        env = {
            "VOICESTUDIO_PLUGIN_ID": self.config.plugin_id,
            "VOICESTUDIO_PLUGIN_PATH": self.config.container_workdir,
            "VOICESTUDIO_SUBPROCESS": "1",
            "VOICESTUDIO_DOCKER": "1",
            "PYTHONUNBUFFERED": "1",
            **self.config.env_vars,
        }

        # Build resource constraints
        mem_limit = None
        if self.config.memory_limit_mb:
            mem_limit = f"{self.config.memory_limit_mb}m"

        memswap_limit = None
        if self.config.memory_swap_limit_mb:
            memswap_limit = f"{self.config.memory_swap_limit_mb}m"
        elif self.config.memory_limit_mb:
            # Default: no swap (same as memory limit)
            memswap_limit = mem_limit

        cpu_quota = None
        if self.config.cpu_limit:
            cpu_quota = int(self.config.cpu_limit * self.config.cpu_period)

        # Build the entry command
        entry_cmd = self._build_entry_command()

        # Create container
        self._container = await asyncio.to_thread(
            self._client.containers.create,
            image=self.config.image,
            name=self.container_name,
            command=entry_cmd,
            environment=env,
            working_dir=self.config.container_workdir,
            network_mode=self.config.network_mode,
            read_only=self.config.read_only_root,
            mem_limit=mem_limit,
            memswap_limit=memswap_limit,
            cpu_quota=cpu_quota,
            cpu_period=self.config.cpu_period,
            pids_limit=self.config.pids_limit,
            cap_drop=self.config.cap_drop,
            security_opt=self.config.security_opt,
            auto_remove=self.config.auto_remove,
            stdin_open=True,
            tty=False,
            # Create writable temp directory
            tmpfs={"/tmp": "size=64m,mode=1777"},
        )

        logger.debug(f"Container created: {self.container_id}")

    def _build_entry_command(self) -> List[str]:
        """Build the command to run inside the container."""
        # Python entry script that bootstraps the plugin
        bootstrap_script = f"""
import sys
import os
import asyncio
import json

# Add plugin path
sys.path.insert(0, "{self.config.container_workdir}")

# Run the plugin
try:
    from voicestudio_sdk.subprocess_client import SubprocessClient
    client = SubprocessClient()
    asyncio.run(client.run("{self.config.entry_module}"))
except ImportError:
    # Fallback: minimal bootstrap
    from backend.plugins.sandbox.subprocess_bootstrap import run_plugin_subprocess
    asyncio.run(run_plugin_subprocess("{self.config.entry_module}"))
"""
        return ["python", "-c", bootstrap_script]

    async def _copy_plugin_files(self) -> None:
        """Copy plugin files into the container."""
        if not self._container:
            raise RuntimeError("Container not created")

        # Create a tar archive of the plugin directory
        plugin_path = self.config.plugin_path

        tar_buffer = io.BytesIO()
        with tarfile.open(fileobj=tar_buffer, mode="w") as tar:
            # Add all files from plugin directory
            for file_path in plugin_path.rglob("*"):
                if file_path.is_file():
                    arcname = file_path.relative_to(plugin_path)
                    tar.add(str(file_path), arcname=str(arcname))

        tar_buffer.seek(0)

        # Copy to container
        await asyncio.to_thread(
            self._container.put_archive,
            self.config.container_workdir,
            tar_buffer.getvalue(),
        )

        logger.debug(f"Plugin files copied to container: {self.config.plugin_id}")

    async def _initialize_plugin(self) -> None:
        """Initialize the plugin inside the container."""
        self._set_state(ContainerState.INITIALIZING)

        # Wait for container to be ready
        timeout = self.config.startup_timeout_sec
        start_time = time.time()

        while time.time() - start_time < timeout:
            try:
                # Refresh container status
                await asyncio.to_thread(self._container.reload)

                if self._container.status == "running":
                    # Container is running, plugin should be initializing
                    logger.debug(f"Container running: {self.config.plugin_id}")
                    return

                elif self._container.status in ("exited", "dead"):
                    # Container crashed during startup
                    logs = await self.get_logs(tail=50)
                    raise RuntimeError(f"Container exited during startup: {logs}")

            except NotFound:
                raise RuntimeError("Container was removed unexpectedly")

            await asyncio.sleep(0.5)

        raise TimeoutError(f"Plugin initialization timed out after {timeout}s")

    def _start_health_monitoring(self) -> None:
        """Start the health monitoring task."""
        if self._health_task and not self._health_task.done():
            return

        self._health_task = asyncio.create_task(self._health_loop())

    def _stop_health_monitoring(self) -> None:
        """Stop the health monitoring task."""
        if self._health_task:
            self._health_task.cancel()
            self._health_task = None

    async def _health_loop(self) -> None:
        """Periodic health check loop."""
        interval = self.config.health_check_interval_sec

        while self.is_running:
            try:
                await asyncio.sleep(interval)

                if not self._container:
                    continue

                # Check container status
                await asyncio.to_thread(self._container.reload)

                if self._container.status != "running":
                    logger.warning(
                        f"Container not running: {self.config.plugin_id} "
                        f"(status: {self._container.status})"
                    )
                    await self._handle_crash("Container stopped unexpectedly")
                    break

                self._last_health_check = time.time()

            except asyncio.CancelledError:
                break
            except NotFound:
                await self._handle_crash("Container not found")
                break
            except Exception as e:
                logger.warning(f"Health check error: {e}")

    async def _handle_crash(self, reason: str) -> None:
        """Handle container crash."""
        logger.error(f"Plugin container crashed: {self.config.plugin_id} - {reason}")

        old_state = self.state
        self._set_state(ContainerState.CRASHED)

        # Get logs for debugging
        logs = await self.get_logs(tail=100)
        if logs:
            logger.error(f"Container logs:\n{logs}")

        # Notify callbacks
        for callback in self._on_crash:
            try:
                await callback(reason)
            except Exception as e:
                logger.warning(f"Crash callback error: {e}")

    async def _cleanup(self) -> None:
        """Clean up resources."""
        self._stop_health_monitoring()

        # Cancel pending requests
        for future in self._response_futures.values():
            if not future.done():
                future.cancel()
        self._response_futures.clear()

        # Close Docker client
        if self._client:
            try:
                self._client.close()
            except Exception as e:
                # Best-effort cleanup during shutdown
                logger.debug(f"Docker client close failed: {e}")
            self._client = None

    def _get_next_request_id(self) -> str:
        """Get the next request ID."""
        self._next_request_id += 1
        return f"req-{self._next_request_id}"

    async def _send_request(
        self,
        request: Dict[str, Any],
        timeout_sec: Optional[float] = None,
    ) -> Any:
        """Send a request to the plugin in the container."""
        request_id = request["id"]

        # Create future for response
        future: asyncio.Future = asyncio.get_event_loop().create_future()
        self._response_futures[request_id] = future

        try:
            # Send via docker exec
            request_json = json.dumps(request)
            exec_result = await asyncio.to_thread(
                self._container.exec_run,
                ["python", "-c", f"print('{request_json}')"],
                stdin=True,
            )

            # For now, use a simple exec-based protocol
            # A more robust implementation would use a socket or pipe
            if exec_result.exit_code != 0:
                raise RuntimeError(f"Exec failed: {exec_result.output}")

            # Wait for response
            timeout = timeout_sec or 30.0
            result = await asyncio.wait_for(future, timeout=timeout)
            return result

        except asyncio.TimeoutError:
            raise TimeoutError(f"Request {request_id} timed out")
        finally:
            self._response_futures.pop(request_id, None)

    def _parse_stats(self, stats: Dict[str, Any]) -> Dict[str, Any]:
        """Parse Docker stats into a simpler format."""
        result = {
            "timestamp": time.time(),
            "container_id": self.container_id,
            "plugin_id": self.config.plugin_id,
        }

        # Memory stats
        memory = stats.get("memory_stats", {})
        if memory:
            result["memory"] = {
                "usage_bytes": memory.get("usage", 0),
                "limit_bytes": memory.get("limit", 0),
                "usage_mb": memory.get("usage", 0) / (1024 * 1024),
                "limit_mb": memory.get("limit", 0) / (1024 * 1024),
                "percent": 0,
            }
            if result["memory"]["limit_bytes"] > 0:
                result["memory"]["percent"] = (
                    result["memory"]["usage_bytes"] / result["memory"]["limit_bytes"] * 100
                )

        # CPU stats
        cpu = stats.get("cpu_stats", {})
        precpu = stats.get("precpu_stats", {})
        if cpu and precpu:
            cpu_delta = cpu.get("cpu_usage", {}).get("total_usage", 0) - precpu.get(
                "cpu_usage", {}
            ).get("total_usage", 0)
            system_delta = cpu.get("system_cpu_usage", 0) - precpu.get("system_cpu_usage", 0)
            online_cpus = cpu.get("online_cpus", 1)

            if system_delta > 0:
                result["cpu"] = {
                    "percent": (cpu_delta / system_delta) * online_cpus * 100,
                    "online_cpus": online_cpus,
                }

        return result


# =============================================================================
# Docker Runner Manager
# =============================================================================


@dataclass
class DockerRunnerManager:
    """
    Manager for Docker-based plugin runners.

    Provides lifecycle management for multiple Docker plugin containers.
    """

    _runners: Dict[str, DockerRunner] = field(default_factory=dict, repr=False)
    _docker_available: Optional[bool] = field(default=None, repr=False)

    def __post_init__(self):
        self._runners = {}

    def is_docker_available(self) -> bool:
        """Check if Docker is available."""
        if self._docker_available is None:
            self._docker_available = DockerRunner.is_available()
        return self._docker_available

    async def create_runner(
        self,
        config: DockerRunnerConfig,
    ) -> DockerRunner:
        """Create a new Docker runner for a plugin."""
        if not self.is_docker_available():
            raise RuntimeError("Docker is not available")

        if config.plugin_id in self._runners:
            raise ValueError(f"Runner already exists for plugin: {config.plugin_id}")

        runner = DockerRunner(config)
        self._runners[config.plugin_id] = runner

        logger.debug(f"Created Docker runner for: {config.plugin_id}")
        return runner

    def get_runner(self, plugin_id: str) -> Optional[DockerRunner]:
        """Get a runner by plugin ID."""
        return self._runners.get(plugin_id)

    async def remove_runner(self, plugin_id: str) -> bool:
        """Remove a runner and clean up its container."""
        runner = self._runners.pop(plugin_id, None)
        if runner:
            await runner.remove()
            return True
        return False

    async def stop_all(self, force: bool = False) -> None:
        """Stop all running containers."""
        tasks = [runner.stop(force=force) for runner in self._runners.values()]
        if tasks:
            await asyncio.gather(*tasks, return_exceptions=True)

    async def remove_all(self) -> None:
        """Remove all containers and clean up."""
        tasks = [runner.remove() for runner in self._runners.values()]
        if tasks:
            await asyncio.gather(*tasks, return_exceptions=True)
        self._runners.clear()

    def list_runners(self) -> List[str]:
        """List all plugin IDs with active runners."""
        return list(self._runners.keys())

    async def get_all_stats(self) -> Dict[str, Dict[str, Any]]:
        """Get stats for all running containers."""
        stats = {}
        for plugin_id, runner in self._runners.items():
            if runner.is_running:
                stats[plugin_id] = await runner.get_stats()
        return stats


# Global manager instance
_docker_manager: Optional[DockerRunnerManager] = None


def get_docker_manager() -> DockerRunnerManager:
    """Get the global Docker runner manager."""
    global _docker_manager
    if _docker_manager is None:
        _docker_manager = DockerRunnerManager()
    return _docker_manager


def reset_docker_manager() -> None:
    """Reset the global Docker runner manager (for testing)."""
    global _docker_manager
    _docker_manager = None
