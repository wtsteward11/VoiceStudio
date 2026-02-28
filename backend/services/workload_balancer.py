"""
CPU/GPU Workload Balancer.

Task 1.2.3: Dynamic routing based on load.
Routes requests to appropriate compute resources based on current load.
"""

from __future__ import annotations

import asyncio
import contextlib
import logging
import time
from collections.abc import Awaitable, Callable
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from typing import Any

logger = logging.getLogger(__name__)


class ComputeDevice(Enum):
    """Available compute devices."""

    CPU = "cpu"
    GPU_0 = "gpu:0"
    GPU_1 = "gpu:1"
    GPU_2 = "gpu:2"
    GPU_3 = "gpu:3"
    AUTO = "auto"


@dataclass
class DeviceMetrics:
    """Metrics for a compute device."""

    device: ComputeDevice
    utilization_percent: float = 0.0
    memory_used_bytes: int = 0
    memory_total_bytes: int = 0
    active_tasks: int = 0
    queue_depth: int = 0
    avg_latency_ms: float = 0.0
    last_updated: datetime = field(default_factory=datetime.now)

    @property
    def memory_utilization(self) -> float:
        if self.memory_total_bytes == 0:
            return 0.0
        return self.memory_used_bytes / self.memory_total_bytes

    @property
    def is_available(self) -> bool:
        return self.utilization_percent < 95 and self.memory_utilization < 0.95


@dataclass
class WorkloadTask:
    """A task to be balanced."""

    id: str
    engine_type: str
    estimated_memory_bytes: int
    estimated_compute_ms: float
    prefer_gpu: bool = True
    min_memory_bytes: int = 0


@dataclass
class BalancerConfig:
    """Configuration for workload balancer."""

    enable_gpu: bool = True
    gpu_memory_threshold: float = 0.85
    gpu_utilization_threshold: float = 0.90
    cpu_utilization_threshold: float = 0.85
    prefer_gpu_for_engines: list[str] = field(
        default_factory=lambda: ["xtts", "rvc", "whisper", "tortoise", "bark", "styletts2"]
    )
    cpu_only_engines: list[str] = field(default_factory=list)
    metrics_update_interval: float = 5.0


class WorkloadBalancer:
    """
    Balances workloads across CPU and GPU resources.

    Features:
    - Real-time resource monitoring
    - Automatic device selection
    - Load-based routing
    - GPU fallback to CPU
    - Per-engine device preferences
    """

    def __init__(self, config: BalancerConfig | None = None):
        self.config = config or BalancerConfig()

        self._devices: dict[ComputeDevice, DeviceMetrics] = {}
        self._device_locks: dict[ComputeDevice, asyncio.Lock] = {}
        self._running = False
        self._metrics_task: asyncio.Task | None = None

        # Initialize devices
        self._initialize_devices()

    def _initialize_devices(self) -> None:
        """Initialize available compute devices."""
        # Always have CPU
        self._devices[ComputeDevice.CPU] = DeviceMetrics(
            device=ComputeDevice.CPU,
            memory_total_bytes=self._get_system_memory(),
        )
        self._device_locks[ComputeDevice.CPU] = asyncio.Lock()

        # Check for GPUs
        if self.config.enable_gpu:
            gpu_count = self._detect_gpus()
            for i in range(gpu_count):
                device = ComputeDevice(f"gpu:{i}")
                self._devices[device] = DeviceMetrics(
                    device=device,
                    memory_total_bytes=self._get_gpu_memory(i),
                )
                self._device_locks[device] = asyncio.Lock()

            logger.info(f"Initialized {gpu_count} GPU(s) and CPU for workload balancing")

    def _get_system_memory(self) -> int:
        """Get total system memory."""
        try:
            import psutil

            return psutil.virtual_memory().total
        except ImportError:
            return 16 * 1024 * 1024 * 1024  # Default 16GB

    def _detect_gpus(self) -> int:
        """Detect number of available GPUs."""
        try:
            import torch

            if torch.cuda.is_available():
                return torch.cuda.device_count()
        except ImportError:
            # Gap Analysis Fix: Log when torch is not available for GPU detection
            logger.debug("PyTorch not available for GPU detection, trying nvidia-smi")

        # Try nvidia-smi
        try:
            import subprocess

            result = subprocess.run(
                ["nvidia-smi", "--query-gpu=count", "--format=csv,noheader"],
                capture_output=True,
                text=True,
            )
            if result.returncode == 0:
                return len(result.stdout.strip().split("\n"))
        except Exception as e:
            # Gap Analysis Fix: Log nvidia-smi fallback failure
            logger.debug(f"nvidia-smi GPU detection failed: {e}")

        return 0

    def _get_gpu_memory(self, device_id: int) -> int:
        """Get GPU memory for a device."""
        try:
            import torch

            if torch.cuda.is_available():
                return torch.cuda.get_device_properties(device_id).total_memory
        except ImportError:
            # Gap Analysis Fix: Log when torch is not available for GPU memory query
            logger.debug(
                f"PyTorch not available for GPU {device_id} memory query, using default 8GB"
            )

        return 8 * 1024 * 1024 * 1024  # Default 8GB

    async def start(self) -> None:
        """Start the workload balancer."""
        self._running = True
        self._metrics_task = asyncio.create_task(self._update_metrics_loop())
        logger.info("Workload balancer started")

    async def stop(self) -> None:
        """Stop the workload balancer."""
        self._running = False
        if self._metrics_task:
            self._metrics_task.cancel()
            with contextlib.suppress(asyncio.CancelledError):
                await self._metrics_task
        logger.info("Workload balancer stopped")

    async def _update_metrics_loop(self) -> None:
        """Periodically update device metrics."""
        while self._running:
            try:
                await self._update_all_metrics()
                await asyncio.sleep(self.config.metrics_update_interval)
            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"Metrics update error: {e}")
                await asyncio.sleep(1.0)

    async def _update_all_metrics(self) -> None:
        """Update metrics for all devices."""
        # Update CPU metrics
        try:
            import psutil

            cpu = psutil.cpu_percent()
            mem = psutil.virtual_memory()

            self._devices[ComputeDevice.CPU].utilization_percent = cpu
            self._devices[ComputeDevice.CPU].memory_used_bytes = mem.used
            self._devices[ComputeDevice.CPU].last_updated = datetime.now()
        except ImportError:
            # psutil not available for CPU metrics
            logger.debug("psutil not available for CPU metrics update")

        # Update GPU metrics
        try:
            import torch

            if torch.cuda.is_available():
                for i in range(torch.cuda.device_count()):
                    device = ComputeDevice(f"gpu:{i}")
                    if device in self._devices:
                        mem_info = torch.cuda.memory_stats(i)
                        self._devices[device].memory_used_bytes = mem_info.get(
                            "allocated_bytes.all.current", 0
                        )
                        self._devices[device].last_updated = datetime.now()
        except ImportError:
            # PyTorch not available for GPU metrics
            logger.debug("PyTorch not available for GPU metrics update")

    def select_device(self, task: WorkloadTask) -> ComputeDevice:
        """
        Select the best device for a task.

        Returns:
            Selected compute device
        """
        # Check for engine-specific preferences
        if task.engine_type in self.config.cpu_only_engines:
            return ComputeDevice.CPU

        if not task.prefer_gpu or not self.config.enable_gpu:
            return ComputeDevice.CPU

        # Find best GPU
        best_gpu = self._select_best_gpu(task)
        if best_gpu:
            return best_gpu

        # Fallback to CPU
        cpu_metrics = self._devices.get(ComputeDevice.CPU)
        if cpu_metrics and cpu_metrics.is_available:
            return ComputeDevice.CPU

        # Force CPU if no other option
        return ComputeDevice.CPU

    def _select_best_gpu(self, task: WorkloadTask) -> ComputeDevice | None:
        """Select the best available GPU."""
        candidates = []

        for device, metrics in self._devices.items():
            if device == ComputeDevice.CPU:
                continue

            # Check if GPU has enough memory
            free_memory = metrics.memory_total_bytes - metrics.memory_used_bytes
            if free_memory < task.min_memory_bytes:
                continue

            # Check thresholds
            if metrics.utilization_percent > self.config.gpu_utilization_threshold * 100:
                continue
            if metrics.memory_utilization > self.config.gpu_memory_threshold:
                continue

            # Score: lower is better (utilization + memory pressure)
            score = metrics.utilization_percent + (metrics.memory_utilization * 100)
            candidates.append((device, score))

        if not candidates:
            return None

        # Return device with lowest score
        candidates.sort(key=lambda x: x[1])
        return candidates[0][0]

    async def execute_on_device(
        self,
        device: ComputeDevice,
        operation: Callable[[], Awaitable[Any]],
        task: WorkloadTask | None = None,
    ) -> Any:
        """
        Execute an operation on a specific device.

        Tracks metrics during execution.
        """
        self._device_locks.get(device, asyncio.Lock())
        metrics = self._devices.get(device)

        if metrics:
            metrics.active_tasks += 1

        start_time = time.time()

        try:
            # Set device context for GPU
            if device != ComputeDevice.CPU:
                device_id = int(device.value.split(":")[1])
                try:
                    import torch

                    with torch.cuda.device(device_id):
                        result = await operation()
                except ImportError:
                    result = await operation()
            else:
                result = await operation()

            # Update latency metrics
            if metrics:
                latency = (time.time() - start_time) * 1000
                # Exponential moving average
                metrics.avg_latency_ms = metrics.avg_latency_ms * 0.9 + latency * 0.1

            return result

        finally:
            if metrics:
                metrics.active_tasks = max(0, metrics.active_tasks - 1)

    async def route_and_execute(
        self,
        task: WorkloadTask,
        operation: Callable[[ComputeDevice], Awaitable[Any]],
    ) -> Any:
        """
        Automatically route and execute a task.

        Args:
            task: Task specification
            operation: Operation that accepts device as parameter
        """
        device = self.select_device(task)
        logger.debug(f"Routing task {task.id} to {device.value}")

        return await self.execute_on_device(
            device,
            lambda: operation(device),
            task,
        )

    def get_device_metrics(self, device: ComputeDevice) -> dict[str, Any] | None:
        """Get metrics for a specific device."""
        metrics = self._devices.get(device)
        if not metrics:
            return None

        return {
            "device": device.value,
            "utilization_percent": round(metrics.utilization_percent, 1),
            "memory_used_gb": round(metrics.memory_used_bytes / 1e9, 2),
            "memory_total_gb": round(metrics.memory_total_bytes / 1e9, 2),
            "memory_utilization_percent": round(metrics.memory_utilization * 100, 1),
            "active_tasks": metrics.active_tasks,
            "avg_latency_ms": round(metrics.avg_latency_ms, 1),
            "is_available": metrics.is_available,
            "last_updated": metrics.last_updated.isoformat(),
        }

    def get_stats(self) -> dict:
        """Get balancer statistics."""
        return {
            "devices": {d.value: self.get_device_metrics(d) for d in self._devices},
            "gpu_count": sum(1 for d in self._devices if d != ComputeDevice.CPU),
            "config": {
                "enable_gpu": self.config.enable_gpu,
                "gpu_memory_threshold": self.config.gpu_memory_threshold,
                "gpu_utilization_threshold": self.config.gpu_utilization_threshold,
            },
        }


# Global balancer instance
_balancer: WorkloadBalancer | None = None


def get_workload_balancer() -> WorkloadBalancer:
    """Get or create the global workload balancer."""
    global _balancer
    if _balancer is None:
        _balancer = WorkloadBalancer()
    return _balancer
