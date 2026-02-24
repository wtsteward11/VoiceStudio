"""
Hugging Face API Endpoint Fix

Ensures the new router endpoint is used throughout the application.

This module is imported very early in backend startup (see backend/api/main.py) so
environment variables are set before any huggingface_hub imports.
"""

from __future__ import annotations

import logging
import os
from typing import Any

from fastapi import APIRouter

logger = logging.getLogger(__name__)

HF_INFERENCE_API_BASE = "https://router.huggingface.co"
HF_INFERENCE_API_LEGACY = "https://api-inference.huggingface.co"

router = APIRouter(prefix="/api/huggingface-fix", tags=["huggingface-fix"])


def apply_fix() -> dict[str, Any]:
    """
    Apply the Hugging Face endpoint fix in a deterministic, low-risk way.

    - Set HF_INFERENCE_API_BASE and HF_ENDPOINT to the router endpoint.
    - If huggingface_hub is already imported, patch its constant and InferenceClient default base_url.
    - If transformers is already imported, patch API_BASES when available.

    Returns a small status payload useful for logs and diagnostics.
    """

    os.environ["HF_INFERENCE_API_BASE"] = HF_INFERENCE_API_BASE
    os.environ["HF_ENDPOINT"] = HF_INFERENCE_API_BASE

    hub_patched = False
    hf_client_patched = False
    transformers_patched = False

    try:
        import huggingface_hub

        if hasattr(huggingface_hub, "constants"):
            huggingface_hub.constants.HF_INFERENCE_API_BASE = HF_INFERENCE_API_BASE
            hub_patched = True

        if hasattr(huggingface_hub, "InferenceClient"):
            original_init = huggingface_hub.InferenceClient.__init__

            def patched_init(self, *args, **kwargs):
                kwargs["base_url"] = HF_INFERENCE_API_BASE
                return original_init(self, *args, **kwargs)

            huggingface_hub.InferenceClient.__init__ = patched_init
            hf_client_patched = True

    except ImportError as ex:
        # Not imported yet (ideal). Env vars will be picked up by the first import.
        logger.debug("huggingface_hub not available for patching: %s", ex)
    except Exception as ex:
        logger.debug("huggingface_hub patch skipped: %s", ex)

    try:
        import transformers

        if hasattr(transformers, "API_BASES"):
            transformers.API_BASES["huggingface"] = HF_INFERENCE_API_BASE
            transformers_patched = True

    except ImportError as ex:
        logger.debug("transformers not available for patching: %s", ex)
    except Exception as ex:
        logger.debug("transformers patch skipped: %s", ex)

    payload: dict[str, Any] = {
        "hf_inference_api_base": os.environ.get("HF_INFERENCE_API_BASE"),
        "hf_endpoint": os.environ.get("HF_ENDPOINT"),
        "hub_constants_patched": hub_patched,
        "inference_client_patched": hf_client_patched,
        "transformers_api_bases_patched": transformers_patched,
    }

    logger.info("Hugging Face endpoint fix applied: %s", payload)
    return payload


@router.get("/status")
def status() -> dict[str, Any]:
    """Return current endpoint configuration (for smoke/proof diagnostics)."""
    return {
        "hf_inference_api_base": os.environ.get("HF_INFERENCE_API_BASE"),
        "hf_endpoint": os.environ.get("HF_ENDPOINT"),
    }


@router.post("/apply")
def apply() -> dict[str, Any]:
    """Re-apply the fix at runtime (safe, idempotent)."""
    return apply_fix()


# Apply on import (backend/api/main.py imports this module first).
apply_fix()
