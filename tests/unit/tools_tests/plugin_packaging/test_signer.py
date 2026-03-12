"""Tests for the plugin signer module."""

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

from tools.plugin_packaging.signer import (
    SIGNATURE_VERSION,
    PluginSigner,
    SignatureInfo,
    SignResult,
)


class TestSignatureInfo:
    """Tests for SignatureInfo dataclass."""

    def test_from_dict(self):
        """Test creating from dictionary."""
        data = {
            "version": "1.0.0",
            "algorithm": "ed25519",
            "signed_at": "2026-02-17T00:00:00",
            "signer_id": "test-signer",
            "public_key": "base64key",
            "signature": "base64sig",
            "checksums_hash": "abc123",
            "files_hash": "def456",
        }

        info = SignatureInfo.from_dict(data)

        assert info.version == "1.0.0"
        assert info.algorithm == "ed25519"
        assert info.signer_id == "test-signer"
        assert info.public_key == "base64key"

    def test_to_dict_roundtrip(self):
        """Test converting to dict and back."""
        original = SignatureInfo(
            version="1.0.0",
            algorithm="ed25519",
            signed_at="2026-02-17T12:00:00",
            signer_id="roundtrip-test",
            public_key="pk_base64",
            signature="sig_base64",
            checksums_hash="hash1",
            files_hash="hash2",
        )

        data = original.to_dict()
        restored = SignatureInfo.from_dict(data)

        assert restored.signer_id == original.signer_id
        assert restored.signature == original.signature

    def test_to_json(self):
        """Test JSON serialization."""
        info = SignatureInfo(
            version="1.0.0",
            algorithm="ed25519",
            signed_at="2026-02-17T00:00:00",
            signer_id="json-test",
            public_key="pk",
            signature="sig",
            checksums_hash="h1",
            files_hash="h2",
        )

        json_str = info.to_json()
        parsed = json.loads(json_str)

        assert parsed["signer_id"] == "json-test"


@pytest.mark.skipif(not HAS_CRYPTOGRAPHY, reason="cryptography library required (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")
class TestPluginSigner:
    """Tests for PluginSigner class."""

    @pytest.fixture
    def signer(self):
        """Create a signer with generated keys."""
        signer = PluginSigner(signer_id="test-signer")
        signer.generate_key_pair()
        return signer

    @pytest.fixture
    def unsigned_package(self, tmp_path):
        """Create an unsigned .vspkg package."""
        pkg_path = tmp_path / "unsigned.vspkg"

        with zipfile.ZipFile(pkg_path, "w") as zf:
            zf.writestr(
                "MANIFEST.json",
                json.dumps(
                    {
                        "format_version": "1.0.0",
                        "package_id": "unsigned-pkg",
                        "package_version": "1.0.0",
                        "plugin_manifest": "manifest.json",
                        "created_at": "2026-02-17T00:00:00",
                        "created_by": "test",
                        "min_voicestudio_version": "1.0.0",
                    }
                ),
            )
            zf.writestr(
                "manifest.json",
                json.dumps(
                    {
                        "id": "unsigned-pkg",
                        "name": "Unsigned Package",
                        "version": "1.0.0",
                    }
                ),
            )
            zf.writestr("CHECKSUMS.sha256", "abc123  manifest.json\n")
            zf.writestr("plugin.py", "# Plugin code")

        return pkg_path

    def test_generate_key_pair(self):
        """Test key pair generation."""
        signer = PluginSigner()
        result = signer.generate_key_pair()

        assert result is not None
        private_key, public_key = result
        assert len(private_key) == 32
        assert len(public_key) == 32

    def test_sign_package(self, signer, unsigned_package):
        """Test signing a package."""
        result = signer.sign_package(unsigned_package)

        assert result.success is True
        assert result.signature_info is not None
        assert result.signature_info.signer_id == "test-signer"
        assert result.signature_info.algorithm == "ed25519"

    def test_signed_package_contains_signature(self, signer, unsigned_package):
        """Test signed package contains SIGNATURE.json."""
        signer.sign_package(unsigned_package)

        with zipfile.ZipFile(unsigned_package, "r") as zf:
            assert "SIGNATURE.json" in zf.namelist()
            sig_data = json.loads(zf.read("SIGNATURE.json"))
            assert sig_data["signer_id"] == "test-signer"

    def test_sign_without_keys(self, unsigned_package):
        """Test signing fails without loaded keys."""
        signer = PluginSigner()
        result = signer.sign_package(unsigned_package)

        assert result.success is False
        assert "keys" in result.error.lower()

    def test_sign_missing_package(self, signer, tmp_path):
        """Test signing fails for missing package."""
        result = signer.sign_package(tmp_path / "nonexistent.vspkg")

        assert result.success is False
        assert "not found" in result.error.lower()

    def test_sign_package_missing_checksums(self, signer, tmp_path):
        """Test signing fails without CHECKSUMS.sha256."""
        pkg_path = tmp_path / "no-checksums.vspkg"

        with zipfile.ZipFile(pkg_path, "w") as zf:
            zf.writestr("MANIFEST.json", "{}")
            zf.writestr("manifest.json", "{}")

        result = signer.sign_package(pkg_path)

        assert result.success is False
        assert "CHECKSUMS" in result.error

    def test_save_and_load_keys(self, tmp_path):
        """Test saving and loading key pairs."""
        # Generate keys
        signer1 = PluginSigner()
        keys = signer1.generate_key_pair()
        assert keys is not None

        # Save keys
        priv_path = tmp_path / "private.key"
        pub_path = tmp_path / "public.key"
        assert signer1.save_keys(priv_path, pub_path, format="raw") is True

        # Load keys in new signer
        signer2 = PluginSigner()
        assert signer2.load_key_pair(priv_path, pub_path) is True

    def test_save_keys_pem_format(self, tmp_path):
        """Test saving keys in PEM format."""
        signer = PluginSigner()
        signer.generate_key_pair()

        priv_path = tmp_path / "private.pem"
        pub_path = tmp_path / "public.pem"

        assert signer.save_keys(priv_path, pub_path, format="pem") is True
        assert priv_path.exists()
        assert pub_path.exists()

        # PEM files should start with -----BEGIN
        priv_content = priv_path.read_text()
        assert "-----BEGIN" in priv_content


class TestSignResult:
    """Tests for SignResult dataclass."""

    def test_success_result(self, tmp_path):
        """Test creating success result."""
        sig_info = SignatureInfo(
            version="1.0.0",
            algorithm="ed25519",
            signed_at="2026-02-17T00:00:00",
            signer_id="test",
            public_key="pk",
            signature="sig",
            checksums_hash="h1",
            files_hash="h2",
        )

        result = SignResult(
            success=True,
            signature_info=sig_info,
            package_path=tmp_path / "signed.vspkg",
        )

        assert result.success is True
        assert result.error is None

    def test_failure_result(self):
        """Test creating failure result."""
        result = SignResult(
            success=False,
            error="Signing failed",
        )

        assert result.success is False
        assert result.error == "Signing failed"


class TestSignatureConstants:
    """Tests for signature module constants."""

    def test_signature_version(self):
        """Test signature version is set."""
        assert SIGNATURE_VERSION == "1.0.0"
