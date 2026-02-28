"""
LLM Function Calling Service for VoiceStudio (Phase 9.1.6)

Manages tool/function definitions that the LLM can invoke during
conversations. Bridges LLM function calls to VoiceStudio backend services.
"""

from __future__ import annotations

import json
import logging
from collections.abc import Callable, Coroutine
from typing import Any

logger = logging.getLogger(__name__)

from app.core.engines.llm_interface import FunctionSpec


class FunctionRegistry:
    """
    Registry of callable functions available to the LLM.

    Functions are registered with their specification (name, description,
    parameters schema) and an async handler. When the LLM requests a
    function call, the registry dispatches to the appropriate handler.
    """

    def __init__(self):
        self._functions: dict[str, FunctionSpec] = {}
        self._handlers: dict[str, Callable[..., Coroutine[Any, Any, Any]]] = {}

    def register(
        self,
        name: str,
        description: str,
        parameters: dict[str, Any],
        handler: Callable[..., Coroutine[Any, Any, Any]],
    ) -> None:
        """Register a function that the LLM can call."""
        spec = FunctionSpec(
            name=name,
            description=description,
            parameters=parameters,
        )
        self._functions[name] = spec
        self._handlers[name] = handler
        logger.info(f"Registered LLM function: {name}")

    def unregister(self, name: str) -> None:
        """Remove a registered function."""
        self._functions.pop(name, None)
        self._handlers.pop(name, None)

    def get_specs(self) -> list[FunctionSpec]:
        """Get all function specifications for the LLM."""
        return list(self._functions.values())

    def get_spec(self, name: str) -> FunctionSpec | None:
        """Get a specific function specification."""
        return self._functions.get(name)

    async def execute(self, name: str, arguments: dict[str, Any]) -> Any:
        """
        Execute a registered function.

        Args:
            name: Function name.
            arguments: Function arguments parsed from LLM response.

        Returns:
            Function result.

        Raises:
            KeyError: If function not found.
            Exception: If function execution fails.
        """
        if name not in self._handlers:
            raise KeyError(f"Function '{name}' not registered")

        handler = self._handlers[name]
        logger.info(f"Executing LLM function call: {name}")
        try:
            result = await handler(**arguments)
            logger.info(f"Function '{name}' executed successfully")
            return result
        except Exception as exc:
            logger.error(f"Function '{name}' failed: {exc}")
            raise

    async def process_tool_calls(self, tool_calls: list[dict[str, Any]]) -> list[dict[str, Any]]:
        """
        Process tool calls from the LLM response.

        Args:
            tool_calls: List of tool call objects from LLM response.

        Returns:
            List of tool results ready to be sent back to the LLM.
        """
        results = []
        for call in tool_calls:
            call_id = call.get("id", "")
            function_data = call.get("function", {})
            func_name = function_data.get("name", "")
            try:
                arguments = json.loads(function_data.get("arguments", "{}"))
            except json.JSONDecodeError as e:
                # GAP-PY-001: Invalid JSON arguments from LLM, using empty dict
                logger.debug(f"Failed to parse LLM function arguments for '{func_name}': {e}")
                arguments = {}

            try:
                result = await self.execute(func_name, arguments)
                results.append(
                    {
                        "tool_call_id": call_id,
                        "role": "tool",
                        "content": json.dumps(result) if not isinstance(result, str) else result,
                    }
                )
            except Exception as exc:
                results.append(
                    {
                        "tool_call_id": call_id,
                        "role": "tool",
                        "content": json.dumps({"error": str(exc)}),
                    }
                )

        return results


# Singleton instance
_registry: FunctionRegistry | None = None


def get_function_registry() -> FunctionRegistry:
    """Get the global function registry singleton."""
    global _registry
    if _registry is None:
        _registry = FunctionRegistry()
        _register_default_functions(_registry)
    return _registry


def _register_default_functions(registry: FunctionRegistry) -> None:
    """Register default VoiceStudio functions for the LLM."""

    async def synthesize_voice(
        text: str, voice_id: str = "default", language: str = "en"
    ) -> dict[str, Any]:
        """Synthesize speech from text."""
        try:
            from backend.services.engine_service import get_engine_service

            service = get_engine_service()
            result = await service.synthesize(text=text, voice_id=voice_id, language=language)
            return {
                "status": "success",
                "audio_id": result.get("audio_id", ""),
                "message": f"Synthesized '{text[:50]}...'",
            }
        except Exception as exc:
            return {"status": "error", "message": str(exc)}

    async def list_voices() -> dict[str, Any]:
        """List available voice profiles."""
        try:
            from backend.services.engine_service import get_engine_service

            service = get_engine_service()
            voices = service.list_voices()
            return {"voices": voices[:20], "total": len(voices)}
        except Exception as exc:
            return {"status": "error", "message": str(exc)}

    async def list_engines() -> dict[str, Any]:
        """List available audio engines."""
        try:
            from backend.services.engine_service import get_engine_service

            service = get_engine_service()
            engines = service.list_engines()
            return {"engines": engines}
        except Exception as exc:
            return {"status": "error", "message": str(exc)}

    async def get_project_status(project_id: str = "current") -> dict[str, Any]:
        """Get the status of a project."""
        try:
            from backend.services.ProjectStoreService import get_project_store_service

            store = get_project_store_service()
            projects = store.list_projects()
            return {"projects": len(projects), "status": "active"}
        except Exception as exc:
            return {"status": "error", "message": str(exc)}

    # Register functions
    registry.register(
        name="synthesize_voice",
        description="Synthesize speech audio from text using a specified voice profile.",
        parameters={
            "type": "object",
            "properties": {
                "text": {"type": "string", "description": "Text to synthesize"},
                "voice_id": {
                    "type": "string",
                    "description": "Voice profile ID",
                    "default": "default",
                },
                "language": {"type": "string", "description": "Language code", "default": "en"},
            },
            "required": ["text"],
        },
        handler=synthesize_voice,
    )

    registry.register(
        name="list_voices",
        description="List all available voice profiles for synthesis.",
        parameters={"type": "object", "properties": {}},
        handler=list_voices,
    )

    registry.register(
        name="list_engines",
        description="List all available audio processing engines.",
        parameters={"type": "object", "properties": {}},
        handler=list_engines,
    )

    registry.register(
        name="get_project_status",
        description="Get the current status of a VoiceStudio project.",
        parameters={
            "type": "object",
            "properties": {
                "project_id": {"type": "string", "description": "Project ID", "default": "current"},
            },
        },
        handler=get_project_status,
    )
