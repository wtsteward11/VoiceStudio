"""
Quality text analysis and recommendation service.

Owns text analysis and quality recommendation logic.
Service layer must not depend on API layer.
"""

from __future__ import annotations

import re
from enum import Enum
from typing import Any


class TextComplexity(Enum):
    """Text complexity levels."""

    SIMPLE = "simple"
    MODERATE = "moderate"
    COMPLEX = "complex"
    VERY_COMPLEX = "very_complex"


class ContentType(Enum):
    """Content type classification."""

    DIALOGUE = "dialogue"
    NARRATION = "narration"
    TECHNICAL = "technical"
    MIXED = "mixed"


class TextAnalysisResult:
    """Result of text analysis."""

    def __init__(
        self,
        text: str,
        complexity: TextComplexity,
        content_type: ContentType,
        word_count: int,
        sentence_count: int,
        character_count: int,
        avg_words_per_sentence: float,
        has_dialogue: bool,
        has_technical_terms: bool,
        detected_emotions: list[str],
        language: str = "en",
    ):
        self.text = text
        self.complexity = complexity
        self.content_type = content_type
        self.word_count = word_count
        self.sentence_count = sentence_count
        self.character_count = character_count
        self.avg_words_per_sentence = avg_words_per_sentence
        self.has_dialogue = has_dialogue
        self.has_technical_terms = has_technical_terms
        self.detected_emotions = detected_emotions
        self.language = language

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary for JSON serialization."""
        return {
            "complexity": self.complexity.value,
            "content_type": self.content_type.value,
            "word_count": self.word_count,
            "sentence_count": self.sentence_count,
            "character_count": self.character_count,
            "avg_words_per_sentence": round(self.avg_words_per_sentence, 2),
            "has_dialogue": self.has_dialogue,
            "has_technical_terms": self.has_technical_terms,
            "detected_emotions": self.detected_emotions,
            "language": self.language,
        }


class QualityRecommendation:
    """Quality settings recommendation based on text analysis."""

    def __init__(
        self,
        recommended_engine: str,
        recommended_quality_mode: str,
        recommended_enhance_quality: bool,
        predicted_quality_score: float,
        reasoning: str,
        confidence: float,
    ):
        self.recommended_engine = recommended_engine
        self.recommended_quality_mode = recommended_quality_mode
        self.recommended_enhance_quality = recommended_enhance_quality
        self.predicted_quality_score = predicted_quality_score
        self.reasoning = reasoning
        self.confidence = confidence


def analyze_text(text: str, language: str = "en") -> TextAnalysisResult:
    """
    Perform comprehensive text analysis.

    Args:
        text: Input text to analyze
        language: Language code (default: "en")

    Returns:
        TextAnalysisResult with all analysis data
    """
    if not text or not text.strip():
        return TextAnalysisResult(
            text=text,
            complexity=TextComplexity.SIMPLE,
            content_type=ContentType.NARRATION,
            word_count=0,
            sentence_count=0,
            character_count=0,
            avg_words_per_sentence=0.0,
            has_dialogue=False,
            has_technical_terms=False,
            detected_emotions=["neutral"],
            language=language,
        )

    words = text.split()
    word_count = len(words)
    character_count = len(text)

    sentences = re.split(r"[.!?]+", text)
    sentences = [s.strip() for s in sentences if s.strip()]
    sentence_count = len(sentences) if sentences else 1
    avg_words_per_sentence = word_count / sentence_count if sentence_count > 0 else 0.0

    complexity = _analyze_text_complexity(text)
    content_type = _detect_content_type(text)
    has_dialogue = bool(re.search(r'["\']', text))
    has_technical = bool(
        re.search(r"\b[A-Z]{2,}\b|\d+\s*(?:Hz|kHz|MHz|GHz|GB|MB|KB|%|dB|ms|s|min|hr)", text)
    )
    emotions = _detect_emotions(text)

    return TextAnalysisResult(
        text=text,
        complexity=complexity,
        content_type=content_type,
        word_count=word_count,
        sentence_count=sentence_count,
        character_count=character_count,
        avg_words_per_sentence=avg_words_per_sentence,
        has_dialogue=has_dialogue,
        has_technical_terms=has_technical,
        detected_emotions=emotions,
        language=language,
    )


def recommend_quality_settings(
    text_analysis: TextAnalysisResult,
    available_engines: list[str] | None = None,
    target_quality: float | None = None,
) -> QualityRecommendation:
    """
    Recommend optimal quality settings based on text analysis.

    Args:
        text_analysis: TextAnalysisResult from text analysis
        available_engines: List of available engines
        target_quality: Target quality score (0.0-1.0), None for auto

    Returns:
        QualityRecommendation with recommended settings
    """
    if available_engines is None:
        available_engines = ["xtts", "chatterbox", "tortoise"]

    recommended_engine = _select_engine(
        text_analysis.content_type,
        text_analysis.complexity,
        text_analysis.word_count,
        available_engines,
    )
    quality_mode = _select_quality_mode(
        text_analysis.complexity, text_analysis.content_type, text_analysis.word_count
    )
    enhance_quality = _should_enhance_quality(
        text_analysis.complexity, text_analysis.content_type, target_quality
    )
    predicted_quality = _predict_quality_score(
        recommended_engine,
        quality_mode,
        enhance_quality,
        text_analysis.complexity,
        text_analysis.content_type,
    )
    reasoning = _generate_reasoning(
        text_analysis, recommended_engine, quality_mode, enhance_quality
    )
    confidence = _calculate_confidence(text_analysis, target_quality)

    return QualityRecommendation(
        recommended_engine=recommended_engine,
        recommended_quality_mode=quality_mode,
        recommended_enhance_quality=enhance_quality,
        predicted_quality_score=predicted_quality,
        reasoning=reasoning,
        confidence=confidence,
    )


def _analyze_text_complexity(text: str) -> TextComplexity:
    words = text.split()
    if not words:
        return TextComplexity.SIMPLE

    sentences = re.split(r"[.!?]+", text)
    sentences = [s.strip() for s in sentences if s.strip()]
    sentence_count = len(sentences) if sentences else 1
    avg_words_per_sentence = len(words) / sentence_count if sentence_count > 0 else len(words)
    long_words = sum(1 for w in words if len(w) >= 4)
    long_word_ratio = long_words / len(words) if words else 0
    complex_punct = len(re.findall(r"[:;—]", text))

    if avg_words_per_sentence > 25 or long_word_ratio > 0.5 or complex_punct > 5:
        return TextComplexity.VERY_COMPLEX
    elif avg_words_per_sentence > 15 or long_word_ratio > 0.3 or complex_punct > 2:
        return TextComplexity.COMPLEX
    elif avg_words_per_sentence > 10 or long_word_ratio > 0.2:
        return TextComplexity.MODERATE
    return TextComplexity.SIMPLE


def _detect_content_type(text: str) -> ContentType:
    dialogue_indicators = [
        r'["\']',
        r"said|says|asked|replied|responded",
        r':\s*["\']',
    ]
    has_dialogue = any(re.search(p, text, re.IGNORECASE) for p in dialogue_indicators)

    technical_indicators = [
        r"\b[A-Z]{2,}\b",
        r"\d+\s*(?:Hz|kHz|MHz|GHz|GB|MB|KB|%|dB|ms|s|min|hr)",
        r"\b(?:API|CPU|GPU|RAM|HTTP|HTTPS|JSON|XML|SQL|HTML|CSS|JS)\b",
        r"\b(?:function|method|parameter|variable|class|interface|protocol)\b",
    ]
    has_technical = any(re.search(p, text, re.IGNORECASE) for p in technical_indicators)

    if has_dialogue and has_technical:
        return ContentType.MIXED
    elif has_dialogue:
        return ContentType.DIALOGUE
    elif has_technical:
        return ContentType.TECHNICAL
    return ContentType.NARRATION


def _detect_emotions(text: str) -> list[str]:
    emotion_keywords = {
        "happy": ["happy", "joy", "glad", "excited", "cheerful", "delighted", "pleased", "smile", "laugh"],
        "sad": ["sad", "unhappy", "disappointed", "depressed", "sorrow", "tears", "cry"],
        "angry": ["angry", "mad", "furious", "annoyed", "irritated", "rage"],
        "neutral": ["okay", "fine", "alright", "normal"],
        "surprised": ["surprised", "shocked", "amazed", "astonished", "wow"],
    }
    text_lower = text.lower()
    detected = [e for e, kws in emotion_keywords.items() if any(k in text_lower for k in kws)]
    return detected if detected else ["neutral"]


def _select_engine(
    content_type: ContentType,
    complexity: TextComplexity,
    word_count: int,
    available_engines: list[str],
) -> str:
    if word_count > 500:
        if "xtts" in available_engines:
            return "xtts"
        if "chatterbox" in available_engines:
            return "chatterbox"
    if complexity in [TextComplexity.COMPLEX, TextComplexity.VERY_COMPLEX]:
        if content_type == ContentType.NARRATION and "tortoise" in available_engines:
            return "tortoise"
        if "chatterbox" in available_engines:
            return "chatterbox"
    if content_type == ContentType.DIALOGUE and "chatterbox" in available_engines:
        return "chatterbox"
    if content_type == ContentType.TECHNICAL and "chatterbox" in available_engines:
        return "chatterbox"
    return available_engines[0] if available_engines else "xtts"


def _select_quality_mode(
    complexity: TextComplexity, content_type: ContentType, word_count: int
) -> str:
    if word_count > 500:
        return "standard"
    if complexity == TextComplexity.VERY_COMPLEX:
        return "ultra"
    if complexity == TextComplexity.COMPLEX:
        return "high"
    if content_type == ContentType.TECHNICAL:
        return "high"
    if content_type == ContentType.DIALOGUE:
        return "fast" if complexity == TextComplexity.SIMPLE else "standard"
    if content_type == ContentType.NARRATION:
        if complexity in [TextComplexity.COMPLEX, TextComplexity.VERY_COMPLEX]:
            return "ultra"
        if complexity == TextComplexity.MODERATE:
            return "high"
        return "standard"
    return "standard"


def _should_enhance_quality(
    complexity: TextComplexity,
    content_type: ContentType,
    target_quality: float | None,
) -> bool:
    if target_quality and target_quality > 0.85:
        return True
    if complexity in [TextComplexity.COMPLEX, TextComplexity.VERY_COMPLEX]:
        return True
    if content_type == ContentType.TECHNICAL:
        return True
    if content_type == ContentType.NARRATION and complexity != TextComplexity.SIMPLE:
        return True
    if content_type == ContentType.DIALOGUE and complexity == TextComplexity.SIMPLE:
        return False
    return False


def _predict_quality_score(
    engine: str,
    quality_mode: str,
    enhance_quality: bool,
    complexity: TextComplexity,
    content_type: ContentType,
) -> float:
    engine_quality: dict[str, float] = {"xtts": 0.75, "chatterbox": 0.85, "tortoise": 0.90}
    base_quality = engine_quality.get(engine, 0.80)
    mode_multipliers: dict[str, float] = {"fast": 0.90, "standard": 1.00, "high": 1.05, "ultra": 1.10}
    quality = base_quality * mode_multipliers.get(quality_mode, 1.0)
    if enhance_quality:
        quality *= 1.05
    complexity_penalties = {
        TextComplexity.SIMPLE: 1.00,
        TextComplexity.MODERATE: 0.98,
        TextComplexity.COMPLEX: 0.95,
        TextComplexity.VERY_COMPLEX: 0.92,
    }
    quality *= complexity_penalties.get(complexity, 1.0)
    if content_type == ContentType.DIALOGUE:
        quality *= 0.98
    elif content_type == ContentType.TECHNICAL:
        quality *= 0.97
    return max(0.0, min(1.0, quality))


def _generate_reasoning(
    text_analysis: TextAnalysisResult,
    engine: str,
    quality_mode: str,
    enhance_quality: bool,
) -> str:
    reasons = []
    if engine == "tortoise":
        reasons.append("Tortoise engine selected for maximum quality on complex content")
    elif engine == "chatterbox":
        reasons.append("Chatterbox engine selected for balanced quality and speed")
    else:
        reasons.append("XTTS engine selected for fast synthesis")
    if quality_mode == "ultra":
        reasons.append("Ultra quality mode for complex content")
    elif quality_mode == "high":
        reasons.append("High quality mode for better clarity")
    elif quality_mode == "fast":
        reasons.append("Fast mode for simple, short content")
    if text_analysis.content_type == ContentType.DIALOGUE:
        reasons.append("Dialogue content optimized for naturalness")
    elif text_analysis.content_type == ContentType.TECHNICAL:
        reasons.append("Technical content optimized for clarity")
    if enhance_quality:
        reasons.append("Quality enhancement enabled for improved output")
    if text_analysis.complexity in [TextComplexity.COMPLEX, TextComplexity.VERY_COMPLEX]:
        reasons.append(f"Higher quality settings for {text_analysis.complexity.value} text")
    return ". ".join(reasons) + "."


def _calculate_confidence(text_analysis: TextAnalysisResult, target_quality: float | None) -> float:
    confidence = 0.7
    if text_analysis.content_type != ContentType.MIXED:
        confidence += 0.1
    if text_analysis.complexity in [TextComplexity.SIMPLE, TextComplexity.VERY_COMPLEX]:
        confidence += 0.1
    if target_quality is not None:
        confidence += 0.1
    return min(1.0, confidence)
