"""
Error Recovery Mechanisms

Provides automatic error recovery, retry logic, and fallback mechanisms for API operations.
"""

from __future__ import annotations

import asyncio
import logging
from collections.abc import Awaitable, Callable
from dataclasses import dataclass, field
from functools import wraps
from typing import Any, TypeVar, cast

logger = logging.getLogger(__name__)

T = TypeVar("T")

# Try to import resilience features
try:
    from app.core.resilience.circuit_breaker import CircuitBreaker, CircuitState
    from app.core.resilience.graceful_degradation import (
        DegradationLevel,
        GracefulDegradation,
    )
    from app.core.resilience.retry import RetryStrategy, retry_with_backoff

    HAS_RESILIENCE = True
except ImportError:
    HAS_RESILIENCE = False


@dataclass
class RetryConfig:
    """Configuration for retry behavior."""

    max_attempts: int = 3
    strategy: str = "exponential"
    initial_delay: float = 1.0
    max_delay: float = 30.0
    multiplier: float = 2.0
    retryable_exceptions: list[type[Exception]] = field(default_factory=lambda: [Exception])


class ErrorRecoveryManager:
    """Manages error recovery for API operations."""

    def __init__(self) -> None:
        """Initialize error recovery manager."""
        self.circuit_breakers: dict[str, CircuitBreaker] = {}
        self.degradation_handlers: dict[str, GracefulDegradation] = {}

    def get_circuit_breaker(self, service_name: str) -> CircuitBreaker | None:
        """Get or create circuit breaker for a service."""
        if not HAS_RESILIENCE:
            return None

        if service_name not in self.circuit_breakers:
            self.circuit_breakers[service_name] = CircuitBreaker(
                name=service_name, failure_threshold=5, timeout=60.0
            )

        return self.circuit_breakers[service_name]

    def get_degradation_handler(self, operation_name: str) -> GracefulDegradation | None:
        """Get or create graceful degradation handler for an operation."""
        if not HAS_RESILIENCE:
            return None

        if operation_name not in self.degradation_handlers:
            handler = GracefulDegradation(operation_name)
            self.degradation_handlers[operation_name] = handler

        return self.degradation_handlers[operation_name]

    def execute_with_recovery(
        self,
        func: Callable[[], T],
        service_name: str,
        operation_name: str,
        retry_config: RetryConfig | None = None,
        fallback: Callable[[], T] | None = None,
    ) -> T:
        """
        Execute function with error recovery mechanisms.

        Args:
            func: Function to execute
            service_name: Name of the service (for circuit breaker)
            operation_name: Name of the operation (for degradation)
            retry_config: Retry configuration
            fallback: Fallback function if operation fails

        Returns:
            Result of function execution
        """
        circuit_breaker = self.get_circuit_breaker(service_name)
        degradation_handler = self.get_degradation_handler(operation_name)

        if degradation_handler and fallback:
            degradation_handler.register_fallback(DegradationLevel.DEGRADED, fallback)

        if circuit_breaker:
            try:
                loop = asyncio.new_event_loop()
                try:
                    result: T = loop.run_until_complete(circuit_breaker.call(func))
                finally:
                    loop.close()
                return result
            except Exception as e:
                logger.warning(f"Circuit breaker triggered for {service_name}: {e}")
                if degradation_handler and fallback:
                    try:
                        loop = asyncio.new_event_loop()
                        try:
                            fallback_result: T = loop.run_until_complete(
                                degradation_handler.execute(fallback)
                            )
                        finally:
                            loop.close()
                        return fallback_result
                    except Exception as fallback_error:
                        logger.error(f"Fallback also failed for {operation_name}: {fallback_error}")
                        raise
                raise
        else:
            return func()

    async def execute_with_recovery_async(
        self,
        func: Callable[[], Awaitable[T]],
        service_name: str,
        operation_name: str,
        retry_config: RetryConfig | None = None,
        fallback: Callable[[], Awaitable[T]] | None = None,
    ) -> T:
        """
        Execute async function with error recovery mechanisms.

        Args:
            func: Async function to execute
            service_name: Name of the service (for circuit breaker)
            operation_name: Name of the operation (for degradation)
            retry_config: Retry configuration
            fallback: Fallback async function if operation fails

        Returns:
            Result of function execution
        """
        circuit_breaker = self.get_circuit_breaker(service_name)
        degradation_handler = self.get_degradation_handler(operation_name)

        if degradation_handler and fallback:
            degradation_handler.register_fallback(DegradationLevel.DEGRADED, fallback)

        if circuit_breaker:
            try:
                cb_func: Callable[..., Any] = cast(Callable[..., Any], func)
                result: T = await circuit_breaker.call(cb_func)
                return result
            except Exception as e:
                logger.warning(f"Circuit breaker triggered for {service_name}: {e}")
                if degradation_handler and fallback:
                    try:
                        dg_func: Callable[..., Any] = cast(Callable[..., Any], fallback)
                        fallback_result: T = await degradation_handler.execute(dg_func)
                        return fallback_result
                    except Exception as fallback_error:
                        logger.error(f"Fallback also failed for {operation_name}: {fallback_error}")
                        raise
                raise
        else:
            return await func()


_error_recovery_manager: ErrorRecoveryManager | None = None


def get_error_recovery_manager() -> ErrorRecoveryManager:
    """Get or create global error recovery manager."""
    global _error_recovery_manager
    if _error_recovery_manager is None:
        _error_recovery_manager = ErrorRecoveryManager()
    return _error_recovery_manager


def with_error_recovery(
    service_name: str,
    operation_name: str | None = None,
    retry_config: RetryConfig | None = None,
    fallback: Callable | None = None,
) -> Callable:
    """
    Decorator for adding error recovery to functions.

    Args:
        service_name: Name of the service
        operation_name: Name of the operation (defaults to function name)
        retry_config: Retry configuration
        fallback: Fallback function
    """

    def decorator(func: Callable) -> Callable:
        @wraps(func)
        def sync_wrapper(*args: object, **kwargs: object) -> object:
            manager = get_error_recovery_manager()
            op_name = operation_name or func.__name__
            return manager.execute_with_recovery(
                lambda: func(*args, **kwargs),
                service_name=service_name,
                operation_name=op_name,
                retry_config=retry_config,
                fallback=fallback,
            )

        @wraps(func)
        async def async_wrapper(*args: object, **kwargs: object) -> object:
            manager = get_error_recovery_manager()
            op_name = operation_name or func.__name__
            return await manager.execute_with_recovery_async(
                lambda: func(*args, **kwargs),
                service_name=service_name,
                operation_name=op_name,
                retry_config=retry_config,
                fallback=fallback,
            )

        if asyncio.iscoroutinefunction(func):
            return async_wrapper
        else:
            return sync_wrapper

    return decorator
