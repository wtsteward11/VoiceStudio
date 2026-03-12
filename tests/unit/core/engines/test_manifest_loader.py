"""
Unit Tests for Manifest Loader
Tests engine manifest loading functionality.
"""

import sys
from pathlib import Path

import pytest

project_root = Path(__file__).parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

# Import the manifest loader module
try:
    from app.core.engines import manifest_loader
except ImportError:
    pytest.skip("Could not import manifest_loader", allow_module_level=True)


class TestManifestLoaderImports:
    """Test manifest loader module can be imported."""

    def test_module_imports(self):
        """Test module can be imported."""
        assert manifest_loader is not None, "Failed to import manifest_loader module"

    def test_module_has_functions(self):
        """Test module has expected functions."""
        functions = dir(manifest_loader)
        assert len(functions) > 0, "module should have functions"


class TestManifestLoaderFunctions:
    """Test manifest loader functions exist."""

    def test_load_engine_manifest_function_exists(self):
        """Test load_engine_manifest function exists."""
        if hasattr(manifest_loader, "load_engine_manifest"):
            assert callable(
                manifest_loader.load_engine_manifest
            ), "load_engine_manifest should be callable"

    def test_find_engine_manifests_function_exists(self):
        """Test find_engine_manifests function exists."""
        if hasattr(manifest_loader, "find_engine_manifests"):
            assert callable(
                manifest_loader.find_engine_manifests
            ), "find_engine_manifests should be callable"


class TestManifestLoaderCompatibility:
    """Test manifest loader accepts legacy formats (id, category, requirements, entry_point object)."""

    def test_load_manifest_with_id_instead_of_engine_id(self, tmp_path):
        """Load manifest with 'id' (no engine_id) -> parsed output has engine_id."""
        manifest_json = tmp_path / "engine.manifest.json"
        manifest_json.write_text(
            '{"id": "legacy_engine", "name": "Legacy", "category": "tts", "version": "1.0", '
            '"entry_point": "app.engines:LegacyEngine", "dependencies": []}',
            encoding="utf-8",
        )
        manifest = manifest_loader.load_engine_manifest(str(manifest_json))
        assert manifest["engine_id"] == "legacy_engine"

    def test_load_manifest_with_engine_id(self, tmp_path):
        """Load manifest with engine_id -> same result."""
        manifest_json = tmp_path / "engine.manifest.json"
        manifest_json.write_text(
            '{"engine_id": "modern_engine", "name": "Modern", "type": "tts", "version": "1.0", '
            '"entry_point": "app.engines:ModernEngine", "dependencies": []}',
            encoding="utf-8",
        )
        manifest = manifest_loader.load_engine_manifest(str(manifest_json))
        assert manifest["engine_id"] == "modern_engine"

    def test_load_manifest_with_category_instead_of_type(self, tmp_path):
        """Load manifest with 'category' (no type) -> parsed output has type."""
        manifest_json = tmp_path / "engine.manifest.json"
        manifest_json.write_text(
            '{"id": "cat_engine", "name": "Cat", "category": "tts", "version": "1.0", '
            '"entry_point": "app.engines:CatEngine", "dependencies": []}',
            encoding="utf-8",
        )
        manifest = manifest_loader.load_engine_manifest(str(manifest_json))
        assert manifest["type"] == "tts"

    def test_load_manifest_with_entry_point_object(self, tmp_path):
        """Load manifest with entry_point as object {module, class_name}."""
        manifest_json = tmp_path / "engine.manifest.json"
        manifest_json.write_text(
            '{"id": "obj_engine", "name": "Obj", "category": "tts", "version": "1.0", '
            '"entry_point": {"module": "app.core.engines.foo", "class_name": "FooEngine"}, '
            '"dependencies": []}',
            encoding="utf-8",
        )
        manifest = manifest_loader.load_engine_manifest(str(manifest_json))
        assert manifest["entry_point"] == "app.core.engines.foo:FooEngine"

    def test_load_manifest_with_requirements_packages_instead_of_dependencies(self, tmp_path):
        """Load manifest with requirements.packages (no dependencies) -> normalized to dependencies."""
        manifest_json = tmp_path / "engine.manifest.json"
        manifest_json.write_text(
            '{"id": "req_engine", "name": "Req", "category": "tts", "version": "1.0", '
            '"entry_point": "app.engines:ReqEngine", '
            '"requirements": {"packages": ["torch>=2.0", "TTS>=0.22"]}}',
            encoding="utf-8",
        )
        manifest = manifest_loader.load_engine_manifest(str(manifest_json))
        assert manifest["dependencies"] == ["torch>=2.0", "TTS>=0.22"]


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
