"""
Engine Service - Abstraction layer for engine operations.

This service provides a clean architecture boundary between API routes and the engine layer.
Routes should inject EngineService instead of importing from app.core.engines directly.

Architecture:
    Routes (API) -> EngineService (Service) -> Engine Layer (app.core.engines)

This follows the Clean Architecture principle of dependency inversion.
"""

from abc import ABC, abstractmethod
from pathlib import Path
from typing import Any, Dict, List, Optional, Union

# Type definitions for engine operations
EngineId = str
AudioPath = Union[str, Path]
MetricsResult = Dict[str, Any]
SynthesisResult = Dict[str, Any]


class IEngineService(ABC):
    """Abstract interface for engine operations.
    
    This interface defines the contract that all engine service implementations
    must follow. Routes should depend on this interface, not concrete implementations.
    """

    # -------------------------------------------------------------------------
    # Engine Discovery and Management
    # -------------------------------------------------------------------------

    @abstractmethod
    def list_engines(self) -> List[Dict[str, Any]]:
        """List all available engines with their metadata."""
        ...

    @abstractmethod
    def get_engine(self, engine_id: EngineId) -> Optional[Any]:
        """Get an engine instance by ID."""
        ...

    @abstractmethod
    def is_engine_available(self, engine_id: EngineId) -> bool:
        """Check if an engine is available and ready to use."""
        ...

    @abstractmethod
    def get_engine_status(self, engine_id: EngineId) -> Dict[str, Any]:
        """Get the current status of an engine."""
        ...

    # -------------------------------------------------------------------------
    # Voice Synthesis Operations
    # -------------------------------------------------------------------------

    @abstractmethod
    def synthesize(
        self,
        engine_id: EngineId,
        text: str,
        voice_id: Optional[str] = None,
        **kwargs: Any,
    ) -> SynthesisResult:
        """Synthesize speech from text using the specified engine."""
        ...

    @abstractmethod
    def clone_voice(
        self,
        engine_id: EngineId,
        reference_audio: AudioPath,
        text: str,
        **kwargs: Any,
    ) -> SynthesisResult:
        """Clone a voice using reference audio."""
        ...

    # -------------------------------------------------------------------------
    # Transcription Operations
    # -------------------------------------------------------------------------

    @abstractmethod
    def transcribe(
        self,
        engine_id: EngineId,
        audio_path: AudioPath,
        language: Optional[str] = None,
        **kwargs: Any,
    ) -> Dict[str, Any]:
        """Transcribe audio to text using the specified engine."""
        ...

    # -------------------------------------------------------------------------
    # Quality Metrics
    # -------------------------------------------------------------------------

    @abstractmethod
    def calculate_metrics(
        self,
        audio_path: AudioPath,
        reference_path: Optional[AudioPath] = None,
    ) -> MetricsResult:
        """Calculate quality metrics for an audio file."""
        ...

    @abstractmethod
    def calculate_similarity(
        self,
        audio1_path: AudioPath,
        audio2_path: AudioPath,
    ) -> float:
        """Calculate similarity between two audio files."""
        ...

    @abstractmethod
    def calculate_mos_score(self, audio_path: AudioPath) -> float:
        """Calculate Mean Opinion Score for an audio file."""
        ...

    # -------------------------------------------------------------------------
    # Quality Optimization
    # -------------------------------------------------------------------------

    @abstractmethod
    def get_quality_presets(self) -> List[Dict[str, Any]]:
        """Get available quality presets."""
        ...

    @abstractmethod
    def get_synthesis_params_from_preset(
        self,
        preset_name: str,
        engine_id: Optional[EngineId] = None,
    ) -> Dict[str, Any]:
        """Get synthesis parameters for a quality preset."""
        ...


class EngineService(IEngineService):
    """Concrete implementation of the engine service.
    
    This implementation delegates to the existing engine layer (app.core.engines).
    It provides a stable interface while allowing the underlying implementation
    to evolve.
    """

    def __init__(self):
        """Initialize the engine service with lazy loading."""
        self._engine_router = None
        self._quality_metrics = None
        self._quality_optimizer = None
        self._quality_presets = None
        self._engines_loaded = False

    def _ensure_engines_loaded(self):
        """Lazy load engine modules to avoid import issues at startup."""
        if self._engines_loaded:
            return

        try:
            from app.core.engines import router
            self._engine_router = router
        except ImportError:
            self._engine_router = None

        try:
            from app.core.engines import quality_metrics
            self._quality_metrics = quality_metrics
        except ImportError:
            self._quality_metrics = None

        try:
            from app.core.engines import quality_optimizer
            self._quality_optimizer = quality_optimizer
        except ImportError:
            self._quality_optimizer = None

        try:
            from app.core.engines import quality_presets
            self._quality_presets = quality_presets
        except ImportError:
            self._quality_presets = None

        self._engines_loaded = True

    # -------------------------------------------------------------------------
    # Engine Discovery and Management
    # -------------------------------------------------------------------------

    def list_engines(self) -> List[Dict[str, Any]]:
        """List all available engines with their metadata."""
        self._ensure_engines_loaded()
        if self._engine_router is None:
            return []
        
        try:
            return self._engine_router.list_engines()
        except Exception:
            return []

    def get_engine(self, engine_id: EngineId) -> Optional[Any]:
        """Get an engine instance by ID."""
        self._ensure_engines_loaded()
        if self._engine_router is None:
            return None
        
        try:
            return self._engine_router.get_engine(engine_id)
        except Exception:
            return None

    def is_engine_available(self, engine_id: EngineId) -> bool:
        """Check if an engine is available and ready to use."""
        engine = self.get_engine(engine_id)
        return engine is not None

    def get_engine_status(self, engine_id: EngineId) -> Dict[str, Any]:
        """Get the current status of an engine."""
        self._ensure_engines_loaded()
        if self._engine_router is None:
            return {"status": "unavailable", "error": "Engine router not loaded"}
        
        try:
            engine = self._engine_router.get_engine(engine_id)
            if engine is None:
                return {"status": "not_found", "engine_id": engine_id}
            
            return {
                "status": "available",
                "engine_id": engine_id,
                "ready": getattr(engine, "ready", True),
            }
        except Exception as e:
            return {"status": "error", "error": str(e)}

    # -------------------------------------------------------------------------
    # Voice Synthesis Operations
    # -------------------------------------------------------------------------

    def synthesize(
        self,
        engine_id: EngineId,
        text: str,
        voice_id: Optional[str] = None,
        **kwargs: Any,
    ) -> SynthesisResult:
        """Synthesize speech from text using the specified engine."""
        self._ensure_engines_loaded()
        if self._engine_router is None:
            return {"error": "Engine router not available"}
        
        try:
            engine = self._engine_router.get_engine(engine_id)
            if engine is None:
                return {"error": f"Engine {engine_id} not found"}
            
            result = engine.synthesize(text, voice_id=voice_id, **kwargs)
            return result if isinstance(result, dict) else {"audio_path": str(result)}
        except Exception as e:
            return {"error": str(e)}

    def clone_voice(
        self,
        engine_id: EngineId,
        reference_audio: AudioPath,
        text: str,
        **kwargs: Any,
    ) -> SynthesisResult:
        """Clone a voice using reference audio."""
        self._ensure_engines_loaded()
        if self._engine_router is None:
            return {"error": "Engine router not available"}
        
        try:
            engine = self._engine_router.get_engine(engine_id)
            if engine is None:
                return {"error": f"Engine {engine_id} not found"}
            
            result = engine.clone_voice(
                reference_audio=str(reference_audio),
                text=text,
                **kwargs,
            )
            return result if isinstance(result, dict) else {"audio_path": str(result)}
        except Exception as e:
            return {"error": str(e)}

    # -------------------------------------------------------------------------
    # Transcription Operations
    # -------------------------------------------------------------------------

    def transcribe(
        self,
        engine_id: EngineId,
        audio_path: AudioPath,
        language: Optional[str] = None,
        **kwargs: Any,
    ) -> Dict[str, Any]:
        """Transcribe audio to text using the specified engine."""
        self._ensure_engines_loaded()
        if self._engine_router is None:
            return {"error": "Engine router not available"}
        
        try:
            engine = self._engine_router.get_engine(engine_id)
            if engine is None:
                return {"error": f"Engine {engine_id} not found"}
            
            result = engine.transcribe(
                audio_path=str(audio_path),
                language=language,
                **kwargs,
            )
            return result if isinstance(result, dict) else {"text": str(result)}
        except Exception as e:
            return {"error": str(e)}

    # -------------------------------------------------------------------------
    # Quality Metrics
    # -------------------------------------------------------------------------

    def calculate_metrics(
        self,
        audio_path: AudioPath,
        reference_path: Optional[AudioPath] = None,
    ) -> MetricsResult:
        """Calculate quality metrics for an audio file."""
        self._ensure_engines_loaded()
        if self._quality_metrics is None:
            return {"error": "Quality metrics not available"}
        
        try:
            return self._quality_metrics.calculate_all_metrics(
                audio_path=str(audio_path),
                reference_path=str(reference_path) if reference_path else None,
            )
        except Exception as e:
            return {"error": str(e)}

    def calculate_similarity(
        self,
        audio1_path: AudioPath,
        audio2_path: AudioPath,
    ) -> float:
        """Calculate similarity between two audio files."""
        self._ensure_engines_loaded()
        if self._quality_metrics is None:
            return 0.0
        
        try:
            return self._quality_metrics.calculate_similarity(
                str(audio1_path),
                str(audio2_path),
            )
        except Exception:
            return 0.0

    def calculate_mos_score(self, audio_path: AudioPath) -> float:
        """Calculate Mean Opinion Score for an audio file."""
        self._ensure_engines_loaded()
        if self._quality_metrics is None:
            return 0.0
        
        try:
            return self._quality_metrics.calculate_mos_score(str(audio_path))
        except Exception:
            return 0.0

    # -------------------------------------------------------------------------
    # Quality Optimization
    # -------------------------------------------------------------------------

    def get_quality_presets(self) -> List[Dict[str, Any]]:
        """Get available quality presets."""
        self._ensure_engines_loaded()
        if self._quality_presets is None:
            return []
        
        try:
            return self._quality_presets.list_quality_presets()
        except Exception:
            return []

    def get_synthesis_params_from_preset(
        self,
        preset_name: str,
        engine_id: Optional[EngineId] = None,
    ) -> Dict[str, Any]:
        """Get synthesis parameters for a quality preset."""
        self._ensure_engines_loaded()
        if self._quality_presets is None:
            return {}
        
        try:
            return self._quality_presets.get_synthesis_params_from_preset(
                preset_name,
                engine_id=engine_id,
            )
        except Exception:
            return {}


# Singleton instance for dependency injection
_engine_service_instance: Optional[EngineService] = None


def get_engine_service() -> IEngineService:
    """FastAPI dependency for engine service injection.
    
    Usage in routes:
        from backend.services.engine_service import get_engine_service, IEngineService
        
        @router.get("/engines")
        async def list_engines(engine_service: IEngineService = Depends(get_engine_service)):
            return engine_service.list_engines()
    """
    global _engine_service_instance
    if _engine_service_instance is None:
        _engine_service_instance = EngineService()
    return _engine_service_instance


# Type alias for FastAPI dependency
EngineServiceDep = IEngineService
