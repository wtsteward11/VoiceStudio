"""
Safety Scanning Routes

Endpoints for scanning text and audio content for safety issues
like inappropriate content, hate speech, or policy violations.
"""

from __future__ import annotations

import logging
import re
from typing import Any

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from ..models_additional import SafetyScanRequest

logger = logging.getLogger(__name__)


class SafetyFlag(BaseModel):
    """A safety flag detected in content."""

    category: str
    severity: str
    matches: int
    message: str


class SafetyScanResponse(BaseModel):
    """Response model for safety scanning."""

    flags: list[SafetyFlag]
    overall_safe: bool
    severity_scores: dict[str, float]
    max_severity: float
    recommendations: list[str]
    text_length: int


class SafetyCategory(BaseModel):
    """Info about a safety category."""

    id: str
    name: str
    description: str


class SafetyCategoriesResponse(BaseModel):
    """Response model for safety categories."""

    categories: list[SafetyCategory]


router = APIRouter(prefix="/api/safety", tags=["safety"])

# Safety patterns (in production, use a proper content moderation service)
_SAFETY_PATTERNS = {
    "hate_speech": [
        r"\b(hate|kill|destroy)\s+(you|them|those)\b",
        r"\b(racial|ethnic)\s+(slur|epithet)\b",
    ],
    "violence": [
        r"\b(kill|murder|violence|attack|harm)\b",
        r"\b(weapon|gun|knife|bomb)\b",
    ],
    "explicit": [
        r"\b(explicit|sexual|pornographic)\b",
    ],
    "self_harm": [
        r"\b(suicide|self.harm|cutting)\b",
    ],
}


@router.post("/scan", response_model=SafetyScanResponse)
async def scan(req: SafetyScanRequest) -> SafetyScanResponse:
    """
    Scan text content for safety issues.

    Checks for:
    - Hate speech
    - Violence
    - Explicit content
    - Self-harm references

    Args:
        req: Request with text to scan

    Returns:
        Dictionary with safety flags and recommendations
    """
    try:
        text = req.text
        if not text:
            raise HTTPException(status_code=400, detail="text is required")

        # Normalize text for scanning
        text_lower = text.lower()

        # Scan for safety issues
        flags: list[dict[str, Any]] = []
        severity_scores = {
            "hate_speech": 0.0,
            "violence": 0.0,
            "explicit": 0.0,
            "self_harm": 0.0,
        }

        # Check each category
        for category, patterns in _SAFETY_PATTERNS.items():
            category_name = category
            patterns = patterns

            matches = []
            for pattern in patterns:
                matches_found = re.findall(pattern, text_lower, re.IGNORECASE)
                matches.extend(matches_found)

            if matches:
                # Calculate severity based on number of matches
                match_count = len(matches)
                severity = min(1.0, match_count / 5.0)  # Normalize to 0.0-1.0

                severity_scores[category_name] = severity

                flags.append(
                    {
                        "category": category_name,
                        "severity": (
                            "high" if severity > 0.7 else "medium" if severity > 0.3 else "low"
                        ),
                        "matches": len(matches),
                        "message": f"Detected {category_name} content ({len(matches)} matches)",
                    }
                )

        # Calculate overall safety score (0.0 = safe, 1.0 = unsafe)
        max_severity = max(severity_scores.values()) if severity_scores.values() else 0.0
        overall_safe = max_severity < 0.3

        # Generate recommendations
        recommendations = []
        if not overall_safe:
            if severity_scores.get("hate_speech", 0.0) > 0.3:
                recommendations.append("Content contains hate speech. Consider revising.")
            if severity_scores.get("violence", 0.0) > 0.3:
                recommendations.append("Content contains violent language. Consider revising.")
            if severity_scores.get("explicit", 0.0) > 0.3:
                recommendations.append("Content contains explicit material. Consider revising.")
            if severity_scores.get("self_harm", 0.0) > 0.3:
                recommendations.append("Content contains self-harm references. Consider revising.")
        else:
            recommendations.append("Content appears safe for use.")

        logger.info(
            f"Safety scan completed: {len(flags)} flags found, "
            f"overall_safe={overall_safe}, max_severity={max_severity:.2f}"
        )

        return SafetyScanResponse(
            flags=[SafetyFlag(**f) for f in flags],
            overall_safe=overall_safe,
            severity_scores=severity_scores,
            max_severity=float(max_severity),
            recommendations=recommendations,
            text_length=len(text),
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Safety scan failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Safety scan failed: {e!s}") from e


@router.get("/categories", response_model=SafetyCategoriesResponse)
async def get_safety_categories() -> SafetyCategoriesResponse:
    """Get list of safety categories that are scanned."""
    return SafetyCategoriesResponse(
        categories=[
            SafetyCategory(
                id="hate_speech",
                name="Hate Speech",
                description="Content containing hateful or discriminatory language",
            ),
            SafetyCategory(
                id="violence",
                name="Violence",
                description="Content containing violent language or threats",
            ),
            SafetyCategory(
                id="explicit",
                name="Explicit Content",
                description="Content containing explicit or adult material",
            ),
            SafetyCategory(
                id="self_harm",
                name="Self-Harm",
                description="Content containing references to self-harm or suicide",
            ),
        ],
    )
