"""
Graceful Shutdown Orchestration.

Task 1.1.3: Coordinate shutdown across all subsystems.
Ensures clean shutdown of engines, connections, and resources.
"""

from __future__ import annotations

import asyncio
import logging
import signal
from collections.abc import Awaitable, Callable
from dataclasses import dataclass
from datetime import datetime
from enum import Enum, auto
from typing import Any

logger = logging.getLogger(__name__)


class ShutdownPhase(Enum):
    """Phases of the shutdown process."""

    RUNNING = auto()
    INITIATED = auto()
    DRAINING = auto()  # Stop accepting new requests
    COMPLETING = auto()  # Complete in-flight requests
    ENGINES = auto()  # Shutdown engines
    CONNECTIONS = auto()  # Close connections
    CLEANUP = auto()  # Final cleanup
    COMPLETED = auto()


@dataclass
class ShutdownHandler:
    """Handler for a shutdown task."""

    name: str
    phase: ShutdownPhase
    handler: Callable[[], Awaitable[None]]
    timeout_seconds: float = 10.0
    priority: int = 0  # Higher priority runs first within phase

    def __hash__(self) -> int:
        return hash(self.name)


@dataclass
class ShutdownResult:
    """Result of a shutdown handler execution."""

    handler_name: str
    phase: ShutdownPhase
    success: bool
    duration_ms: float
    error: str | None = None


class GracefulShutdownOrchestrator:
    """
    Orchestrates graceful shutdown across all backend subsystems.

    Features:
    - Phased shutdown with configurable timeouts
    - Handler registration with priority ordering
    - Signal handling integration
    - Shutdown progress tracking
    - Force shutdown after timeout
    """

    def __init__(
        self,
        total_timeout_seconds: float = 30.0,
        drain_timeout_seconds: float = 5.0,
    ):
        self.total_timeout_seconds = total_timeout_seconds
        self.drain_timeout_seconds = drain_timeout_seconds

        self._handlers: list[ShutdownHandler] = []
        self._phase = ShutdownPhase.RUNNING
        self._results: list[ShutdownResult] = []
        self._shutdown_event = asyncio.Event()
        self._shutdown_started: datetime | None = None
        self._in_flight_requests = 0
        self._accepting_requests = True
        self._lock = asyncio.Lock()

        # Callbacks
        self._on_phase_change: Callable[[ShutdownPhase], None] | None = None
        self._on_complete: Callable[[list[ShutdownResult]], None] | None = None

    @property
    def phase(self) -> ShutdownPhase:
        """Current shutdown phase."""
        return self._phase

    @property
    def is_shutting_down(self) -> bool:
        """Whether shutdown has been initiated."""
        return self._phase != ShutdownPhase.RUNNING

    @property
    def is_accepting_requests(self) -> bool:
        """Whether new requests are being accepted."""
        return self._accepting_requests

    @property
    def in_flight_count(self) -> int:
        """Number of in-flight requests."""
        return self._in_flight_requests

    def register_handler(self, handler: ShutdownHandler) -> None:
        """Register a shutdown handler."""
        self._handlers.append(handler)
        # Sort by phase, then priority (descending)
        self._handlers.sort(key=lambda h: (h.phase.value, -h.priority))
        logger.debug(f"Registered shutdown handler: {handler.name} (phase: {handler.phase.name})")

    def register(
        self,
        name: str,
        phase: ShutdownPhase,
        timeout_seconds: float = 10.0,
        priority: int = 0,
    ) -> Callable[[Callable[[], Awaitable[None]]], Callable[[], Awaitable[None]]]:
        """Decorator to register a shutdown handler."""

        def decorator(func: Callable[[], Awaitable[None]]) -> Callable[[], Awaitable[None]]:
            self.register_handler(
                ShutdownHandler(
                    name=name,
                    phase=phase,
                    handler=func,
                    timeout_seconds=timeout_seconds,
                    priority=priority,
                )
            )
            return func

        return decorator

    def set_callbacks(
        self,
        on_phase_change: Callable[[ShutdownPhase], None] | None = None,
        on_complete: Callable[[list[ShutdownResult]], None] | None = None,
    ) -> None:
        """Set event callbacks."""
        self._on_phase_change = on_phase_change
        self._on_complete = on_complete

    def _set_phase(self, phase: ShutdownPhase) -> None:
        """Update phase and fire callback."""
        old_phase = self._phase
        if old_phase != phase:
            self._phase = phase
            logger.info(f"Shutdown phase: {phase.name}")
            if self._on_phase_change:
                try:
                    self._on_phase_change(phase)
                except Exception as e:
                    logger.warning(f"Phase change callback error: {e}")

    async def request_started(self) -> bool:
        """
        Track a new request starting.
        Returns False if shutdown is in progress and request should be rejected.
        """
        if not self._accepting_requests:
            return False
        self._in_flight_requests += 1
        return True

    async def request_completed(self) -> None:
        """Track a request completing."""
        self._in_flight_requests = max(0, self._in_flight_requests - 1)

    async def initiate_shutdown(self, reason: str = "requested") -> None:
        """
        Initiate the graceful shutdown process.

        Args:
            reason: Reason for shutdown (for logging)
        """
        async with self._lock:
            if self._phase != ShutdownPhase.RUNNING:
                logger.warning("Shutdown already in progress")
                return

            logger.info(f"Initiating graceful shutdown: {reason}")
            self._shutdown_started = datetime.now()
            self._set_phase(ShutdownPhase.INITIATED)
            self._shutdown_event.set()

    async def execute_shutdown(self) -> list[ShutdownResult]:
        """
        Execute the full shutdown sequence.

        Returns list of results from all handlers.
        """
        if self._phase == ShutdownPhase.RUNNING:
            await self.initiate_shutdown()

        try:
            # Phase: Draining
            self._set_phase(ShutdownPhase.DRAINING)
            self._accepting_requests = False

            # Wait for in-flight requests to complete
            self._set_phase(ShutdownPhase.COMPLETING)
            drain_start = asyncio.get_event_loop().time()
            while self._in_flight_requests > 0:
                elapsed = asyncio.get_event_loop().time() - drain_start
                if elapsed > self.drain_timeout_seconds:
                    logger.warning(
                        f"Drain timeout, {self._in_flight_requests} requests still in flight"
                    )
                    break
                await asyncio.sleep(0.1)

            # Execute handlers by phase
            for phase in [ShutdownPhase.ENGINES, ShutdownPhase.CONNECTIONS, ShutdownPhase.CLEANUP]:
                self._set_phase(phase)
                await self._execute_phase_handlers(phase)

            self._set_phase(ShutdownPhase.COMPLETED)

        except Exception as e:
            logger.error(f"Shutdown error: {e}")

        if self._on_complete:
            try:
                self._on_complete(self._results)
            except Exception as e:
                logger.warning(f"Complete callback error: {e}")

        return self._results

    async def _execute_phase_handlers(self, phase: ShutdownPhase) -> None:
        """Execute all handlers for a phase."""
        phase_handlers = [h for h in self._handlers if h.phase == phase]

        for handler in phase_handlers:
            result = await self._execute_handler(handler)
            self._results.append(result)

    async def _execute_handler(self, handler: ShutdownHandler) -> ShutdownResult:
        """Execute a single shutdown handler with timeout."""
        start_time = asyncio.get_event_loop().time()

        try:
            await asyncio.wait_for(
                handler.handler(),
                timeout=handler.timeout_seconds,
            )
            duration_ms = (asyncio.get_event_loop().time() - start_time) * 1000
            logger.debug(f"Handler {handler.name} completed in {duration_ms:.1f}ms")
            return ShutdownResult(
                handler_name=handler.name,
                phase=handler.phase,
                success=True,
                duration_ms=duration_ms,
            )
        except asyncio.TimeoutError:
            duration_ms = (asyncio.get_event_loop().time() - start_time) * 1000
            logger.warning(f"Handler {handler.name} timed out after {handler.timeout_seconds}s")
            return ShutdownResult(
                handler_name=handler.name,
                phase=handler.phase,
                success=False,
                duration_ms=duration_ms,
                error="timeout",
            )
        except Exception as e:
            duration_ms = (asyncio.get_event_loop().time() - start_time) * 1000
            logger.error(f"Handler {handler.name} failed: {e}")
            return ShutdownResult(
                handler_name=handler.name,
                phase=handler.phase,
                success=False,
                duration_ms=duration_ms,
                error=str(e),
            )

    async def wait_for_shutdown(self) -> None:
        """Wait until shutdown is initiated."""
        await self._shutdown_event.wait()

    def setup_signal_handlers(self) -> None:
        """Set up signal handlers for graceful shutdown."""
        loop = asyncio.get_event_loop()

        def signal_handler(sig: signal.Signals) -> None:
            logger.info(f"Received signal {sig.name}")
            asyncio.create_task(self.initiate_shutdown(f"signal: {sig.name}"))

        for sig in (signal.SIGTERM, signal.SIGINT):
            try:

                def _make_sig_handler(s: signal.Signals = sig) -> None:
                    signal_handler(s)

                loop.add_signal_handler(sig, _make_sig_handler)
            except NotImplementedError:

                def _sig_cb(_s: int, _f: Any, captured_sig: signal.Signals = sig) -> None:
                    signal_handler(captured_sig)

                signal.signal(sig, _sig_cb)

    def get_status(self) -> dict:
        """Get current shutdown status."""
        return {
            "phase": self._phase.name,
            "is_shutting_down": self.is_shutting_down,
            "accepting_requests": self._accepting_requests,
            "in_flight_requests": self._in_flight_requests,
            "registered_handlers": len(self._handlers),
            "completed_results": len(self._results),
            "shutdown_started": (
                self._shutdown_started.isoformat() if self._shutdown_started else None
            ),
        }


# Global orchestrator instance
_orchestrator: GracefulShutdownOrchestrator | None = None


def get_shutdown_orchestrator() -> GracefulShutdownOrchestrator:
    """Get or create the global shutdown orchestrator."""
    global _orchestrator
    if _orchestrator is None:
        _orchestrator = GracefulShutdownOrchestrator()
    return _orchestrator
