"""
Engine Router Hook for Governor
Connects Governor + learners orchestration to engine router and runtime engines

Note: Migrated to use EnhancedRuntimeEngine (GAP-ENG-003)
"""

from __future__ import annotations

from typing import Any

from app.core.engines.protocols import EngineProtocol
from app.core.engines.router import router

RuntimeEngine: Any = None
RuntimeEngineManager: Any = None

try:
    from app.core.runtime.runtime_engine_enhanced import (
        EnhancedRuntimeEngine,
        EnhancedRuntimeEngineManager,
    )

    RuntimeEngine = EnhancedRuntimeEngine
    RuntimeEngineManager = EnhancedRuntimeEngineManager
except ImportError:
    try:
        from app.core.runtime.runtime_engine import RuntimeEngine as _FallbackRE
        from app.core.runtime.runtime_engine import RuntimeEngineManager as _FallbackREM

        RuntimeEngine = _FallbackRE
        RuntimeEngineManager = _FallbackREM
    # ALLOWED: bare except - optional dependency, import failure acceptable
    except ImportError:
        pass


class EngineHook:
    """
    Bridge between Governor and engine router.

    Provides a clean interface for Governor and learners to access
    engines without directly managing engine instances.
    """

    def __init__(self):
        """Initialize engine hook with router and runtime manager."""
        self.router = router
        self.runtime_manager = RuntimeEngineManager()

    def get_engine(self, engine_name: str, **kwargs) -> EngineProtocol | RuntimeEngine | None:
        """
        Get engine instance for Governor use.

        Tries class-based engine first, then runtime engine.

        Args:
            engine_name: Name of the engine to get
            **kwargs: Additional arguments for engine initialization

        Returns:
            Engine instance (class-based or runtime) or None if not found
        """
        # Try class-based engine first
        engine = self.router.get_engine(engine_name, **kwargs)
        if engine:
            return engine

        # Try runtime engine
        runtime_engine = self.runtime_manager.get_engine(engine_name)
        if runtime_engine:
            return runtime_engine

        return None

    def list_available_engines(self) -> list:
        """
        List engines available to Governor.

        Returns:
            List of available engine names (both class-based and runtime)
        """
        engines = set(self.router.list_engines())
        engines.update(self.runtime_manager.list_engines())
        return list(engines)

    def get_engine_for_task(self, task: str) -> EngineProtocol | RuntimeEngine | None:
        """
        Get an engine that supports a specific task.

        Args:
            task: Task name (e.g., "tts", "clone_infer")

        Returns:
            Engine instance or None
        """
        # Map task to task type
        task_type_map = {
            "tts": "tts",
            "clone_infer": "tts",
            "embed_voice": "tts",
            "text_to_image": "image_gen",
            "image_to_image": "image_gen",
            "image_to_video": "video_gen",
            "video_generation": "video_gen",
        }
        task_type = task_type_map.get(task, task)

        # Try runtime engines first (they have explicit task declarations)
        runtime_engine = self.runtime_manager.get_engine_for_task(task, prefer_default=True)
        if runtime_engine:
            return runtime_engine

        # Try class-based engines by task type
        class_engine = self.router.get_engine_for_task_type(task_type, prefer_default=True)
        if class_engine:
            return class_engine

        return None

    def get_default_engine(self, task_type: str) -> EngineProtocol | RuntimeEngine | None:
        """
        Get default engine for a task type.

        Args:
            task_type: Task type (e.g., "tts", "image_gen", "video_gen")

        Returns:
            Engine instance or None
        """
        from app.core.engines.config import get_engine_config

        config = get_engine_config()
        engine_id = config.get_default_engine(task_type)

        if engine_id:
            # Try runtime engine first
            runtime_engine = self.runtime_manager.get_engine(engine_id)
            if runtime_engine:
                return runtime_engine

            # Try class-based engine
            class_engine = self.router.get_engine(engine_id)
            if class_engine:
                return class_engine

        return None

    def register_engine(self, name: str, engine_class: type):
        """
        Register an engine class with the router.

        Args:
            name: Engine name
            engine_class: Engine class (must inherit EngineProtocol)
        """
        self.router.register_engine(name, engine_class)


# Global hook instance for easy access
hook = EngineHook()
