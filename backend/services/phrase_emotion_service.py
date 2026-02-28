"""
Phrase-Level Emotion Control Service

Phase 9.2: Advanced Emotion Control (Fish Audio Parity)
Enables inline emotion markup and phrase-level emotion control.

Features:
- Emotion tag system ([happy]Hello[/happy])
- Phrase-level emotion application
- Emotion intensity control (0-100%)
- Emotion style presets
"""

from __future__ import annotations

import logging
import re
import uuid
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from typing import Any

logger = logging.getLogger(__name__)


class EmotionType(Enum):
    """Available emotion types."""

    HAPPY = "happy"
    SAD = "sad"
    ANGRY = "angry"
    EXCITED = "excited"
    CALM = "calm"
    FEARFUL = "fearful"
    SURPRISED = "surprised"
    DISGUSTED = "disgusted"
    NEUTRAL = "neutral"
    WHISPERING = "whispering"
    SHOUTING = "shouting"
    SARCASTIC = "sarcastic"
    TENDER = "tender"
    CONFIDENT = "confident"


@dataclass
class EmotionTag:
    """Represents an emotion tag with position and intensity."""

    emotion: EmotionType
    intensity: float  # 0-100
    start_index: int
    end_index: int
    text: str

    def to_dict(self) -> dict[str, Any]:
        return {
            "emotion": self.emotion.value,
            "intensity": self.intensity,
            "start_index": self.start_index,
            "end_index": self.end_index,
            "text": self.text,
        }


@dataclass
class EmotionPhrase:
    """A phrase with applied emotion."""

    text: str
    emotion: EmotionType
    intensity: float
    start_char: int
    end_char: int

    def to_dict(self) -> dict[str, Any]:
        return {
            "text": self.text,
            "emotion": self.emotion.value,
            "intensity": self.intensity,
            "start_char": self.start_char,
            "end_char": self.end_char,
        }


@dataclass
class EmotionPreset:
    """Saved emotion combination preset."""

    preset_id: str
    name: str
    description: str | None
    primary_emotion: EmotionType
    primary_intensity: float
    secondary_emotion: EmotionType | None = None
    secondary_intensity: float = 0.0
    blend_mode: str = "linear"  # linear, smooth, crossfade
    created_at: str = field(default_factory=lambda: datetime.utcnow().isoformat())
    updated_at: str = field(default_factory=lambda: datetime.utcnow().isoformat())

    def to_dict(self) -> dict[str, Any]:
        return {
            "preset_id": self.preset_id,
            "name": self.name,
            "description": self.description,
            "primary_emotion": self.primary_emotion.value,
            "primary_intensity": self.primary_intensity,
            "secondary_emotion": self.secondary_emotion.value if self.secondary_emotion else None,
            "secondary_intensity": self.secondary_intensity,
            "blend_mode": self.blend_mode,
            "created_at": self.created_at,
            "updated_at": self.updated_at,
        }


@dataclass
class ParsedEmotionText:
    """Result of parsing emotion-tagged text."""

    original_text: str
    clean_text: str  # Text with tags removed
    phrases: list[EmotionPhrase]
    has_emotions: bool

    def to_dict(self) -> dict[str, Any]:
        return {
            "original_text": self.original_text,
            "clean_text": self.clean_text,
            "phrases": [p.to_dict() for p in self.phrases],
            "has_emotions": self.has_emotions,
        }


class PhraseEmotionService:
    """
    Service for phrase-level emotion control in voice synthesis.

    Implements Phase 9.2 features:
    - 9.2.1: Emotion tag system ([happy]...[/happy])
    - 9.2.2: Phrase-level emotion application
    - 9.2.3: Emotion intensity slider (0-100%)
    - 9.2.4: Emotion style presets
    """

    # Regex pattern for emotion tags: [emotion:intensity]text[/emotion] or [emotion]text[/emotion]
    EMOTION_TAG_PATTERN = re.compile(
        r"\[(\w+)(?::(\d+(?:\.\d+)?))?\](.*?)\[/\1\]", re.DOTALL | re.IGNORECASE
    )

    # Simple tag pattern: [emotion]text[/emotion]
    SIMPLE_TAG_PATTERN = re.compile(r"\[(\w+)\](.*?)\[/\1\]", re.DOTALL | re.IGNORECASE)

    # Valid emotion names (lowercase)
    VALID_EMOTIONS = {e.value for e in EmotionType}

    # Default intensity when not specified
    DEFAULT_INTENSITY = 75.0

    def __init__(self):
        self._presets: dict[str, EmotionPreset] = {}
        self._init_default_presets()
        logger.info("PhraseEmotionService initialized")

    def _init_default_presets(self):
        """Initialize default emotion presets."""
        default_presets = [
            EmotionPreset(
                preset_id="preset_enthusiastic",
                name="Enthusiastic",
                description="Upbeat and energetic delivery",
                primary_emotion=EmotionType.EXCITED,
                primary_intensity=80.0,
                secondary_emotion=EmotionType.HAPPY,
                secondary_intensity=50.0,
            ),
            EmotionPreset(
                preset_id="preset_somber",
                name="Somber",
                description="Serious and thoughtful tone",
                primary_emotion=EmotionType.SAD,
                primary_intensity=60.0,
                secondary_emotion=EmotionType.CALM,
                secondary_intensity=40.0,
            ),
            EmotionPreset(
                preset_id="preset_dramatic",
                name="Dramatic",
                description="Theatrical and intense delivery",
                primary_emotion=EmotionType.SURPRISED,
                primary_intensity=70.0,
                secondary_emotion=EmotionType.EXCITED,
                secondary_intensity=60.0,
            ),
            EmotionPreset(
                preset_id="preset_soothing",
                name="Soothing",
                description="Calm and relaxing voice",
                primary_emotion=EmotionType.CALM,
                primary_intensity=85.0,
                secondary_emotion=EmotionType.TENDER,
                secondary_intensity=40.0,
            ),
            EmotionPreset(
                preset_id="preset_assertive",
                name="Assertive",
                description="Confident and commanding",
                primary_emotion=EmotionType.CONFIDENT,
                primary_intensity=80.0,
                secondary_emotion=EmotionType.NEUTRAL,
                secondary_intensity=30.0,
            ),
            EmotionPreset(
                preset_id="preset_whisper",
                name="Whisper",
                description="Soft, intimate whisper",
                primary_emotion=EmotionType.WHISPERING,
                primary_intensity=90.0,
            ),
        ]

        for preset in default_presets:
            self._presets[preset.preset_id] = preset

    def parse_emotion_tags(self, text: str) -> ParsedEmotionText:
        """
        Parse text with emotion tags and extract phrase-level emotions.

        Supports formats:
        - [happy]Hello world[/happy]
        - [sad:80]I'm feeling down[/sad]
        - [excited:100]This is amazing![/excited]

        Args:
            text: Text with emotion markup

        Returns:
            ParsedEmotionText with extracted emotions and clean text
        """
        if not text:
            return ParsedEmotionText(
                original_text=text,
                clean_text=text,
                phrases=[],
                has_emotions=False,
            )

        phrases: list[EmotionPhrase] = []
        current_pos = 0
        clean_parts = []
        clean_offset = 0

        # Find all emotion tags
        for match in self.EMOTION_TAG_PATTERN.finditer(text):
            emotion_name = match.group(1).lower()
            intensity_str = match.group(2)
            tagged_text = match.group(3)

            # Validate emotion
            if emotion_name not in self.VALID_EMOTIONS:
                # Skip invalid emotions, treat as plain text
                continue

            # Parse intensity
            if intensity_str:
                intensity = min(100.0, max(0.0, float(intensity_str)))
            else:
                intensity = self.DEFAULT_INTENSITY

            # Add text before this tag
            if match.start() > current_pos:
                before_text = text[current_pos : match.start()]
                clean_parts.append(before_text)

                # Add neutral phrase for untagged text
                if before_text.strip():
                    phrases.append(
                        EmotionPhrase(
                            text=before_text,
                            emotion=EmotionType.NEUTRAL,
                            intensity=50.0,
                            start_char=clean_offset,
                            end_char=clean_offset + len(before_text),
                        )
                    )
                clean_offset += len(before_text)

            # Add emotion phrase
            clean_parts.append(tagged_text)

            try:
                emotion_type = EmotionType(emotion_name)
            except ValueError:
                emotion_type = EmotionType.NEUTRAL

            phrases.append(
                EmotionPhrase(
                    text=tagged_text,
                    emotion=emotion_type,
                    intensity=intensity,
                    start_char=clean_offset,
                    end_char=clean_offset + len(tagged_text),
                )
            )

            clean_offset += len(tagged_text)
            current_pos = match.end()

        # Add remaining text after last tag
        if current_pos < len(text):
            remaining_text = text[current_pos:]
            clean_parts.append(remaining_text)

            if remaining_text.strip():
                phrases.append(
                    EmotionPhrase(
                        text=remaining_text,
                        emotion=EmotionType.NEUTRAL,
                        intensity=50.0,
                        start_char=clean_offset,
                        end_char=clean_offset + len(remaining_text),
                    )
                )

        clean_text = "".join(clean_parts)

        return ParsedEmotionText(
            original_text=text,
            clean_text=clean_text,
            phrases=phrases,
            has_emotions=len([p for p in phrases if p.emotion != EmotionType.NEUTRAL]) > 0,
        )

    def apply_emotion_to_text(
        self,
        text: str,
        emotion: EmotionType,
        intensity: float = 75.0,
    ) -> str:
        """
        Wrap text in emotion tags.

        Args:
            text: Text to wrap
            emotion: Emotion to apply
            intensity: Emotion intensity (0-100)

        Returns:
            Text with emotion tags
        """
        intensity = min(100.0, max(0.0, intensity))
        if intensity == self.DEFAULT_INTENSITY:
            return f"[{emotion.value}]{text}[/{emotion.value}]"
        else:
            return f"[{emotion.value}:{intensity}]{text}[/{emotion.value}]"

    def blend_emotions(
        self,
        primary: EmotionType,
        primary_intensity: float,
        secondary: EmotionType | None,
        secondary_intensity: float = 0.0,
        blend_mode: str = "linear",
    ) -> dict[str, float]:
        """
        Blend two emotions for synthesis parameters.

        Args:
            primary: Primary emotion
            primary_intensity: Primary intensity (0-100)
            secondary: Optional secondary emotion
            secondary_intensity: Secondary intensity (0-100)
            blend_mode: Blending mode (linear, smooth, crossfade)

        Returns:
            Blended synthesis parameters
        """
        # Emotion parameter mappings (pitch_shift, speed_factor, energy)
        emotion_params = {
            EmotionType.HAPPY: {"pitch": 1.05, "speed": 1.05, "energy": 1.1},
            EmotionType.SAD: {"pitch": 0.95, "speed": 0.90, "energy": 0.85},
            EmotionType.ANGRY: {"pitch": 1.10, "speed": 1.10, "energy": 1.3},
            EmotionType.EXCITED: {"pitch": 1.15, "speed": 1.15, "energy": 1.25},
            EmotionType.CALM: {"pitch": 0.98, "speed": 0.92, "energy": 0.90},
            EmotionType.FEARFUL: {"pitch": 1.08, "speed": 1.05, "energy": 0.95},
            EmotionType.SURPRISED: {"pitch": 1.20, "speed": 1.10, "energy": 1.15},
            EmotionType.DISGUSTED: {"pitch": 0.92, "speed": 0.95, "energy": 0.95},
            EmotionType.NEUTRAL: {"pitch": 1.0, "speed": 1.0, "energy": 1.0},
            EmotionType.WHISPERING: {"pitch": 0.98, "speed": 0.85, "energy": 0.5},
            EmotionType.SHOUTING: {"pitch": 1.15, "speed": 1.20, "energy": 1.5},
            EmotionType.SARCASTIC: {"pitch": 1.02, "speed": 0.95, "energy": 0.95},
            EmotionType.TENDER: {"pitch": 0.97, "speed": 0.90, "energy": 0.80},
            EmotionType.CONFIDENT: {"pitch": 1.02, "speed": 1.0, "energy": 1.15},
        }

        # Get primary params
        primary_params = emotion_params.get(primary, emotion_params[EmotionType.NEUTRAL])
        primary_weight = primary_intensity / 100.0

        # Apply primary emotion with intensity
        result = {}
        for key, base_value in primary_params.items():
            deviation = base_value - 1.0
            result[key] = 1.0 + (deviation * primary_weight)

        # Blend with secondary if provided
        if secondary and secondary_intensity > 0:
            secondary_params = emotion_params.get(secondary, emotion_params[EmotionType.NEUTRAL])
            secondary_weight = secondary_intensity / 100.0

            # Total weight normalization
            total_weight = primary_weight + secondary_weight
            if total_weight > 0:
                primary_norm = primary_weight / total_weight
                secondary_norm = secondary_weight / total_weight

                for key in result:
                    primary_deviation = primary_params[key] - 1.0
                    secondary_deviation = secondary_params[key] - 1.0

                    if blend_mode == "smooth":
                        # Smooth blending (ease in/out)
                        import math

                        t = secondary_norm
                        smooth_t = t * t * (3 - 2 * t)  # Smoothstep
                        blended_deviation = (
                            primary_deviation * (1 - smooth_t) + secondary_deviation * smooth_t
                        )
                    elif blend_mode == "crossfade":
                        # Crossfade (equal power)
                        import math

                        blended_deviation = primary_deviation * math.cos(
                            secondary_norm * math.pi / 2
                        ) + secondary_deviation * math.sin(secondary_norm * math.pi / 2)
                    else:
                        # Linear blending
                        blended_deviation = (
                            primary_deviation * primary_norm + secondary_deviation * secondary_norm
                        )

                    result[key] = 1.0 + blended_deviation

        return result

    def create_preset(
        self,
        name: str,
        primary_emotion: EmotionType,
        primary_intensity: float,
        description: str | None = None,
        secondary_emotion: EmotionType | None = None,
        secondary_intensity: float = 0.0,
        blend_mode: str = "linear",
    ) -> EmotionPreset:
        """
        Create a new emotion preset.

        Args:
            name: Preset name
            primary_emotion: Primary emotion
            primary_intensity: Primary intensity (0-100)
            description: Optional description
            secondary_emotion: Optional secondary emotion
            secondary_intensity: Secondary intensity (0-100)
            blend_mode: Blending mode

        Returns:
            Created EmotionPreset
        """
        preset_id = f"preset_{uuid.uuid4().hex[:8]}"

        preset = EmotionPreset(
            preset_id=preset_id,
            name=name,
            description=description,
            primary_emotion=primary_emotion,
            primary_intensity=min(100.0, max(0.0, primary_intensity)),
            secondary_emotion=secondary_emotion,
            secondary_intensity=min(100.0, max(0.0, secondary_intensity)),
            blend_mode=blend_mode,
        )

        self._presets[preset_id] = preset
        logger.info(f"Created emotion preset: {name} ({preset_id})")

        return preset

    def get_preset(self, preset_id: str) -> EmotionPreset | None:
        """Get emotion preset by ID."""
        return self._presets.get(preset_id)

    def list_presets(self) -> list[EmotionPreset]:
        """List all emotion presets."""
        return list(self._presets.values())

    def delete_preset(self, preset_id: str) -> bool:
        """Delete an emotion preset."""
        if preset_id in self._presets:
            del self._presets[preset_id]
            logger.info(f"Deleted emotion preset: {preset_id}")
            return True
        return False

    def apply_preset(self, preset_id: str) -> dict[str, float] | None:
        """
        Apply a preset and get synthesis parameters.

        Args:
            preset_id: Preset ID

        Returns:
            Synthesis parameters dict or None if preset not found
        """
        preset = self.get_preset(preset_id)
        if not preset:
            return None

        return self.blend_emotions(
            primary=preset.primary_emotion,
            primary_intensity=preset.primary_intensity,
            secondary=preset.secondary_emotion,
            secondary_intensity=preset.secondary_intensity,
            blend_mode=preset.blend_mode,
        )

    def get_available_emotions(self) -> list[str]:
        """Get list of available emotions."""
        return [e.value for e in EmotionType]

    def generate_synthesis_timeline(
        self,
        parsed_text: ParsedEmotionText,
    ) -> list[dict[str, Any]]:
        """
        Generate a synthesis timeline from parsed emotion text.

        This creates a sequence of synthesis segments with emotion parameters
        for phrase-level emotion control during synthesis.

        Args:
            parsed_text: Parsed emotion text with phrases

        Returns:
            List of synthesis segments with timing and emotion parameters
        """
        timeline = []

        for phrase in parsed_text.phrases:
            # Get emotion parameters
            params = self.blend_emotions(
                primary=phrase.emotion,
                primary_intensity=phrase.intensity,
                secondary=None,
                secondary_intensity=0.0,
            )

            segment = {
                "text": phrase.text,
                "start_char": phrase.start_char,
                "end_char": phrase.end_char,
                "emotion": phrase.emotion.value,
                "intensity": phrase.intensity,
                "synthesis_params": params,
            }

            timeline.append(segment)

        return timeline


# Singleton instance
_phrase_emotion_service: PhraseEmotionService | None = None


def get_phrase_emotion_service() -> PhraseEmotionService:
    """Get or create the phrase emotion service singleton."""
    global _phrase_emotion_service
    if _phrase_emotion_service is None:
        _phrase_emotion_service = PhraseEmotionService()
    return _phrase_emotion_service
