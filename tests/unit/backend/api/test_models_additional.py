"""
Unit Tests for Additional API Models
Tests additional API model definitions.
"""

import sys
from pathlib import Path

import pytest

project_root = Path(__file__).parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

# Import the models_additional module
try:
    from backend.api import models_additional
except ImportError:
    pytest.skip("Could not import models_additional", allow_module_level=True)


class TestModelsAdditionalImports:
    """Test models_additional module can be imported."""

    def test_module_imports(self):
        """Test module can be imported."""
        assert models_additional is not None, "Failed to import models_additional module"

    def test_module_has_classes(self):
        """Test module has expected classes."""
        classes = [
            name
            for name in dir(models_additional)
            if name[0].isupper() and not name.startswith("_")
        ]
        assert len(classes) > 0, "module should have classes"


class TestVoiceSynthesizeRequestOptionalFieldValidators:
    """Test optional-field validators guard against None and non-string inputs."""

    def test_engine_none_accepted(self):
        """engine=None (explicit or omitted) is accepted."""
        m = models_additional.VoiceSynthesizeRequest(
            text="hello",
            profile_id="p1",
            engine=None,
        )
        assert m.engine is None

    def test_engine_valid_string_normalized(self):
        """Valid engine string is normalized to lowercase."""
        m = models_additional.VoiceSynthesizeRequest(
            text="hello",
            profile_id="p1",
            engine="XTTS",
        )
        assert m.engine == "xtts"

    def test_engine_non_string_raises(self):
        """Non-string engine raises ValueError."""
        with pytest.raises(ValueError, match="Engine must be a string"):
            models_additional.VoiceSynthesizeRequest(
                text="hello",
                profile_id="p1",
                engine=123,
            )

    def test_language_none_accepted(self):
        """language=None is accepted."""
        m = models_additional.VoiceSynthesizeRequest(
            text="hello",
            profile_id="p1",
            language=None,
        )
        assert m.language is None

    def test_language_non_string_raises(self):
        """Non-string language raises ValueError."""
        with pytest.raises(ValueError, match="Language must be a string"):
            models_additional.VoiceSynthesizeRequest(
                text="hello",
                profile_id="p1",
                language=42,
            )


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
