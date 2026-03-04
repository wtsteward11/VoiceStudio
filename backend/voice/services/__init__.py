"""Voice services: synthesis, policy enforcement, artifact creation.

M4: SynthesisService moved to backend.services.synthesis_service.
Re-export for backward compatibility.
"""

from backend.services.synthesis_service import SynthesisService

__all__ = ["SynthesisService"]
