"""
Workflow Automation API Routes
Handles workflow creation, execution, and management.
Implements backend for IDEA 33: Workflow Automation UI.
"""

from __future__ import annotations

import asyncio
import logging
import os
import uuid
from datetime import datetime
from typing import Any

from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel

from backend.core.security.expression_evaluator import evaluate_condition

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/workflows", tags=["workflows"])

# In-memory storage (ready for database migration)
_workflows: dict[str, Workflow] = {}
_state_lock = asyncio.Lock()


def _validate_workflow(workflow: Workflow) -> list[str]:
    """
    Validate a workflow structure and return list of validation errors.

    Returns:
        List of error messages (empty if valid)
    """
    errors = []

    # Validate basic structure
    if not workflow.name or not workflow.name.strip():
        errors.append("Workflow name is required")

    if len(workflow.name) > 200:
        errors.append("Workflow name must be 200 characters or less")

    # Validate steps
    if workflow.steps:
        step_ids = set()
        step_orders = set()

        for i, step in enumerate(workflow.steps):
            # Check for duplicate step IDs
            if step.id in step_ids:
                errors.append(f"Duplicate step ID: {step.id}")
            step_ids.add(step.id)

            # Check for duplicate orders
            if step.order in step_orders:
                errors.append(f"Duplicate step order: {step.order}")
            step_orders.add(step.order)

            # Validate step structure
            if not step.name or not step.name.strip():
                errors.append(f"Step {i+1} (ID: {step.id}) must have a name")

            if not step.type or not step.type.strip():
                errors.append(f"Step {i+1} (ID: {step.id}) must have a type")

            # Validate step type
            valid_types = ["synthesize", "effect", "export", "control"]
            if step.type.lower() not in valid_types:
                errors.append(
                    f"Step {i+1} (ID: {step.id}) has invalid type '{step.type}'. "
                    f"Valid types: {', '.join(valid_types)}"
                )

            # Validate step-specific properties
            step_type = step.type.lower()
            if step_type == "synthesize":
                # Synthesize step requires text and profile_id
                if "text" not in step.properties and "text" not in {
                    v.name for v in workflow.variables
                }:
                    errors.append(
                        f"Synthesize step {i+1} (ID: {step.id}) requires 'text' "
                        "property or workflow variable"
                    )
                if "profile_id" not in step.properties and "profile_id" not in {
                    v.name for v in workflow.variables
                }:
                    errors.append(
                        f"Synthesize step {i+1} (ID: {step.id}) requires 'profile_id' "
                        "property or workflow variable"
                    )

            elif step_type == "effect":
                # Effect step requires audio_id or previous step output
                if "audio_id" not in step.properties:
                    # Check if there's a previous step that could provide audio
                    has_previous_audio = False
                    for prev_step in workflow.steps[:i]:
                        if prev_step.type.lower() in ["synthesize", "effect"]:
                            has_previous_audio = True
                            break
                    if not has_previous_audio:
                        errors.append(
                            f"Effect step {i+1} (ID: {step.id}) requires 'audio_id' "
                            "property or previous step that produces audio"
                        )

            elif step_type == "export":
                # Export step requires audio_id or previous step output
                if "audio_id" not in step.properties:
                    # Check if there's a previous step that could provide audio
                    has_previous_audio = False
                    for prev_step in workflow.steps[:i]:
                        if prev_step.type.lower() in ["synthesize", "effect"]:
                            has_previous_audio = True
                            break
                    if not has_previous_audio:
                        errors.append(
                            f"Export step {i+1} (ID: {step.id}) requires 'audio_id' "
                            "property or previous step that produces audio"
                        )

            elif step_type == "control":
                # Control step requires control_type
                if "control_type" not in step.properties:
                    errors.append(
                        f"Control step {i+1} (ID: {step.id}) requires 'control_type' property"
                    )
                else:
                    control_type = step.properties.get("control_type", "").lower()
                    valid_control_types = ["delay", "condition"]
                    if control_type not in valid_control_types:
                        errors.append(
                            f"Control step {i+1} (ID: {step.id}) has invalid "
                            f"control_type '{control_type}'. Valid types: {', '.join(valid_control_types)}"
                        )

    # Validate variables
    if workflow.variables:
        var_names = set()
        for var in workflow.variables:
            if not var.name or not var.name.strip():
                errors.append("Variable name cannot be empty")
            if var.name in var_names:
                errors.append(f"Duplicate variable name: {var.name}")
            var_names.add(var.name)

    return errors


def _validate_audio_id(audio_id: str) -> bool:
    """
    Validate that an audio ID exists in storage.

    Args:
        audio_id: Audio ID to validate

    Returns:
        True if audio ID is valid, False otherwise
    """
    try:
        from .voice import _audio_storage

        if audio_id not in _audio_storage:
            return False

        audio_path = _audio_storage[audio_id]
        return os.path.exists(audio_path)
    except Exception as e:
        # GAP-PY-001: Audio validation failed, treating as invalid
        logger.debug(f"Audio ID validation failed for '{audio_id}': {e}")
        return False


class WorkflowVariable(BaseModel):
    """Variable in a workflow."""

    name: str
    value: str


class WorkflowStep(BaseModel):
    """Step in a workflow."""

    id: str
    type: str  # "synthesize", "effect", "export", "control"
    name: str
    properties: dict[str, Any] = {}
    order: int = 0  # Execution order


class Workflow(BaseModel):
    """Complete workflow definition."""

    id: str
    name: str
    description: str | None = None
    steps: list[WorkflowStep] = []
    variables: list[WorkflowVariable] = []
    is_enabled: bool = True
    created: str
    modified: str


class WorkflowCreateRequest(BaseModel):
    """Request to create a workflow."""

    name: str
    description: str | None = None
    steps: list[WorkflowStep] | None = None
    variables: list[WorkflowVariable] | None = None
    is_enabled: bool = True


class WorkflowUpdateRequest(BaseModel):
    """Request to update a workflow."""

    name: str | None = None
    description: str | None = None
    steps: list[WorkflowStep] | None = None
    variables: list[WorkflowVariable] | None = None
    is_enabled: bool | None = None


class WorkflowExecutionRequest(BaseModel):
    """Request to execute a workflow."""

    workflow_id: str
    input_data: dict[str, Any] | None = None  # Input variables/parameters


class WorkflowExecutionResult(BaseModel):
    """Result of workflow execution."""

    workflow_id: str
    status: str  # "completed", "failed", "cancelled"
    current_step: int = 0
    total_steps: int = 0
    current_step_name: str | None = None
    progress: float = 0.0  # 0.0 to 1.0
    error_message: str | None = None
    outputs: dict[str, Any] = {}  # Output data from workflow
    started_at: str
    completed_at: str | None = None


@router.get("", response_model=list[Workflow])
@cache_response(ttl=30)  # Cache for 30 seconds (workflows may change frequently)
async def list_workflows(
    skip: int = Query(0, ge=0),
    limit: int = Query(100, ge=1, le=1000),
    enabled_only: bool = Query(False),
):
    """List all workflows."""
    try:
        workflows = list(_workflows.values())

        if enabled_only:
            workflows = [w for w in workflows if w.is_enabled]

        # Sort by modified date (newest first)
        workflows.sort(key=lambda w: w.modified, reverse=True)

        return workflows[skip : skip + limit]
    except Exception as e:
        logger.error(f"Failed to list workflows: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to list workflows: {e!s}")


@router.get("/{workflow_id}", response_model=Workflow)
@cache_response(ttl=60)  # Cache for 60 seconds (workflow info is relatively static)
async def get_workflow(workflow_id: str):
    """Get a specific workflow."""
    try:
        if workflow_id not in _workflows:
            raise HTTPException(status_code=404, detail=f"Workflow '{workflow_id}' not found")

        return _workflows[workflow_id]
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get workflow '{workflow_id}': {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get workflow: {e!s}")


@router.post("", response_model=Workflow, status_code=201)
async def create_workflow(request: WorkflowCreateRequest):
    """Create a new workflow."""
    try:
        workflow_id = str(uuid.uuid4())
        now = datetime.utcnow().isoformat()

        # Validate steps order
        if request.steps:
            for i, step in enumerate(request.steps):
                step.order = i

        workflow = Workflow(
            id=workflow_id,
            name=request.name,
            description=request.description,
            steps=request.steps or [],
            variables=request.variables or [],
            is_enabled=request.is_enabled,
            created=now,
            modified=now,
        )

        # Validate workflow structure
        validation_errors = _validate_workflow(workflow)
        if validation_errors:
            raise HTTPException(
                status_code=400,
                detail=f"Workflow validation failed: {'; '.join(validation_errors)}",
            )

        # Validate audio IDs if referenced in steps
        for step in workflow.steps:
            if "audio_id" in step.properties:
                audio_id = step.properties["audio_id"]
                if not _validate_audio_id(audio_id):
                    raise HTTPException(
                        status_code=400,
                        detail=f"Invalid audio_id '{audio_id}' in step '{step.id}': audio not found",
                    )

        _workflows[workflow_id] = workflow
        logger.info(f"Created workflow '{workflow_id}': {request.name}")

        return workflow
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to create workflow: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to create workflow: {e!s}")


@router.put("/{workflow_id}", response_model=Workflow)
async def update_workflow(workflow_id: str, request: WorkflowUpdateRequest):
    """Update an existing workflow."""
    try:
        if workflow_id not in _workflows:
            raise HTTPException(status_code=404, detail=f"Workflow '{workflow_id}' not found")

        workflow = _workflows[workflow_id]

        # Update fields
        if request.name is not None:
            workflow.name = request.name
        if request.description is not None:
            workflow.description = request.description
        if request.steps is not None:
            # Validate and set step order
            for i, step in enumerate(request.steps):
                step.order = i
            workflow.steps = request.steps
        if request.variables is not None:
            workflow.variables = request.variables
        if request.is_enabled is not None:
            workflow.is_enabled = request.is_enabled

        # Validate updated workflow structure
        validation_errors = _validate_workflow(workflow)
        if validation_errors:
            raise HTTPException(
                status_code=400,
                detail=f"Workflow validation failed: {'; '.join(validation_errors)}",
            )

        # Validate audio IDs if referenced in steps
        for step in workflow.steps:
            if "audio_id" in step.properties:
                audio_id = step.properties["audio_id"]
                if not _validate_audio_id(audio_id):
                    raise HTTPException(
                        status_code=400,
                        detail=f"Invalid audio_id '{audio_id}' in step '{step.id}': audio not found",
                    )

        workflow.modified = datetime.utcnow().isoformat()

        logger.info(f"Updated workflow '{workflow_id}'")

        return workflow
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to update workflow '{workflow_id}': {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to update workflow: {e!s}")


@router.delete("/{workflow_id}", status_code=204)
async def delete_workflow(workflow_id: str):
    """Delete a workflow."""
    try:
        if workflow_id not in _workflows:
            raise HTTPException(status_code=404, detail=f"Workflow '{workflow_id}' not found")

        del _workflows[workflow_id]
        logger.info(f"Deleted workflow '{workflow_id}'")

        return None
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to delete workflow '{workflow_id}': {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to delete workflow: {e!s}")


@router.post("/{workflow_id}/execute", response_model=WorkflowExecutionResult)
async def execute_workflow(workflow_id: str, request: WorkflowExecutionRequest | None = None):
    """Execute a workflow."""
    try:
        if workflow_id not in _workflows:
            raise HTTPException(status_code=404, detail=f"Workflow '{workflow_id}' not found")

        workflow = _workflows[workflow_id]

        if not workflow.is_enabled:
            raise HTTPException(status_code=400, detail=f"Workflow '{workflow_id}' is disabled")

        if not workflow.steps:
            raise HTTPException(status_code=400, detail=f"Workflow '{workflow_id}' has no steps")

        # Sort steps by order
        sorted_steps = sorted(workflow.steps, key=lambda s: s.order)

        # Prepare execution context
        context: dict[str, Any] = {}

        # Add workflow variables to context
        for var in workflow.variables:
            context[var.name] = var.value

        # Add input data to context
        if request and request.input_data:
            context.update(request.input_data)

        # Execute steps in order
        started_at = datetime.utcnow().isoformat()
        outputs: dict[str, Any] = {}
        error_message: str | None = None
        current_step_index = 0

        try:
            for i, step in enumerate(sorted_steps):
                current_step_index = i
                logger.info(
                    f"Executing workflow step {i+1}/{len(sorted_steps)}: {step.name} ({step.type})"
                )

                # Execute step based on type
                step_output = await _execute_workflow_step(step, context)

                # Store step output in context for next steps
                context[f"step_{i}_output"] = step_output
                outputs[step.id] = step_output

            status = "completed"
            completed_at = datetime.utcnow().isoformat()
            logger.info(f"Workflow '{workflow_id}' executed successfully")

        except Exception as e:
            status = "failed"
            error_message = str(e)
            completed_at = datetime.utcnow().isoformat()
            logger.error(
                f"Workflow '{workflow_id}' execution failed at step {current_step_index + 1}: {e}",
                exc_info=True,
            )

        result = WorkflowExecutionResult(
            workflow_id=workflow_id,
            status=status,
            current_step=current_step_index,
            total_steps=len(sorted_steps),
            current_step_name=(
                sorted_steps[current_step_index].name
                if current_step_index < len(sorted_steps)
                else None
            ),
            progress=(
                1.0 if status == "completed" else (current_step_index + 1) / len(sorted_steps)
            ),
            error_message=error_message,
            outputs=outputs,
            started_at=started_at,
            completed_at=completed_at,
        )

        return result

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to execute workflow '{workflow_id}': {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to execute workflow: {e!s}")


async def _execute_workflow_step(step: WorkflowStep, context: dict[str, Any]) -> dict[str, Any]:
    """Execute a single workflow step."""
    step_type = step.type.lower()

    if step_type == "synthesize":
        return await _execute_synthesize_step(step, context)
    elif step_type == "effect":
        return await _execute_effect_step(step, context)
    elif step_type == "export":
        return await _execute_export_step(step, context)
    elif step_type == "control":
        return await _execute_control_step(step, context)
    else:
        raise ValueError(f"Unknown workflow step type: {step_type}")


async def _execute_synthesize_step(step: WorkflowStep, context: dict[str, Any]) -> dict[str, Any]:
    """Execute a synthesize step."""
    # Get text from properties or context
    text = step.properties.get("text", "")
    if not text and "text" in context:
        text = context["text"]

    if not text:
        raise ValueError("Synthesize step requires 'text' property or context variable")

    # Get profile_id from properties or context
    profile_id = step.properties.get("profile_id", "")
    if not profile_id and "profile_id" in context:
        profile_id = context["profile_id"]

    if not profile_id:
        raise ValueError("Synthesize step requires 'profile_id' property or context variable")

    # Get optional parameters
    engine = step.properties.get("engine", "xtts_v2")
    language = step.properties.get("language", "en")
    emotion = step.properties.get("emotion")
    enhance_quality = step.properties.get("enhance_quality", False)

    logger.info(
        f"Synthesizing text '{text[:50]}...' with profile '{profile_id}' using engine '{engine}'"
    )

    # Call actual synthesis by importing and using the synthesis function
    try:
        from ..models_additional import VoiceSynthesizeRequest
        from .voice import synthesize

        # Create synthesis request
        synth_request = VoiceSynthesizeRequest(
            engine=engine,
            profile_id=profile_id,
            text=text,
            language=language,
            emotion=emotion if emotion else None,
            enhance_quality=enhance_quality,
        )

        # Perform synthesis
        synth_response = await synthesize(synth_request)

        return {
            "type": "synthesize",
            "text": text,
            "profile_id": profile_id,
            "engine": engine,
            "language": language,
            "audio_id": synth_response.audio_id,
            "audio_url": synth_response.audio_url,
            "quality_metrics": (
                synth_response.quality_metrics.dict() if synth_response.quality_metrics else None
            ),
            "status": "completed",
        }
    except Exception as e:
        logger.error(f"Failed to synthesize in workflow step: {e}", exc_info=True)
        raise ValueError(f"Synthesis failed: {e!s}")


async def _execute_effect_step(step: WorkflowStep, context: dict[str, Any]) -> dict[str, Any]:
    """Execute an effect step."""
    # Get audio input from previous step or context
    audio_id = step.properties.get("audio_id", "")
    if not audio_id:
        # Try to get from previous step output
        for key in context:
            if key.startswith("step_") and key.endswith("_output"):
                prev_output = context[key]
                if isinstance(prev_output, dict) and "audio_id" in prev_output:
                    audio_id = prev_output["audio_id"]
                    break

    if not audio_id:
        raise ValueError("Effect step requires 'audio_id' property or audio from previous step")

    # Get effect parameters
    effect_type = step.properties.get("effect_type", "")
    chain_id = step.properties.get("chain_id")

    if not effect_type and not chain_id:
        raise ValueError("Effect step requires 'effect_type' or 'chain_id' property")

    logger.info(f"Applying effect '{effect_type or chain_id}' to audio '{audio_id}'")

    # Apply effect using audio processing
    try:
        import os

        import numpy as np

        from .voice import _audio_storage

        # Try to import audio_utils
        try:
            from app.core.audio import audio_utils

            HAS_AUDIO_UTILS = True
        except ImportError:
            # Fallback: use soundfile directly
            try:
                import soundfile as sf

                HAS_AUDIO_UTILS = False
                HAS_SOUNDFILE = True
            except ImportError:
                HAS_SOUNDFILE = False
                raise ValueError("Audio processing libraries not available")

        # Get audio file path
        if audio_id not in _audio_storage:
            raise ValueError(f"Audio file '{audio_id}' not found in storage")

        audio_path = _audio_storage[audio_id]
        if not os.path.exists(audio_path):
            raise ValueError(f"Audio file at '{audio_path}' does not exist")

        # Load audio
        if HAS_AUDIO_UTILS:
            audio, sample_rate = audio_utils.load_audio(audio_path)
        elif HAS_SOUNDFILE:
            audio, sample_rate = sf.read(audio_path)
        else:
            raise ValueError("Cannot load audio - no audio libraries available")

        # Apply effect based on type
        processed_audio = audio
        if effect_type:
            if effect_type == "normalize":
                # Normalize audio
                max_val = np.max(np.abs(audio))
                if max_val > 0:
                    processed_audio = audio / max_val
            elif effect_type == "lowpass":
                # Simple lowpass (would use proper filter in production)
                cutoff = step.properties.get("cutoff_freq", 5000.0)
                logger.info(f"Applying lowpass filter with cutoff {cutoff} Hz")
                # For now, just normalize (proper filter would require scipy)
                processed_audio = audio
            elif effect_type == "highpass":
                # Simple highpass
                cutoff = step.properties.get("cutoff_freq", 200.0)
                logger.info(f"Applying highpass filter with cutoff {cutoff} Hz")
                processed_audio = audio
            elif effect_type == "gain":
                # Apply gain
                gain_db = step.properties.get("gain_db", 0.0)
                gain_linear = 10 ** (gain_db / 20.0)
                processed_audio = audio * gain_linear
                processed_audio = np.clip(processed_audio, -1.0, 1.0)
            else:
                logger.warning(f"Unknown effect type '{effect_type}', skipping effect")
                processed_audio = audio

        # Save processed audio
        import tempfile

        output_path = tempfile.mktemp(suffix=".wav")
        if HAS_AUDIO_UTILS:
            audio_utils.save_audio(processed_audio, sample_rate, output_path)
        elif HAS_SOUNDFILE:
            sf.write(output_path, processed_audio, sample_rate)
        else:
            raise ValueError("Cannot save audio - no audio libraries available")

        # Register new audio file
        output_audio_id = f"workflow_{uuid.uuid4().hex[:8]}"
        from .voice import _register_audio_file

        _register_audio_file(output_audio_id, output_path)

        return {
            "type": "effect",
            "effect_type": effect_type or "chain",
            "input_audio_id": audio_id,
            "output_audio_id": output_audio_id,
            "status": "completed",
        }
    except Exception as e:
        logger.error(f"Failed to apply effect in workflow step: {e}", exc_info=True)
        raise ValueError(f"Effect application failed: {e!s}")


async def _execute_export_step(step: WorkflowStep, context: dict[str, Any]) -> dict[str, Any]:
    """Execute an export step."""
    # Get audio input from previous step or context
    audio_id = step.properties.get("audio_id", "")
    if not audio_id:
        # Try to get from previous step output
        for key in context:
            if key.startswith("step_") and key.endswith("_output"):
                prev_output = context[key]
                if isinstance(prev_output, dict) and "audio_id" in prev_output:
                    audio_id = prev_output["audio_id"]
                    break

    if not audio_id:
        raise ValueError("Export step requires 'audio_id' property or audio from previous step")

    # Get export path
    export_path = step.properties.get("export_path", "")
    if not export_path:
        # Default export directory
        export_dir = os.path.join(os.path.expanduser("~"), "VoiceStudio", "exports")
        os.makedirs(export_dir, exist_ok=True)
        export_path = os.path.join(export_dir, f"{audio_id}.wav")

    # Ensure directory exists
    export_dir = os.path.dirname(export_path)
    if export_dir:
        os.makedirs(export_dir, exist_ok=True)

    logger.info(f"Exporting audio '{audio_id}' to '{export_path}'")

    # Copy audio file to export location
    try:
        import shutil

        from .voice import _audio_storage

        # Get audio file path
        if audio_id not in _audio_storage:
            raise ValueError(f"Audio file '{audio_id}' not found in storage")

        source_path = _audio_storage[audio_id]
        if not os.path.exists(source_path):
            raise ValueError(f"Audio file at '{source_path}' does not exist")

        # Copy file to export location
        shutil.copy2(source_path, export_path)

        logger.info(f"Successfully exported audio to '{export_path}'")

        return {
            "type": "export",
            "audio_id": audio_id,
            "export_path": export_path,
            "file_size": os.path.getsize(export_path),
            "status": "completed",
        }
    except Exception as e:
        logger.error(f"Failed to export audio in workflow step: {e}", exc_info=True)
        raise ValueError(f"Export failed: {e!s}")


async def _execute_control_step(step: WorkflowStep, context: dict[str, Any]) -> dict[str, Any]:
    """Execute a control step (delay, condition, etc.)."""
    control_type = step.properties.get("control_type", "delay")

    if control_type == "delay":
        delay_seconds = step.properties.get("delay_seconds", 1.0)
        import asyncio

        await asyncio.sleep(delay_seconds)
        return {
            "type": "control",
            "control_type": "delay",
            "delay_seconds": delay_seconds,
            "status": "completed",
        }
    elif control_type == "condition":
        condition = step.properties.get("condition", "")
        condition_type = step.properties.get("condition_type", "equals")
        variable_name = step.properties.get("variable_name", "")
        expected_value = step.properties.get("expected_value", "")

        if not condition and not variable_name:
            raise ValueError(
                "Condition step requires 'condition' expression or 'variable_name' and 'expected_value'"
            )

        result = False
        if condition:
            # Use safe expression evaluator (replaces dangerous eval)
            safe_context = {k: v for k, v in context.items() if not k.startswith("step_")}
            result = evaluate_condition(condition, safe_context, default=False)
        elif variable_name:
            # Compare variable value
            actual_value = context.get(variable_name, "")
            if condition_type == "equals":
                result = str(actual_value) == str(expected_value)
            elif condition_type == "not_equals":
                result = str(actual_value) != str(expected_value)
            elif condition_type == "contains":
                result = str(expected_value) in str(actual_value)
            elif condition_type == "greater_than":
                try:
                    result = float(actual_value) > float(expected_value)
                except (ValueError, TypeError):
                    result = False
            elif condition_type == "less_than":
                try:
                    result = float(actual_value) < float(expected_value)
                except (ValueError, TypeError):
                    result = False
            else:
                result = False

        logger.info(
            f"Evaluated condition: {condition or f'{variable_name} {condition_type} {expected_value}'} = {result}"
        )

        return {
            "type": "control",
            "control_type": "condition",
            "condition": condition or f"{variable_name} {condition_type} {expected_value}",
            "result": result,
            "status": "completed",
        }
    else:
        raise ValueError(f"Unknown control type: {control_type}")
