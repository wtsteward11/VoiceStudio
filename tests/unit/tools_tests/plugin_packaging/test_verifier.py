"""Tests for the plugin verifier module."""

import hashlib
import json
import zipfile
from pathlib import Path

import pytest

# Check if cryptography is available
try:
    from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey

    HAS_CRYPTOGRAPHY = True
except ImportError:
    HAS_CRYPTOGRAPHY = False

from tools.plugin_packaging.verifier import (
    PluginVerifier,
    VerificationLevel,
    VerificationResult,
    VerificationStatus,
    verify_package,
)


class TestVerificationLevel:
    """Tests for VerificationLevel enum."""

    def test_levels_exist(self):
        """Test all verification levels exist."""
        assert VerificationLevel.NONE.value == "none"
        assert VerificationLevel.STRUCTURE.value == "structure"
        assert VerificationLevel.CHECKSUMS.value == "checksums"
        assert VerificationLevel.SIGNATURE.value == "signature"
        assert VerificationLevel.TRUSTED.value == "trusted"


class TestVerificationStatus:
    """Tests for VerificationStatus enum."""

    def test_statuses_exist(self):
        """Test all verification statuses exist."""
        assert VerificationStatus.VALID.value == "valid"
        assert VerificationStatus.INVALID.value == "invalid"
        assert VerificationStatus.UNSIGNED.value == "unsigned"
        assert VerificationStatus.UNTRUSTED.value == "untrusted"


class TestVerificationResult:
    """Tests for VerificationResult dataclass."""

    def test_is_valid_for_valid(self):
        """Test is_valid returns True for valid status."""
        result = VerificationResult(
            status=VerificationStatus.VALID,
            level=VerificationLevel.TRUSTED,
        )
        assert result.is_valid is True

    def test_is_valid_for_unsigned(self):
        """Test is_valid returns True for unsigned (valid but not signed)."""
        result = VerificationResult(
            status=VerificationStatus.UNSIGNED,
            level=VerificationLevel.CHECKSUMS,
        )
        assert result.is_valid is True

    def test_is_valid_for_invalid(self):
        """Test is_valid returns False for invalid status."""
        result = VerificationResult(
            status=VerificationStatus.INVALID,
            level=VerificationLevel.NONE,
        )
        assert result.is_valid is False

    def test_is_signed_for_signature_level(self):
        """Test is_signed returns True for signature level."""
        result = VerificationResult(
            status=VerificationStatus.UNTRUSTED,
            level=VerificationLevel.SIGNATURE,
        )
        assert result.is_signed is True

    def test_is_signed_for_checksums_level(self):
        """Test is_signed returns False for checksums level."""
        result = VerificationResult(
            status=VerificationStatus.UNSIGNED,
            level=VerificationLevel.CHECKSUMS,
        )
        assert result.is_signed is False

    def test_is_trusted(self):
        """Test is_trusted property."""
        trusted = VerificationResult(
            status=VerificationStatus.VALID,
            level=VerificationLevel.TRUSTED,
        )
        untrusted = VerificationResult(
            status=VerificationStatus.UNTRUSTED,
            level=VerificationLevel.SIGNATURE,
        )

        assert trusted.is_trusted is True
        assert untrusted.is_trusted is False


class TestPluginVerifier:
    """Tests for PluginVerifier class."""

    @pytest.fixture
    def valid_package(self, tmp_path):
        """Create a valid unsigned .vspkg package with correct checksums."""
        pkg_path = tmp_path / "valid.vspkg"

        # Create content
        manifest_content = json.dumps(
            {
                "id": "valid-plugin",
                "name": "Valid Plugin",
                "version": "1.0.0",
            }
        ).encode("utf-8")

        plugin_content = b"# Plugin code"

        # Calculate checksums
        manifest_hash = hashlib.sha256(manifest_content).hexdigest()
        plugin_hash = hashlib.sha256(plugin_content).hexdigest()

        checksums = f"{manifest_hash}  manifest.json\n{plugin_hash}  plugin.py\n"

        with zipfile.ZipFile(pkg_path, "w") as zf:
            zf.writestr(
                "MANIFEST.json",
                json.dumps(
                    {
                        "format_version": "1.0.0",
                        "package_id": "valid-plugin",
                        "package_version": "1.0.0",
                        "plugin_manifest": "manifest.json",
                        "created_at": "2026-02-17T00:00:00",
                        "created_by": "test",
                        "min_voicestudio_version": "1.0.0",
                    }
                ),
            )
            zf.writestr("manifest.json", manifest_content)
            zf.writestr("CHECKSUMS.sha256", checksums)
            zf.writestr("plugin.py", plugin_content)

        return pkg_path

    @pytest.fixture
    def invalid_structure_package(self, tmp_path):
        """Create a package with invalid structure."""
        pkg_path = tmp_path / "invalid-structure.vspkg"

        with zipfile.ZipFile(pkg_path, "w") as zf:
            zf.writestr("plugin.py", "# Missing required files")

        return pkg_path

    @pytest.fixture
    def invalid_checksum_package(self, tmp_path):
        """Create a package with invalid checksums."""
        pkg_path = tmp_path / "invalid-checksum.vspkg"

        with zipfile.ZipFile(pkg_path, "w") as zf:
            zf.writestr(
                "MANIFEST.json",
                json.dumps(
                    {
                        "format_version": "1.0.0",
                        "package_id": "bad-checksum",
                        "package_version": "1.0.0",
                        "plugin_manifest": "manifest.json",
                        "created_at": "2026-02-17T00:00:00",
                        "created_by": "test",
                        "min_voicestudio_version": "1.0.0",
                    }
                ),
            )
            zf.writestr("manifest.json", json.dumps({"id": "bad-checksum"}))
            # Wrong checksum
            zf.writestr("CHECKSUMS.sha256", "wronghash  manifest.json\n")
            zf.writestr("plugin.py", "# Plugin")

        return pkg_path

    def test_verify_valid_package(self, valid_package):
        """Test verification of valid unsigned package."""
        verifier = PluginVerifier()
        result = verifier.verify(valid_package)

        assert result.status == VerificationStatus.UNSIGNED
        assert result.level == VerificationLevel.CHECKSUMS
        assert result.is_valid is True
        assert result.manifest is not None

    def test_verify_invalid_structure(self, invalid_structure_package):
        """Test verification fails for invalid structure."""
        verifier = PluginVerifier()
        result = verifier.verify(invalid_structure_package)

        assert result.status == VerificationStatus.INVALID
        assert result.level == VerificationLevel.NONE
        assert result.is_valid is False

    def test_verify_invalid_checksums(self, invalid_checksum_package):
        """Test verification fails for invalid checksums."""
        verifier = PluginVerifier()
        result = verifier.verify(invalid_checksum_package)

        assert result.status == VerificationStatus.INVALID
        assert result.level == VerificationLevel.STRUCTURE
        assert len(result.errors) > 0
        assert "checksum" in result.errors[0].lower()

    def test_verify_missing_package(self, tmp_path):
        """Test verification fails for missing package."""
        verifier = PluginVerifier()
        result = verifier.verify(tmp_path / "nonexistent.vspkg")

        assert result.status == VerificationStatus.INVALID
        assert "not found" in result.errors[0].lower()

    def test_verify_quick(self, valid_package):
        """Test quick verification."""
        verifier = PluginVerifier()
        assert verifier.verify_quick(valid_package) is True

    def test_verify_quick_invalid(self, invalid_structure_package):
        """Test quick verification returns False for invalid."""
        verifier = PluginVerifier()
        assert verifier.verify_quick(invalid_structure_package) is False

    def test_add_trusted_signer(self):
        """Test adding trusted signers."""
        verifier = PluginVerifier()
        verifier.add_trusted_signer("official-signer")
        verifier.add_trusted_signer("partner-signer", b"public_key_bytes")

        assert "official-signer" in verifier._trusted_signers
        assert "partner-signer" in verifier._trusted_signers

    def test_load_trusted_keys(self, tmp_path):
        """Test loading trusted keys from directory."""
        keys_dir = tmp_path / "keys"
        keys_dir.mkdir()

        # Create fake public key files
        (keys_dir / "signer1.pub").write_bytes(b"x" * 32)
        (keys_dir / "signer2.pub").write_bytes(b"y" * 32)

        verifier = PluginVerifier()
        count = verifier.load_trusted_keys(keys_dir)

        assert count == 2
        assert "signer1" in verifier._trusted_signers
        assert "signer2" in verifier._trusted_signers


@pytest.mark.skipif(not HAS_CRYPTOGRAPHY, reason="cryptography library required (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")
class TestPluginVerifierWithSigning:
    """Tests for verifier with signed packages."""

    @pytest.fixture
    def signed_package(self, tmp_path):
        """Create a signed .vspkg package."""
        from tools.plugin_packaging.signer import PluginSigner

        pkg_path = tmp_path / "signed.vspkg"

        # Create content with correct checksums
        manifest_content = json.dumps(
            {
                "id": "signed-plugin",
                "name": "Signed Plugin",
                "version": "1.0.0",
            }
        ).encode("utf-8")

        plugin_content = b"# Signed plugin code"

        manifest_hash = hashlib.sha256(manifest_content).hexdigest()
        plugin_hash = hashlib.sha256(plugin_content).hexdigest()

        checksums = f"{manifest_hash}  manifest.json\n{plugin_hash}  plugin.py\n"

        with zipfile.ZipFile(pkg_path, "w") as zf:
            zf.writestr(
                "MANIFEST.json",
                json.dumps(
                    {
                        "format_version": "1.0.0",
                        "package_id": "signed-plugin",
                        "package_version": "1.0.0",
                        "plugin_manifest": "manifest.json",
                        "created_at": "2026-02-17T00:00:00",
                        "created_by": "test",
                        "min_voicestudio_version": "1.0.0",
                    }
                ),
            )
            zf.writestr("manifest.json", manifest_content)
            zf.writestr("CHECKSUMS.sha256", checksums)
            zf.writestr("plugin.py", plugin_content)

        # Sign the package
        signer = PluginSigner(signer_id="test-signer")
        signer.generate_key_pair()
        signer.sign_package(pkg_path)

        return pkg_path

    def test_verify_signed_package_untrusted(self, signed_package):
        """Test signed package is verified but untrusted without trusted list."""
        verifier = PluginVerifier()
        result = verifier.verify(signed_package)

        assert result.status == VerificationStatus.UNTRUSTED
        assert result.level == VerificationLevel.SIGNATURE
        assert result.is_valid is True
        assert result.is_signed is True
        assert result.is_trusted is False

    def test_verify_signed_package_trusted(self, signed_package):
        """Test signed package is trusted when signer is in list."""
        verifier = PluginVerifier(trusted_signers=["test-signer"])
        result = verifier.verify(signed_package)

        assert result.status == VerificationStatus.VALID
        assert result.level == VerificationLevel.TRUSTED
        assert result.is_trusted is True


class TestVerifyPackageConvenience:
    """Tests for verify_package convenience function."""

    @pytest.fixture
    def package(self, tmp_path):
        """Create a valid package."""
        pkg_path = tmp_path / "conv-test.vspkg"

        manifest_content = json.dumps({"id": "conv-test"}).encode("utf-8")
        manifest_hash = hashlib.sha256(manifest_content).hexdigest()

        with zipfile.ZipFile(pkg_path, "w") as zf:
            zf.writestr(
                "MANIFEST.json",
                json.dumps(
                    {
                        "format_version": "1.0.0",
                        "package_id": "conv-test",
                        "package_version": "1.0.0",
                        "plugin_manifest": "manifest.json",
                        "created_at": "2026-02-17T00:00:00",
                        "created_by": "test",
                        "min_voicestudio_version": "1.0.0",
                    }
                ),
            )
            zf.writestr("manifest.json", manifest_content)
            zf.writestr("CHECKSUMS.sha256", f"{manifest_hash}  manifest.json\n")

        return pkg_path

    def test_verify_package_function(self, package):
        """Test the convenience function."""
        result = verify_package(package)

        assert result.is_valid is True

    def test_verify_package_string_path(self, package):
        """Test function accepts string paths."""
        result = verify_package(str(package))

        assert result.is_valid is True
