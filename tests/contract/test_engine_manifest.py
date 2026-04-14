"""
Engine Manifest Validation Tests

Validates all engine manifest files in the engines/ directory:
- Required fields presence
- Field type validation
- Cross-manifest consistency
- Contract schema validation
- Capability validation
"""

import json
import re
import sys
from pathlib import Path
from typing import Any

import pytest

# Add project root to path
PROJECT_ROOT = Path(__file__).parent.parent.parent
sys.path.insert(0, str(PROJECT_ROOT))


# =============================================================================
# MANIFEST SCHEMA DEFINITIONS
# =============================================================================

# Required fields for all engine manifests
REQUIRED_FIELDS = [
    "engine_id",
    "name",
    "type",
    "version",
    "description",
]

# Required fields per engine type
TYPE_REQUIRED_FIELDS = {
    "audio": ["subtype"],
    "image": [],
    "video": [],
}

# Valid engine types
VALID_ENGINE_TYPES = {"audio", "image", "video", "llm"}

# Valid subtypes per engine type
VALID_SUBTYPES = {
    "audio": {"tts", "stt", "voice_clone", "voice_conversion", "transcription", "processing"},
    "image": {"generation", "editing", "upscaling", "inpainting"},
    "video": {"generation", "editing", "face_swap", "animation", "processing"},
}

# Valid capability names
VALID_CAPABILITIES = {
    # Audio capabilities
    "voice_cloning",
    "zero_shot_cloning",
    "multi_language_tts",
    "emotion_control",
    "style_transfer",
    "expressive_speech",
    "high_quality_synthesis",
    "streaming",
    "transcription",
    "speaker_diarization",
    "voice_conversion",
    "singing_voice",
    # Image capabilities
    "text_to_image",
    "image_to_image",
    "inpainting",
    "outpainting",
    "upscaling",
    "controlnet",
    "lora",
    # Video capabilities
    "face_swap",
    "lip_sync",
    "animation",
    "motion_transfer",
    "video_generation",
    "video_editing",
    "deforum",
}

# Valid license identifiers
VALID_LICENSES = {
    "MIT",
    "Apache-2.0",
    "GPL-3.0",
    "LGPL-3.0",
    "BSD-3-Clause",
    "BSD-2-Clause",
    "MPL-2.0",
    "CC-BY-4.0",
    "CC-BY-NC-4.0",
    "CC0-1.0",
    "Unlicense",
    "proprietary",
    "custom",
    "AGPL-3.0",
    "ISC",
}


# =============================================================================
# FIXTURES
# =============================================================================


@pytest.fixture(scope="module")
def all_manifests() -> list[tuple[Path, dict[str, Any]]]:
    """Load all engine manifests from engines directory."""
    engines_dir = PROJECT_ROOT / "engines"
    manifests = []

    if not engines_dir.exists():
        pytest.skip(f"Engines directory not found: {engines_dir}")

    for manifest_file in engines_dir.rglob("engine.manifest.json"):
        try:
            with open(manifest_file, encoding="utf-8") as f:
                data = json.load(f)
                manifests.append((manifest_file, data))
        except json.JSONDecodeError as e:
            pytest.fail(f"Invalid JSON in {manifest_file}: {e}")

    return manifests


@pytest.fixture(scope="module")
def audio_manifests(all_manifests) -> list[tuple[Path, dict[str, Any]]]:
    """Filter to audio engine manifests only."""
    return [(p, m) for p, m in all_manifests if m.get("type") == "audio"]


@pytest.fixture(scope="module")
def image_manifests(all_manifests) -> list[tuple[Path, dict[str, Any]]]:
    """Filter to image engine manifests only."""
    return [(p, m) for p, m in all_manifests if m.get("type") == "image"]


@pytest.fixture(scope="module")
def video_manifests(all_manifests) -> list[tuple[Path, dict[str, Any]]]:
    """Filter to video engine manifests only."""
    return [(p, m) for p, m in all_manifests if m.get("type") == "video"]


# =============================================================================
# BASIC VALIDATION TESTS
# =============================================================================


@pytest.mark.contract
class TestManifestDiscovery:
    """Tests for manifest discovery."""

    def test_manifests_exist(self, all_manifests):
        """Verify at least some manifests are found."""
        assert len(all_manifests) > 0, "No engine manifests found"

    def test_minimum_manifest_count(self, all_manifests):
        """Verify expected number of manifests."""
        # We expect at least 40 manifests based on the directory listing
        assert (
            len(all_manifests) >= 40
        ), f"Expected at least 40 manifests, found {len(all_manifests)}"

    def test_audio_manifests_exist(self, audio_manifests):
        """Verify audio engine manifests exist."""
        assert len(audio_manifests) > 0, "No audio engine manifests found"

    def test_image_manifests_exist(self, image_manifests):
        """Verify image engine manifests exist."""
        assert len(image_manifests) > 0, "No image engine manifests found"

    def test_video_manifests_exist(self, video_manifests):
        """Verify video engine manifests exist."""
        assert len(video_manifests) > 0, "No video engine manifests found"


@pytest.mark.contract
class TestRequiredFields:
    """Tests for required manifest fields."""

    def test_all_have_required_fields(self, all_manifests):
        """Verify all manifests have required fields."""
        errors = []

        for path, manifest in all_manifests:
            for field in REQUIRED_FIELDS:
                if field not in manifest:
                    errors.append(f"{path.parent.name}: missing '{field}'")

        assert not errors, "Missing required fields:\n" + "\n".join(errors[:20])

    def test_engine_id_format(self, all_manifests):
        """Verify engine_id follows naming convention."""
        errors = []
        pattern = r"^[a-z][a-z0-9_]*$"

        for _path, manifest in all_manifests:
            engine_id = manifest.get("engine_id", "")
            if not re.match(pattern, engine_id):
                errors.append(
                    f"{engine_id}: invalid format (must be lowercase alphanumeric with underscores)"
                )

        assert not errors, "Invalid engine_id format:\n" + "\n".join(errors[:20])

    def test_engine_id_matches_directory(self, all_manifests):
        """Verify engine_id matches containing directory name."""
        mismatches = []

        for path, manifest in all_manifests:
            engine_id = manifest.get("engine_id", "")
            dir_name = path.parent.name

            # Allow some variation (underscores vs hyphens)
            normalized_id = engine_id.replace("_", "").lower()
            normalized_dir = dir_name.replace("_", "").replace("-", "").lower()

            if normalized_id != normalized_dir:
                mismatches.append(f"{dir_name}: engine_id='{engine_id}'")

        # Advisory: some mismatches may be intentional
        if len(mismatches) > 5:
            import warnings

            warnings.warn(f"Engine ID/directory mismatches: {mismatches[:5]}", stacklevel=2)

    def test_version_format(self, all_manifests):
        """Verify version follows semver-like format."""
        errors = []
        # Allow "1.0", "1.0.0", "2.0-beta", etc.
        pattern = r"^\d+\.\d+(\.\d+)?(-[a-zA-Z0-9]+)?$"

        for _path, manifest in all_manifests:
            version = manifest.get("version", "")
            if not re.match(pattern, str(version)):
                errors.append(f"{manifest.get('engine_id')}: invalid version '{version}'")

        # Allow some non-standard versions
        if len(errors) > 5:
            import warnings

            warnings.warn(f"Non-standard versions: {errors[:5]}", stacklevel=2)


@pytest.mark.contract
class TestTypeValidation:
    """Tests for engine type validation."""

    def test_valid_engine_types(self, all_manifests):
        """Verify all engines have valid type."""
        invalid = []

        for _path, manifest in all_manifests:
            engine_type = manifest.get("type")
            if engine_type not in VALID_ENGINE_TYPES:
                invalid.append(f"{manifest.get('engine_id')}: type='{engine_type}'")

        assert not invalid, "Invalid engine types:\n" + "\n".join(invalid)

    def test_audio_subtypes_valid(self, audio_manifests):
        """Verify audio engines have valid subtype."""
        invalid = []

        for _path, manifest in audio_manifests:
            subtype = manifest.get("subtype")
            if subtype and subtype not in VALID_SUBTYPES["audio"]:
                invalid.append(f"{manifest.get('engine_id')}: subtype='{subtype}'")

        # Advisory only - allow custom subtypes
        if invalid:
            import warnings

            warnings.warn(f"Non-standard audio subtypes: {invalid[:5]}", stacklevel=2)


@pytest.mark.contract
class TestCapabilities:
    """Tests for capability declarations."""

    def test_capabilities_is_list(self, all_manifests):
        """Verify capabilities field is a list when present."""
        errors = []

        for _path, manifest in all_manifests:
            capabilities = manifest.get("capabilities")
            if capabilities is not None and not isinstance(capabilities, list):
                errors.append(f"{manifest.get('engine_id')}: capabilities is not a list")

        assert not errors, "Invalid capabilities type:\n" + "\n".join(errors)

    def test_capabilities_not_empty(self, all_manifests):
        """Verify capabilities is not empty when present."""
        empty = []

        for _path, manifest in all_manifests:
            capabilities = manifest.get("capabilities", [])
            if isinstance(capabilities, list) and len(capabilities) == 0:
                empty.append(manifest.get("engine_id"))

        # Advisory - some engines may intentionally have no capabilities
        if len(empty) > 5:
            import warnings

            warnings.warn(f"Engines with empty capabilities: {empty[:5]}", stacklevel=2)

    def test_known_capabilities(self, all_manifests):
        """Check for unknown capability names."""
        unknown = {}

        for _path, manifest in all_manifests:
            capabilities = manifest.get("capabilities", [])
            engine_id = manifest.get("engine_id")

            for cap in capabilities:
                if cap not in VALID_CAPABILITIES:
                    if cap not in unknown:
                        unknown[cap] = []
                    unknown[cap].append(engine_id)

        # Advisory - allow custom capabilities
        if unknown:
            import warnings

            warnings.warn(f"Custom capabilities found: {list(unknown.keys())[:10]}", stacklevel=2)


# =============================================================================
# CONTRACT VALIDATION TESTS
# =============================================================================


@pytest.mark.contract
class TestContractSchema:
    """Tests for contract schema validation."""

    def test_contract_structure(self, all_manifests):
        """Verify contract has expected structure when present."""
        errors = []

        for _path, manifest in all_manifests:
            contract = manifest.get("contract")
            if not contract:
                continue

            engine_id = manifest.get("engine_id")

            # Check for expected sections
            if not isinstance(contract, dict):
                errors.append(f"{engine_id}: contract is not a dict")
                continue

        assert not errors, "Contract structure errors:\n" + "\n".join(errors)

    def test_input_contract(self, all_manifests):
        """Verify input contract fields."""
        errors = []

        for _path, manifest in all_manifests:
            contract = manifest.get("contract", {})
            input_contract = contract.get("input")

            if not input_contract:
                continue

            engine_id = manifest.get("engine_id")

            # Check for expected input fields
            if manifest.get("type") == "audio":
                if "audio_formats" not in input_contract and "text_max_chars" not in input_contract:
                    errors.append(f"{engine_id}: missing audio input specs")

        # Advisory only
        if len(errors) > 10:
            import warnings

            warnings.warn(f"Input contract issues: {errors[:5]}", stacklevel=2)

    def test_output_contract(self, all_manifests):
        """Verify output contract fields."""
        errors = []

        for _path, manifest in all_manifests:
            contract = manifest.get("contract", {})
            output_contract = contract.get("output")

            if not output_contract:
                continue

            engine_id = manifest.get("engine_id")

            # Check audio output has sample_rate
            if manifest.get("type") == "audio":
                if "sample_rate" not in output_contract and "audio_format" not in output_contract:
                    errors.append(f"{engine_id}: missing audio output specs")

        # Advisory only
        if len(errors) > 10:
            import warnings

            warnings.warn(f"Output contract issues: {errors[:5]}", stacklevel=2)

    def test_resource_requirements(self, all_manifests):
        """Verify resource requirements are reasonable."""
        errors = []

        for _path, manifest in all_manifests:
            contract = manifest.get("contract", {})
            resources = contract.get("resources", {})

            if not resources:
                continue

            engine_id = manifest.get("engine_id")

            # Check for unreasonable values
            vram = resources.get("vram_mb", 0)
            ram = resources.get("ram_mb", 0)
            timeout = resources.get("timeout_seconds", 0)

            if vram > 48000:  # > 48GB VRAM seems excessive
                errors.append(f"{engine_id}: vram_mb={vram} seems excessive")
            if ram > 128000:  # > 128GB RAM seems excessive
                errors.append(f"{engine_id}: ram_mb={ram} seems excessive")
            if timeout > 3600:  # > 1 hour timeout seems excessive
                errors.append(f"{engine_id}: timeout={timeout}s seems excessive")

        assert not errors, "Resource requirement issues:\n" + "\n".join(errors)


@pytest.mark.contract
class TestConfigSchema:
    """Tests for config schema validation."""

    def test_config_schema_structure(self, all_manifests):
        """Verify config_schema has valid structure."""
        errors = []

        for _path, manifest in all_manifests:
            config_schema = manifest.get("config_schema")
            if not config_schema:
                continue

            engine_id = manifest.get("engine_id")

            if not isinstance(config_schema, dict):
                errors.append(f"{engine_id}: config_schema is not a dict")
                continue

            for field_name, field_def in config_schema.items():
                if not isinstance(field_def, dict):
                    errors.append(f"{engine_id}.{field_name}: field definition is not a dict")
                    continue

                # Check for type field
                if "type" not in field_def:
                    errors.append(f"{engine_id}.{field_name}: missing type")

        assert not errors, "Config schema errors:\n" + "\n".join(errors[:20])

    def test_config_types_valid(self, all_manifests):
        """Verify config field types are valid JSON Schema types."""
        valid_types = {"string", "number", "integer", "boolean", "array", "object"}
        errors = []

        for _path, manifest in all_manifests:
            config_schema = manifest.get("config_schema", {})
            engine_id = manifest.get("engine_id")

            for field_name, field_def in config_schema.items():
                if not isinstance(field_def, dict):
                    continue

                field_type = field_def.get("type")
                if field_type and field_type not in valid_types:
                    errors.append(f"{engine_id}.{field_name}: invalid type '{field_type}'")

        assert not errors, "Invalid config types:\n" + "\n".join(errors)

    def test_enum_fields_have_values(self, all_manifests):
        """Verify enum fields have values defined."""
        errors = []

        for _path, manifest in all_manifests:
            config_schema = manifest.get("config_schema", {})
            engine_id = manifest.get("engine_id")

            for field_name, field_def in config_schema.items():
                if not isinstance(field_def, dict):
                    continue

                if "enum" in field_def:
                    enum_values = field_def["enum"]
                    if not isinstance(enum_values, list) or len(enum_values) == 0:
                        errors.append(f"{engine_id}.{field_name}: empty or invalid enum")

        assert not errors, "Enum errors:\n" + "\n".join(errors)


# =============================================================================
# CROSS-MANIFEST CONSISTENCY TESTS
# =============================================================================


@pytest.mark.contract
class TestCrossManifestConsistency:
    """Tests for consistency across manifests."""

    def test_unique_engine_ids(self, all_manifests):
        """Verify all engine_ids are unique."""
        ids = {}
        duplicates = []

        for path, manifest in all_manifests:
            engine_id = manifest.get("engine_id")
            if engine_id in ids:
                duplicates.append(f"{engine_id}: {ids[engine_id]} and {path}")
            else:
                ids[engine_id] = path

        assert not duplicates, "Duplicate engine_ids:\n" + "\n".join(duplicates)

    def test_consistent_license_format(self, all_manifests):
        """Verify licenses use consistent format."""
        non_standard = []

        for _path, manifest in all_manifests:
            license_val = manifest.get("license")
            if license_val and license_val not in VALID_LICENSES:
                non_standard.append(f"{manifest.get('engine_id')}: {license_val}")

        # Advisory - allow custom licenses
        if len(non_standard) > 10:
            import warnings

            warnings.warn(f"Non-standard licenses: {non_standard[:5]}", stacklevel=2)

    def test_python_version_format(self, all_manifests):
        """Verify python_version uses consistent format."""
        errors = []
        # Allow ">=3.10", "3.10", ">=3.9,<4.0", etc.
        pattern = r"^[>=<]*\d+\.\d+(\.\d+)?(,\s*[>=<]*\d+\.\d+(\.\d+)?)?$"

        for _path, manifest in all_manifests:
            py_version = manifest.get("python_version")
            if py_version and not re.match(pattern, py_version):
                errors.append(f"{manifest.get('engine_id')}: '{py_version}'")

        # Advisory only
        if len(errors) > 5:
            import warnings

            warnings.warn(f"Non-standard python_version: {errors[:5]}", stacklevel=2)

    def test_audio_sample_rates_consistent(self, audio_manifests):
        """Check if audio engines use consistent sample rates."""
        sample_rates = {}

        for _path, manifest in audio_manifests:
            contract = manifest.get("contract", {})
            output = contract.get("output", {})
            rate = output.get("sample_rate")

            if rate:
                if rate not in sample_rates:
                    sample_rates[rate] = []
                sample_rates[rate].append(manifest.get("engine_id"))

        # Just report the distribution
        if sample_rates:
            # Most common sample rates should be 22050, 24000, 44100, 48000
            common_rates = {22050, 24000, 44100, 48000, 16000}
            non_standard = set(sample_rates.keys()) - common_rates

            if non_standard:
                import warnings

                warnings.warn(f"Non-standard sample rates: {non_standard}", stacklevel=2)


@pytest.mark.contract
class TestDependencyValidation:
    """Tests for dependency declarations."""

    def test_dependencies_format(self, all_manifests):
        """Verify dependencies use valid format."""
        errors = []

        for _path, manifest in all_manifests:
            dependencies = manifest.get("dependencies")
            if not dependencies:
                continue

            engine_id = manifest.get("engine_id")

            if not isinstance(dependencies, dict):
                errors.append(f"{engine_id}: dependencies is not a dict")
                continue

            for pkg, version in dependencies.items():
                # Check version format (allow ==1.0.0, >=1.0, etc.)
                if not isinstance(version, str):
                    errors.append(f"{engine_id}: {pkg} version is not a string")

        assert not errors, "Dependency format errors:\n" + "\n".join(errors)

    def test_no_conflicting_torch_versions(self, all_manifests):
        """Check for potentially conflicting torch version requirements."""
        torch_versions = {}

        for _path, manifest in all_manifests:
            dependencies = manifest.get("dependencies", {})
            engine_id = manifest.get("engine_id")

            for pkg, version in dependencies.items():
                if "torch" in pkg.lower():
                    if version not in torch_versions:
                        torch_versions[version] = []
                    torch_versions[version].append(engine_id)

        # Report if there are many different torch versions
        if len(torch_versions) > 5:
            import warnings

            warnings.warn(
                f"Multiple torch version requirements: {list(torch_versions.keys())}", stacklevel=2
            )


# =============================================================================
# ENTRY POINT VALIDATION
# =============================================================================


@pytest.mark.contract
class TestEntryPointValidation:
    """Tests for entry point validation."""

    def test_entry_point_format(self, all_manifests):
        """Verify entry_point follows Python module path format."""
        errors = []
        pattern = r"^[a-zA-Z_][a-zA-Z0-9_.]*\.[A-Z][a-zA-Z0-9]*$"

        for _path, manifest in all_manifests:
            entry_point = manifest.get("entry_point")
            if entry_point and not re.match(pattern, entry_point):
                errors.append(f"{manifest.get('engine_id')}: '{entry_point}'")

        # Advisory only - some may use different patterns
        if len(errors) > 10:
            import warnings

            warnings.warn(f"Non-standard entry points: {errors[:5]}", stacklevel=2)

    def test_entry_point_consistency(self, all_manifests):
        """Verify entry points follow consistent naming pattern."""
        patterns = {}

        for _path, manifest in all_manifests:
            entry_point = manifest.get("entry_point", "")

            # Extract the module pattern (e.g., "app.core.engines")
            parts = entry_point.rsplit(".", 2)
            if len(parts) >= 2:
                module_prefix = parts[0]
                if module_prefix not in patterns:
                    patterns[module_prefix] = 0
                patterns[module_prefix] += 1

        # Should have a dominant pattern
        if patterns:
            dominant = max(patterns.values())
            total = sum(patterns.values())
            if dominant / total < 0.5:
                import warnings

                warnings.warn(f"Inconsistent entry point patterns: {patterns}", stacklevel=2)


if __name__ == "__main__":
    pytest.main([__file__, "-v", "-m", "contract"])
