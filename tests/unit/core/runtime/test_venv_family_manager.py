"""
Unit Tests for Venv Family Manager
Tests TD-015 venv family management functionality.
"""

import sys
from pathlib import Path

import pytest

project_root = Path(__file__).parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

# Import the venv family manager module
try:
    from app.core.runtime import venv_family_manager
    from app.core.runtime.venv_family_manager import (
        ENGINE_TO_FAMILY,
        FAMILY_CONFIGS,
        VenvFamily,
        VenvFamilyManager,
        get_venv_manager,
    )
except ImportError as e:
    pytest.skip(f"Could not import venv_family_manager: {e}", allow_module_level=True)


class TestVenvFamilyManager:
    """Test VenvFamilyManager class."""

    def test_venv_family_manager_imports(self):
        """Test venv_family_manager can be imported."""
        assert venv_family_manager is not None

    def test_venv_families_defined(self):
        """Test that all 3 families are defined."""
        assert VenvFamily.CORE_TTS is not None
        assert VenvFamily.ADVANCED_TTS is not None
        assert VenvFamily.STT is not None

    def test_family_configs_exist(self):
        """Test that family configs exist for all families."""
        assert VenvFamily.CORE_TTS in FAMILY_CONFIGS
        assert VenvFamily.ADVANCED_TTS in FAMILY_CONFIGS
        assert VenvFamily.STT in FAMILY_CONFIGS

    def test_engine_to_family_mapping(self):
        """Test engine to family mapping."""
        # Core TTS engines
        assert ENGINE_TO_FAMILY.get("xtts_v2") == VenvFamily.CORE_TTS
        assert ENGINE_TO_FAMILY.get("silero") == VenvFamily.CORE_TTS

        # Fast TTS engines (piper moved here)
        assert ENGINE_TO_FAMILY.get("piper") == VenvFamily.FAST_TTS

        # Advanced TTS engines
        assert ENGINE_TO_FAMILY.get("chatterbox") == VenvFamily.ADVANCED_TTS
        assert ENGINE_TO_FAMILY.get("f5_tts") == VenvFamily.ADVANCED_TTS

        # STT engines
        assert ENGINE_TO_FAMILY.get("whisper") == VenvFamily.STT
        assert ENGINE_TO_FAMILY.get("whisper_cpp") == VenvFamily.STT


class TestVenvFamilyManagerInstance:
    """Test VenvFamilyManager instance methods."""

    @pytest.fixture
    def manager(self, tmp_path):
        """Create a VenvFamilyManager with temp directories."""
        return VenvFamilyManager(
            venvs_root=tmp_path / "venvs",
            requirements_root=tmp_path / "requirements",
            advanced_tts_venv_path=tmp_path / "runtime" / "venvs" / "torch26",
        )

    def test_get_family_for_engine(self, manager):
        """Test getting family for engine."""
        assert manager.get_family_for_engine("xtts_v2") == VenvFamily.CORE_TTS
        assert manager.get_family_for_engine("chatterbox") == VenvFamily.ADVANCED_TTS
        assert manager.get_family_for_engine("whisper") == VenvFamily.STT
        assert manager.get_family_for_engine("unknown_engine") is None

    def test_get_venv_path(self, manager):
        """Test getting venv path for family."""
        path = manager.get_venv_path(VenvFamily.CORE_TTS)
        assert path.name == "venv_core_tts"

    def test_get_venv_path_advanced_tts_matches_provision_torch26(self, manager):
        """ADVANCED_TTS resolves to the same tree as create_engine_venv family torch26."""
        path = manager.get_venv_path(VenvFamily.ADVANCED_TTS)
        assert path.name == "torch26"

    def test_get_python_executable(self, manager):
        """Test getting Python executable path."""
        python_exe = manager.get_python_executable(VenvFamily.CORE_TTS)
        if sys.platform == "win32":
            assert python_exe.name == "python.exe"
            assert "Scripts" in str(python_exe)
        else:
            assert python_exe.name == "python"
            assert "bin" in str(python_exe)

    def test_is_venv_created_false(self, manager):
        """Test venv not created by default."""
        assert not manager.is_venv_created(VenvFamily.CORE_TTS)
        assert not manager.is_venv_created(VenvFamily.ADVANCED_TTS)
        assert not manager.is_venv_created(VenvFamily.STT)

    def test_get_status(self, manager):
        """Test getting status of all families."""
        status = manager.get_status()

        assert "venv_core_tts" in status
        assert "venv_advanced_tts" in status
        assert "venv_stt" in status

        # Check status structure
        core_status = status["venv_core_tts"]
        assert "exists" in core_status
        assert "path" in core_status
        assert "description" in core_status
        assert "engines" in core_status
        assert core_status["exists"] is False


class TestGlobalManager:
    """Test global manager instance."""

    def test_get_venv_manager(self):
        """Test getting global venv manager."""
        manager = get_venv_manager()
        assert isinstance(manager, VenvFamilyManager)

    def test_get_venv_manager_singleton(self):
        """Test that get_venv_manager returns singleton."""
        manager1 = get_venv_manager()
        manager2 = get_venv_manager()
        assert manager1 is manager2


class TestFamilyConfigs:
    """Test family configuration details."""

    def test_core_tts_config(self):
        """Test core TTS family config."""
        config = FAMILY_CONFIGS[VenvFamily.CORE_TTS]
        assert config.requirements_file == "requirements-core-tts.txt"
        assert "xtts_v2" in config.engines
        # Note: piper is now in FAST_TTS, not CORE_TTS
        assert config.estimated_size_gb == 8.0

    def test_fast_tts_config(self):
        """Test fast TTS family config (includes piper)."""
        config = FAMILY_CONFIGS[VenvFamily.FAST_TTS]
        assert "piper" in config.engines
        assert "espeak_ng" in config.engines

    def test_advanced_tts_config(self):
        """Test advanced TTS family config."""
        config = FAMILY_CONFIGS[VenvFamily.ADVANCED_TTS]
        assert config.requirements_file == "requirements-advanced-tts.txt"
        assert "chatterbox" in config.engines
        assert config.gpu_required is True
        assert config.estimated_size_gb == 10.0

    def test_stt_config(self):
        """Test STT family config."""
        config = FAMILY_CONFIGS[VenvFamily.STT]
        assert config.requirements_file == "requirements-stt.txt"
        assert "whisper" in config.engines
        assert config.estimated_size_gb == 4.0


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
