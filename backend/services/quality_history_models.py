"""
Pydantic models for quality history (shared by quality routes and batch pipeline).

Kept in services so route modules do not cross-import each other for this model.
"""

from __future__ import annotations

from typing import Any

from pydantic import BaseModel


class QualityHistoryEntry(BaseModel):
    """Quality history entry for a voice profile."""

    id: str
    profile_id: str
    project_id: str | None = None
    timestamp: str
    engine: str
    metrics: dict[str, Any]
    quality_score: float
    synthesis_text: str | None = None
    audio_url: str | None = None
    enhanced_quality: bool = False
    metadata: dict[str, Any] | None = None
