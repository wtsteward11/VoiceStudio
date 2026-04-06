"""GAP-062: Single authority for torch-family venv resolution diagnostics.

Probes each relevant VenvFamily's python.exe via subprocess (no torch import in API worker).
"""

from __future__ import annotations

import logging
import subprocess
from enum import Enum
from pathlib import Path
from typing import Any, Callable

logger = logging.getLogger(__name__)

SOURCE_RESOLVER = "torch_venv_resolver"


class TorchRuntimeStatus(str, Enum):
    PRESENT = "present"
    MISSING = "missing"
    INCOMPATIBLE = "incompatible"
    UNRESOLVED = "unresolved"


def _torch_relevant_families() -> tuple[Any, ...]:
    from app.core.runtime.venv_family_manager import VenvFamily

    return (
        VenvFamily.CORE_TTS,
        VenvFamily.ADVANCED_TTS,
        VenvFamily.STT,
        VenvFamily.VOICE_CONVERSION,
    )


def _family_config_engines(family: Any) -> list[str]:
    from app.core.runtime.venv_family_manager import FAMILY_CONFIGS

    cfg = FAMILY_CONFIGS.get(family)
    return list(cfg.engines) if cfg and cfg.engines else []


def probe_torch_version(
    python_exe: Path,
    *,
    timeout_sec: float = 45.0,
) -> tuple[str | None, str | None]:
    """Run torch version probe in the given interpreter. Returns (version, error_message)."""
    cmd = [str(python_exe), "-c", "import torch; print(torch.__version__)"]

    try:
        proc = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=timeout_sec,
            check=False,
        )
        if proc.returncode != 0:
            err = (proc.stderr or proc.stdout or "").strip() or f"exit_code={proc.returncode}"
            return None, err
        ver = (proc.stdout or "").strip()
        if not ver:
            return None, "empty_torch_version"
        return ver, None
    except subprocess.TimeoutExpired:
        return None, "probe_timeout"
    except OSError as e:
        return None, str(e)
    except Exception as e:
        logger.warning("torch probe failed: %s", e)
        return None, str(e)


def resolve_family_torch_status(
    manager: Any,
    family: Any,
    *,
    probe_fn: Callable[[Path], tuple[str | None, str | None]] | None = None,
) -> dict[str, Any]:
    """Resolve torch status for one VenvFamily."""
    from app.core.runtime.venv_family_manager import FAMILY_CONFIGS

    cfg = FAMILY_CONFIGS.get(family)
    family_value = family.value if hasattr(family, "value") else str(family)
    engines = _family_config_engines(family)
    base: dict[str, Any] = {
        "family": family_value,
        "engines": engines,
        "source": SOURCE_RESOLVER,
    }

    if not manager.is_venv_created(family):
        base.update(
            {
                "status": TorchRuntimeStatus.MISSING.value,
                "python_exe": None,
                "torch_version": None,
                "detail": "venv_not_created",
            }
        )
        return base

    python_exe = manager.get_python_executable(family)
    exe_str = str(python_exe)
    base["python_exe"] = exe_str

    if probe_fn is not None:
        version, err = probe_fn(Path(python_exe))
    else:
        version, err = probe_torch_version(Path(python_exe))

    if version:
        base.update(
            {
                "status": TorchRuntimeStatus.PRESENT.value,
                "torch_version": version,
                "detail": None,
            }
        )
    else:
        base.update(
            {
                "status": TorchRuntimeStatus.INCOMPATIBLE.value,
                "torch_version": None,
                "detail": err or "probe_failed",
            }
        )
    return base


def resolve_torch_runtime(
    engine_id: str,
    *,
    probe_fn: Callable[[Path], tuple[str | None, str | None]] | None = None,
) -> dict[str, Any]:
    """Map engine_id to VenvFamily and return resolution (including UNRESOLVED)."""
    from app.core.runtime.venv_family_manager import get_venv_manager

    mgr = get_venv_manager()
    fam = mgr.get_family_for_engine(engine_id)
    if fam is None:
        return {
            "engine_id": engine_id,
            "status": TorchRuntimeStatus.UNRESOLVED.value,
            "family": None,
            "python_exe": None,
            "torch_version": None,
            "source": SOURCE_RESOLVER,
            "detail": "engine_not_mapped",
        }

    if fam not in _torch_relevant_families():
        return {
            "engine_id": engine_id,
            "status": TorchRuntimeStatus.UNRESOLVED.value,
            "family": fam.value,
            "python_exe": None,
            "torch_version": None,
            "source": SOURCE_RESOLVER,
            "detail": "not_torch_relevant_family",
        }

    row = resolve_family_torch_status(mgr, fam, probe_fn=probe_fn)
    row["engine_id"] = engine_id
    return row


def build_effective_torch_status_payload() -> dict[str, Any]:
    """Payload for GET /api/settings/torch-venv/effective."""
    from app.core.runtime.venv_family_manager import get_venv_manager

    mgr = get_venv_manager()
    families_out: list[dict[str, Any]] = []
    for fam in _torch_relevant_families():
        families_out.append(resolve_family_torch_status(mgr, fam))

    return {
        "source": SOURCE_RESOLVER,
        "families": families_out,
    }
