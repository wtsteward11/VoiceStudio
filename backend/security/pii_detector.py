"""
PII Detection and Handling.

Task 2.4.4: Auto-detect and protect PII.
Detects and redacts personally identifiable information.
"""

from __future__ import annotations

import logging
import re
from dataclasses import dataclass, field
from enum import Enum
from typing import Any

logger = logging.getLogger(__name__)


class PIIType(Enum):
    """Types of PII."""

    EMAIL = "email"
    PHONE = "phone"
    SSN = "ssn"
    CREDIT_CARD = "credit_card"
    IP_ADDRESS = "ip_address"
    ADDRESS = "address"
    NAME = "name"
    DATE_OF_BIRTH = "date_of_birth"
    PASSPORT = "passport"
    DRIVER_LICENSE = "driver_license"
    BANK_ACCOUNT = "bank_account"
    API_KEY = "api_key"


@dataclass
class PIIMatch:
    """A detected PII match."""

    pii_type: PIIType
    value: str
    start: int
    end: int
    confidence: float
    context: str = ""


@dataclass
class PIIDetectorConfig:
    """Configuration for PII detector."""

    enabled_types: set[PIIType] = field(default_factory=lambda: set(PIIType))
    redaction_char: str = "*"
    min_confidence: float = 0.7
    max_context_chars: int = 20


class PIIDetector:
    """
    PII detection and redaction.

    Features:
    - Multiple PII type detection
    - Configurable sensitivity
    - Redaction support
    - Context capture
    - Confidence scoring
    """

    # Regex patterns for PII detection
    PATTERNS = {
        PIIType.EMAIL: (
            r"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b",
            0.95,
        ),
        PIIType.PHONE: (
            r"\b(?:\+?1[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}\b",
            0.85,
        ),
        PIIType.SSN: (
            r"\b\d{3}[-.\s]?\d{2}[-.\s]?\d{4}\b",
            0.90,
        ),
        PIIType.CREDIT_CARD: (
            r"\b(?:4[0-9]{12}(?:[0-9]{3})?|5[1-5][0-9]{14}|3[47][0-9]{13}|6(?:011|5[0-9]{2})[0-9]{12})\b",
            0.95,
        ),
        PIIType.IP_ADDRESS: (
            r"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b",
            0.90,
        ),
        PIIType.API_KEY: (
            r'\b(?:api[_-]?key|token|secret)["\'\s:=]+([A-Za-z0-9_\-]{20,})\b',
            0.85,
        ),
        PIIType.DATE_OF_BIRTH: (
            r"\b(?:0[1-9]|1[0-2])[/\-](?:0[1-9]|[12][0-9]|3[01])[/\-](?:19|20)\d{2}\b",
            0.80,
        ),
    }

    def __init__(self, config: PIIDetectorConfig | None = None):
        self.config = config or PIIDetectorConfig()

        # Compile patterns
        self._compiled_patterns: dict[PIIType, tuple[re.Pattern, float]] = {}
        for pii_type, (pattern, confidence) in self.PATTERNS.items():
            if pii_type in self.config.enabled_types:
                self._compiled_patterns[pii_type] = (
                    re.compile(pattern, re.IGNORECASE),
                    confidence,
                )

    def detect(self, text: str) -> list[PIIMatch]:
        """
        Detect PII in text.

        Args:
            text: Text to scan

        Returns:
            List of PII matches
        """
        if not text:
            return []

        matches: list[PIIMatch] = []

        for pii_type, (pattern, base_confidence) in self._compiled_patterns.items():
            for match in pattern.finditer(text):
                confidence = self._calculate_confidence(pii_type, match.group(), base_confidence)

                if confidence >= self.config.min_confidence:
                    # Get context
                    start = max(0, match.start() - self.config.max_context_chars)
                    end = min(len(text), match.end() + self.config.max_context_chars)
                    context = text[start:end]

                    matches.append(
                        PIIMatch(
                            pii_type=pii_type,
                            value=match.group(),
                            start=match.start(),
                            end=match.end(),
                            confidence=confidence,
                            context=context,
                        )
                    )

        # Sort by position
        matches.sort(key=lambda m: m.start)

        return matches

    def _calculate_confidence(
        self,
        pii_type: PIIType,
        value: str,
        base_confidence: float,
    ) -> float:
        """Calculate confidence score for a match."""
        confidence = base_confidence

        # Adjust based on type-specific validation
        if pii_type == PIIType.CREDIT_CARD:
            if self._validate_luhn(value.replace("-", "").replace(" ", "")):
                confidence = min(1.0, confidence + 0.05)
            else:
                confidence = max(0.0, confidence - 0.3)

        elif pii_type == PIIType.SSN:
            # SSNs shouldn't start with 000, 666, or 9xx
            clean = value.replace("-", "").replace(" ", "")
            if clean.startswith(("000", "666")) or clean[0] == "9":
                confidence = max(0.0, confidence - 0.3)

        elif pii_type == PIIType.PHONE:
            # Check for common non-phone patterns
            clean = re.sub(r"\D", "", value)
            if clean.startswith("0000") or clean == "1234567890":
                confidence = max(0.0, confidence - 0.5)

        return confidence

    @staticmethod
    def _validate_luhn(number: str) -> bool:
        """Validate credit card number using Luhn algorithm."""
        try:
            digits = [int(d) for d in number if d.isdigit()]
            if len(digits) < 13:
                return False

            # Double every second digit from right
            for i in range(len(digits) - 2, -1, -2):
                digits[i] *= 2
                if digits[i] > 9:
                    digits[i] -= 9

            return sum(digits) % 10 == 0
        except Exception:
            return False

    def redact(
        self,
        text: str,
        pii_types: set[PIIType] | None = None,
    ) -> str:
        """
        Redact PII from text.

        Args:
            text: Text to redact
            pii_types: Specific types to redact (None for all)

        Returns:
            Redacted text
        """
        matches = self.detect(text)

        if pii_types:
            matches = [m for m in matches if m.pii_type in pii_types]

        # Sort in reverse order to preserve positions
        matches.sort(key=lambda m: m.start, reverse=True)

        result = text
        for match in matches:
            # Keep first and last char, redact middle
            value = match.value
            if len(value) > 4:
                redacted = value[0] + self.config.redaction_char * (len(value) - 2) + value[-1]
            else:
                redacted = self.config.redaction_char * len(value)

            result = result[: match.start] + redacted + result[match.end :]

        return result

    def redact_dict(
        self,
        data: dict,
        pii_types: set[PIIType] | None = None,
    ) -> dict:
        """
        Recursively redact PII from a dictionary.

        Args:
            data: Dictionary to redact
            pii_types: Specific types to redact

        Returns:
            Redacted dictionary
        """
        result: dict[str, Any] = {}

        for key, value in data.items():
            if isinstance(value, str):
                result[key] = self.redact(value, pii_types)
            elif isinstance(value, dict):
                result[key] = self.redact_dict(value, pii_types)
            elif isinstance(value, list):
                result[key] = [
                    (
                        self.redact_dict(item, pii_types)
                        if isinstance(item, dict)
                        else self.redact(item, pii_types) if isinstance(item, str) else item
                    )
                    for item in value
                ]
            else:
                result[key] = value

        return result

    def has_pii(self, text: str) -> bool:
        """Check if text contains PII."""
        return len(self.detect(text)) > 0

    def get_pii_summary(self, text: str) -> dict[str, int]:
        """Get summary of PII types found."""
        matches = self.detect(text)
        summary: dict[str, int] = {}

        for match in matches:
            type_name = match.pii_type.value
            summary[type_name] = summary.get(type_name, 0) + 1

        return summary


# Global detector
_detector: PIIDetector | None = None


def get_pii_detector() -> PIIDetector:
    """Get or create the global PII detector."""
    global _detector
    if _detector is None:
        _detector = PIIDetector()
    return _detector
