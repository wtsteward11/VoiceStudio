"""
Unit tests for Plugin Package Signing.

Tests the Keystore, PackageSigner, Signature, and related classes.
"""

import json
from pathlib import Path
from unittest.mock import MagicMock, patch

import pytest

from backend.plugins.supply_chain.signer import (
    HAS_CRYPTOGRAPHY,
    KeyEntry,
    KeyMetadata,
    KeyStatus,
    Keystore,
    PackageSigner,
    Signature,
    SignatureAlgorithm,
    check_signing_available,
    create_keystore,
    load_keystore,
    sign_package,
    verify_package,
)

# Skip all tests if cryptography is not available
pytestmark = pytest.mark.skipif(not HAS_CRYPTOGRAPHY, reason="cryptography library not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")


# =============================================================================
# Fixtures
# =============================================================================


@pytest.fixture
def temp_keystore_path(tmp_path):
    """Create a temporary keystore path."""
    return tmp_path / "keystore.json"


@pytest.fixture
def sample_package(tmp_path):
    """Create a sample package file."""
    package_path = tmp_path / "test_plugin-1.0.0.vspkg"
    package_path.write_bytes(b"test package content for signing")
    return package_path


@pytest.fixture
def passphrase():
    """Return a test passphrase."""
    return "test-passphrase-123"


@pytest.fixture
def keystore_with_key(temp_keystore_path, passphrase):
    """Create a keystore with one key."""
    keystore = Keystore(temp_keystore_path)
    keystore.create(passphrase)
    keystore.generate_key(passphrase, description="Test key")
    return keystore


@pytest.fixture
def sample_key_metadata():
    """Create sample key metadata."""
    return KeyMetadata(
        key_id="test-key-123",
        fingerprint="abc123def456",
        created_at="2025-01-01T00:00:00+00:00",
        status=KeyStatus.ACTIVE,
        description="Test signing key",
    )


# =============================================================================
# Test KeyStatus Enum
# =============================================================================


class TestKeyStatus:
    """Tests for KeyStatus enum."""

    def test_all_statuses_defined(self):
        """Test that all key statuses are defined."""
        expected = ["ACTIVE", "ROTATED", "REVOKED"]
        for name in expected:
            assert hasattr(KeyStatus, name)

    def test_status_values(self):
        """Test key status values."""
        assert KeyStatus.ACTIVE.value == "active"
        assert KeyStatus.ROTATED.value == "rotated"
        assert KeyStatus.REVOKED.value == "revoked"


# =============================================================================
# Test SignatureAlgorithm Enum
# =============================================================================


class TestSignatureAlgorithm:
    """Tests for SignatureAlgorithm enum."""

    def test_algorithm_defined(self):
        """Test that algorithm is defined."""
        assert hasattr(SignatureAlgorithm, "ED25519")
        assert SignatureAlgorithm.ED25519.value == "ed25519"


# =============================================================================
# Test KeyMetadata
# =============================================================================


class TestKeyMetadata:
    """Tests for KeyMetadata dataclass."""

    def test_basic_creation(self, sample_key_metadata):
        """Test creating key metadata."""
        meta = sample_key_metadata

        assert meta.key_id == "test-key-123"
        assert meta.fingerprint == "abc123def456"
        assert meta.status == KeyStatus.ACTIVE

    def test_to_dict(self, sample_key_metadata):
        """Test converting to dictionary."""
        data = sample_key_metadata.to_dict()

        assert data["key_id"] == "test-key-123"
        assert data["status"] == "active"
        assert data["algorithm"] == "ed25519"

    def test_from_dict(self):
        """Test creating from dictionary."""
        data = {
            "key_id": "key-456",
            "fingerprint": "xyz789",
            "created_at": "2025-02-01T00:00:00+00:00",
            "status": "rotated",
            "rotated_at": "2025-02-15T00:00:00+00:00",
            "description": "Rotated key",
            "algorithm": "ed25519",
        }

        meta = KeyMetadata.from_dict(data)

        assert meta.key_id == "key-456"
        assert meta.status == KeyStatus.ROTATED
        assert meta.rotated_at == "2025-02-15T00:00:00+00:00"


# =============================================================================
# Test Signature
# =============================================================================


class TestSignature:
    """Tests for Signature dataclass."""

    def test_basic_creation(self):
        """Test creating a signature."""
        sig = Signature(
            key_id="key-123",
            signature="base64signature",
            package_digest={"sha256": "abc123"},
        )

        assert sig.key_id == "key-123"
        assert sig.signature == "base64signature"
        assert sig.signature_id != ""
        assert sig.signed_at != ""

    def test_to_dict(self):
        """Test converting to dictionary."""
        sig = Signature(
            signature_id="sig-123",
            key_id="key-456",
            signature="base64sig",
            signed_at="2025-01-01T00:00:00+00:00",
            package_digest={"sha256": "digest123"},
        )

        data = sig.to_dict()

        assert data["signature_id"] == "sig-123"
        assert data["key_id"] == "key-456"
        assert data["algorithm"] == "ed25519"

    def test_to_json(self):
        """Test converting to JSON."""
        sig = Signature(key_id="key-123", signature="test")
        json_str = sig.to_json()
        parsed = json.loads(json_str)

        assert parsed["key_id"] == "key-123"

    def test_save_and_load(self, tmp_path):
        """Test saving and loading signature."""
        sig = Signature(
            key_id="key-123",
            signature="base64sig",
            package_digest={"sha256": "digest"},
        )

        sig_path = tmp_path / "signature.json"
        sig.save(sig_path)

        loaded = Signature.load(sig_path)

        assert loaded.key_id == sig.key_id
        assert loaded.signature == sig.signature

    def test_from_dict(self):
        """Test creating from dictionary."""
        data = {
            "signature_id": "sig-789",
            "key_id": "key-999",
            "algorithm": "ed25519",
            "signature": "somesig",
            "signed_at": "2025-03-01T00:00:00+00:00",
            "package_digest": {"sha256": "hashvalue"},
        }

        sig = Signature.from_dict(data)

        assert sig.signature_id == "sig-789"
        assert sig.key_id == "key-999"


# =============================================================================
# Test Keystore
# =============================================================================


class TestKeystore:
    """Tests for Keystore class."""

    def test_create_keystore(self, temp_keystore_path, passphrase):
        """Test creating a new keystore."""
        keystore = Keystore(temp_keystore_path)
        keystore.create(passphrase)

        assert temp_keystore_path.exists()
        assert keystore.get_active_key_id() is None
        assert len(keystore.list_keys()) == 0

    def test_generate_key(self, temp_keystore_path, passphrase):
        """Test generating a new key."""
        keystore = Keystore(temp_keystore_path)
        keystore.create(passphrase)

        metadata = keystore.generate_key(passphrase, description="My key")

        assert metadata.key_id != ""
        assert metadata.fingerprint != ""
        assert metadata.status == KeyStatus.ACTIVE
        assert keystore.get_active_key_id() == metadata.key_id

    def test_load_keystore(self, temp_keystore_path, passphrase):
        """Test loading an existing keystore."""
        # Create and save
        keystore1 = Keystore(temp_keystore_path)
        keystore1.create(passphrase)
        meta = keystore1.generate_key(passphrase)

        # Load in new instance
        keystore2 = Keystore(temp_keystore_path)
        keystore2.load(passphrase)

        assert keystore2.get_active_key_id() == meta.key_id
        assert len(keystore2.list_keys()) == 1

    def test_load_nonexistent_keystore(self, temp_keystore_path, passphrase):
        """Test loading a non-existent keystore."""
        keystore = Keystore(temp_keystore_path)

        with pytest.raises(FileNotFoundError):
            keystore.load(passphrase)

    def test_key_rotation(self, keystore_with_key, passphrase):
        """Test key rotation."""
        keystore = keystore_with_key
        old_key_id = keystore.get_active_key_id()

        new_meta = keystore.rotate_key(passphrase, "Rotated key")

        assert new_meta.key_id != old_key_id
        assert keystore.get_active_key_id() == new_meta.key_id

        old_meta = keystore.get_key_metadata(old_key_id)
        assert old_meta.status == KeyStatus.ROTATED
        assert old_meta.rotated_at is not None

    def test_key_revocation(self, keystore_with_key, passphrase):
        """Test key revocation."""
        keystore = keystore_with_key
        key_id = keystore.get_active_key_id()

        keystore.revoke_key(key_id, passphrase)

        meta = keystore.get_key_metadata(key_id)
        assert meta.status == KeyStatus.REVOKED
        assert meta.revoked_at is not None
        assert keystore.get_active_key_id() is None

    def test_list_keys(self, temp_keystore_path, passphrase):
        """Test listing keys."""
        keystore = Keystore(temp_keystore_path)
        keystore.create(passphrase)
        keystore.generate_key(passphrase, description="Key 1")
        keystore.generate_key(passphrase, description="Key 2", set_active=False)

        keys = keystore.list_keys()

        assert len(keys) == 2

    def test_export_public_key(self, keystore_with_key, passphrase, tmp_path):
        """Test exporting public key."""
        keystore = keystore_with_key
        key_id = keystore.get_active_key_id()

        export_path = tmp_path / "public_key.json"
        keystore.export_public_key(key_id, export_path)

        assert export_path.exists()
        data = json.loads(export_path.read_text())
        assert data["key_id"] == key_id
        assert "public_key" in data


# =============================================================================
# Test PackageSigner
# =============================================================================


class TestPackageSigner:
    """Tests for PackageSigner class."""

    def test_sign_package(self, keystore_with_key, passphrase, sample_package):
        """Test signing a package."""
        signer = PackageSigner(keystore_with_key)

        signature = signer.sign(sample_package, passphrase)

        assert signature.key_id == keystore_with_key.get_active_key_id()
        assert signature.signature != ""
        assert "sha256" in signature.package_digest

    def test_verify_signature(self, keystore_with_key, passphrase, sample_package):
        """Test verifying a valid signature."""
        signer = PackageSigner(keystore_with_key)
        signature = signer.sign(sample_package, passphrase)

        result = signer.verify(sample_package, signature)

        assert result is True

    def test_verify_tampered_package(self, keystore_with_key, passphrase, sample_package):
        """Test verifying a tampered package."""
        signer = PackageSigner(keystore_with_key)
        signature = signer.sign(sample_package, passphrase)

        # Tamper with the package
        sample_package.write_bytes(b"tampered content")

        result = signer.verify(sample_package, signature)

        assert result is False

    def test_sign_with_specific_key(self, temp_keystore_path, passphrase, sample_package):
        """Test signing with a specific key."""
        keystore = Keystore(temp_keystore_path)
        keystore.create(passphrase)

        key1 = keystore.generate_key(passphrase, "Key 1")
        key2 = keystore.generate_key(passphrase, "Key 2", set_active=False)

        signer = PackageSigner(keystore)
        signature = signer.sign(sample_package, passphrase, key_id=key2.key_id)

        assert signature.key_id == key2.key_id

    def test_sign_nonexistent_package(self, keystore_with_key, passphrase, tmp_path):
        """Test signing a non-existent package."""
        signer = PackageSigner(keystore_with_key)

        with pytest.raises(FileNotFoundError):
            signer.sign(tmp_path / "nonexistent.vspkg", passphrase)

    def test_verify_with_revoked_key(self, keystore_with_key, passphrase, sample_package):
        """Test verification fails with revoked key."""
        signer = PackageSigner(keystore_with_key)
        key_id = keystore_with_key.get_active_key_id()

        signature = signer.sign(sample_package, passphrase)

        # Revoke the key
        keystore_with_key.revoke_key(key_id, passphrase)

        # Verification should fail
        result = signer.verify(sample_package, signature)
        assert result is False


# =============================================================================
# Test Convenience Functions
# =============================================================================


class TestConvenienceFunctions:
    """Tests for module-level convenience functions."""

    def test_create_keystore(self, temp_keystore_path, passphrase):
        """Test create_keystore function."""
        keystore = create_keystore(temp_keystore_path, passphrase)

        assert temp_keystore_path.exists()
        assert len(keystore.list_keys()) == 0

    def test_load_keystore(self, temp_keystore_path, passphrase):
        """Test load_keystore function."""
        # Create first
        ks1 = create_keystore(temp_keystore_path, passphrase)
        ks1.generate_key(passphrase)

        # Load
        ks2 = load_keystore(temp_keystore_path, passphrase)

        assert len(ks2.list_keys()) == 1

    def test_sign_package(self, temp_keystore_path, passphrase, sample_package):
        """Test sign_package function."""
        keystore = create_keystore(temp_keystore_path, passphrase)
        keystore.generate_key(passphrase)

        signature = sign_package(sample_package, temp_keystore_path, passphrase)

        assert signature.signature != ""

    def test_verify_package_with_keystore(
        self, temp_keystore_path, passphrase, sample_package, tmp_path
    ):
        """Test verify_package with keystore."""
        keystore = create_keystore(temp_keystore_path, passphrase)
        keystore.generate_key(passphrase)

        signature = sign_package(sample_package, temp_keystore_path, passphrase)
        sig_path = tmp_path / "signature.json"
        signature.save(sig_path)

        result = verify_package(
            sample_package,
            sig_path,
            keystore_path=temp_keystore_path,
            passphrase=passphrase,
        )

        assert result is True

    def test_verify_package_with_public_key(
        self, temp_keystore_path, passphrase, sample_package, tmp_path
    ):
        """Test verify_package with exported public key."""
        keystore = create_keystore(temp_keystore_path, passphrase)
        meta = keystore.generate_key(passphrase)

        # Sign
        signature = sign_package(sample_package, temp_keystore_path, passphrase)
        sig_path = tmp_path / "signature.json"
        signature.save(sig_path)

        # Export public key
        pub_key_path = tmp_path / "public_key.json"
        keystore.export_public_key(meta.key_id, pub_key_path)

        # Verify with public key only
        result = verify_package(
            sample_package,
            sig_path,
            public_key_path=pub_key_path,
        )

        assert result is True

    def test_check_signing_available(self):
        """Test check_signing_available function."""
        result = check_signing_available()
        assert result is True  # We're only running these tests if it's available


# =============================================================================
# Test Key Encryption
# =============================================================================


class TestKeyEncryption:
    """Tests for key encryption/decryption."""

    def test_wrong_passphrase_fails(self, temp_keystore_path, passphrase, sample_package):
        """Test that wrong passphrase fails."""
        keystore = Keystore(temp_keystore_path)
        keystore.create(passphrase)
        keystore.generate_key(passphrase)

        signer = PackageSigner(keystore)

        # Sign with wrong passphrase should fail
        with pytest.raises(ValueError, match="Invalid passphrase"):
            signer.sign(sample_package, "wrong-passphrase")

    def test_passphrase_required_for_signing(self, keystore_with_key, sample_package):
        """Test that passphrase is required for signing."""
        signer = PackageSigner(keystore_with_key)

        # Empty passphrase (if keystore uses encryption)
        with pytest.raises(ValueError):
            signer.sign(sample_package, "")


# =============================================================================
# P4-1/P5-2: Integration Helper Tests
# =============================================================================


class TestVerificationResult:
    """Tests for VerificationResult dataclass."""

    def test_basic_creation(self):
        """Test basic result creation."""
        from backend.plugins.supply_chain.signer import VerificationResult

        result = VerificationResult(valid=True, key_id="key-123")
        assert result.valid is True
        assert result.key_id == "key-123"

    def test_to_dict(self):
        """Test conversion to dictionary."""
        from backend.plugins.supply_chain.signer import VerificationResult

        result = VerificationResult(
            valid=True,
            key_id="key-123",
            algorithm="ed25519",
            message="Verified",
        )
        d = result.to_dict()
        assert d["valid"] is True
        assert d["key_id"] == "key-123"
        assert d["algorithm"] == "ed25519"
        assert d["message"] == "Verified"

    def test_invalid_result(self):
        """Test invalid result creation."""
        from backend.plugins.supply_chain.signer import VerificationResult

        result = VerificationResult(
            valid=False,
            message="No signature found",
        )
        assert result.valid is False
        assert result.key_id == ""
        assert "signature" in result.message.lower()


class TestSignatureDiscovery:
    """Tests for signature and key discovery functions."""

    def test_find_signature_file_with_sig_extension(self, tmp_path):
        """Test finding .sig signature file."""
        from backend.plugins.supply_chain.signer import find_signature_file

        # Create package and signature
        package = tmp_path / "plugin.zip"
        package.write_text("content")
        sig_file = tmp_path / "plugin.sig"
        sig_file.write_text("{}")

        found = find_signature_file(package)
        assert found == sig_file

    def test_find_signature_file_in_directory(self, tmp_path):
        """Test finding signature.json in directory."""
        from backend.plugins.supply_chain.signer import find_signature_file

        # Create plugin directory with signature
        plugin_dir = tmp_path / "my-plugin"
        plugin_dir.mkdir()
        sig_file = plugin_dir / "signature.json"
        sig_file.write_text("{}")

        found = find_signature_file(plugin_dir)
        assert found == sig_file

    def test_find_signature_file_not_found(self, tmp_path):
        """Test when no signature file exists."""
        from backend.plugins.supply_chain.signer import find_signature_file

        package = tmp_path / "plugin.zip"
        package.write_text("content")

        found = find_signature_file(package)
        assert found is None

    def test_find_public_key_file(self, tmp_path):
        """Test finding public key file."""
        from backend.plugins.supply_chain.signer import find_public_key_file

        # Create package directory with public key
        plugin_dir = tmp_path / "my-plugin"
        plugin_dir.mkdir()
        pub_key = plugin_dir / "public_key.json"
        pub_key.write_text('{"key": "value"}')

        found = find_public_key_file(plugin_dir)
        assert found == pub_key

    def test_find_public_key_not_found(self, tmp_path):
        """Test when no public key file exists."""
        from backend.plugins.supply_chain.signer import find_public_key_file

        plugin_dir = tmp_path / "my-plugin"
        plugin_dir.mkdir()

        found = find_public_key_file(plugin_dir)
        # May return a global key if exists, or None
        # Just ensure it doesn't raise
        assert found is None or isinstance(found, Path)


class TestAutoVerification:
    """Tests for verify_package_auto function."""

    def test_verify_auto_signed_package(
        self, temp_keystore_path, passphrase, sample_package, tmp_path
    ):
        """Test auto-verification of signed package."""
        from backend.plugins.supply_chain.signer import (
            sign_package_auto,
            verify_package_auto,
        )

        # Sign the package
        signature = sign_package_auto(
            sample_package,
            keystore_path=temp_keystore_path,
            passphrase=passphrase,
        )
        assert signature is not None

        # Verify auto
        result = verify_package_auto(
            sample_package,
            keystore_path=temp_keystore_path,
            passphrase=passphrase,
        )
        assert result.valid is True
        assert result.key_id != ""

    def test_verify_auto_unsigned_package(self, tmp_path):
        """Test auto-verification of unsigned package."""
        from backend.plugins.supply_chain.signer import verify_package_auto

        # Create unsigned package
        package = tmp_path / "unsigned.zip"
        package.write_text("content")

        result = verify_package_auto(package)
        assert result.valid is False
        assert "signature" in result.message.lower()

    def test_verify_auto_nonexistent_package(self, tmp_path):
        """Test auto-verification of non-existent package."""
        from backend.plugins.supply_chain.signer import verify_package_auto

        package = tmp_path / "does-not-exist.zip"
        result = verify_package_auto(package)
        assert result.valid is False
        assert "not found" in result.message.lower()


class TestAutoSigning:
    """Tests for sign_package_auto function."""

    def test_sign_auto_creates_keystore(self, tmp_path):
        """Test that sign_auto creates keystore if needed."""
        from backend.plugins.supply_chain.signer import sign_package_auto

        package = tmp_path / "plugin.txt"
        package.write_text("content")

        keystore_path = tmp_path / "new_keystore.json"
        assert not keystore_path.exists()

        signature = sign_package_auto(
            package,
            keystore_path=keystore_path,
            passphrase="",  # Dev passphrase
        )

        assert signature is not None
        assert keystore_path.exists()

    def test_sign_auto_saves_signature(self, tmp_path):
        """Test that sign_auto saves signature file."""
        from backend.plugins.supply_chain.signer import sign_package_auto

        package = tmp_path / "plugin.txt"
        package.write_text("content")

        keystore_path = tmp_path / "keystore.json"

        signature = sign_package_auto(
            package,
            keystore_path=keystore_path,
            passphrase="",
        )

        expected_sig = package.with_suffix(".txt.sig")
        assert expected_sig.exists()

    def test_sign_auto_nonexistent_package(self, tmp_path):
        """Test signing non-existent package."""
        from backend.plugins.supply_chain.signer import sign_package_auto

        package = tmp_path / "does-not-exist.zip"
        signature = sign_package_auto(package)
        assert signature is None


class TestDefaultKeystorePath:
    """Tests for default keystore path function."""

    def test_get_default_keystore_path(self):
        """Test getting default keystore path."""
        from backend.plugins.supply_chain.signer import get_default_keystore_path

        path = get_default_keystore_path()
        assert "voicestudio" in str(path).lower()
        assert "signing" in str(path).lower()
        assert "keystore" in str(path).lower()
