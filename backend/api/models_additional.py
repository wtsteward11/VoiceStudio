"""
Additional API models for voice synthesis, image generation, and quality analysis.

GAP-I16: Models should inherit from VoiceStudioBaseModel for consistent null handling.
New models MUST inherit from VoiceStudioBaseModel. Existing models are being migrated.

See: docs/contracts/NULL_HANDLING_POLICY.md
"""

from __future__ import annotations

import re
from typing import Any

from pydantic import BaseModel, ConfigDict, Field, field_validator

# GAP-I16: Import base model for consistent null handling
from backend.api.models import VoiceStudioBaseModel

# =============================================================================
# Testing and Evaluation Models (use VoiceStudioBaseModel)
# =============================================================================


class AbxStartRequest(VoiceStudioBaseModel):
    items: list[str]


class AbxResult(VoiceStudioBaseModel):
    item: str
    mos: float
    pref: str


class DatasetScoreRequest(VoiceStudioBaseModel):
    clips: list[str]


class ScoreResult(VoiceStudioBaseModel):
    clip: str
    snr: float
    lufs: float
    quality: float


class Telemetry(VoiceStudioBaseModel):
    engine_ms: float
    underruns: int
    vram_pct: float


# =============================================================================
# Profile Models (shared by profiles route and voice_morph, voice_cloning_wizard)
# =============================================================================


class VoiceProfile(VoiceStudioBaseModel):
    """Voice profile model with avatar support."""

    id: str
    name: str
    language: str = "en"
    emotion: str | None = None
    quality_score: float = 0.0
    tags: list[str] = []
    reference_audio_url: str | None = None
    reference_audio_bound: bool = False
    avatar_url: str | None = None  # URL or path to profile avatar image


class ProfileCreateRequest(VoiceStudioBaseModel):
    """Request model for creating a new voice profile."""

    name: str
    language: str = "en"
    emotion: str | None = None
    tags: list[str] = []
    avatar_url: str | None = None
    description: str | None = None  # Used by voice_cloning_wizard
    reference_audio_id: str | None = None  # Used by voice_cloning_wizard
    engine: str | None = None  # Used by voice_cloning_wizard
    quality_mode: str | None = None  # Used by voice_cloning_wizard


# =============================================================================
# Audio Processing Models (use VoiceStudioBaseModel)
# =============================================================================


class AdrAlignRequest(VoiceStudioBaseModel):
    video_id: str
    audio_id: str


class ProsodyQuantizeRequest(VoiceStudioBaseModel):
    audio_id: str
    grid: str = "1/8"


class EmotionApplyRequest(VoiceStudioBaseModel):
    audio_id: str
    curve: list[float]


class FormantEditRequest(VoiceStudioBaseModel):
    audio_id: str
    shifts: dict[str, float]


class SpectralInpaintRequest(VoiceStudioBaseModel):
    audio_id: str
    mask: str


class ModelInspectRequest(VoiceStudioBaseModel):
    layer: int


class GranularRenderRequest(VoiceStudioBaseModel):
    audio_id: str
    params: dict[str, Any]


class RvcStartRequest(VoiceStudioBaseModel):
    target_voice: str


class DubTranslateRequest(VoiceStudioBaseModel):
    audio_id: str
    lang: str


class ArticulationAnalyzeRequest(VoiceStudioBaseModel):
    audio_id: str


class NrApplyRequest(VoiceStudioBaseModel):
    audio_id: str
    noise_print_id: str


class RepairClippingRequest(VoiceStudioBaseModel):
    audio_id: str


class SceneMixAnalyzeRequest(VoiceStudioBaseModel):
    tracks: list[str]


class RmTrainRequest(VoiceStudioBaseModel):
    ratings: list[dict[str, Any]]


class SafetyScanRequest(VoiceStudioBaseModel):
    text: str


class ImgSamplerRequest(VoiceStudioBaseModel):
    prompt: str
    sampler: str = "ddim"


class AssistantRunRequest(VoiceStudioBaseModel):
    action_id: str
    params: dict[str, Any] | None = None


# =============================================================================
# Voice style / pronunciation (explicit JSON bodies for OpenAPI contract)
# =============================================================================


class PronunciationTestRequest(VoiceStudioBaseModel):
    """Request body for pronunciation testing."""

    word: str = ""
    phonemes: str | None = None
    language: str = "en"


class VoiceSynthesizeWithStyleRequest(VoiceStudioBaseModel):
    """JSON body for style-controlled synthesis (replaces query-only parameters)."""

    text: str = Field(..., min_length=1, max_length=10000)
    profile_id: str = Field(..., min_length=1, max_length=100)
    engine: str = Field(default="openvoice", max_length=50)
    language: str = Field(default="en", max_length=10)
    emotion: str | None = Field(default=None, max_length=50)
    accent: str | None = Field(default=None, max_length=50)
    rhythm: float | None = Field(default=None)
    pauses: str | None = Field(default=None, max_length=200)
    pitch_shift: float | None = Field(default=None)
    pitch_variance: float | None = Field(default=None)
    energy: float | None = Field(default=None)
    enhance_quality: bool = True
    calculate_quality: bool = True


class VoiceSynthesizeCrossLingualRequest(VoiceStudioBaseModel):
    """JSON body for cross-lingual synthesis."""

    text: str = Field(..., min_length=1, max_length=10000)
    profile_id: str = Field(..., min_length=1, max_length=100)
    source_language: str = Field(default="en", max_length=10)
    target_language: str = Field(default="es", max_length=10)
    engine: str = Field(default="openvoice", max_length=50)
    enhance_quality: bool = True
    calculate_quality: bool = True


# =============================================================================
# Voice Cloning Models (GAP-I16: Use VoiceStudioBaseModel)
# =============================================================================


class VoiceSynthesizeRequest(VoiceStudioBaseModel):
    """Request model for voice synthesis with validation."""

    model_config = ConfigDict(
        # Optimize validation performance
        validate_assignment=False,  # Skip validation on assignment
        use_enum_values=True,  # Use enum values directly
        str_strip_whitespace=True,  # Strip whitespace automatically
        validate_default=False,  # Skip validation for default values
    )

    engine: str | None = Field(
        default=None,
        description=(
            "Engine name (e.g., xtts_v2, piper, chatterbox). " "Defaults to XTTS if not specified."
        ),
        min_length=1,
        max_length=50,
    )
    profile_id: str = Field(
        ...,
        description="Voice profile ID",
        min_length=1,
        max_length=100,
    )
    text: str = Field(
        ...,
        description="Text to synthesize",
        min_length=1,
        max_length=10000,
    )
    language: str | None = Field(
        default="en",
        description="Language code (ISO 639-1)",
        max_length=10,
    )
    emotion: str | None = Field(
        default=None,
        description="Emotion to apply",
        max_length=50,
    )
    enhance_quality: bool | None = Field(
        default=False,
        description="Enable quality enhancement pipeline",
    )
    consent_id: str | None = Field(
        default=None,
        description="Consent record ID required when synthesizing with a third-party voice profile",
        max_length=100,
    )
    speed: float | None = Field(
        default=None,
        description="Speech speed (0.5-2.0, default 1.0)",
        ge=0.5,
        le=2.0,
    )
    pitch: float | None = Field(
        default=None,
        description="Pitch shift in semitones (-12 to 12)",
        ge=-12.0,
        le=12.0,
    )
    stability: float | None = Field(
        default=None,
        description="Voice stability (0-1)",
        ge=0.0,
        le=1.0,
    )
    clarity: float | None = Field(
        default=None,
        description="Voice clarity (0-1)",
        ge=0.0,
        le=1.0,
    )
    temperature: float | None = Field(
        default=None,
        description="Sampling temperature (0-1)",
        ge=0.0,
        le=1.0,
    )

    @field_validator("engine", mode="before")
    @classmethod
    def validate_engine(cls, v):
        """Validate engine name format."""
        if v is None:
            return v
        if not isinstance(v, str):
            raise ValueError("Engine must be a string")
        if not re.match(r"^[a-z0-9_-]+$", v.lower()):
            raise ValueError(
                "Engine name must contain only lowercase letters, numbers, "
                "hyphens, and underscores"
            )
        return v.lower()

    @field_validator("profile_id", mode="before")
    @classmethod
    def validate_profile_id(cls, v):
        """Validate profile ID format. Allows alphanumeric, hyphen, underscore for local;
        also colon, slash, dot for third-party IDs (e.g. external/voice_001)."""
        if not re.match(r"^[a-zA-Z0-9_\-:./]{1,100}$", v):
            raise ValueError(
                "Profile ID must contain only alphanumeric, hyphens, underscores, "
                "and optionally colons/slashes/dots for third-party IDs"
            )
        return v

    @field_validator("language", mode="before")
    @classmethod
    def validate_language(cls, v):
        """Validate language code format."""
        if v is None:
            return v
        if not isinstance(v, str):
            raise ValueError("Language must be a string")
        if not re.match(r"^[a-z]{2}(-[A-Z]{2})?$", v.lower()):
            raise ValueError("Language must be a valid ISO 639-1 code (e.g., 'en', 'en-US')")
        return v.lower()

    @field_validator("text", mode="before")
    @classmethod
    def validate_text(cls, v):
        """Validate text content."""
        if v is not None and not isinstance(v, str):
            raise ValueError("Text must be a string")
        if not v or not v.strip():
            raise ValueError("Text cannot be empty")
        if len(v.strip()) > 10000:
            raise ValueError("Text cannot exceed 10000 characters")
        return v.strip()


class QualityMetrics(VoiceStudioBaseModel):
    """Detailed quality metrics for voice cloning (GAP-I16)."""

    # Mean Opinion Score (1.0-5.0)
    mos_score: float | None = None
    # Voice similarity (0.0-1.0)
    similarity: float | None = None
    # Naturalness score (0.0-1.0)
    naturalness: float | None = None
    # Signal-to-noise ratio (dB)
    snr_db: float | None = None
    # Artifact score (0.0-1.0, lower is better)
    artifact_score: float | None = None
    # Whether clicks detected
    has_clicks: bool | None = None
    # Whether distortion detected
    has_distortion: bool | None = None
    voice_profile_match: dict[str, Any] | None = None  # Voice profile matching results


class SsmlHandlingDiagnostics(VoiceStudioBaseModel):
    """Structured SSML handling outcome for synthesis responses (GAP-054)."""

    ssml_detected: bool
    capability_class: str
    action: str
    warnings: list[str] = Field(default_factory=list)
    engine_id: str = ""


class ProsodyHandlingDiagnostics(VoiceStudioBaseModel):
    """Structured prosody transform outcome for apply/control responses (GAP-023)."""

    action: str = Field(
        ...,
        description='Outcome: "applied" | "none"',
    )
    applied_operations: list[str] = Field(default_factory=list)
    skipped_operations: list[dict[str, str]] = Field(default_factory=list)
    warnings: list[str] = Field(default_factory=list)
    errors: list[str] = Field(default_factory=list)
    pitch_factor: float = 1.0
    rate_factor: float = 1.0
    volume_factor: float = 1.0
    context: str = ""


class EmotionApplyExtendedResponseModel(VoiceStudioBaseModel):
    """Response for POST /api/emotion/apply-extended (GAP-050)."""

    audio_id: str
    audio_url: str
    prosody_handling: ProsodyHandlingDiagnostics
    emotion_mapping_source: str = Field(
        default="",
        description=(
            "Mapping lane: canonical_preset, legacy_emotion, blended, "
            "canonical_blended, legacy_blended, canonical_legacy_blended, none"
        ),
    )


class ProsodyApplyResponseModel(VoiceStudioBaseModel):
    """Response body for POST /api/prosody/apply (GAP-023)."""

    audio_id: str
    original_audio_id: str
    audio_url: str
    duration: float
    prosody_applied: bool
    config_applied: dict[str, object]
    prosody_handling: ProsodyHandlingDiagnostics


class VoiceSynthesizeResponse(VoiceStudioBaseModel):
    """Voice synthesis response (GAP-I16)."""

    audio_id: str
    audio_url: str
    duration: float
    # Overall quality score (0.0-1.0)
    quality_score: float
    # Detailed quality metrics
    quality_metrics: QualityMetrics | None = None
    # Present when SSML markup was detected or transformed (GAP-054).
    ssml_handling: SsmlHandlingDiagnostics | None = None


class ABTestRequest(VoiceStudioBaseModel):
    """Request for A/B testing two synthesis configurations (GAP-I16).

    Implements IDEA 46: A/B Testing Interface for Quality Comparison.
    """

    profile_id: str = Field(..., description="Voice profile ID")
    text: str = Field(
        ...,
        description="Text to synthesize for both A and B",
    )
    language: str | None = Field(
        default="en",
        description="Language code",
    )

    # Configuration A
    engine_a: str = Field(..., description="Engine for sample A")
    emotion_a: str | None = Field(
        None,
        description="Emotion for sample A",
    )
    enhance_quality_a: bool = Field(
        default=True,
        description="Enable quality enhancement for A",
    )

    # Configuration B
    engine_b: str = Field(..., description="Engine for sample B")
    emotion_b: str | None = Field(
        None,
        description="Emotion for sample B",
    )
    enhance_quality_b: bool = Field(
        default=True,
        description="Enable quality enhancement for B",
    )


class ABTestResult(VoiceStudioBaseModel):
    """Result for one side of A/B test (GAP-I16)."""

    sample_label: str = Field(
        ...,
        description="Sample label (A or B)",
    )
    audio_id: str = Field(..., description="Audio identifier")
    audio_url: str = Field(..., description="URL to access audio")
    duration: float = Field(
        ...,
        description="Audio duration in seconds",
    )
    engine: str = Field(..., description="Engine used")
    emotion: str | None = Field(
        None,
        description="Emotion applied",
    )
    quality_score: float | None = Field(
        None,
        description="Overall quality score",
    )
    quality_metrics: QualityMetrics | None = Field(None, description="Detailed quality metrics")


class ABTestResponse(VoiceStudioBaseModel):
    """Response from A/B test (GAP-I16)."""

    sample_a: ABTestResult = Field(..., description="Result for sample A")
    sample_b: ABTestResult = Field(..., description="Result for sample B")
    comparison: dict[str, Any] = Field(
        default_factory=dict,
        description="Quality comparison metrics",
    )
    test_id: str = Field(..., description="Unique test identifier")


class VoiceAnalyzeResponse(VoiceStudioBaseModel):
    """Voice analysis response (GAP-I16)."""

    # mos, similarity, naturalness, snr, lufs, etc.
    metrics: dict[str, float]
    quality_score: float | None = None
    missing_dependencies: list[str] = Field(default_factory=list)


class VoiceCloneRequest(VoiceStudioBaseModel):
    """Request model for voice cloning with validation and advanced features (GAP-I16)."""

    reference_audio: str | list[str] = Field(
        ...,
        description="Reference audio file path(s) or ID(s) for single or multi-reference cloning",
        min_length=1,
    )
    text: str | None = Field(
        default=None, description="Optional text for synthesis", max_length=10000
    )
    engine: str = Field(default="xtts", description="Engine to use", min_length=1, max_length=50)
    quality_mode: str = Field(
        default="standard", description="Quality mode: fast, standard, high, ultra"
    )
    enhance_quality: bool = Field(
        default=False, description="Apply advanced quality enhancement pipeline"
    )
    use_multi_reference: bool = Field(
        default=False,
        description="Use ensemble approach when multiple references provided",
    )
    prosody_params: dict[str, float] | None = Field(
        default=None,
        description="Advanced prosody control: pitch (semitones), tempo (multiplier), formant_shift (factor), energy (multiplier)",
    )
    use_rvc_postprocessing: bool = Field(
        default=False,
        description="Apply RVC post-processing for enhanced voice similarity",
    )
    language: str = Field(default="en", description="Language code for synthesis")

    @field_validator("engine", mode="before")
    @classmethod
    def validate_engine(cls, v):
        """Validate engine name format."""
        if not re.match(r"^[a-z0-9_-]+$", v.lower()):
            raise ValueError(
                "Engine name must contain only lowercase letters, numbers, "
                "hyphens, and underscores"
            )
        return v.lower()

    @field_validator("quality_mode", mode="before")
    @classmethod
    def validate_quality_mode(cls, v):
        """Validate quality mode."""
        if v is None:
            return v
        if not isinstance(v, str):
            raise ValueError("Quality mode must be a string")
        valid_modes = ["fast", "standard", "high", "ultra"]
        if v.lower() not in valid_modes:
            raise ValueError(f"Quality mode must be one of: {', '.join(valid_modes)}")
        return v.lower()

    @field_validator("text", mode="before")
    @classmethod
    def validate_text(cls, v):
        """Validate text if provided."""
        if v is not None:
            if not isinstance(v, str):
                raise ValueError("Text must be a string")
            v = v.strip()
            if not v:
                return None
            if len(v) > 10000:
                raise ValueError("Text cannot exceed 10000 characters")
        return v

    @field_validator("prosody_params", mode="before")
    @classmethod
    def validate_prosody_params(cls, v):
        """Validate prosody parameters if provided."""
        if v is not None:
            valid_keys = {"pitch", "tempo", "formant_shift", "energy"}
            invalid_keys = set(v.keys()) - valid_keys
            if invalid_keys:
                raise ValueError(f"Invalid prosody parameters: {', '.join(invalid_keys)}")

            # Validate ranges
            if "pitch" in v and not (-12 <= v["pitch"] <= 12):
                raise ValueError("Pitch must be between -12 and +12 semitones")
            if "tempo" in v and not (0.5 <= v["tempo"] <= 2.0):
                raise ValueError("Tempo must be between 0.5 and 2.0")
            if "formant_shift" in v and not (0.5 <= v["formant_shift"] <= 2.0):
                raise ValueError("Formant shift must be between 0.5 and 2.0")
            if "energy" in v and not (0.5 <= v["energy"] <= 2.0):
                raise ValueError("Energy must be between 0.5 and 2.0")
        return v


class VoiceCloneResponse(VoiceStudioBaseModel):
    """Voice cloning response (GAP-I16)."""

    profile_id: str
    audio_id: str | None = None
    audio_url: str | None = None
    duration: float | None = None
    quality_score: float  # Overall quality score (0.0-1.0)
    quality_metrics: QualityMetrics | None = None  # Detailed quality metrics
    device: str | None = None
    candidate_metrics: list[dict[str, Any]] | None = None


# =============================================================================
# Quality Improvement Models (IDEA 61-70, GAP-I16)
# =============================================================================


class MultiPassSynthesisRequest(VoiceStudioBaseModel):
    """Request model for multi-pass synthesis with quality refinement."""

    engine: str = Field(
        ...,
        description="Engine name",
        min_length=1,
        max_length=50,
    )
    profile_id: str = Field(
        ...,
        description="Voice profile ID",
        min_length=1,
        max_length=100,
    )
    text: str = Field(
        ...,
        description="Text to synthesize",
        min_length=1,
        max_length=10000,
    )
    language: str | None = Field(
        default="en",
        description="Language code",
    )
    emotion: str | None = Field(
        default=None,
        description="Emotion to apply",
    )
    max_passes: int | None = Field(
        default=3,
        description="Maximum number of passes",
        ge=1,
        le=10,
    )
    min_quality_improvement: float | None = Field(
        default=0.02,
        description="Minimum quality improvement to continue (0.0-1.0)",
        ge=0.0,
        le=1.0,
    )
    pass_preset: str | None = Field(
        default=None,
        description=("Pass preset: naturalness_focus, similarity_focus, artifact_focus"),
    )
    adaptive: bool | None = Field(
        default=True,
        description="Adaptively determine optimal pass count",
    )
    consent_id: str | None = Field(
        default=None,
        description="Consent record ID required when synthesizing with a third-party voice profile",
    )

    @field_validator("engine", mode="before")
    @classmethod
    def validate_engine(cls, v):
        if not re.match(r"^[a-z0-9_-]+$", v.lower()):
            raise ValueError(
                "Engine name must contain only lowercase letters, numbers, "
                "hyphens, and underscores"
            )
        return v.lower()


class PassResult(VoiceStudioBaseModel):
    """Result from a single synthesis pass (GAP-I16)."""

    pass_number: int
    audio_id: str
    audio_url: str
    quality_metrics: QualityMetrics
    quality_score: float
    improvement: float | None = None  # Improvement over previous pass


class MultiPassSynthesisResponse(VoiceStudioBaseModel):
    """Response model for multi-pass synthesis (GAP-I16)."""

    audio_id: str  # Final selected audio
    audio_url: str
    duration: float
    quality_score: float
    quality_metrics: QualityMetrics
    passes_completed: int
    passes: list[PassResult]  # All passes for comparison
    best_pass: int  # Pass number with best quality
    improvement_tracking: list[float]  # Quality improvement per pass


class LongFormChunkResult(VoiceStudioBaseModel):
    """One failed chunk in long-form synthesis (GAP-049)."""

    chunk_index: int
    error: str


class LongFormSynthesisRequest(VoiceStudioBaseModel):
    """Long-form TTS: chunked synthesis with merged output (GAP-049)."""

    model_config = ConfigDict(
        validate_assignment=False,
        use_enum_values=True,
        str_strip_whitespace=True,
        validate_default=False,
    )

    engine: str | None = Field(
        default=None,
        description="Engine id; defaults via config when omitted.",
        min_length=1,
        max_length=50,
    )
    profile_id: str = Field(
        ...,
        description="Voice profile ID",
        min_length=1,
        max_length=100,
    )
    text: str = Field(
        ...,
        description="Full text to synthesize (chunked server-side)",
        min_length=1,
        max_length=100_000,
    )
    language: str | None = Field(default="en", description="Language code", max_length=10)
    emotion: str | None = Field(default=None, description="Emotion", max_length=50)
    enhance_quality: bool | None = Field(
        default=False,
        description="Enable quality enhancement pipeline",
    )
    consent_id: str | None = Field(default=None, max_length=100)
    speed: float | None = Field(default=None, ge=0.5, le=2.0)
    pitch: float | None = Field(default=None, ge=-12.0, le=12.0)
    stability: float | None = Field(default=None, ge=0.0, le=1.0)
    clarity: float | None = Field(default=None, ge=0.0, le=1.0)
    temperature: float | None = Field(default=None, ge=0.0, le=1.0)
    chunk_size_chars: int = Field(
        default=1800,
        description="Target max characters per chunk (sentence-bounded)",
        ge=200,
        le=9000,
    )

    @field_validator("engine", mode="before")
    @classmethod
    def validate_engine_long_form(cls, v):
        if v is None:
            return v
        if not isinstance(v, str):
            raise ValueError("Engine must be a string")
        if not re.match(r"^[a-z0-9_-]+$", v.lower()):
            raise ValueError(
                "Engine name must contain only lowercase letters, numbers, "
                "hyphens, and underscores"
            )
        return v.lower()

    @field_validator("profile_id", mode="before")
    @classmethod
    def validate_profile_id_long_form(cls, v):
        if not re.match(r"^[a-zA-Z0-9_\-:./]{1,100}$", v):
            raise ValueError(
                "Profile ID must contain only alphanumeric, hyphens, underscores, "
                "and optionally colons/slashes/dots for third-party IDs"
            )
        return v


class LongFormSynthesisResponse(VoiceStudioBaseModel):
    """Merged long-form synthesis result (GAP-049)."""

    audio_id: str
    audio_url: str
    duration: float
    quality_score: float
    chunks_total: int
    chunks_succeeded: int
    partial_failure: bool
    failed_chunks: list[LongFormChunkResult] = Field(default_factory=list)


class SpeechToSpeechRequest(VoiceStudioBaseModel):
    """Batch speech-to-speech conversion (GAP-051)."""

    model_config = ConfigDict(
        validate_assignment=False,
        use_enum_values=True,
        str_strip_whitespace=True,
        validate_default=False,
    )

    source_audio_id: str = Field(
        ...,
        description="Registered audio artifact id for source speech",
        min_length=1,
        max_length=200,
    )
    target_voice_profile_id: str = Field(
        ...,
        description="Voice profile id whose RVC checkpoint (optional .pth) defines target timbre",
        min_length=1,
        max_length=100,
    )
    engine_id: str | None = Field(
        default=None,
        description="Optional engine hint (reserved; RVC path uses RVCEngine)",
        max_length=50,
    )
    pitch_shift: float = Field(
        default=0.0,
        ge=-12.0,
        le=12.0,
        description="Pitch shift in semitones (applied as integer to RVC)",
    )
    index_rate: float = Field(
        default=0.5,
        ge=0.0,
        le=1.0,
        description="RVC index retrieval blend",
    )
    protect: float = Field(
        default=0.33,
        ge=0.0,
        le=0.5,
        description="Protect voiceless consonants (RVC protect)",
    )
    consent_acknowledged: bool = Field(
        default=False,
        description=(
            "User asserts they have permission to transform this voice. "
            "Required True before conversion proceeds."
        ),
    )
    consent_id: str | None = Field(
        default=None,
        description=(
            "Optional reference to a formal VoiceConsentRecord. "
            "When provided, validated as GRANTED and unexpired."
        ),
        max_length=100,
    )


class SpeechToSpeechResponse(VoiceStudioBaseModel):
    """Speech-to-speech conversion result (GAP-051 + GAP-056 disclosure + watermark)."""

    audio_id: str
    audio_url: str
    duration: float
    quality_score: float | None = None
    engine_used: str = "rvc"
    is_transformed: bool = True
    transformation_type: str = "speech_to_speech"
    source_audio_id: str | None = None
    disclosure_text: str | None = None
    watermark_applied: bool = False
    watermark_method: str | None = None


class StsMarkingStatus(VoiceStudioBaseModel):
    """Transformed-audio durable marking status (GAP-056 slices 2–3)."""

    audio_id: str
    is_transformed: bool
    transformation_type: str | None = None
    source_reference_id: str | None = None
    marked_at: str | None = None
    watermark_applied: bool = False
    watermark_verified: bool | None = None
    watermark_method: str | None = None


class ReferenceAudioPreprocessRequest(BaseModel):
    """Request model for reference audio pre-processing."""

    profile_id: str | None = Field(
        default=None,
        description="Profile ID if processing existing profile",
    )
    reference_audio_path: str | None = Field(
        default=None,
        description="Path to reference audio file",
    )
    auto_enhance: bool | None = Field(
        default=True,
        description="Automatically enhance reference audio",
    )
    select_optimal_segments: bool | None = Field(
        default=True,
        description="Select optimal segments for cloning",
    )
    min_segment_duration: float | None = Field(
        default=1.0,
        description="Minimum segment duration in seconds",
        ge=0.5,
        le=10.0,
    )
    max_segments: int | None = Field(
        default=5,
        description="Maximum number of segments to select",
        ge=1,
        le=20,
    )


class ReferenceAudioAnalysis(BaseModel):
    """Analysis results for reference audio."""

    quality_score: float  # Overall quality score (1-10)
    has_noise: bool
    has_clipping: bool
    has_distortion: bool
    sample_rate: int
    duration: float
    channels: int
    recommendations: list[str]  # List of recommended improvements
    # Selected optimal segments
    optimal_segments: list[dict[str, Any]] | None = None


class ReferenceAudioPreprocessResponse(BaseModel):
    """Response model for reference audio pre-processing."""

    processed_audio_id: str
    processed_audio_url: str
    original_analysis: ReferenceAudioAnalysis
    processed_analysis: ReferenceAudioAnalysis | None = None
    improvements_applied: list[str]  # List of enhancements applied
    quality_improvement: float  # Quality improvement score (0.0-1.0)


class ArtifactRemovalRequest(BaseModel):
    """Request model for artifact removal."""

    audio_id: str = Field(..., description="Audio ID to process")
    artifact_types: list[str] | None = Field(
        default=None,
        description=(
            "Specific artifact types to remove: clicks, pops, distortion, " "glitches, phase_issues"
        ),
    )
    preview: bool | None = Field(default=False, description="Preview removal without applying")
    repair_preset: str | None = Field(
        default=None,
        description="Repair preset: click_removal, distortion_repair, comprehensive",
    )


class ArtifactDetection(BaseModel):
    """Artifact detection results."""

    artifact_type: str
    severity: float  # Severity score (1-10)
    location: float | None = None  # Time location in seconds
    confidence: float  # Detection confidence (0.0-1.0)


class ArtifactRemovalResponse(BaseModel):
    """Response model for artifact removal."""

    audio_id: str  # Original audio ID
    repaired_audio_id: str | None = None  # Repaired audio ID (if not preview)
    repaired_audio_url: str | None = None
    artifacts_detected: list[ArtifactDetection]
    artifacts_removed: list[str]  # Types of artifacts removed
    quality_improvement: float  # Quality improvement score (0.0-1.0)
    preview_available: bool  # Whether preview is available


# Image Generation Models
class ImageGenerateRequest(BaseModel):
    """Request model for image generation with validation."""

    engine: str = Field(
        ...,
        description="Engine name (e.g., sdxl, comfyui, automatic1111)",
        min_length=1,
        max_length=50,
    )
    prompt: str = Field(
        ...,
        description="Text prompt for image generation",
        min_length=1,
        max_length=2000,
    )
    negative_prompt: str | None = Field(
        default="",
        description="Negative prompt",
        max_length=2000,
    )
    width: int | None = Field(
        default=512,
        description="Image width",
        ge=64,
        le=2048,
    )
    height: int | None = Field(
        default=512,
        description="Image height",
        ge=64,
        le=2048,
    )
    steps: int | None = Field(
        default=20,
        description="Number of sampling steps",
        ge=1,
        le=150,
    )
    cfg_scale: float | None = Field(
        default=7.0,
        description="Classifier-free guidance scale",
        ge=1.0,
        le=30.0,
    )
    sampler: str | None = Field(default=None, description="Sampling method")
    seed: int | None = Field(
        default=None,
        description="Random seed (-1 for random)",
    )
    additional_params: dict[str, Any] | None = Field(
        default=None,
        description="Additional engine-specific parameters",
    )

    @field_validator("engine", mode="before")
    @classmethod
    def validate_engine(cls, v):
        """Validate engine name format."""
        if not re.match(r"^[a-z0-9_-]+$", v.lower()):
            raise ValueError(
                "Engine name must contain only lowercase letters, numbers, "
                "hyphens, and underscores"
            )
        return v.lower()

    @field_validator("prompt", mode="before")
    @classmethod
    def validate_prompt(cls, v):
        """Validate prompt content."""
        if v is not None and not isinstance(v, str):
            raise ValueError("Prompt must be a string")
        if not v or not v.strip():
            raise ValueError("Prompt cannot be empty")
        if len(v.strip()) > 2000:
            raise ValueError("Prompt cannot exceed 2000 characters")
        return v.strip()


class ImageGenerateResponse(BaseModel):
    """Response model for image generation."""

    image_id: str
    image_url: str
    image_base64: str  # Base64-encoded image data URL
    width: int
    height: int
    format: str
    metadata: dict[str, Any] | None = None


class ImageUpscaleRequest(BaseModel):
    """Request model for image upscaling."""

    engine: str | None = Field(
        default="realesrgan",
        description="Upscaling engine name",
    )
    image_id: str | None = Field(
        default=None,
        description="ID of stored image to upscale",
    )
    scale: int | None = Field(
        default=4,
        description="Upscaling factor",
        ge=2,
        le=8,
    )
    additional_params: dict[str, Any] | None = Field(
        default=None,
        description="Additional parameters",
    )


class ImageUpscaleResponse(BaseModel):
    """Response model for image upscaling."""

    image_id: str
    image_url: str
    image_base64: str  # Base64-encoded image data URL
    width: int
    height: int
    scale: int


# Video Generation Models
class VideoGenerateRequest(BaseModel):
    """Request model for video generation with validation."""

    engine: str = Field(
        ...,
        description="Engine name (e.g., svd, deforum, fomm)",
        min_length=1,
        max_length=50,
    )
    prompt: str | None = Field(
        default=None,
        description="Text prompt for video generation",
        max_length=2000,
    )
    image_id: str | None = Field(
        default=None,
        description="ID of input image (for image-to-video)",
    )
    audio_id: str | None = Field(
        default=None,
        description="ID of input audio (for audio-to-video)",
    )
    width: int | None = Field(
        default=512,
        description="Video width",
        ge=64,
        le=2048,
    )
    height: int | None = Field(
        default=512,
        description="Video height",
        ge=64,
        le=2048,
    )
    fps: float | None = Field(
        default=24,
        description="Frames per second",
        ge=1,
        le=120,
    )
    duration: float | None = Field(
        default=5.0,
        description="Video duration in seconds",
        ge=0.1,
        le=60,
    )
    steps: int | None = Field(
        default=20,
        description="Number of sampling steps",
        ge=1,
        le=150,
    )
    cfg_scale: float | None = Field(
        default=7.0,
        description="Classifier-free guidance scale",
        ge=1.0,
        le=30.0,
    )
    seed: int | None = Field(
        default=None,
        description="Random seed (-1 for random)",
    )
    additional_params: dict[str, Any] | None = Field(
        default=None,
        description="Additional engine-specific parameters",
    )

    @field_validator("engine", mode="before")
    @classmethod
    def validate_engine(cls, v):
        """Validate engine name format."""
        if not re.match(r"^[a-z0-9_-]+$", v.lower()):
            raise ValueError(
                "Engine name must contain only lowercase letters, numbers, "
                "hyphens, and underscores"
            )
        return v.lower()


class VideoGenerateResponse(BaseModel):
    """Response model for video generation."""

    video_id: str
    video_url: str
    width: int
    height: int
    fps: float
    duration: float
    format: str
    metadata: dict[str, Any] | None = None


class VideoUpscaleRequest(BaseModel):
    """Request model for video upscaling."""

    engine: str | None = Field(
        default="realesrgan",
        description="Upscaling engine name",
    )
    video_id: str | None = Field(
        default=None,
        description="ID of stored video to upscale",
    )
    scale: int | None = Field(
        default=4,
        description="Upscaling factor",
        ge=2,
        le=8,
    )
    additional_params: dict[str, Any] | None = Field(
        default=None,
        description="Additional parameters",
    )


class VideoUpscaleResponse(BaseModel):
    """Response model for video upscaling."""

    video_id: str
    video_url: str
    width: int
    height: int
    fps: float
    duration: float
    scale: int


# Additional Quality Improvement Models (IDEA 64-70)
class VoiceCharacteristicAnalysisRequest(BaseModel):
    """Request model for voice characteristic analysis."""

    audio_id: str = Field(..., description="Audio ID to analyze")
    reference_audio_id: str | None = Field(
        default=None,
        description="Reference audio for comparison",
    )
    include_pitch: bool | None = Field(
        default=True,
        description="Include pitch analysis",
    )
    include_formants: bool | None = Field(
        default=True,
        description="Include formant analysis",
    )
    include_timbre: bool | None = Field(
        default=True,
        description="Include timbre analysis",
    )
    include_prosody: bool | None = Field(
        default=True,
        description="Include prosody analysis",
    )


class VoiceCharacteristicData(BaseModel):
    """Voice characteristic data."""

    pitch_mean: float | None = None
    pitch_std: float | None = None
    formants: list[float] | None = None  # F1, F2, F3
    spectral_centroid: float | None = None
    spectral_rolloff: float | None = None
    mfcc: list[float] | None = None
    prosody_patterns: dict[str, Any] | None = None


class VoiceCharacteristicAnalysisResponse(BaseModel):
    """Response model for voice characteristic analysis."""

    audio_id: str
    characteristics: VoiceCharacteristicData
    reference_characteristics: VoiceCharacteristicData | None = None
    similarity_score: float | None = None  # 0.0-1.0
    preservation_score: float | None = None  # 0.0-1.0
    recommendations: list[str] = []


class ProsodyControlRequest(BaseModel):
    """Request model for prosody and intonation control."""

    audio_id: str = Field(..., description="Audio ID to process")
    pitch_contour: list[float] | None = Field(
        default=None,
        description="Pitch contour adjustments",
    )
    rhythm_adjustments: dict[str, float] | None = Field(
        default=None,
        description="Rhythm adjustments",
    )
    stress_markers: list[dict[str, Any]] | None = Field(
        default=None,
        description="Word stress markers",
    )
    intonation_pattern: str | None = Field(
        default=None,
        description="Intonation pattern: rising, falling, flat",
    )
    prosody_template: str | None = Field(
        default=None,
        description="Prosody template name",
    )


class ProsodyControlResponse(BaseModel):
    """Response model for prosody control."""

    audio_id: str
    processed_audio_id: str
    processed_audio_url: str
    prosody_applied: dict[str, Any]
    quality_improvement: float  # 0.0-1.0


class FaceEnhancementRequest(BaseModel):
    """Request model for face quality enhancement."""

    image_id: str | None = Field(
        default=None,
        description="Image ID to enhance",
    )
    video_id: str | None = Field(
        default=None,
        description="Video ID to enhance",
    )
    enhancement_preset: str | None = Field(
        default=None,
        description="Enhancement preset: portrait, full_body, close_up",
    )
    multi_stage: bool | None = Field(
        default=True,
        description="Apply multi-stage enhancement",
    )
    face_specific: bool | None = Field(
        default=True,
        description="Apply face-specific enhancement",
    )
    model: str | None = Field(
        default=None,
        description="Enhancement model to use (e.g. realesrgan)",
    )
    strength: float | None = Field(
        default=None,
        description="Enhancement strength (0.0-1.0)",
        ge=0.0,
        le=1.0,
    )


class FaceQualityAnalysis(BaseModel):
    """Face quality analysis results."""

    resolution_score: float  # 1-10
    artifact_score: float  # 1-10 (lower is better)
    alignment_score: float  # 1-10
    realism_score: float  # 1-10
    overall_quality: float  # 1-10
    recommendations: list[str] = []


class FaceEnhancementResponse(BaseModel):
    """Response model for face enhancement."""

    image_id: str | None = None
    video_id: str | None = None
    enhanced_image_id: str | None = None
    enhanced_video_id: str | None = None
    enhanced_image_url: str | None = None
    enhanced_video_url: str | None = None
    original_analysis: FaceQualityAnalysis
    enhanced_analysis: FaceQualityAnalysis | None = None
    quality_improvement: float  # 0.0-1.0


class TemporalConsistencyRequest(BaseModel):
    """Request model for temporal consistency enhancement."""

    video_id: str = Field(..., description="Video ID to process")
    smoothing_strength: float | None = Field(
        default=0.5,
        description="Temporal smoothing strength (0.0-1.0)",
        ge=0.0,
        le=1.0,
    )
    motion_consistency: bool | None = Field(
        default=True,
        description="Ensure motion consistency",
    )
    detect_artifacts: bool | None = Field(
        default=True,
        description="Detect temporal artifacts",
    )


class TemporalAnalysis(BaseModel):
    """Temporal analysis results."""

    frame_stability: float  # 0.0-1.0
    motion_smoothness: float  # 0.0-1.0
    flicker_score: float  # 0.0-1.0 (lower is better)
    jitter_score: float  # 0.0-1.0 (lower is better)
    overall_consistency: float  # 0.0-1.0
    artifacts_detected: list[str] = []


class TemporalConsistencyResponse(BaseModel):
    """Response model for temporal consistency."""

    video_id: str
    processed_video_id: str
    processed_video_url: str
    original_analysis: TemporalAnalysis
    processed_analysis: TemporalAnalysis | None = None
    quality_improvement: float  # 0.0-1.0


class TrainingDataOptimizationRequest(BaseModel):
    """Request model for training data optimization."""

    dataset_id: str = Field(..., description="Dataset ID to optimize")
    analyze_quality: bool | None = Field(
        default=True,
        description="Analyze data quality",
    )
    select_optimal: bool | None = Field(
        default=True,
        description="Select optimal samples",
    )
    suggest_augmentation: bool | None = Field(
        default=True,
        description="Suggest augmentation strategies",
    )
    analyze_diversity: bool | None = Field(
        default=True,
        description="Analyze data diversity",
    )


class TrainingDataAnalysis(BaseModel):
    """Training data analysis results."""

    quality_score: float  # 1-10
    diversity_score: float  # 1-10
    coverage_score: float  # 1-10
    optimal_samples: list[str] = []  # Sample IDs
    recommendations: list[str] = []
    augmentation_suggestions: list[str] = []


class TrainingDataOptimizationResponse(BaseModel):
    """Response model for training data optimization."""

    dataset_id: str
    analysis: TrainingDataAnalysis
    optimized_dataset_id: str | None = None
    quality_improvement: float  # 0.0-1.0


class PostProcessingPipelineRequest(BaseModel):
    """Request model for post-processing enhancement pipeline."""

    audio_id: str | None = Field(
        default=None,
        description="Audio ID to process",
    )
    image_id: str | None = Field(
        default=None,
        description="Image ID to process",
    )
    video_id: str | None = Field(
        default=None,
        description="Video ID to process",
    )
    enhancement_stages: list[str] | None = Field(
        default=None,
        description="Enhancement stages: denoise, normalize, enhance, repair",
    )
    optimize_order: bool | None = Field(
        default=True,
        description="Optimize enhancement order",
    )
    preview: bool | None = Field(
        default=False,
        description="Preview without applying",
    )


class EnhancementStageResult(BaseModel):
    """Result from a single enhancement stage."""

    stage_name: str
    quality_before: float
    quality_after: float
    improvement: float


class PostProcessingPipelineResponse(BaseModel):
    """Response model for post-processing pipeline."""

    audio_id: str | None = None
    image_id: str | None = None
    video_id: str | None = None
    processed_audio_id: str | None = None
    processed_image_id: str | None = None
    processed_video_id: str | None = None
    processed_audio_url: str | None = None
    processed_image_url: str | None = None
    processed_video_url: str | None = None
    stages_applied: list[EnhancementStageResult]
    total_quality_improvement: float  # 0.0-1.0
    preview_available: bool


# ============================================================================
# Dubbing Models
# ============================================================================


class DubSyncRequest(BaseModel):
    """Request model for dubbing synchronization."""

    audio_id: str = Field(..., description="Audio ID to sync")
    translated_text: str = Field(..., description="Translated text to sync")
    original_text: str | None = Field(default=None, description="Original text (optional)")
    original_timing: list[dict[str, Any]] | None = Field(
        default=None, description="Original timing segments"
    )
    target_language: str = Field(default="en", description="Target language code")


class DubSyncResponse(BaseModel):
    """Response model for dubbing synchronization."""

    audio_id: str
    translated_text: str
    alignment: dict[str, Any]
    message: str


# ============================================================================
# Dataset Cull Models
# ============================================================================


class DatasetCullRequest(BaseModel):
    """Request model for dataset culling."""

    dataset_id: str = Field(..., description="Dataset ID to cull")
    clips: list[str] | None = Field(default=None, description="List of clips to evaluate")
    min_quality: float = Field(default=0.7, ge=0.0, le=1.0, description="Minimum quality threshold")
    min_snr: float = Field(default=20.0, ge=0.0, description="Minimum SNR threshold")
    max_lufs: float = Field(default=-10.0, description="Maximum LUFS threshold")


# ============================================================================
# Reward Model Prediction Models
# ============================================================================


class RmPredictRequest(BaseModel):
    """Request model for reward model prediction."""

    model_id: str | None = Field(
        default=None, description="Model ID to use (uses latest if not specified)"
    )
    audio_id: str | None = Field(default=None, description="Audio ID to predict score for")
    features: list[float] | None = Field(default=None, description="Pre-computed features")


class RmPredictResponse(BaseModel):
    """Response model for reward model prediction."""

    score: float
    model_id: str
    audio_id: str | None = None
    confidence: float


class RmTrainResponse(BaseModel):
    """Response model for reward model training."""

    status: str
    job_id: str
    model_id: str
    samples: int
    message: str


# ============================================================================
# Dubbing Translation Response
# ============================================================================


class DubTranslateResponse(BaseModel):
    """Response model for dubbing translation."""

    text: str
    source_language: str
    target_language: str
    confidence: float


# ============================================================================
# Reward Model Responses
# ============================================================================


class RmModelInfo(BaseModel):
    """Info about a reward model."""

    id: str
    training_samples: int
    mean_score: float
    created: str
    status: str


class RmModelsListResponse(BaseModel):
    """Response model for listing reward models."""

    models: list[RmModelInfo]
    count: int


class RmTrainingJobResponse(BaseModel):
    """Response model for training job status."""

    job_id: str
    model_id: str
    status: str
    samples: int
    started: str
    completed: str | None = None


# ============================================================================
# Engine Responses
# ============================================================================


class EngineInfo(BaseModel):
    """Info about an engine."""

    id: str
    name: str
    description: str | None = None
    status: str = "available"
    capabilities: list[str] | None = None


class EngineListResponse(BaseModel):
    """Response model for listing engines."""

    engines: list[EngineInfo]
    count: int


class TelemetryRecordResponse(BaseModel):
    """Response model for telemetry recording."""

    status: str
    engine_id: str
    recorded_at: str


class PreflightResponse(BaseModel):
    """Response model for engine preflight check."""

    status: str
    engines_ready: int
    engines_missing: int
    details: dict[str, Any]


class GPUSettingsResponse(BaseModel):
    """Response model for GPU settings update."""

    status: str
    settings: dict[str, Any]


class DefaultEngineResponse(BaseModel):
    """Response model for setting default engine."""

    status: str
    task_type: str
    engine_id: str


class ConfigValidationResponse(BaseModel):
    """Response model for configuration validation."""

    valid: bool
    errors: list[str]
    warnings: list[str]


# ============================================================================
# RVC Responses
# ============================================================================


class RvcStartResponse(BaseModel):
    """Response model for RVC conversion start."""

    conversion_id: str
    status: str
    target_voice: str
    message: str


# ============================================================================
# Audio Processing Responses
# ============================================================================


class SpectralInpaintResponse(BaseModel):
    """Response model for spectral inpainting."""

    audio_id: str
    processed_audio_id: str
    status: str
    message: str


class RepairClippingResponse(BaseModel):
    """Response model for clipping repair."""

    audio_id: str
    processed_audio_id: str
    clipping_detected: bool
    samples_repaired: int
    status: str


class NrApplyResponse(BaseModel):
    """Response model for noise reduction."""

    audio_id: str
    processed_audio_id: str
    noise_reduction_db: float
    status: str


class NoisePrintResponse(BaseModel):
    """Response model for noise print creation."""

    noise_print_id: str
    name: str
    audio_id: str
    duration: float
    status: str


class GranularRenderResponse(BaseModel):
    """Response model for granular synthesis render."""

    audio_id: str
    output_audio_id: str
    duration: float
    status: str


class FormantAnalyzeResponse(BaseModel):
    """Response model for formant analysis."""

    audio_id: str
    formants: list[dict[str, float]]
    f0_mean: float | None = None
    f0_std: float | None = None


class ArticulationAnalyzeResponse(BaseModel):
    """Response model for articulation analysis."""

    audio_id: str
    articulation_rate: float
    pause_ratio: float
    syllable_count: int
    phoneme_distribution: dict[str, int] | None = None


# ============================================================================
# Safety Responses
# ============================================================================


class SafetyScanResponse(BaseModel):
    """Response model for safety scanning."""

    text: str
    is_safe: bool
    violations: list[dict[str, Any]]
    categories: dict[str, float]


class SafetyCategoriesResponse(BaseModel):
    """Response model for safety categories."""

    categories: list[dict[str, Any]]


# ============================================================================
# Model Inspection Responses
# ============================================================================


class ModelInspectResponse(BaseModel):
    """Response model for model inspection."""

    layer: int
    layer_name: str
    layer_type: str
    input_shape: list[int] | None = None
    output_shape: list[int] | None = None
    parameters: int
    activations: dict[str, Any] | None = None


class ModelLayersResponse(BaseModel):
    """Response model for listing model layers."""

    model_name: str
    layers: list[dict[str, Any]]
    total_parameters: int


# ============================================================================
# Image Sampler Responses
# ============================================================================


class ImgSamplerRenderResponse(BaseModel):
    """Response model for image sampler render."""

    image_id: str
    sampler: str
    steps: int
    seed: int
    status: str


class SamplerInfo(BaseModel):
    """Info about a sampler."""

    name: str
    description: str | None = None
    supports_cfg: bool = True
    default_steps: int = 20


class SamplersListResponse(BaseModel):
    """Response model for listing samplers."""

    samplers: list[SamplerInfo]


# ============================================================================
# ADR Response
# ============================================================================


class AdrAlignResponse(BaseModel):
    """Response model for ADR alignment."""

    video_id: str
    audio_id: str
    alignment_score: float
    sync_offset_ms: float
    status: str


# ============================================================================
# Emotion Analysis Response
# ============================================================================


class EmotionAnalyzeResponse(BaseModel):
    """Response model for emotion analysis."""

    audio_id: str
    emotions: dict[str, float]
    dominant_emotion: str
    confidence: float


# ============================================================================
# Assistant Run Responses
# ============================================================================


class AssistantRunResponse(BaseModel):
    """Response model for assistant run."""

    run_id: str
    status: str
    output: str | None = None
    actions_taken: list[str]
    execution_time_ms: int


class ActionInfo(BaseModel):
    """Info about an action."""

    id: str
    name: str
    description: str | None = None
    parameters: dict[str, Any] | None = None


class ActionsListResponse(BaseModel):
    """Response model for listing actions."""

    actions: list[ActionInfo]
    count: int


# =============================================================================
# Voice Consent (Item 22: Trust and Safety)
# =============================================================================


class VoiceConsentRecord(VoiceStudioBaseModel):
    """Voice consent record for API responses."""

    consent_id: str = Field(description="Unique consent identifier")
    voice_id: str = Field(description="Voice/subject identifier")
    grantor_id: str = Field(description="Identity of the consent grantor")
    grantor_name: str | None = Field(default=None, description="Display name of grantor")
    consent_type: str = Field(description="Type: voice_cloning, voice_usage, etc.")
    status: str = Field(description="pending, granted, denied, revoked, expired")
    granted_at: str | None = Field(default=None, description="When consent was granted (ISO)")
    expires_at: str | None = Field(default=None, description="When consent expires (ISO)")
    reference_audio_hash: str | None = Field(
        default=None,
        description="SHA256 hash of reference audio (proof of consent scope)",
    )
    allowed_uses: dict[str, Any] | None = Field(default=None, description="Scope")
    signature: str | None = Field(default=None, description="Consent signature")


class VoiceConsentCreate(VoiceStudioBaseModel):
    """Request to create or request consent."""

    voice_id: str = Field(description="Voice identifier")
    grantor_id: str = Field(description="Grantor identity")
    grantor_name: str | None = None
    consent_type: str = Field(default="voice_cloning", description="Type of consent")
    reference_audio_hash: str | None = Field(default=None, description="Hash of reference audio")
    expires_days: int | None = Field(default=None, description="Consent validity in days")
    allowed_uses: dict[str, Any] | None = None


class VoiceConsentRevoke(VoiceStudioBaseModel):
    """Request to revoke consent."""

    consent_id: str = Field(description="Consent to revoke")
