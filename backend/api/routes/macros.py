"""
Macros and Automation Routes

CRUD operations for macros and automation curves.
"""

from __future__ import annotations

import asyncio
import logging
import os
import uuid
from datetime import datetime, timedelta
from pathlib import Path
from typing import Any

from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/macros", tags=["macros"])

# Try to import scheduler
try:
    from backend.tasks.facade import TaskPriority, get_scheduler

    HAS_SCHEDULER = True
except ImportError:
    HAS_SCHEDULER = False
    logger.warning("Task scheduler not available for macro scheduling")


# Pydantic models
class MacroPort(BaseModel):
    id: str
    name: str
    type: str  # "audio", "control", "data"
    is_required: bool = False


class MacroNode(BaseModel):
    id: str
    type: str  # "source", "processor", "control", "conditional", "output"
    name: str
    x: float
    y: float
    properties: dict[str, Any] = {}
    input_ports: list[MacroPort] = []
    output_ports: list[MacroPort] = []


class MacroConnection(BaseModel):
    id: str
    source_node_id: str
    source_port_id: str
    target_node_id: str
    target_port_id: str


class Macro(BaseModel):
    id: str
    name: str
    description: str | None = None
    project_id: str
    nodes: list[MacroNode] = []
    connections: list[MacroConnection] = []
    is_enabled: bool = True
    created: str
    modified: str


class MacroCreateRequest(BaseModel):
    name: str
    description: str | None = None
    project_id: str
    nodes: list[MacroNode] | None = None
    connections: list[MacroConnection] | None = None
    is_enabled: bool = True


class MacroUpdateRequest(BaseModel):
    name: str | None = None
    description: str | None = None
    nodes: list[MacroNode] | None = None
    connections: list[MacroConnection] | None = None
    is_enabled: bool | None = None


class MacroExecutionStatus(BaseModel):
    macro_id: str
    status: str  # "idle", "running", "completed", "failed"
    current_node_index: int = 0
    total_nodes: int = 0
    current_node_name: str | None = None
    progress: float = 0.0  # 0.0 to 1.0
    error_message: str | None = None
    started_at: str | None = None
    completed_at: str | None = None


class AutomationPoint(BaseModel):
    time: float
    value: float
    bezier_handle_in_x: float | None = None
    bezier_handle_in_y: float | None = None
    bezier_handle_out_x: float | None = None
    bezier_handle_out_y: float | None = None


class AutomationCurve(BaseModel):
    id: str
    name: str
    parameter_id: str
    track_id: str
    points: list[AutomationPoint] = []
    interpolation: str = "linear"  # "linear", "bezier", "step"


class AutomationCurveCreateRequest(BaseModel):
    name: str
    parameter_id: str
    track_id: str
    points: list[AutomationPoint] | None = None
    interpolation: str = "linear"


class AutomationCurveUpdateRequest(BaseModel):
    name: str | None = None
    parameter_id: str | None = None
    points: list[AutomationPoint] | None = None
    interpolation: str | None = None


from backend.services.persistent_store import PersistentStore

_macros: PersistentStore = PersistentStore("macros")
_automation_curves: PersistentStore = PersistentStore("macro_automation_curves")
_macro_execution_status: PersistentStore = PersistentStore("macro_execution_status")
_macro_schedules: PersistentStore = PersistentStore("macro_schedules")
_state_lock = asyncio.Lock()
_MAX_MACROS = 1000
_MAX_AUTOMATION_CURVES = 2000


def _validate_macro_id(macro_id: str) -> None:
    """Validate that macro_id is not empty."""
    if not macro_id or not macro_id.strip():
        raise HTTPException(status_code=400, detail="Macro ID is required")


def _validate_curve_id(curve_id: str) -> None:
    """Validate that curve_id is not empty."""
    if not curve_id or not curve_id.strip():
        raise HTTPException(status_code=400, detail="Curve ID is required")


def _validate_project_id(project_id: str) -> None:
    """Validate that project_id is not empty."""
    if not project_id or not project_id.strip():
        raise HTTPException(status_code=400, detail="Project ID is required")


def _validate_track_id(track_id: str) -> None:
    """Validate that track_id is not empty."""
    if not track_id or not track_id.strip():
        raise HTTPException(status_code=400, detail="Track ID is required")


def _validate_macro_structure(macro: Macro) -> list[str]:
    """
    Validate macro structure comprehensively.

    Returns:
        List of validation errors (empty if valid)
    """
    errors = []

    # Validate nodes
    if not macro.nodes:
        errors.append("Macro must contain at least one node")
        return errors

    # Check for duplicate node IDs
    node_ids = set()
    for node in macro.nodes:
        if not node.id or not node.id.strip():
            errors.append("All nodes must have a non-empty ID")
        elif node.id in node_ids:
            errors.append(f"Duplicate node ID: {node.id}")
        else:
            node_ids.add(node.id)

        # Validate node type
        valid_types = ["source", "processor", "control", "conditional", "output"]
        if node.type not in valid_types:
            errors.append(
                f"Node {node.id} has invalid type '{node.type}'. "
                f"Valid types: {', '.join(valid_types)}"
            )

    # Validate connections
    for conn in macro.connections:
        # Check source node exists
        if conn.source_node_id not in node_ids:
            errors.append(f"Connection references non-existent source node: {conn.source_node_id}")

        # Check target node exists
        if conn.target_node_id not in node_ids:
            errors.append(f"Connection references non-existent target node: {conn.target_node_id}")

        # Check for self-connections (may be valid, but warn)
        if conn.source_node_id == conn.target_node_id:
            errors.append(f"Connection {conn.id} connects node to itself: {conn.source_node_id}")

    # Check for cycles (using DFS)
    if macro.connections:
        graph: dict[str, list[str]] = {node_id: [] for node_id in node_ids}
        for conn in macro.connections:
            if conn.source_node_id in graph:
                graph[conn.source_node_id].append(conn.target_node_id)

        def has_cycle(node_id: str, visited: set[str], rec_stack: set[str]) -> bool:
            visited.add(node_id)
            rec_stack.add(node_id)

            for neighbor in graph.get(node_id, []):
                if neighbor not in visited:
                    if has_cycle(neighbor, visited, rec_stack):
                        return True
                elif neighbor in rec_stack:
                    return True

            rec_stack.remove(node_id)
            return False

        visited: set[str] = set()
        for node_id in node_ids:
            if node_id not in visited and has_cycle(node_id, visited, set()):
                errors.append("Macro contains circular dependencies")
                break

    # Check for at least one source node
    source_nodes = [n for n in macro.nodes if n.type == "source"]
    if not source_nodes:
        errors.append("Macro must contain at least one source node")

    # Check for at least one output node
    output_nodes = [n for n in macro.nodes if n.type == "output"]
    if not output_nodes:
        errors.append("Macro must contain at least one output node")

    return errors


@router.get("", response_model=list[Macro])
@cache_response(ttl=30)  # Cache for 30 seconds (macros may change frequently)
def list_macros(
    project_id: str | None = Query(None, description="Filter by project ID")
) -> list[Macro]:
    """
    List all macros, optionally filtered by project ID.
    """
    try:
        macros = list(_macros.values())

        # Filter by project_id if provided
        if project_id:
            _validate_project_id(project_id)
            macros = [m for m in macros if m.project_id == project_id]

        logger.info(
            f"Listed {len(macros)} macros" + (f" (project: {project_id})" if project_id else "")
        )
        return macros
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error listing macros: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to list macros: {e!s}")


@router.get("/{macro_id}", response_model=Macro)
@cache_response(ttl=60)  # Cache for 60 seconds (macro info is relatively static)
def get_macro(macro_id: str) -> Macro:
    """
    Get a specific macro.
    """
    try:
        _validate_macro_id(macro_id)

        if macro_id not in _macros:
            logger.warning(f"Macro not found: {macro_id}")
            raise HTTPException(status_code=404, detail=f"Macro not found: {macro_id}")

        logger.info(f"Retrieved macro {macro_id}")
        return _macros[macro_id]
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error retrieving macro {macro_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to retrieve macro: {e!s}")


@router.post("", response_model=Macro)
def create_macro(request: MacroCreateRequest) -> Macro:
    """
    Create a new macro.
    """
    try:
        # Validate required fields
        if not request.name or not request.name.strip():
            raise HTTPException(status_code=400, detail="Macro name is required")

        _validate_project_id(request.project_id)

        # Check limit
        project_macros = [m for m in _macros.values() if m.project_id == request.project_id]
        if len(project_macros) >= _MAX_MACROS:
            raise HTTPException(
                status_code=429,
                detail=f"Maximum number of macros ({_MAX_MACROS}) reached for this project",
            )

        # Generate ID
        macro_id = str(uuid.uuid4())
        now = datetime.utcnow().isoformat()

        # Create macro
        macro = Macro(
            id=macro_id,
            name=request.name.strip(),
            description=request.description.strip() if request.description else None,
            project_id=request.project_id,
            nodes=request.nodes or [],
            connections=request.connections or [],
            is_enabled=request.is_enabled,
            created=now,
            modified=now,
        )

        # Validate macro structure
        validation_errors = _validate_macro_structure(macro)
        if validation_errors:
            raise HTTPException(
                status_code=400,
                detail=f"Macro validation failed: {'; '.join(validation_errors)}",
            )

        _macros[macro_id] = macro

        logger.info(f"Created macro {macro_id} for project {request.project_id}")
        return macro
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error creating macro: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to create macro: {e!s}")


@router.put("/{macro_id}", response_model=Macro)
def update_macro(macro_id: str, request: MacroUpdateRequest) -> Macro:
    """
    Update an existing macro.
    """
    try:
        _validate_macro_id(macro_id)

        if macro_id not in _macros:
            logger.warning(f"Macro not found: {macro_id}")
            raise HTTPException(status_code=404, detail=f"Macro not found: {macro_id}")

        macro = _macros[macro_id]

        # Update fields
        if request.name is not None:
            if not request.name.strip():
                raise HTTPException(status_code=400, detail="Macro name cannot be empty")
            macro.name = request.name.strip()

        if request.description is not None:
            macro.description = request.description.strip() if request.description else None

        if request.nodes is not None:
            macro.nodes = request.nodes

        if request.connections is not None:
            macro.connections = request.connections

        if request.is_enabled is not None:
            macro.is_enabled = request.is_enabled

        # Validate updated macro structure
        validation_errors = _validate_macro_structure(macro)
        if validation_errors:
            raise HTTPException(
                status_code=400,
                detail=f"Macro validation failed: {'; '.join(validation_errors)}",
            )

        macro.modified = datetime.utcnow().isoformat()

        logger.info(f"Updated macro {macro_id}")
        return macro
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error updating macro {macro_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to update macro: {e!s}")


@router.delete("/{macro_id}")
def delete_macro(macro_id: str) -> dict[str, bool]:
    """
    Delete a macro.
    """
    try:
        _validate_macro_id(macro_id)

        if macro_id not in _macros:
            logger.warning(f"Macro not found: {macro_id}")
            raise HTTPException(status_code=404, detail=f"Macro not found: {macro_id}")

        # Check if macro is currently executing
        if macro_id in _macro_execution_status:
            status = _macro_execution_status[macro_id]
            if status.status == "running":
                raise HTTPException(
                    status_code=409, detail="Cannot delete macro while it is executing"
                )
            # Clean up execution status
            del _macro_execution_status[macro_id]

        del _macros[macro_id]

        logger.info(f"Deleted macro {macro_id}")
        return {"success": True}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error deleting macro {macro_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to delete macro: {e!s}")


@router.post("/{macro_id}/execute")
def execute_macro(macro_id: str) -> dict[str, bool]:
    """
    Execute a macro.

    Executes macro nodes in dependency order based on connections.
    """
    try:
        _validate_macro_id(macro_id)

        if macro_id not in _macros:
            logger.warning(f"Macro not found: {macro_id}")
            raise HTTPException(status_code=404, detail=f"Macro not found: {macro_id}")

        macro = _macros[macro_id]

        if not macro.is_enabled:
            raise HTTPException(status_code=400, detail="Cannot execute disabled macro")

        # Validate macro before execution
        validation_errors = _validate_macro_structure(macro)
        if validation_errors:
            raise HTTPException(
                status_code=400,
                detail=f"Cannot execute invalid macro: {'; '.join(validation_errors)}",
            )

        # Check if already running
        if macro_id in _macro_execution_status:
            status = _macro_execution_status[macro_id]
            if status.status == "running":
                raise HTTPException(status_code=409, detail="Macro is already executing")

        # Initialize execution status
        now = datetime.utcnow().isoformat()
        total_nodes = len(macro.nodes)

        execution_status = MacroExecutionStatus(
            macro_id=macro_id,
            status="running",
            current_node_index=0,
            total_nodes=total_nodes,
            current_node_name=macro.nodes[0].name if macro.nodes else None,
            progress=0.0,
            error_message=None,
            started_at=now,
            completed_at=None,
        )

        _macro_execution_status[macro_id] = execution_status

        logger.info(f"Started execution of macro {macro_id} ({total_nodes} nodes)")

        # Execute macro nodes based on connections and dependencies
        try:
            # Build execution order from connections
            execution_order = _build_execution_order(macro)

            # Execute nodes in order
            node_outputs: dict[str, Any] = {}
            for i, node_id in enumerate(execution_order):
                node = next((n for n in macro.nodes if n.id == node_id), None)
                if not node:
                    continue

                execution_status.current_node_index = i
                execution_status.current_node_name = node.name
                execution_status.progress = (
                    (i + 1) / len(execution_order) if execution_order else 1.0
                )

                logger.info(
                    f"Executing node {i+1}/{len(execution_order)}: {node.name} ({node.type})"
                )

                # Execute node based on type
                try:
                    output = _execute_macro_node(node, node_outputs, macro)
                    node_outputs[node.id] = output
                except Exception as e:
                    logger.error(f"Node {node.name} execution failed: {e}")
                    execution_status.status = "failed"
                    execution_status.error_message = f"Node '{node.name}' failed: {e!s}"
                    execution_status.completed_at = datetime.utcnow().isoformat()
                    raise

            execution_status.status = "completed"
            execution_status.progress = 1.0
            execution_status.current_node_index = total_nodes
            execution_status.completed_at = datetime.utcnow().isoformat()

            logger.info(f"Macro {macro_id} execution completed successfully")
            return {"success": True}
        except Exception as e:
            execution_status.status = "failed"
            execution_status.error_message = str(e)
            execution_status.completed_at = datetime.utcnow().isoformat()
            raise
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error executing macro {macro_id}: {e!s}", exc_info=True)

        # Update execution status to failed
        if macro_id in _macro_execution_status:
            _macro_execution_status[macro_id].status = "failed"
            _macro_execution_status[macro_id].error_message = str(e)
            _macro_execution_status[macro_id].completed_at = datetime.utcnow().isoformat()

        raise HTTPException(status_code=500, detail=f"Failed to execute macro: {e!s}")


def _build_execution_order(macro: Macro) -> list[str]:
    """Build execution order for macro nodes based on connections."""
    if not macro.nodes:
        return []

    if not macro.connections:
        # No connections - execute in node order
        return [node.id for node in macro.nodes]

    # Build dependency graph
    node_deps: dict[str, list[str]] = {node.id: [] for node in macro.nodes}
    for conn in macro.connections:
        if conn.target_node_id not in node_deps:
            node_deps[conn.target_node_id] = []
        node_deps[conn.target_node_id].append(conn.source_node_id)

    # Topological sort
    execution_order = []
    remaining = {node.id for node in macro.nodes}
    processed = set()

    while remaining:
        # Find nodes with no unprocessed dependencies
        ready = [
            node_id
            for node_id in remaining
            if all(dep in processed for dep in node_deps.get(node_id, []))
        ]

        if not ready:
            # Circular dependency or missing nodes - use node order
            logger.warning("Circular dependency detected, using node order")
            return [node.id for node in macro.nodes]

        # Add ready nodes to execution order
        execution_order.extend(ready)
        processed.update(ready)
        remaining -= processed

    return execution_order


def _execute_macro_node(
    node: MacroNode, node_outputs: dict[str, Any], macro: Macro
) -> dict[str, Any]:
    """Execute a single macro node."""
    node_type = node.type.lower()

    # Get input values from connected nodes
    inputs: dict[str, Any] = {}
    for conn in macro.connections:
        if conn.target_node_id == node.id:
            source_output = node_outputs.get(conn.source_node_id, {})
            if isinstance(source_output, dict):
                inputs[conn.target_port_id] = source_output.get(conn.source_port_id)
            else:
                inputs[conn.target_port_id] = source_output

    # Execute based on node type
    if node_type == "source":
        return _execute_source_node(node, inputs)
    elif node_type == "processor":
        return _execute_processor_node(node, inputs)
    elif node_type == "control":
        return _execute_control_node(node, inputs)
    elif node_type == "conditional":
        return _execute_conditional_node(node, inputs)
    elif node_type == "output":
        return _execute_output_node(node, inputs)
    elif node_type == "mcp_tool":
        return _execute_mcp_tool_node(node, inputs)
    else:
        raise ValueError(f"Unknown node type: {node_type}")


def _execute_mcp_tool_node(node: MacroNode, inputs: dict[str, Any]) -> dict[str, Any]:
    """Execute an MCP tool node by calling the MCP server."""
    tool_name = node.properties.get("tool_name", "")
    if not tool_name:
        return {"error": "MCP tool node missing 'tool_name' property"}

    tool_args = dict(node.properties.get("tool_arguments", {}))
    for key, val in inputs.items():
        if val is not None:
            tool_args[key] = val

    try:
        import asyncio

        from backend.mcp.engine_server import execute_tool

        loop = asyncio.new_event_loop()
        try:
            result = loop.run_until_complete(execute_tool(tool_name, tool_args))
        finally:
            loop.close()
        return {"output": result}
    except Exception as e:
        logger.error(f"MCP tool execution failed: {tool_name}: {e}")
        return {"error": str(e), "tool": tool_name}


def _execute_source_node(node: MacroNode, inputs: dict[str, Any]) -> dict[str, Any]:
    """Execute a source node (generates data)."""
    source_type = node.properties.get("source_type", "constant")

    if source_type == "constant":
        value = node.properties.get("value", "")
        return {"output": value}
    elif source_type == "audio_file":
        file_path = node.properties.get("file_path", "")
        return {"output": file_path, "type": "audio"}
    elif source_type == "text":
        text = node.properties.get("text", "")
        return {"output": text, "type": "text"}
    else:
        return {"output": None}


def _execute_processor_node(node: MacroNode, inputs: dict[str, Any]) -> dict[str, Any]:
    """Execute a processor node (transforms data)."""
    processor_type = node.properties.get("processor_type", "passthrough")

    if processor_type == "passthrough":
        return {"output": inputs.get("input")}
    elif processor_type == "transform":
        # Apply transformation based on properties
        input_value = inputs.get("input")
        transform = node.properties.get("transform", "none")

        if transform == "uppercase" and isinstance(input_value, str):
            return {"output": input_value.upper()}
        elif transform == "lowercase" and isinstance(input_value, str):
            return {"output": input_value.lower()}
        elif transform == "reverse" and isinstance(input_value, str):
            return {"output": input_value[::-1]}
        else:
            return {"output": input_value}
    else:
        return {"output": inputs.get("input")}


def _execute_control_node(node: MacroNode, inputs: dict[str, Any]) -> dict[str, Any]:
    """Execute a control node (delay, loop, etc.)."""
    control_type = node.properties.get("control_type", "delay")

    if control_type == "delay":
        delay_seconds = node.properties.get("delay_seconds", 1.0)
        import time

        time.sleep(delay_seconds)
        return {"output": inputs.get("input")}
    elif control_type == "loop":
        iterations = node.properties.get("iterations", 1)
        results = []
        for _i in range(iterations):
            results.append(inputs.get("input"))
        return {"output": results}
    else:
        return {"output": inputs.get("input")}


def _execute_conditional_node(node: MacroNode, inputs: dict[str, Any]) -> dict[str, Any]:
    """Execute a conditional node (if/else logic)."""
    condition_type = node.properties.get("condition_type", "equals")

    input_value = inputs.get("input")
    expected_value = node.properties.get("expected_value", None)

    result = False
    if condition_type == "equals":
        result = str(input_value) == str(expected_value)
    elif condition_type == "not_equals":
        result = str(input_value) != str(expected_value)
    elif condition_type == "greater_than":
        try:
            result = float(str(input_value)) > float(str(expected_value))
        except (ValueError, TypeError):
            result = False
    elif condition_type == "less_than":
        try:
            result = float(str(input_value)) < float(str(expected_value))
        except (ValueError, TypeError):
            result = False
    elif condition_type == "contains":
        result = str(expected_value) in str(input_value)
    else:
        result = True

    if result:
        return {
            "output": inputs.get("true_output", input_value),
            "condition_result": True,
        }
    else:
        return {"output": inputs.get("false_output"), "condition_result": False}


def _execute_output_node(node: MacroNode, inputs: dict[str, Any]) -> dict[str, Any]:
    """Execute an output node (final result)."""
    output_type = node.properties.get("output_type", "result")
    input_value = inputs.get("input")

    if output_type == "save_file":
        file_path = node.properties.get("file_path", "")
        tmp_path = None
        try:
            path = Path(file_path)
            path.parent.mkdir(parents=True, exist_ok=True)
            tmp_path = path.with_suffix(path.suffix + ".tmp")
            tmp_path.write_text(str(input_value), encoding="utf-8")
            os.replace(tmp_path, path)
            return {"output": file_path, "saved": True}
        except Exception as e:
            if tmp_path is not None and tmp_path.exists():
                try:
                    tmp_path.unlink()
                # ALLOWED: bare except - Best effort cleanup, failure is acceptable
                except Exception as cleanup_e:
                    logger.debug(f"Cleanup of temp file failed (non-critical): {cleanup_e}")
            logger.error(f"Failed to save output: {e}")
            return {"output": None, "saved": False}
    else:
        return {"output": input_value}


@router.get("/{macro_id}/status", response_model=MacroExecutionStatus)
@cache_response(ttl=5)  # Cache for 5 seconds (status changes frequently during execution)
def get_macro_execution_status(macro_id: str) -> MacroExecutionStatus:
    """
    Get the execution status of a macro.
    """
    try:
        _validate_macro_id(macro_id)

        if macro_id not in _macros:
            logger.warning(f"Macro not found: {macro_id}")
            raise HTTPException(status_code=404, detail=f"Macro not found: {macro_id}")

        # Return status if exists, otherwise return idle status
        if macro_id in _macro_execution_status:
            return _macro_execution_status[macro_id]

        # Return default idle status
        return MacroExecutionStatus(
            macro_id=macro_id,
            status="idle",
            current_node_index=0,
            total_nodes=len(_macros[macro_id].nodes),
            current_node_name=None,
            progress=0.0,
            error_message=None,
            started_at=None,
            completed_at=None,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(
            f"Error retrieving macro execution status {macro_id}: {e!s}",
            exc_info=True,
        )
        raise HTTPException(status_code=500, detail=f"Failed to retrieve execution status: {e!s}")


@router.get("/{macro_id}/execution-status", response_model=MacroExecutionStatus)
@cache_response(ttl=5)
def get_macro_execution_status_alias(macro_id: str) -> MacroExecutionStatus:
    """
    Get the execution status of a macro (alias for /status).

    This is an alias route for UI compatibility.
    """
    status: MacroExecutionStatus = get_macro_execution_status(macro_id)
    return status


class MacroScheduleRequest(BaseModel):
    """Request to schedule a macro."""

    scheduled_at: str | None = None  # ISO datetime string
    interval_seconds: float | None = None  # Repeat interval
    max_executions: int | None = None  # Max times to execute (None = unlimited)
    priority: str = "normal"  # "low", "normal", "high", "critical"


class MacroScheduleResponse(BaseModel):
    """Response for macro scheduling."""

    macro_id: str
    scheduled_at: str | None
    interval_seconds: float | None
    next_execution: str | None
    max_executions: int | None
    execution_count: int = 0
    is_scheduled: bool


@router.post("/{macro_id}/schedule", response_model=MacroScheduleResponse)
async def schedule_macro(macro_id: str, request: MacroScheduleRequest):
    """
    Schedule a macro to execute at a specific time or interval.

    Supports one-time execution (scheduled_at) or recurring execution (interval_seconds).
    """
    try:
        _validate_macro_id(macro_id)

        if macro_id not in _macros:
            raise HTTPException(status_code=404, detail=f"Macro not found: {macro_id}")

        macro = _macros[macro_id]

        if not macro.is_enabled:
            raise HTTPException(status_code=400, detail="Cannot schedule disabled macro")

        # Validate scheduling parameters
        if not request.scheduled_at and not request.interval_seconds:
            raise HTTPException(
                status_code=400,
                detail="Either scheduled_at or interval_seconds must be provided",
            )

        if request.scheduled_at and request.interval_seconds:
            raise HTTPException(
                status_code=400,
                detail="Cannot specify both scheduled_at and interval_seconds",
            )

        # Parse scheduled_at if provided
        scheduled_datetime = None
        if request.scheduled_at:
            try:
                scheduled_datetime = datetime.fromisoformat(
                    request.scheduled_at.replace("Z", "+00:00")
                )
                if scheduled_datetime < datetime.now(scheduled_datetime.tzinfo):
                    raise HTTPException(
                        status_code=400, detail="scheduled_at must be in the future"
                    )
            except ValueError:
                raise HTTPException(
                    status_code=400,
                    detail="scheduled_at must be a valid ISO datetime string",
                )

        # Validate priority
        valid_priorities = ["low", "normal", "high", "critical"]
        if request.priority not in valid_priorities:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid priority. Must be one of: {', '.join(valid_priorities)}",
            )

        # Map priority string to TaskPriority
        priority_map = {
            "low": TaskPriority.LOW,
            "normal": TaskPriority.NORMAL,
            "high": TaskPriority.HIGH,
            "critical": TaskPriority.CRITICAL,
        }
        task_priority = priority_map[request.priority]

        # Store schedule info
        schedule_info = {
            "macro_id": macro_id,
            "scheduled_at": request.scheduled_at,
            "scheduled_datetime": (scheduled_datetime.isoformat() if scheduled_datetime else None),
            "interval_seconds": request.interval_seconds,
            "max_executions": request.max_executions,
            "execution_count": 0,
            "priority": request.priority,
        }
        _macro_schedules[macro_id] = schedule_info

        # Calculate next execution time
        if scheduled_datetime:
            next_execution = scheduled_datetime.isoformat()
        elif request.interval_seconds:
            next_execution = (
                datetime.now() + timedelta(seconds=request.interval_seconds)
            ).isoformat()
        else:
            next_execution = None

        # Schedule with task scheduler if available
        if HAS_SCHEDULER:
            try:
                scheduler = get_scheduler()

                # Create execution function
                def execute_macro_task():
                    try:
                        from backend.services.macro_execution_service import execute_macro

                        result = execute_macro(macro_id)
                        schedule_info["execution_count"] += 1
                        return result
                    except Exception as e:
                        logger.error(f"Scheduled macro execution failed: {e}")
                        raise

                # Add task to scheduler
                if request.interval_seconds:
                    # Recurring task
                    task_id = scheduler.add_task(
                        name=f"Macro {macro_id}",
                        func=execute_macro_task,
                        priority=task_priority,
                        interval=request.interval_seconds,
                        max_retries=3,
                    )
                    schedule_info["task_id"] = task_id
                elif scheduled_datetime:
                    # One-time scheduled task
                    task_id = scheduler.add_task(
                        name=f"Macro {macro_id}",
                        func=execute_macro_task,
                        priority=task_priority,
                        scheduled_at=scheduled_datetime,
                        max_retries=3,
                    )
                    schedule_info["task_id"] = task_id

                logger.info(
                    f"Scheduled macro {macro_id} "
                    f"(scheduled_at={request.scheduled_at}, "
                    f"interval={request.interval_seconds})"
                )
            except Exception as e:
                logger.warning(f"Failed to schedule macro with scheduler: {e}")

        return MacroScheduleResponse(
            macro_id=macro_id,
            scheduled_at=request.scheduled_at,
            interval_seconds=request.interval_seconds,
            next_execution=next_execution,
            max_executions=request.max_executions,
            execution_count=0,
            is_scheduled=True,
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to schedule macro {macro_id}: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to schedule macro: {e!s}") from e


@router.get("/{macro_id}/schedule", response_model=MacroScheduleResponse)
@cache_response(ttl=30)  # Cache for 30 seconds (schedule info changes moderately)
async def get_macro_schedule(macro_id: str):
    """Get the schedule information for a macro."""
    try:
        _validate_macro_id(macro_id)

        if macro_id not in _macros:
            raise HTTPException(status_code=404, detail=f"Macro not found: {macro_id}")

        if macro_id in _macro_schedules:
            schedule_info = _macro_schedules[macro_id]
            return MacroScheduleResponse(
                macro_id=macro_id,
                scheduled_at=schedule_info.get("scheduled_at"),
                interval_seconds=schedule_info.get("interval_seconds"),
                next_execution=schedule_info.get("scheduled_datetime"),
                max_executions=schedule_info.get("max_executions"),
                execution_count=schedule_info.get("execution_count", 0),
                is_scheduled=True,
            )
        else:
            return MacroScheduleResponse(
                macro_id=macro_id,
                scheduled_at=None,
                interval_seconds=None,
                next_execution=None,
                max_executions=None,
                execution_count=0,
                is_scheduled=False,
            )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get macro schedule {macro_id}: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get macro schedule: {e!s}") from e


@router.delete("/{macro_id}/schedule")
async def cancel_macro_schedule(macro_id: str) -> dict[str, bool]:
    """Cancel a scheduled macro."""
    try:
        _validate_macro_id(macro_id)

        if macro_id not in _macro_schedules:
            raise HTTPException(status_code=404, detail=f"Macro {macro_id} is not scheduled")

        # Cancel in scheduler if available
        if HAS_SCHEDULER:
            try:
                schedule_info = _macro_schedules[macro_id]
                task_id = schedule_info.get("task_id")
                if task_id:
                    scheduler = get_scheduler()
                    scheduler.cancel_task(task_id)
            except Exception as e:
                logger.warning(f"Failed to cancel scheduled task: {e}")

        # Remove schedule
        del _macro_schedules[macro_id]

        logger.info(f"Cancelled schedule for macro {macro_id}")
        return {"success": True}

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to cancel macro schedule {macro_id}: {e}", exc_info=True)
        raise HTTPException(
            status_code=500, detail=f"Failed to cancel macro schedule: {e!s}"
        ) from e


# Automation curves endpoints
@router.get("/automation/curves", response_model=list[AutomationCurve])
@cache_response(ttl=30)  # Cache for 30 seconds (automation curves may change frequently)
def list_automation_curves(
    track_id: str = Query(..., description="Track ID")
) -> list[AutomationCurve]:
    """
    List all automation curves for a track.
    """
    try:
        _validate_track_id(track_id)

        # Filter curves by track_id
        curves = [c for c in _automation_curves.values() if c.track_id == track_id]

        logger.info(f"Listed {len(curves)} automation curves for track {track_id}")
        return curves
    except HTTPException:
        raise
    except Exception as e:
        logger.error(
            f"Error listing automation curves for track {track_id}: {e!s}",
            exc_info=True,
        )
        raise HTTPException(status_code=500, detail=f"Failed to list automation curves: {e!s}")


@router.post("/automation/curves", response_model=AutomationCurve)
def create_automation_curve(request: AutomationCurveCreateRequest) -> AutomationCurve:
    """
    Create a new automation curve.
    """
    try:
        # Validate required fields
        if not request.name or not request.name.strip():
            raise HTTPException(status_code=400, detail="Curve name is required")

        if not request.parameter_id or not request.parameter_id.strip():
            raise HTTPException(status_code=400, detail="Parameter ID is required")

        _validate_track_id(request.track_id)

        # Validate interpolation
        valid_interpolations = ["linear", "bezier", "step"]
        if request.interpolation not in valid_interpolations:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid interpolation type. Must be one of: {', '.join(valid_interpolations)}",
            )

        # Check limit
        track_curves = [c for c in _automation_curves.values() if c.track_id == request.track_id]
        if len(track_curves) >= _MAX_AUTOMATION_CURVES:
            raise HTTPException(
                status_code=429,
                detail=f"Maximum number of automation curves ({_MAX_AUTOMATION_CURVES}) reached for this track",
            )

        # Generate ID
        curve_id = str(uuid.uuid4())

        # Create curve
        curve = AutomationCurve(
            id=curve_id,
            name=request.name.strip(),
            parameter_id=request.parameter_id.strip(),
            track_id=request.track_id,
            points=request.points or [],
            interpolation=request.interpolation,
        )

        _automation_curves[curve_id] = curve

        logger.info(f"Created automation curve {curve_id} for track {request.track_id}")
        return curve
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error creating automation curve: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to create automation curve: {e!s}")


@router.put("/automation/curves/{curve_id}", response_model=AutomationCurve)
def update_automation_curve(
    curve_id: str, request: AutomationCurveUpdateRequest
) -> AutomationCurve:
    """
    Update an existing automation curve.
    """
    try:
        _validate_curve_id(curve_id)

        if curve_id not in _automation_curves:
            logger.warning(f"Automation curve not found: {curve_id}")
            raise HTTPException(status_code=404, detail=f"Automation curve not found: {curve_id}")

        curve = _automation_curves[curve_id]

        # Update fields
        if request.name is not None:
            if not request.name.strip():
                raise HTTPException(status_code=400, detail="Curve name cannot be empty")
            curve.name = request.name.strip()

        if request.parameter_id is not None:
            if not request.parameter_id.strip():
                raise HTTPException(status_code=400, detail="Parameter ID cannot be empty")
            curve.parameter_id = request.parameter_id.strip()

        if request.points is not None:
            curve.points = request.points

        if request.interpolation is not None:
            valid_interpolations = ["linear", "bezier", "step"]
            if request.interpolation not in valid_interpolations:
                raise HTTPException(
                    status_code=400,
                    detail=f"Invalid interpolation type. Must be one of: {', '.join(valid_interpolations)}",
                )
            curve.interpolation = request.interpolation

        logger.info(f"Updated automation curve {curve_id}")
        return curve
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error updating automation curve {curve_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to update automation curve: {e!s}")


@router.delete("/automation/curves/{curve_id}")
def delete_automation_curve(curve_id: str) -> dict[str, bool]:
    """
    Delete an automation curve.
    """
    try:
        _validate_curve_id(curve_id)

        if curve_id not in _automation_curves:
            logger.warning(f"Automation curve not found: {curve_id}")
            raise HTTPException(status_code=404, detail=f"Automation curve not found: {curve_id}")

        del _automation_curves[curve_id]

        logger.info(f"Deleted automation curve {curve_id}")
        return {"success": True}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error deleting automation curve {curve_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to delete automation curve: {e!s}")


# =============================================================================
# Frontend-compatible automation routes (path-based without /curves segment)
# Frontend expects: /api/macros/automation/{trackId} and /api/macros/automation/{curveId}
# =============================================================================


@router.get("/automation/{track_id}", response_model=list[AutomationCurve])
@cache_response(ttl=30)
def list_track_automation(track_id: str) -> list[AutomationCurve]:
    """
    List all automation curves for a track.
    Frontend-compatible path-based route.
    """
    try:
        _validate_track_id(track_id)
        curves = [c for c in _automation_curves.values() if c.track_id == track_id]
        logger.info(f"Listed {len(curves)} automation curves for track {track_id}")
        return curves
    except HTTPException:
        raise
    except Exception as e:
        logger.error(
            f"Error listing automation curves for track {track_id}: {e!s}",
            exc_info=True,
        )
        raise HTTPException(status_code=500, detail=f"Failed to list automation curves: {e!s}")


@router.post("/automation", response_model=AutomationCurve)
def create_track_automation(request: AutomationCurveCreateRequest) -> AutomationCurve:
    """
    Create a new automation curve.
    Frontend-compatible path-based route.
    """
    try:
        if not request.name or not request.name.strip():
            raise HTTPException(status_code=400, detail="Curve name is required")

        if not request.parameter_id or not request.parameter_id.strip():
            raise HTTPException(status_code=400, detail="Parameter ID is required")

        _validate_track_id(request.track_id)

        valid_interpolations = ["linear", "bezier", "step"]
        if request.interpolation not in valid_interpolations:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid interpolation type. Must be one of: {', '.join(valid_interpolations)}",
            )

        track_curves = [c for c in _automation_curves.values() if c.track_id == request.track_id]
        if len(track_curves) >= _MAX_AUTOMATION_CURVES:
            raise HTTPException(
                status_code=429,
                detail=f"Maximum number of automation curves ({_MAX_AUTOMATION_CURVES}) reached for this track",
            )

        curve_id = str(uuid.uuid4())

        curve = AutomationCurve(
            id=curve_id,
            name=request.name.strip(),
            parameter_id=request.parameter_id.strip(),
            track_id=request.track_id,
            points=request.points or [],
            interpolation=request.interpolation,
        )

        _automation_curves[curve_id] = curve
        logger.info(f"Created automation curve {curve_id} for track {request.track_id}")
        return curve
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error creating automation curve: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to create automation curve: {e!s}")


@router.put("/automation/{curve_id}", response_model=AutomationCurve)
def update_track_automation(
    curve_id: str, request: AutomationCurveUpdateRequest
) -> AutomationCurve:
    """
    Update an existing automation curve.
    Frontend-compatible path-based route.
    """
    try:
        _validate_curve_id(curve_id)

        if curve_id not in _automation_curves:
            raise HTTPException(status_code=404, detail=f"Automation curve not found: {curve_id}")

        curve = _automation_curves[curve_id]

        if request.name is not None:
            if not request.name.strip():
                raise HTTPException(status_code=400, detail="Curve name cannot be empty")
            curve.name = request.name.strip()

        if request.parameter_id is not None:
            if not request.parameter_id.strip():
                raise HTTPException(status_code=400, detail="Parameter ID cannot be empty")
            curve.parameter_id = request.parameter_id.strip()

        if request.points is not None:
            curve.points = request.points

        if request.interpolation is not None:
            valid_interpolations = ["linear", "bezier", "step"]
            if request.interpolation not in valid_interpolations:
                raise HTTPException(
                    status_code=400,
                    detail=f"Invalid interpolation type. Must be one of: {', '.join(valid_interpolations)}",
                )
            curve.interpolation = request.interpolation

        logger.info(f"Updated automation curve {curve_id}")
        return curve
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error updating automation curve {curve_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to update automation curve: {e!s}")


@router.delete("/automation/{curve_id}")
def delete_track_automation(curve_id: str) -> dict[str, bool]:
    """
    Delete an automation curve.
    Frontend-compatible path-based route.
    """
    try:
        _validate_curve_id(curve_id)

        if curve_id not in _automation_curves:
            raise HTTPException(status_code=404, detail=f"Automation curve not found: {curve_id}")

        del _automation_curves[curve_id]
        logger.info(f"Deleted automation curve {curve_id}")
        return {"success": True}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error deleting automation curve {curve_id}: {e!s}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to delete automation curve: {e!s}")


from backend.services.macro_execution_service import register_execute_macro_handler

register_execute_macro_handler(execute_macro)
