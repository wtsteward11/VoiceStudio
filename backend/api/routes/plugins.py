"""
Plugin Management Routes

Endpoints for plugin discovery, loading, unloading, configuration, status,
and Wasm plugin execution (Phase 6A).
"""

from __future__ import annotations

import importlib as _il
import json
import logging
from pathlib import Path
from typing import Any

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel, Field

from ..optimization import cache_response
from ..plugins import get_plugin_loader

PLUGIN_SERVICE_AVAILABLE = False
get_plugin_service: Any = None
try:
    _psm = _il.import_module("backend.plugins.plugin_service")
    get_plugin_service = _psm.get_plugin_service
    PLUGIN_SERVICE_AVAILABLE = True
except (ImportError, AttributeError):
    pass

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/plugins", tags=["plugins"])

# Plugin directory path
PLUGINS_DIRECTORY = Path("plugins")


class PluginManifest(BaseModel):
    """Plugin manifest information."""

    name: str
    version: str
    author: str
    description: str
    capabilities: dict[str, Any] = {}
    entry_points: dict[str, str] = {}
    dependencies: list[str] = []
    requirements: list[str] = []


class PluginInfo(BaseModel):
    """Plugin information."""

    plugin_id: str
    name: str
    version: str
    author: str
    description: str
    status: str  # loaded, unloaded, error
    directory: str
    capabilities: dict[str, Any] = {}
    is_enabled: bool = True
    error_message: str | None = None
    metadata: dict[str, Any] = {}


class PluginConfiguration(BaseModel):
    """Plugin configuration."""

    plugin_id: str
    config: dict[str, Any] = {}


class PluginLoadRequest(BaseModel):
    """Request to load a plugin."""

    plugin_id: str
    force_reload: bool = False


class PluginUnloadRequest(BaseModel):
    """Request to unload a plugin."""

    plugin_id: str


class PluginStatusResponse(BaseModel):
    """Plugin status response."""

    total_plugins: int
    loaded_plugins: int
    unloaded_plugins: int
    error_plugins: int
    plugins: list[PluginInfo]


def _discover_plugins() -> list[dict[str, Any]]:
    """
    Discover all plugins in the plugins directory.

    Returns:
        List of plugin manifests
    """
    plugins: list[dict[str, Any]] = []

    if not PLUGINS_DIRECTORY.exists():
        logger.warning(f"Plugins directory does not exist: {PLUGINS_DIRECTORY}")
        return plugins

    for plugin_dir in PLUGINS_DIRECTORY.iterdir():
        if not plugin_dir.is_dir():
            continue

        # Skip hidden directories
        if plugin_dir.name.startswith("."):
            continue

        manifest_path = plugin_dir / "manifest.json"
        if not manifest_path.exists():
            continue

        try:
            with open(manifest_path, encoding="utf-8") as f:
                manifest_data = json.load(f)

            plugin_id = manifest_data.get("name", plugin_dir.name)

            plugins.append(
                {
                    "plugin_id": plugin_id,
                    "name": manifest_data.get("name", plugin_dir.name),
                    "version": manifest_data.get("version", "1.0.0"),
                    "author": manifest_data.get("author", "Unknown"),
                    "description": manifest_data.get("description", ""),
                    "directory": str(plugin_dir),
                    "capabilities": manifest_data.get("capabilities", {}),
                    "entry_points": manifest_data.get("entry_points", {}),
                    "dependencies": manifest_data.get("dependencies", []),
                    "requirements": manifest_data.get("requirements", []),
                }
            )
        except Exception as e:
            logger.error(f"Failed to load manifest from {manifest_path}: {e}")
            continue

    return plugins


def _get_plugin_status(plugin_id: str) -> str:
    """
    Get the status of a plugin.

    Args:
        plugin_id: Plugin identifier

    Returns:
        Plugin status (loaded, unloaded, error)
    """
    loader = get_plugin_loader()
    if loader is None:
        return "unloaded"

    if plugin_id in loader.list_plugins():
        return "loaded"

    return "unloaded"


@router.get("", response_model=PluginStatusResponse)
@cache_response(ttl=60)  # Cache for 60 seconds (plugin list may change)
async def list_plugins():
    """List all available plugins."""
    try:
        discovered_plugins = _discover_plugins()
        loader = get_plugin_loader()

        loaded_plugin_names = set()
        if loader is not None:
            loaded_plugin_names = set(loader.list_plugins())

        plugins = []
        loaded_count = 0
        unloaded_count = 0
        error_count = 0

        for plugin_data in discovered_plugins:
            plugin_id = plugin_data["plugin_id"]
            status = _get_plugin_status(plugin_id)

            if status == "loaded":
                loaded_count += 1
            elif status == "error":
                error_count += 1
            else:
                unloaded_count += 1

            # Get plugin info from loader if loaded
            plugin_info = None
            error_message = None
            if loader is not None and plugin_id in loaded_plugin_names:
                plugin_info = loader.get_plugin_info(plugin_id)
                if plugin_info is None:
                    error_message = "Plugin loaded but info not available"

            plugins.append(
                PluginInfo(
                    plugin_id=plugin_id,
                    name=plugin_data["name"],
                    version=plugin_data["version"],
                    author=plugin_data["author"],
                    description=plugin_data["description"],
                    status=status,
                    directory=plugin_data["directory"],
                    capabilities=plugin_data["capabilities"],
                    is_enabled=status == "loaded",
                    error_message=error_message,
                    metadata={
                        "entry_points": plugin_data["entry_points"],
                        "dependencies": plugin_data["dependencies"],
                        "requirements": plugin_data["requirements"],
                    },
                )
            )

        return PluginStatusResponse(
            total_plugins=len(plugins),
            loaded_plugins=loaded_count,
            unloaded_plugins=unloaded_count,
            error_plugins=error_count,
            plugins=plugins,
        )
    except Exception as e:
        logger.error(f"Failed to list plugins: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to list plugins: {e!s}",
        ) from e


@router.get("/{plugin_id}", response_model=PluginInfo)
@cache_response(ttl=300)  # Cache for 5 minutes (plugin info is relatively static)
async def get_plugin(plugin_id: str):
    """Get information about a specific plugin."""
    try:
        discovered_plugins = _discover_plugins()

        plugin_data = None
        for p in discovered_plugins:
            if p["plugin_id"] == plugin_id:
                plugin_data = p
                break

        if plugin_data is None:
            raise HTTPException(
                status_code=404,
                detail=f"Plugin '{plugin_id}' not found",
            )

        status = _get_plugin_status(plugin_id)
        loader = get_plugin_loader()

        error_message = None
        if loader is not None:
            loader.get_plugin_info(plugin_id)

        return PluginInfo(
            plugin_id=plugin_id,
            name=plugin_data["name"],
            version=plugin_data["version"],
            author=plugin_data["author"],
            description=plugin_data["description"],
            status=status,
            directory=plugin_data["directory"],
            capabilities=plugin_data["capabilities"],
            is_enabled=status == "loaded",
            error_message=error_message,
            metadata={
                "entry_points": plugin_data["entry_points"],
                "dependencies": plugin_data["dependencies"],
                "requirements": plugin_data["requirements"],
            },
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get plugin '{plugin_id}': {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to get plugin: {e!s}",
        ) from e


@router.get("/{plugin_id}/manifest", response_model=PluginManifest)
@cache_response(ttl=600)  # Cache for 10 minutes (plugin manifest is static)
async def get_plugin_manifest(plugin_id: str):
    """Get the manifest for a specific plugin."""
    try:
        discovered_plugins = _discover_plugins()

        plugin_data = None
        for p in discovered_plugins:
            if p["plugin_id"] == plugin_id:
                plugin_data = p
                break

        if plugin_data is None:
            raise HTTPException(
                status_code=404,
                detail=f"Plugin '{plugin_id}' not found",
            )

        return PluginManifest(
            name=plugin_data["name"],
            version=plugin_data["version"],
            author=plugin_data["author"],
            description=plugin_data["description"],
            capabilities=plugin_data["capabilities"],
            entry_points=plugin_data["entry_points"],
            dependencies=plugin_data["dependencies"],
            requirements=plugin_data["requirements"],
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get plugin manifest '{plugin_id}': {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to get plugin manifest: {e!s}",
        ) from e


@router.post("/{plugin_id}/load")
async def load_plugin(plugin_id: str, request: PluginLoadRequest | None = None):
    """
    Load a plugin dynamically.

    GAP-PY-004: Wired to PluginService.load_plugin() for actual lifecycle management.
    """
    try:
        # Verify plugin exists
        discovered_plugins = _discover_plugins()

        plugin_data = None
        for p in discovered_plugins:
            if p["plugin_id"] == plugin_id:
                plugin_data = p
                break

        if plugin_data is None:
            raise HTTPException(
                status_code=404,
                detail=f"Plugin '{plugin_id}' not found",
            )

        # Check if already loaded (unless force_reload requested)
        status = _get_plugin_status(plugin_id)
        if status == "loaded" and (request is None or not request.force_reload):
            return {
                "message": f"Plugin '{plugin_id}' is already loaded",
                "plugin_id": plugin_id,
                "status": "loaded",
            }

        # GAP-PY-004: Wire to PluginService lifecycle
        if not PLUGIN_SERVICE_AVAILABLE:
            raise HTTPException(
                status_code=501,
                detail="Plugin loading not available: PluginService not initialized. "
                "Plugins are loaded at application startup.",
            )

        plugin_service = get_plugin_service()

        # Force reload requires unload first
        if status == "loaded" and request and request.force_reload:
            await plugin_service.unload_plugin(plugin_id)

        success = await plugin_service.load_plugin(plugin_id)

        if success:
            return {
                "message": f"Plugin '{plugin_id}' loaded successfully",
                "plugin_id": plugin_id,
                "status": "loaded",
            }
        else:
            raise HTTPException(
                status_code=500,
                detail=f"Failed to load plugin '{plugin_id}'. Check server logs for details.",
            )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to load plugin '{plugin_id}': {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to load plugin: {e!s}",
        ) from e


@router.post("/{plugin_id}/unload")
async def unload_plugin(plugin_id: str, request: PluginUnloadRequest | None = None):
    """
    Unload a plugin dynamically.

    GAP-PY-004: Wired to PluginService.unload_plugin() for actual lifecycle management.
    """
    try:
        # Verify plugin exists
        discovered_plugins = _discover_plugins()

        plugin_data = None
        for p in discovered_plugins:
            if p["plugin_id"] == plugin_id:
                plugin_data = p
                break

        if plugin_data is None:
            raise HTTPException(
                status_code=404,
                detail=f"Plugin '{plugin_id}' not found",
            )

        # Check if already unloaded
        status = _get_plugin_status(plugin_id)
        if status == "unloaded":
            return {
                "message": f"Plugin '{plugin_id}' is already unloaded",
                "plugin_id": plugin_id,
                "status": "unloaded",
            }

        # GAP-PY-004: Wire to PluginService lifecycle
        if not PLUGIN_SERVICE_AVAILABLE:
            raise HTTPException(
                status_code=501,
                detail="Plugin unloading not available: PluginService not initialized.",
            )

        plugin_service = get_plugin_service()
        success = await plugin_service.unload_plugin(plugin_id)

        if success:
            return {
                "message": f"Plugin '{plugin_id}' unloaded successfully",
                "plugin_id": plugin_id,
                "status": "unloaded",
            }
        else:
            raise HTTPException(
                status_code=500,
                detail=f"Failed to unload plugin '{plugin_id}'. Check server logs for details.",
            )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to unload plugin '{plugin_id}': {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to unload plugin: {e!s}",
        ) from e


@router.get("/{plugin_id}/config", response_model=PluginConfiguration)
@cache_response(ttl=60)  # Cache for 60 seconds (plugin config may change)
async def get_plugin_config(plugin_id: str):
    """Get configuration for a plugin."""
    try:
        discovered_plugins = _discover_plugins()

        plugin_data = None
        for p in discovered_plugins:
            if p["plugin_id"] == plugin_id:
                plugin_data = p
                break

        if plugin_data is None:
            raise HTTPException(
                status_code=404,
                detail=f"Plugin '{plugin_id}' not found",
            )

        # Load plugin configuration from file
        plugin_dir = Path(plugin_data["directory"])
        config_file = plugin_dir / "config.json"

        config = {}
        if config_file.exists():
            try:
                with open(config_file, encoding="utf-8") as f:
                    config = json.load(f)
            except Exception as e:
                logger.warning(f"Failed to load config for plugin '{plugin_id}': {e}")

        return PluginConfiguration(
            plugin_id=plugin_id,
            config=config,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get plugin config '{plugin_id}': {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to get plugin config: {e!s}",
        ) from e


@router.put("/{plugin_id}/config", response_model=PluginConfiguration)
async def update_plugin_config(plugin_id: str, config: dict[str, Any]):
    """Update configuration for a plugin."""
    try:
        discovered_plugins = _discover_plugins()

        plugin_data = None
        for p in discovered_plugins:
            if p["plugin_id"] == plugin_id:
                plugin_data = p
                break

        if plugin_data is None:
            raise HTTPException(
                status_code=404,
                detail=f"Plugin '{plugin_id}' not found",
            )

        # Save plugin configuration to file
        plugin_dir = Path(plugin_data["directory"])
        plugin_dir.mkdir(parents=True, exist_ok=True)
        config_file = plugin_dir / "config.json"

        try:
            with open(config_file, "w", encoding="utf-8") as f:
                json.dump(config, f, indent=2)

            logger.info(f"Updated config for plugin '{plugin_id}'")

            return PluginConfiguration(
                plugin_id=plugin_id,
                config=config,
            )
        except Exception as e:
            logger.error(f"Failed to save config for plugin '{plugin_id}': {e}")
            raise HTTPException(
                status_code=500,
                detail=f"Failed to save plugin config: {e!s}",
            ) from e
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to update plugin config '{plugin_id}': {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to update plugin config: {e!s}",
        ) from e


# =============================================================================
# Phase 6A: Wasm Plugin Execution Routes (D-3)
# =============================================================================


class WasmExecutionRequest(BaseModel):
    """Request to execute a Wasm plugin function."""

    function_name: str | None = Field(
        default=None,
        description="Function to call in the Wasm module (default: main or _start)",
    )
    input_data: str | None = Field(
        default=None,
        description="Base64-encoded input data to pass to the function",
    )
    capabilities: list[str] = Field(
        default_factory=list,
        description="List of capability tokens to grant (e.g., 'fs_read', 'network')",
    )
    memory_limit_mb: int = Field(
        default=64,
        ge=1,
        le=1024,
        description="Maximum memory in MB (1-1024)",
    )
    timeout_seconds: float = Field(
        default=30.0,
        ge=0.1,
        le=300.0,
        description="Execution timeout in seconds (0.1-300)",
    )


class WasmExecutionResponse(BaseModel):
    """Response from Wasm plugin execution."""

    success: bool
    output: Any | None = None
    error: str | None = None
    execution_time_ms: float = 0.0
    metrics: dict[str, Any] = Field(default_factory=dict)


class WasmPluginInfo(BaseModel):
    """Information about a Wasm plugin."""

    plugin_id: str
    name: str
    version: str
    wasm_path: str | None = None
    capabilities_required: list[str] = Field(default_factory=list)
    is_loaded: bool = False


@router.get("/wasm", response_model=list[WasmPluginInfo])
async def list_wasm_plugins():
    """List all available Wasm plugins."""
    if not PLUGIN_SERVICE_AVAILABLE:
        raise HTTPException(
            status_code=503,
            detail="Plugin service not available",
        )

    try:
        service = get_plugin_service()
        wasm_plugins = await service.list_wasm_plugins()

        result = []
        for plugin_info in wasm_plugins:
            wasm_path = service.get_wasm_path(plugin_info.manifest.name)
            result.append(
                WasmPluginInfo(
                    plugin_id=plugin_info.manifest.name,
                    name=plugin_info.manifest.name,
                    version=plugin_info.manifest.version,
                    wasm_path=str(wasm_path) if wasm_path else None,
                    capabilities_required=getattr(
                        plugin_info.manifest, "capabilities_required", []
                    ),
                    is_loaded=plugin_info.instance is not None,
                )
            )

        return result
    except Exception as e:
        logger.error(f"Failed to list Wasm plugins: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to list Wasm plugins: {e!s}",
        ) from e


@router.get("/{plugin_id}/wasm/status")
async def get_wasm_plugin_status(plugin_id: str):
    """Get the Wasm runtime status for a plugin."""
    if not PLUGIN_SERVICE_AVAILABLE:
        raise HTTPException(
            status_code=503,
            detail="Plugin service not available",
        )

    try:
        service = get_plugin_service()

        # Check if plugin exists
        plugin_info = service.get_plugin(plugin_id)
        if plugin_info is None:
            raise HTTPException(
                status_code=404,
                detail=f"Plugin '{plugin_id}' not found",
            )

        # Check if it's a Wasm plugin
        is_wasm = service.is_wasm_plugin(plugin_id)
        wasm_path = service.get_wasm_path(plugin_id) if is_wasm else None

        # Get runtime availability
        try:
            from backend.plugins.wasm.wasm_runner import WASMTIME_AVAILABLE
        except ImportError:
            WASMTIME_AVAILABLE = False

        return {
            "plugin_id": plugin_id,
            "is_wasm_plugin": is_wasm,
            "wasm_path": str(wasm_path) if wasm_path else None,
            "wasm_runtime_available": WASMTIME_AVAILABLE if is_wasm else False,
            "can_execute": is_wasm and WASMTIME_AVAILABLE and wasm_path is not None,
        }

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get Wasm status for '{plugin_id}': {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to get Wasm status: {e!s}",
        ) from e


@router.post("/{plugin_id}/wasm/execute", response_model=WasmExecutionResponse)
async def execute_wasm_plugin(plugin_id: str, request: WasmExecutionRequest):
    """Execute a Wasm plugin function.

    This endpoint allows executing WebAssembly plugins in a sandboxed environment
    with configurable capabilities and resource limits.
    """
    if not PLUGIN_SERVICE_AVAILABLE:
        raise HTTPException(
            status_code=503,
            detail="Plugin service not available",
        )

    try:
        service = get_plugin_service()

        # Decode input data if provided
        input_data = None
        if request.input_data:
            import base64

            try:
                input_data = base64.b64decode(request.input_data)
            except Exception as decode_error:
                raise HTTPException(
                    status_code=400,
                    detail=f"Invalid base64 input_data: {decode_error}",
                ) from decode_error

        # Execute the Wasm plugin
        result = await service.execute_wasm_plugin(
            plugin_id=plugin_id,
            function_name=request.function_name,
            input_data=input_data,
            capabilities=request.capabilities,
            memory_limit_mb=request.memory_limit_mb,
            timeout_seconds=request.timeout_seconds,
        )

        if not result["success"]:
            # Return the error in the response (not as HTTP error)
            # This allows clients to handle execution errors programmatically
            return WasmExecutionResponse(
                success=False,
                output=result.get("output"),
                error=result.get("error"),
                execution_time_ms=result.get("execution_time_ms", 0.0),
                metrics=result.get("metrics", {}),
            )

        return WasmExecutionResponse(
            success=True,
            output=result.get("output"),
            error=None,
            execution_time_ms=result.get("execution_time_ms", 0.0),
            metrics=result.get("metrics", {}),
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to execute Wasm plugin '{plugin_id}': {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to execute Wasm plugin: {e!s}",
        ) from e


@router.get("/{plugin_id}/wasm/capabilities")
async def get_wasm_plugin_capabilities(plugin_id: str):
    """Get available capabilities for a Wasm plugin."""
    # Return available capability tokens that can be granted
    from backend.plugins.wasm.capability_tokens import (
        STANDARD_CAPABILITY_SETS,
        CapabilityToken,
    )

    try:
        # Build capability info
        capabilities = {}
        for token in CapabilityToken:
            capabilities[token.value] = {
                "name": token.value,
                "description": _get_capability_description(token),
            }

        # Include standard sets
        standard_sets = {}
        for name, cap_set in STANDARD_CAPABILITY_SETS.items():
            standard_sets[name] = {
                "name": name,
                "tokens": [t.value for t in cap_set._tokens],
            }

        return {
            "plugin_id": plugin_id,
            "available_capabilities": capabilities,
            "standard_sets": standard_sets,
        }
    except ImportError:
        return {
            "plugin_id": plugin_id,
            "available_capabilities": {},
            "standard_sets": {},
            "error": "Wasm capability module not available",
        }
    except Exception as e:
        logger.error(f"Failed to get Wasm capabilities: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to get Wasm capabilities: {e!s}",
        ) from e


def _get_capability_description(token) -> str:
    """Get a human-readable description for a capability token."""
    descriptions = {
        "fs_read": "Read files from allowed directories",
        "fs_write": "Write files to allowed directories",
        "network_http": "Make HTTP requests",
        "network_socket": "Use raw sockets",
        "env_read": "Read environment variables",
        "env_write": "Set environment variables",
        "process_spawn": "Spawn subprocesses",
        "time_access": "Access system time",
        "random_access": "Access random number generation",
        "audio_input": "Access audio input devices",
        "audio_output": "Access audio output devices",
        "gpu_compute": "Use GPU compute resources",
    }
    return descriptions.get(token.value, f"Capability: {token.value}")


# =============================================================================
# P4-1, P5-2: Signature Verification Routes
# =============================================================================


class SignatureVerificationResponse(BaseModel):
    """Response model for signature verification."""

    plugin_id: str
    verified: bool
    signed: bool
    key_id: str = ""
    algorithm: str = ""
    signed_at: str = ""
    fingerprint: str = ""
    message: str = ""
    error: bool = False


class LoadWithVerificationRequest(BaseModel):
    """Request for loading a plugin with signature verification."""

    require_signature: bool = False


class LoadWithVerificationResponse(BaseModel):
    """Response for load with verification."""

    plugin_id: str
    loaded: bool
    verification: SignatureVerificationResponse | None = None
    error: str | None = None


@router.get("/{plugin_id}/signature", response_model=SignatureVerificationResponse)
async def verify_plugin_signature(
    plugin_id: str,
    require_signature: bool = False,
):
    """
    Verify a plugin's cryptographic signature.

    P4-1: Signature verification endpoint for plugin security.

    Args:
        plugin_id: Plugin identifier
        require_signature: If True, treats missing/invalid signature as error

    Returns:
        Verification result with signature details
    """
    try:
        from backend.plugins.supply_chain.signer import (
            check_signing_available,
            verify_package_auto,
        )

        SIGNING_AVAILABLE = check_signing_available()
        _verify_fn = verify_package_auto
    except ImportError:
        SIGNING_AVAILABLE = False
        _verify_fn = None

    if not SIGNING_AVAILABLE:
        return SignatureVerificationResponse(
            plugin_id=plugin_id,
            verified=False,
            signed=False,
            message="Signing functionality not available",
            error=require_signature,
        )

    # Find plugin path
    discovered_plugins = _discover_plugins()
    plugin_path = None
    for p in discovered_plugins:
        if p["plugin_id"] == plugin_id:
            plugin_path = Path(p["path"])
            break

    if not plugin_path:
        raise HTTPException(
            status_code=404,
            detail=f"Plugin '{plugin_id}' not found",
        )

    if _verify_fn is None:
        raise HTTPException(status_code=500, detail="Verification function unavailable")

    result = _verify_fn(plugin_path)

    return SignatureVerificationResponse(
        plugin_id=plugin_id,
        verified=result.valid,
        signed=bool(result.key_id),
        key_id=result.key_id,
        algorithm=result.algorithm,
        signed_at=result.signed_at,
        fingerprint=result.fingerprint,
        message=result.message,
        error=require_signature and not result.valid,
    )


@router.post(
    "/{plugin_id}/load-verified",
    response_model=LoadWithVerificationResponse,
)
async def load_plugin_with_verification(
    plugin_id: str,
    request: LoadWithVerificationRequest | None = None,
):
    """
    Load a plugin with signature verification.

    P4-1: Full integration of signature verification into plugin load flow.

    Args:
        plugin_id: Plugin identifier
        request: Load options including signature requirement

    Returns:
        Load result with verification status
    """
    require_signature = request.require_signature if request else False

    # First verify the signature
    try:
        from backend.plugins.supply_chain.signer import (
            check_signing_available,
            verify_package_auto,
        )

        SIGNING_AVAILABLE = check_signing_available()
        _verify_fn2 = verify_package_auto
    except ImportError:
        SIGNING_AVAILABLE = False
        _verify_fn2 = None

    # Find plugin path
    discovered_plugins = _discover_plugins()
    plugin_path = None
    for p in discovered_plugins:
        if p["plugin_id"] == plugin_id:
            plugin_path = Path(p["path"])
            break

    if not plugin_path:
        raise HTTPException(
            status_code=404,
            detail=f"Plugin '{plugin_id}' not found",
        )

    verification_result = None
    if SIGNING_AVAILABLE and _verify_fn2:
        result = _verify_fn2(plugin_path)
        verification_result = SignatureVerificationResponse(
            plugin_id=plugin_id,
            verified=result.valid,
            signed=bool(result.key_id),
            key_id=result.key_id,
            algorithm=result.algorithm,
            signed_at=result.signed_at,
            fingerprint=result.fingerprint,
            message=result.message,
            error=require_signature and not result.valid,
        )

        # Reject if signature required but not valid
        if require_signature and not result.valid:
            return LoadWithVerificationResponse(
                plugin_id=plugin_id,
                loaded=False,
                verification=verification_result,
                error=f"Signature verification failed: {result.message}",
            )
    else:
        verification_result = SignatureVerificationResponse(
            plugin_id=plugin_id,
            verified=False,
            signed=False,
            message="Signing functionality not available",
            error=require_signature,
        )

        if require_signature:
            return LoadWithVerificationResponse(
                plugin_id=plugin_id,
                loaded=False,
                verification=verification_result,
                error="Signing functionality not available",
            )

    # Proceed with load (using existing load logic)
    status = _get_plugin_status(plugin_id)
    if status == "loaded":
        return LoadWithVerificationResponse(
            plugin_id=plugin_id,
            loaded=True,
            verification=verification_result,
            error=None,
        )

    # Return load request status (actual loading done by PluginLoader)
    return LoadWithVerificationResponse(
        plugin_id=plugin_id,
        loaded=False,  # Actually "requested" - see load_plugin endpoint
        verification=verification_result,
        error="Plugin load requested. Use PluginLoader for actual loading.",
    )
