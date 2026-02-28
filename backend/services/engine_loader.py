"""
Engine Loader Configuration.

Task 3.3.12: Configure engine loader for the engine pool.
Provides actual engine loading/unloading functions.
"""

from __future__ import annotations

import asyncio
import logging
from collections.abc import Awaitable, Callable
from typing import Any

logger = logging.getLogger("backend.services.engine_loader")

# Type alias for engine instances
EngineInstance = Any


class EngineLoader:
    """
    Loads and unloads engine instances.

    This class provides the concrete implementation for engine lifecycle
    management, replacing the NotImplementedError defaults in EnginePool.
    """

    # Engine memory estimates in bytes
    ENGINE_MEMORY_ESTIMATES: dict[str, int] = {
        "xtts_v2": 4 * 1024 * 1024 * 1024,  # 4 GB
        "chatterbox": 2 * 1024 * 1024 * 1024,  # 2 GB
        "bark": 3 * 1024 * 1024 * 1024,  # 3 GB
        "piper": 512 * 1024 * 1024,  # 512 MB
        "whisper": 2 * 1024 * 1024 * 1024,  # 2 GB
        "faster_whisper": 1 * 1024 * 1024 * 1024,  # 1 GB
        "rvc": 2 * 1024 * 1024 * 1024,  # 2 GB
        "aeneas": 256 * 1024 * 1024,  # 256 MB
        "silero": 256 * 1024 * 1024,  # 256 MB
        "default": 1 * 1024 * 1024 * 1024,  # 1 GB default
    }

    def __init__(self):
        self._loaded_engines: dict[str, EngineInstance] = {}
        self._engine_factories: dict[str, Callable[..., Awaitable[EngineInstance]]] = {}
        self._register_default_factories()

    def _register_default_factories(self) -> None:
        """Register default engine factory functions."""
        # Register known engine types
        self._engine_factories["xtts_v2"] = self._load_xtts
        self._engine_factories["chatterbox"] = self._load_chatterbox
        self._engine_factories["whisper"] = self._load_whisper
        self._engine_factories["rvc"] = self._load_rvc
        self._engine_factories["piper"] = self._load_piper
        self._engine_factories["silero"] = self._load_silero

    def register_factory(
        self,
        engine_type: str,
        factory: Callable[..., Awaitable[EngineInstance]],
    ) -> None:
        """Register a custom engine factory."""
        self._engine_factories[engine_type] = factory
        logger.info(f"Registered engine factory: {engine_type}")

    async def load(
        self,
        engine_type: str,
        config: dict[str, Any],
    ) -> EngineInstance:
        """
        Load an engine instance.

        Args:
            engine_type: Type of engine to load (e.g., "xtts_v2", "whisper")
            config: Engine-specific configuration

        Returns:
            Loaded engine instance
        """
        logger.info(f"Loading engine: {engine_type}")

        # Check for registered factory
        if engine_type in self._engine_factories:
            factory = self._engine_factories[engine_type]
            instance = await factory(config)
            self._loaded_engines[engine_type] = instance
            return instance

        # Try dynamic import based on engine type
        instance = await self._dynamic_load(engine_type, config)
        self._loaded_engines[engine_type] = instance
        return instance

    async def unload(self, instance: EngineInstance) -> None:
        """
        Unload an engine instance.

        Args:
            instance: Engine instance to unload
        """
        # Find and remove from loaded engines
        for engine_type, loaded in list(self._loaded_engines.items()):
            if loaded is instance:
                del self._loaded_engines[engine_type]
                logger.info(f"Unloaded engine: {engine_type}")
                break

        # Call cleanup method if available
        if hasattr(instance, "cleanup"):
            await self._safe_call(instance.cleanup)
        elif hasattr(instance, "close"):
            await self._safe_call(instance.close)
        elif hasattr(instance, "unload"):
            await self._safe_call(instance.unload)

    async def _safe_call(self, method: Callable) -> None:
        """Safely call a cleanup method (sync or async)."""
        try:
            result = method()
            if asyncio.iscoroutine(result):
                await result
        except Exception as e:
            logger.warning(f"Cleanup method failed: {e}")

    def estimate_memory(self, engine_type: str) -> int:
        """Estimate memory usage for an engine type."""
        return self.ENGINE_MEMORY_ESTIMATES.get(
            engine_type, self.ENGINE_MEMORY_ESTIMATES["default"]
        )

    # -------------------------------------------------------------------------
    # Engine-specific loaders
    # -------------------------------------------------------------------------

    async def _load_xtts(self, config: dict[str, Any]) -> EngineInstance:
        """Load XTTS engine."""
        try:
            from app.core.engines.xtts_engine import XTTSEngine

            engine = XTTSEngine()

            # Initialize with config using available method
            if hasattr(engine, "initialize"):
                await asyncio.to_thread(engine.initialize)
            elif hasattr(engine, "_load_model"):
                await asyncio.to_thread(engine._load_model)

            return engine

        except ImportError as e:
            logger.warning(f"XTTS engine not available: {e}")
            return await self._create_stub_engine("xtts_v2", config)

    async def _load_chatterbox(self, config: dict[str, Any]) -> EngineInstance:
        """Load Chatterbox TTS engine."""
        try:
            from app.core.engines.chatterbox_engine import ChatterboxEngine

            engine = ChatterboxEngine()
            await asyncio.to_thread(engine.initialize)
            return engine

        except ImportError as e:
            logger.warning(f"Chatterbox engine not available: {e}")
            return await self._create_stub_engine("chatterbox", config)

    async def _load_whisper(self, config: dict[str, Any]) -> EngineInstance:
        """Load Whisper STT engine."""
        try:
            from app.core.engines.whisper_engine import WhisperEngine

            engine = WhisperEngine()
            if hasattr(engine, "initialize"):
                await asyncio.to_thread(engine.initialize)
            elif hasattr(engine, "_load_model"):
                await asyncio.to_thread(engine._load_model)
            return engine

        except ImportError as e:
            logger.warning(f"Whisper engine not available: {e}")
            return await self._create_stub_engine("whisper", config)

    async def _load_rvc(self, config: dict[str, Any]) -> EngineInstance:
        """Load RVC voice conversion engine."""
        try:
            from app.core.engines.rvc_engine import RVCEngine

            engine = RVCEngine()
            if hasattr(engine, "initialize"):
                await asyncio.to_thread(engine.initialize)
            elif hasattr(engine, "_load_models"):
                await asyncio.to_thread(engine._load_models)

            return engine

        except ImportError as e:
            logger.warning(f"RVC engine not available: {e}")
            return await self._create_stub_engine("rvc", config)

    async def _load_piper(self, config: dict[str, Any]) -> EngineInstance:
        """Load Piper TTS engine."""
        try:
            from app.core.engines.piper_engine import PiperEngine

            engine = PiperEngine()
            if hasattr(engine, "initialize"):
                await asyncio.to_thread(engine.initialize)
            return engine

        except ImportError as e:
            logger.warning(f"Piper engine not available: {e}")
            return await self._create_stub_engine("piper", config)

    async def _load_silero(self, config: dict[str, Any]) -> EngineInstance:
        """Load Silero VAD/TTS engine."""
        try:
            from app.core.engines.silero_engine import SileroEngine

            engine = SileroEngine()
            await asyncio.to_thread(engine.initialize)
            return engine

        except ImportError as e:
            logger.warning(f"Silero engine not available: {e}")
            return await self._create_stub_engine("silero", config)

    async def _dynamic_load(
        self,
        engine_type: str,
        config: dict[str, Any],
    ) -> EngineInstance:
        """Dynamically load an engine by type name."""
        # Try standard engine path
        try:
            module_name = f"app.core.engines.{engine_type}_engine"
            class_name = f"{engine_type.title().replace('_', '')}Engine"

            import importlib

            module = importlib.import_module(module_name)
            engine_class = getattr(module, class_name)

            engine = engine_class()
            if hasattr(engine, "initialize"):
                await asyncio.to_thread(engine.initialize)

            return engine

        except (ImportError, AttributeError) as e:
            logger.warning(f"Dynamic load failed for {engine_type}: {e}")
            return await self._create_stub_engine(engine_type, config)

    async def _create_stub_engine(
        self,
        engine_type: str,
        config: dict[str, Any],
    ) -> EngineInstance:
        """Create a stub engine for unavailable engines."""
        logger.warning(f"Creating stub engine for: {engine_type}")

        class StubEngine:
            """Stub engine returned when the real engine is unavailable.

            Returns graceful error responses instead of raising exceptions,
            allowing callers to handle engine unavailability gracefully.
            """

            def __init__(self, name: str):
                self.name = name
                self.is_stub = True
                self.available = False

            def synthesize(self, *args, **kwargs) -> dict[str, Any]:
                """Return error response indicating engine is unavailable."""
                logger.warning(f"Stub engine {self.name}: synthesize called but engine unavailable")
                return {
                    "success": False,
                    "error": f"Engine '{self.name}' is not available",
                    "error_code": "ENGINE_UNAVAILABLE",
                    "engine": self.name,
                    "is_stub": True,
                }

            def transcribe(self, *args, **kwargs) -> dict[str, Any]:
                """Return error response indicating engine is unavailable."""
                logger.warning(f"Stub engine {self.name}: transcribe called but engine unavailable")
                return {
                    "success": False,
                    "error": f"Engine '{self.name}' is not available",
                    "error_code": "ENGINE_UNAVAILABLE",
                    "engine": self.name,
                    "is_stub": True,
                }

            def process(self, *args, **kwargs) -> dict[str, Any]:
                """Return error response indicating engine is unavailable."""
                logger.warning(f"Stub engine {self.name}: process called but engine unavailable")
                return {
                    "success": False,
                    "error": f"Engine '{self.name}' is not available",
                    "error_code": "ENGINE_UNAVAILABLE",
                    "engine": self.name,
                    "is_stub": True,
                }

            def is_available(self) -> bool:
                """Check if engine is available (always False for stub)."""
                return False

            def cleanup(self):
                """No-op cleanup for stub engine."""
                pass

        return StubEngine(engine_type)


# Global engine loader instance
_engine_loader: EngineLoader | None = None


def get_engine_loader() -> EngineLoader:
    """Get or create the global engine loader."""
    global _engine_loader
    if _engine_loader is None:
        _engine_loader = EngineLoader()
    return _engine_loader


def configure_engine_pool_with_loader():
    """
    Configure the global engine pool with the engine loader.

    Call this during application startup to configure the engine pool
    with real loading/unloading functions.

    Usage:
        from backend.services.engine_loader import configure_engine_pool_with_loader
        configure_engine_pool_with_loader()
    """
    from backend.services.engine_pool import PoolConfig, configure_engine_pool

    loader = get_engine_loader()

    pool = configure_engine_pool(
        loader=loader.load,
        unloader=loader.unload,
        config=PoolConfig(
            max_engines=5,
            preload_engines=["piper"],  # Preload lightweight engine
            idle_timeout_seconds=300,
            enable_preloading=True,
        ),
    )

    # Override memory estimator
    pool._memory_estimator = loader.estimate_memory

    logger.info("Configured engine pool with engine loader")
    return pool
