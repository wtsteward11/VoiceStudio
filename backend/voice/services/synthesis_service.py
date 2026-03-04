"""
M4: SynthesisService moved to backend.services.synthesis_service.

This module re-exports for backward compatibility.
"""

from backend.services.synthesis_service import SynthesisService

__all__ = ["SynthesisService"]
