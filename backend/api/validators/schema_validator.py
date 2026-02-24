"""
Schema Validation Layer.

Task 2.2.1: Pydantic v2 strict mode enforcement.
Provides comprehensive request validation.
"""

from __future__ import annotations

import logging
from dataclasses import dataclass, field
from typing import Any, Callable, Optional, TypeVar

from pydantic import BaseModel, ConfigDict, ValidationError, field_validator

logger = logging.getLogger(__name__)

T = TypeVar("T", bound=BaseModel)


@dataclass
class ValidationResult:
    """Result of validation."""

    is_valid: bool
    errors: list[dict[str, Any]] = field(default_factory=list)
    validated_data: Any | None = None

    @classmethod
    def success(cls, data: Any) -> ValidationResult:
        return cls(is_valid=True, validated_data=data)

    @classmethod
    def failure(cls, errors: list[dict[str, Any]]) -> ValidationResult:
        return cls(is_valid=False, errors=errors)


class StrictBaseModel(BaseModel):
    """Base model with strict validation."""

    model_config = ConfigDict(
        strict=True,
        validate_assignment=True,
        extra="forbid",
        str_strip_whitespace=True,
        str_min_length=0,
    )


class SchemaValidator:
    """
    Schema validation service.

    Features:
    - Pydantic v2 strict mode
    - Custom validation rules
    - Error message formatting
    - Type coercion control
    """

    def __init__(self):
        self._custom_validators: dict[str, Callable[..., Any]] = {}

    def register_validator(
        self,
        name: str,
        validator: Callable[..., Any],
    ) -> None:
        """Register a custom validator function."""
        self._custom_validators[name] = validator

    def validate(
        self,
        data: dict[str, Any],
        schema: type[T],
    ) -> ValidationResult:
        """
        Validate data against a schema.

        Args:
            data: Data to validate
            schema: Pydantic model class

        Returns:
            ValidationResult
        """
        try:
            validated = schema.model_validate(data, strict=True)
            return ValidationResult.success(validated)

        except ValidationError as e:
            errors = self._format_errors(e)
            return ValidationResult.failure(errors)

    def validate_partial(
        self,
        data: dict[str, Any],
        schema: type[T],
        required_fields: list[str] | None = None,
    ) -> ValidationResult:
        """
        Validate partial data (for updates).

        Only validates provided fields.
        """
        # Get schema fields
        schema_fields = schema.model_fields

        # Filter to only provided fields
        filtered_data = {k: v for k, v in data.items() if k in schema_fields}

        # Check required fields
        if required_fields:
            missing = [f for f in required_fields if f not in filtered_data]
            if missing:
                return ValidationResult.failure(
                    [{"field": f, "message": "Field is required"} for f in missing]
                )

        try:
            # Create partial model
            partial_schema = self._create_partial_schema(schema, set(filtered_data.keys()))
            validated = partial_schema.model_validate(filtered_data, strict=True)
            return ValidationResult.success(validated)

        except ValidationError as e:
            errors = self._format_errors(e)
            return ValidationResult.failure(errors)

    def _format_errors(self, error: ValidationError) -> list[dict[str, Any]]:
        """Format Pydantic errors for API response."""
        errors = []

        for e in error.errors():
            field_path = ".".join(str(loc) for loc in e["loc"])

            errors.append(
                {
                    "field": field_path,
                    "message": e["msg"],
                    "type": e["type"],
                    "input": e.get("input"),
                }
            )

        return errors

    def _create_partial_schema(
        self,
        schema: type[T],
        fields: set[str],
    ) -> type[BaseModel]:
        """Create a partial schema with only specified fields."""
        field_definitions = {}

        for field_name, field_info in schema.model_fields.items():
            if field_name in fields:
                # Make field optional for partial updates
                field_definitions[field_name] = (
                    Optional[field_info.annotation],
                    field_info,
                )

        return type(
            f"Partial{schema.__name__}",
            (BaseModel,),
            {"__annotations__": {k: v[0] for k, v in field_definitions.items()}},
        )

    def validate_list(
        self,
        items: list[dict[str, Any]],
        schema: type[T],
        fail_fast: bool = False,
    ) -> list[ValidationResult]:
        """
        Validate a list of items.

        Args:
            items: List of data dicts
            schema: Schema to validate against
            fail_fast: Stop on first error

        Returns:
            List of validation results
        """
        results = []

        for item in items:
            result = self.validate(item, schema)
            results.append(result)

            if fail_fast and not result.is_valid:
                break

        return results


# Common validation models
class AudioUploadSchema(StrictBaseModel):
    """Schema for audio file uploads."""

    filename: str
    content_type: str
    size_bytes: int

    @field_validator("content_type")
    @classmethod
    def validate_content_type(cls, v: str) -> str:
        allowed = {"audio/wav", "audio/mp3", "audio/mpeg", "audio/flac", "audio/ogg"}
        if v not in allowed:
            raise ValueError(f"Content type must be one of: {allowed}")
        return v

    @field_validator("size_bytes")
    @classmethod
    def validate_size(cls, v: int) -> int:
        max_size = 100 * 1024 * 1024  # 100MB
        if v > max_size:
            raise ValueError(f"File size exceeds maximum of {max_size} bytes")
        return v


class SynthesisRequestSchema(StrictBaseModel):
    """Schema for TTS synthesis requests."""

    text: str
    voice_id: str
    engine: str = "xtts"
    language: str = "en"
    speed: float = 1.0
    pitch: float = 1.0

    @field_validator("text")
    @classmethod
    def validate_text(cls, v: str) -> str:
        if len(v) > 10000:
            raise ValueError("Text too long (max 10000 characters)")
        if len(v) < 1:
            raise ValueError("Text is required")
        return v

    @field_validator("speed")
    @classmethod
    def validate_speed(cls, v: float) -> float:
        if not 0.25 <= v <= 4.0:
            raise ValueError("Speed must be between 0.25 and 4.0")
        return v

    @field_validator("pitch")
    @classmethod
    def validate_pitch(cls, v: float) -> float:
        if not 0.5 <= v <= 2.0:
            raise ValueError("Pitch must be between 0.5 and 2.0")
        return v


# Global validator
_validator: SchemaValidator | None = None


def get_schema_validator() -> SchemaValidator:
    """Get or create the global schema validator."""
    global _validator
    if _validator is None:
        _validator = SchemaValidator()
    return _validator
