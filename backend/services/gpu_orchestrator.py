"""
GPU Orchestrator Service

Phase 13.1: Multi-GPU Support
Intelligent GPU resource management and load balancing.

Features:
- Multi-GPU detection and management
- Automatic load balancing
- GPU memory monitoring
- Workload distribution
"""

from __future__ import annotations

import asyncio
import contextlib
import logging
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from typing import Any

logger = logging.getLogger(__name__)


class GPUVendor(Enum):
    """GPU vendor types."""

    NVIDIA = "nvidia"
    AMD = "amd"
    INTEL = "intel"
    APPLE = "apple"  # Apple Silicon
    UNKNOWN = "unknown"


class GPUMemoryState(Enum):
    """GPU memory utilization state."""

    LOW = "low"  # < 50%
    MEDIUM = "medium"  # 50-80%
    HIGH = "high"  # 80-95%
    CRITICAL = "critical"  # > 95%


@dataclass
class GPUInfo:
    """GPU device information."""

    device_id: int
    name: str
    vendor: GPUVendor
    total_memory_mb: int
    available_memory_mb: int
    compute_capability: tuple[int, int] | None
    temperature_c: float | None
    utilization_percent: float
    power_usage_watts: float | None
    is_available: bool

    @property
    def memory_utilization(self) -> float:
        if self.total_memory_mb == 0:
            return 0.0
        return (self.total_memory_mb - self.available_memory_mb) / self.total_memory_mb

    @property
    def memory_state(self) -> GPUMemoryState:
        util = self.memory_utilization
        if util < 0.5:
            return GPUMemoryState.LOW
        elif util < 0.8:
            return GPUMemoryState.MEDIUM
        elif util < 0.95:
            return GPUMemoryState.HIGH
        else:
            return GPUMemoryState.CRITICAL

    def to_dict(self) -> dict[str, Any]:
        return {
            "device_id": self.device_id,
            "name": self.name,
            "vendor": self.vendor.value,
            "total_memory_mb": self.total_memory_mb,
            "available_memory_mb": self.available_memory_mb,
            "memory_utilization": self.memory_utilization,
            "memory_state": self.memory_state.value,
            "compute_capability": self.compute_capability,
            "temperature_c": self.temperature_c,
            "utilization_percent": self.utilization_percent,
            "power_usage_watts": self.power_usage_watts,
            "is_available": self.is_available,
        }


@dataclass
class GPUTask:
    """Task assigned to a GPU."""

    task_id: str
    device_id: int
    task_type: str
    memory_required_mb: int
    started_at: datetime
    priority: int = 0
    metadata: dict[str, Any] = field(default_factory=dict)


class LoadBalancingStrategy(Enum):
    """GPU load balancing strategies."""

    ROUND_ROBIN = "round_robin"
    LEAST_LOADED = "least_loaded"
    MEMORY_AWARE = "memory_aware"
    AFFINITY = "affinity"


class GPUOrchestrator:
    """
    GPU resource orchestration service.

    Phase 13.1: Multi-GPU Support

    Features:
    - Automatic GPU detection
    - Load balancing across GPUs
    - Memory-aware task scheduling
    - GPU health monitoring
    """

    def __init__(self):
        self._initialized = False
        self._gpus: list[GPUInfo] = []
        self._active_tasks: dict[str, GPUTask] = {}
        self._gpu_locks: dict[int, asyncio.Lock] = {}
        self._strategy = LoadBalancingStrategy.MEMORY_AWARE
        self._cuda_available = False
        self._monitoring_task: asyncio.Task | None = None

        logger.info("GPUOrchestrator created")

    async def initialize(self) -> bool:
        """Initialize the GPU orchestrator."""
        if self._initialized:
            return True

        try:
            # Detect GPUs
            self._gpus = await self._detect_gpus()

            # Create locks for each GPU
            for gpu in self._gpus:
                self._gpu_locks[gpu.device_id] = asyncio.Lock()

            # Start monitoring
            self._monitoring_task = asyncio.create_task(self._monitor_gpus())

            self._initialized = True
            logger.info(f"GPUOrchestrator initialized with {len(self._gpus)} GPU(s)")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize GPUOrchestrator: {e}")
            return False

    async def _detect_gpus(self) -> list[GPUInfo]:
        """Detect available GPUs."""
        gpus = []

        # Try CUDA/NVIDIA
        try:
            import torch

            if torch.cuda.is_available():
                self._cuda_available = True
                for i in range(torch.cuda.device_count()):
                    props = torch.cuda.get_device_properties(i)

                    # Get memory info
                    torch.cuda.set_device(i)
                    total_memory = props.total_memory // (1024 * 1024)

                    try:
                        free_memory = torch.cuda.mem_get_info(i)[0] // (1024 * 1024)
                    except Exception:
                        free_memory = total_memory // 2  # Estimate

                    gpus.append(
                        GPUInfo(
                            device_id=i,
                            name=props.name,
                            vendor=GPUVendor.NVIDIA,
                            total_memory_mb=total_memory,
                            available_memory_mb=free_memory,
                            compute_capability=(props.major, props.minor),
                            temperature_c=None,  # Requires pynvml
                            utilization_percent=0.0,
                            power_usage_watts=None,
                            is_available=True,
                        )
                    )

                logger.info(f"Detected {len(gpus)} NVIDIA GPU(s)")
        except ImportError:
            logger.info("PyTorch not available, skipping CUDA detection")
        except Exception as e:
            logger.warning(f"CUDA detection failed: {e}")

        # If no GPUs found, check for CPU fallback
        if not gpus:
            logger.info("No GPUs detected, using CPU fallback")
            gpus.append(
                GPUInfo(
                    device_id=-1,
                    name="CPU",
                    vendor=GPUVendor.UNKNOWN,
                    total_memory_mb=0,
                    available_memory_mb=0,
                    compute_capability=None,
                    temperature_c=None,
                    utilization_percent=0.0,
                    power_usage_watts=None,
                    is_available=True,
                )
            )

        return gpus

    async def _monitor_gpus(self):
        """Continuous GPU monitoring loop."""
        while True:
            try:
                await asyncio.sleep(5)  # Monitor every 5 seconds

                if self._cuda_available:
                    await self._update_gpu_stats()

            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"GPU monitoring error: {e}")

    async def _update_gpu_stats(self):
        """Update GPU statistics."""
        try:
            import torch

            for gpu in self._gpus:
                if gpu.device_id >= 0 and gpu.vendor == GPUVendor.NVIDIA:
                    try:
                        free_memory = torch.cuda.mem_get_info(gpu.device_id)[0] // (1024 * 1024)
                        gpu.available_memory_mb = free_memory
                    except Exception as e:
                        # Gap Analysis Fix: Add debug logging for GPU memory query failures
                        logger.debug(f"Could not query GPU {gpu.device_id} memory: {e}")
        except ImportError:
            # Gap Analysis Fix: Log when torch is not available
            logger.debug("PyTorch not available for GPU memory refresh")

    async def acquire_gpu(
        self,
        task_id: str,
        task_type: str,
        memory_required_mb: int = 0,
        preferred_device: int | None = None,
    ) -> int | None:
        """
        Acquire a GPU for a task.

        Args:
            task_id: Unique task identifier
            task_type: Type of task (synthesis, training, etc.)
            memory_required_mb: Estimated memory requirement
            preferred_device: Optional preferred GPU device ID

        Returns:
            GPU device ID, or None if no GPU available
        """
        if not self._initialized:
            await self.initialize()

        # Select GPU based on strategy
        device_id = await self._select_gpu(
            memory_required_mb,
            preferred_device,
        )

        if device_id is None:
            logger.warning(f"No GPU available for task {task_id}")
            return None

        # Register task
        self._active_tasks[task_id] = GPUTask(
            task_id=task_id,
            device_id=device_id,
            task_type=task_type,
            memory_required_mb=memory_required_mb,
            started_at=datetime.now(),
        )

        logger.debug(f"Acquired GPU {device_id} for task {task_id}")
        return device_id

    async def release_gpu(self, task_id: str):
        """Release a GPU from a task."""
        if task_id in self._active_tasks:
            task = self._active_tasks.pop(task_id)
            logger.debug(f"Released GPU {task.device_id} from task {task_id}")

    async def _select_gpu(
        self,
        memory_required_mb: int,
        preferred_device: int | None,
    ) -> int | None:
        """Select the best GPU for a task."""
        available_gpus = [
            gpu
            for gpu in self._gpus
            if gpu.is_available and gpu.memory_state != GPUMemoryState.CRITICAL
        ]

        if not available_gpus:
            return None

        # If preferred device is valid and available, use it
        if preferred_device is not None:
            for gpu in available_gpus:
                if gpu.device_id == preferred_device:
                    if memory_required_mb == 0 or gpu.available_memory_mb >= memory_required_mb:
                        return gpu.device_id

        # Apply load balancing strategy
        if self._strategy == LoadBalancingStrategy.ROUND_ROBIN:
            return self._select_round_robin(available_gpus)
        elif self._strategy == LoadBalancingStrategy.LEAST_LOADED:
            return self._select_least_loaded(available_gpus)
        elif self._strategy == LoadBalancingStrategy.MEMORY_AWARE:
            return self._select_memory_aware(available_gpus, memory_required_mb)
        else:
            return available_gpus[0].device_id

    def _select_round_robin(self, gpus: list[GPUInfo]) -> int:
        """Round-robin GPU selection."""
        # Count tasks per GPU
        task_counts = {gpu.device_id: 0 for gpu in gpus}
        for task in self._active_tasks.values():
            if task.device_id in task_counts:
                task_counts[task.device_id] += 1

        # Select GPU with fewest tasks
        min_tasks = min(task_counts.values())
        for gpu in gpus:
            if task_counts[gpu.device_id] == min_tasks:
                return gpu.device_id

        return gpus[0].device_id

    def _select_least_loaded(self, gpus: list[GPUInfo]) -> int:
        """Select GPU with lowest utilization."""
        return min(gpus, key=lambda g: g.utilization_percent).device_id

    def _select_memory_aware(self, gpus: list[GPUInfo], required_mb: int) -> int:
        """Select GPU with sufficient memory and lowest utilization."""
        # Filter by memory requirement
        if required_mb > 0:
            suitable = [g for g in gpus if g.available_memory_mb >= required_mb]
            if not suitable:
                suitable = gpus  # Fall back to all GPUs
        else:
            suitable = gpus

        # Select by lowest memory utilization
        return min(suitable, key=lambda g: g.memory_utilization).device_id

    def set_strategy(self, strategy: LoadBalancingStrategy):
        """Set the load balancing strategy."""
        self._strategy = strategy
        logger.info(f"GPU load balancing strategy set to: {strategy.value}")

    def get_gpu_info(self, device_id: int | None = None) -> list[GPUInfo]:
        """Get GPU information."""
        if device_id is not None:
            return [g for g in self._gpus if g.device_id == device_id]
        return self._gpus.copy()

    def get_active_tasks(self, device_id: int | None = None) -> list[GPUTask]:
        """Get active tasks."""
        tasks = list(self._active_tasks.values())
        if device_id is not None:
            tasks = [t for t in tasks if t.device_id == device_id]
        return tasks

    def get_gpu_count(self) -> int:
        """Get the number of available GPUs."""
        return len([g for g in self._gpus if g.is_available and g.device_id >= 0])

    async def cleanup(self):
        """Cleanup resources."""
        if self._monitoring_task:
            self._monitoring_task.cancel()
            with contextlib.suppress(asyncio.CancelledError):
                await self._monitoring_task

        self._active_tasks.clear()
        logger.info("GPUOrchestrator cleaned up")


# Singleton instance
_gpu_orchestrator: GPUOrchestrator | None = None


def get_gpu_orchestrator() -> GPUOrchestrator:
    """Get or create the GPU orchestrator singleton."""
    global _gpu_orchestrator
    if _gpu_orchestrator is None:
        _gpu_orchestrator = GPUOrchestrator()
    return _gpu_orchestrator
