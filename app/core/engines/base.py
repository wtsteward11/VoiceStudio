"""
Base Engine Protocol for VoiceStudio.

All engines must implement this protocol/interface.
Includes @traced decorator for distributed tracing integration (Phase 5.1.4).
"""

from __future__ import annotations

import gc
import json
import logging
import time
import types
import uuid
from abc import ABC, abstractmethod
from collections.abc import Callable
from datetime import datetime
from functools import wraps
from pathlib import Path
from typing import Any, Optional, TypeVar

logger = logging.getLogger(__name__)

# Lazy import for torch to avoid import errors when torch not installed
_torch: types.ModuleType | None = None
_torch_unavailable: bool = False


def _get_torch() -> types.ModuleType | None:
    """Lazy import torch to avoid import errors."""
    global _torch, _torch_unavailable
    if _torch is None and not _torch_unavailable:
        try:
            import torch

            _torch = torch
        except ImportError:
            _torch_unavailable = True
    return _torch


# Type variable for generic function decorator
F = TypeVar("F", bound=Callable[..., Any])

# Trace storage directory for local-first operation
_TRACE_DIR = Path(".voicestudio/traces")


def traced(operation_name: str | None = None, record_args: bool = False) -> Callable[[F], F]:
    """
    Decorator for distributed tracing of engine operations.

    Phase 5.1.4: Engine Tracing Integration

    Args:
        operation_name: Custom operation name (defaults to function name).
        record_args: Whether to record function arguments in trace.

    Returns:
        Decorated function with tracing.

    Example:
        @traced(operation_name="synthesize_audio")
        def synthesize(self, text: str) -> bytes:
            ...
    """

    def decorator(func: F) -> F:
        @wraps(func)
        def wrapper(*args: Any, **kwargs: Any) -> Any:
            op_name = operation_name or func.__name__
            trace_id = uuid.uuid4().hex
            span_id = uuid.uuid4().hex[:16]
            start_time = time.perf_counter()
            start_timestamp = datetime.utcnow().isoformat()

            # Extract class name if method
            class_name = ""
            if args and hasattr(args[0], "__class__"):
                class_name = args[0].__class__.__name__

            span_data: dict[str, Any] = {
                "trace_id": trace_id,
                "span_id": span_id,
                "operation_name": op_name,
                "class_name": class_name,
                "start_time": start_timestamp,
                "status": "IN_PROGRESS",
            }

            if record_args:
                # Safely serialize args (skip self)
                try:
                    safe_args = [str(a)[:200] for a in args[1:]] if len(args) > 1 else []
                    safe_kwargs = {k: str(v)[:200] for k, v in kwargs.items()}
                    span_data["args"] = safe_args
                    span_data["kwargs"] = safe_kwargs
                except Exception:
                    span_data["args_error"] = "Could not serialize arguments"

            logger.debug("Trace started: %s.%s [trace_id=%s]", class_name, op_name, trace_id)

            error_info = None
            result = None
            try:
                result = func(*args, **kwargs)
                span_data["status"] = "OK"
                return result
            except Exception as exc:
                span_data["status"] = "ERROR"
                error_info = {"type": type(exc).__name__, "message": str(exc)[:500]}
                span_data["error"] = error_info
                logger.error(
                    "Trace error: %s.%s [trace_id=%s] - %s: %s",
                    class_name,
                    op_name,
                    trace_id,
                    type(exc).__name__,
                    str(exc),
                )
                raise
            finally:
                duration_ms = (time.perf_counter() - start_time) * 1000
                span_data["duration_ms"] = round(duration_ms, 2)
                span_data["end_time"] = datetime.utcnow().isoformat()

                logger.debug(
                    "Trace completed: %s.%s [trace_id=%s] " "duration=%.2fms status=%s",
                    class_name,
                    op_name,
                    trace_id,
                    duration_ms,
                    span_data["status"],
                )

                # Write trace to local file for offline operation
                _write_trace(span_data)

        return wrapper  # type: ignore[return-value]

    return decorator


def _write_trace(span_data: dict[str, Any]) -> None:
    """
    Write trace span to local file storage.

    Traces are stored in .voicestudio/traces/ for local-first operation.
    """
    try:
        _TRACE_DIR.mkdir(parents=True, exist_ok=True)
        date_str = datetime.utcnow().strftime("%Y-%m-%d")
        trace_file = _TRACE_DIR / f"engine_traces_{date_str}.jsonl"
        with open(trace_file, "a", encoding="utf-8") as f:
            f.write(json.dumps(span_data) + "\n")
    except Exception as exc:
        # Don't let trace writing failures affect engine operations
        logger.warning("Failed to write trace: %s", exc)


import threading


class CancellationToken:
    """
    Cooperative cancellation token for long-running engine operations.

    Usage:
        token = CancellationToken()
        engine.synthesize(text, speaker_wav, cancellation_token=token)

        # From another thread:
        token.cancel()
    """

    def __init__(self) -> None:
        self._cancelled = threading.Event()

    def cancel(self) -> None:
        """Request cancellation of the operation."""
        self._cancelled.set()

    def is_cancelled(self) -> bool:
        """Check if cancellation has been requested."""
        return bool(self._cancelled.is_set())

    def raise_if_cancelled(self) -> None:
        """Raise OperationCancelledError if cancellation requested."""
        if self._cancelled.is_set():
            raise OperationCancelledError("Operation was cancelled")


class OperationCancelledError(Exception):
    """Raised when an operation is cancelled via CancellationToken."""

    pass


class EngineProtocol(ABC):
    """
    Base protocol that all VoiceStudio engines must implement.

    This ensures consistent interface across all engines (XTTS, Whisper, RVC, etc.)

    Required abstract methods:
        initialize() -> bool
        cleanup() -> None

    Synthesis convention:
        Subclasses that perform synthesis should implement a ``synthesize()``
        method with engine-specific parameters.  Because parameter signatures
        differ across engine types (e.g. XTTS requires ``reference_audio``,
        Bark accepts ``emotion``, Piper takes ``voice``), ``synthesize()`` is
        intentionally **not** declared as an abstract method on this base class.
        Engine adapter plugins rely on duck-typing to call it.
    """

    def __init__(self, device: str | None = None, gpu: bool = True):
        """
        Initialize engine with device selection.

        Args:
            device: Device to use ('cuda', 'cpu', or None for auto)
            gpu: Whether to use GPU if available
        """
        # Determine device with proper CUDA availability check
        if device:
            self.device = device
        elif gpu:
            # Check if CUDA is actually functional, not just "available"
            self.device = self._detect_cuda_device()
        else:
            self.device = "cpu"

        self._initialized = False
        self._current_cancellation_token: CancellationToken | None = None
        logger.info(f"{self.__class__.__name__} initialized (device: {self.device})")

    @staticmethod
    def _detect_cuda_device() -> str:
        """
        Detect if CUDA is truly functional (not just driver-available).

        Returns:
            'cuda' if CUDA works, 'cpu' otherwise.
        """
        torch = _get_torch()
        if torch is None:
            return "cpu"

        if not torch.cuda.is_available():
            return "cpu"

        # torch.cuda.is_available() can return True even when PyTorch
        # was compiled without CUDA support (just NVIDIA driver present).
        # Test actual CUDA functionality.
        try:
            # Try to create a small tensor on CUDA
            test_tensor = torch.zeros(1, device="cuda")
            del test_tensor
        except RuntimeError as e:
            error_msg = str(e).lower()
            if "cuda" in error_msg or "not compiled" in error_msg:
                logger.warning(
                    f"CUDA appears available but is not functional: {e}. "
                    "Using CPU instead. To enable GPU, install PyTorch with CUDA support."
                )
            return "cpu"
        except Exception as e:
            logger.warning(f"CUDA detection failed: {e}. Using CPU.")
            return "cpu"

        # Check if device architecture is supported by this PyTorch build.
        # RTX 50-series (sm_120) and other new GPUs may pass the tensor test
        # but fail during complex kernel dispatch (no kernel image available).
        try:
            cap = torch.cuda.get_device_capability(0)
            arch = f"sm_{cap[0]}{cap[1]}"
            supported = torch.cuda.get_arch_list()
            if supported and arch not in supported:
                logger.warning(
                    "GPU arch %s not supported by PyTorch (supports: %s). "
                    "Using CPU.",
                    arch,
                    " ".join(supported),
                )
                return "cpu"
        except Exception:
            pass  # If we can't check, trust the tensor test above

        return "cuda"

    @abstractmethod
    def initialize(self) -> bool:
        """
        Initialize the engine model.

        Returns:
            True if initialization successful, False otherwise

        Note:
            Implementations should apply @traced decorator for tracing.
        """
        ...

    @abstractmethod
    def cleanup(self) -> None:
        """
        Clean up resources and free memory.

        Note:
            Implementations should apply @traced decorator for tracing.
        """
        ...

    def is_initialized(self) -> bool:
        """Check if engine is initialized."""
        return self._initialized

    def get_device(self) -> str:
        """Get current device."""
        return self.device

    def get_info(self) -> dict[str, Any]:
        """
        Get engine information.

        Returns:
            Dictionary with engine metadata
        """
        return {
            "name": self.__class__.__name__,
            "version": getattr(self, "engine_version", "0.0.0"),
            "device": self.device,
            "initialized": self._initialized,
        }

    def health_check(self) -> dict[str, Any]:
        """
        Perform a health check on the engine.

        Optional method that engines can override to provide detailed health status.
        Default implementation returns basic initialization status.

        Returns:
            Dictionary with health status:
                - healthy: bool indicating overall health
                - initialized: bool indicating if engine is initialized
                - device: current device
                - details: optional additional details
        """
        return {
            "healthy": self._initialized,
            "initialized": self._initialized,
            "device": self.device,
            "details": None,
        }

    def get_resource_usage(self) -> dict[str, Any]:
        """
        Get current resource usage for this engine.

        Optional method that engines can override to report resource consumption.
        Default implementation returns GPU memory info if available.

        Returns:
            Dictionary with resource usage:
                - gpu_memory: GPU memory info (if available)
                - model_loaded: whether a model is currently loaded
        """
        return {"gpu_memory": self.get_gpu_memory_info(), "model_loaded": self._initialized}

    def warm_up(self) -> bool:
        """
        Warm up the engine by running a small inference.

        Optional method that engines can override to pre-warm the model.
        This can help reduce latency on the first real inference.

        Returns:
            True if warm-up succeeded, False otherwise
        """
        # Default: no-op, engines can override
        return self._initialized

    def set_cancellation_token(self, token: Optional[CancellationToken]) -> None:
        """Set the current cancellation token for the engine operation."""
        self._current_cancellation_token = token

    def check_cancellation(self) -> None:
        """
        Check if cancellation has been requested and raise if so.

        Call this periodically during long-running operations to support
        cooperative cancellation.

        Raises:
            OperationCancelledError: If cancellation was requested
        """
        if self._current_cancellation_token is not None:
            self._current_cancellation_token.raise_if_cancelled()

    def is_cancelled(self) -> bool:
        """Check if cancellation has been requested."""
        if self._current_cancellation_token is not None:
            return self._current_cancellation_token.is_cancelled()
        return False

    @staticmethod
    def cleanup_gpu_memory(force_gc: bool = True) -> dict[str, Any]:
        """
        Standardized GPU memory cleanup for all engines.

        This method provides a consistent way to release GPU memory across
        all engine implementations. Should be called in cleanup() methods
        and after large operations.

        Args:
            force_gc: Whether to force Python garbage collection before
                      clearing CUDA cache (recommended for thorough cleanup).

        Returns:
            Dictionary with cleanup results:
                - cuda_available: Whether CUDA is available
                - memory_freed: Whether cache was cleared
                - memory_before_mb: Memory allocated before cleanup (if available)
                - memory_after_mb: Memory allocated after cleanup (if available)
        """
        result = {
            "cuda_available": False,
            "memory_freed": False,
            "memory_before_mb": None,
            "memory_after_mb": None,
        }

        torch = _get_torch()
        if torch is None:
            return result

        if not torch.cuda.is_available():
            result["cuda_available"] = False
            # Still run gc for CPU memory
            if force_gc:
                gc.collect()
            return result

        result["cuda_available"] = True

        try:
            # Record memory before cleanup
            result["memory_before_mb"] = torch.cuda.memory_allocated() / (1024 * 1024)

            # Force garbage collection first to release Python references
            if force_gc:
                gc.collect()

            # Clear CUDA cache
            torch.cuda.empty_cache()

            # Record memory after cleanup
            result["memory_after_mb"] = torch.cuda.memory_allocated() / (1024 * 1024)
            result["memory_freed"] = True

            logger.debug(
                f"GPU memory cleanup: {result['memory_before_mb']:.1f}MB -> "
                f"{result['memory_after_mb']:.1f}MB"
            )
        except Exception as e:
            logger.warning(f"GPU memory cleanup failed: {e}")

        return result

    @staticmethod
    def get_gpu_memory_info() -> dict[str, Any]:
        """
        Get current GPU memory usage information.

        Returns:
            Dictionary with GPU memory info:
                - cuda_available: Whether CUDA is available
                - device_name: GPU device name
                - total_memory_mb: Total GPU memory in MB
                - allocated_mb: Currently allocated memory in MB
                - reserved_mb: Currently reserved memory in MB
                - free_mb: Estimated free memory in MB
        """
        result: dict[str, Any] = {
            "cuda_available": False,
            "device_name": None,
            "total_memory_mb": None,
            "allocated_mb": None,
            "reserved_mb": None,
            "free_mb": None,
        }

        torch = _get_torch()
        if torch is None or not torch.cuda.is_available():
            return result

        result["cuda_available"] = True

        try:
            props = torch.cuda.get_device_properties(0)
            result["device_name"] = props.name
            result["total_memory_mb"] = props.total_memory / (1024 * 1024)
            result["allocated_mb"] = torch.cuda.memory_allocated(0) / (1024 * 1024)
            result["reserved_mb"] = torch.cuda.memory_reserved(0) / (1024 * 1024)
            result["free_mb"] = result["total_memory_mb"] - result["allocated_mb"]
        except Exception as e:
            logger.warning(f"Failed to get GPU memory info: {e}")

        return result

    def _cleanup_model_references(self, *model_attrs: str) -> None:
        """
        Helper to clean up model references and free GPU memory.

        Args:
            *model_attrs: Names of instance attributes holding model references.
                          These will be set to None and then GPU memory will be
                          cleared.

        Example:
            def cleanup(self):
                self._cleanup_model_references('model', 'tokenizer', 'processor')
        """
        # Delete model references
        for attr in model_attrs:
            if hasattr(self, attr):
                try:
                    delattr(self, attr)
                except AttributeError:
                    # ALLOWED: Attribute may have been deleted between hasattr and delattr
                    # This is a race condition guard, not error suppression
                    logger.debug(f"Attribute {attr} already deleted during cleanup")
                setattr(self, attr, None)

        # Clear GPU memory
        self.cleanup_gpu_memory(force_gc=True)
