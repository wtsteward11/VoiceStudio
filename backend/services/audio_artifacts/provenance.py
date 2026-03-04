"""
Provenance adapter for the artifact spine.

Calls the canonical provenance writer (write_provenance_sidecar).
Does not import route modules.
"""

from __future__ import annotations


def write_provenance(
    audio_path: str | Path,
    *,
    audio_id: str,
    created_by: str,
    metadata: dict | None = None,
) -> None:
    """
    Write provenance metadata for an audio artifact.

    Delegates to the canonical write_provenance_sidecar.
    """
    from backend.services.security_service import write_provenance_sidecar

    path_str = str(audio_path)
    write_provenance_sidecar(output_base_path=path_str, model_used=created_by)
