"""
Tests for Phase 6A Wasm Capability Tokens

Tests capability-based security for Wasm plugin sandboxing.
"""

import pytest

# Skip entire module if capability_tokens module not fully implemented
pytest.importorskip(
    "backend.plugins.wasm.capability_tokens", reason="Phase 6A not fully implemented"
)

try:
    from backend.plugins.wasm.capability_tokens import (
        Capability,
        CapabilitySet,
        CapabilityToken,
        parse_capabilities_from_manifest,
    )
except ImportError:
    # Define stubs for type checking
    Capability = None
    CapabilitySet = None
    CapabilityToken = None
    parse_capabilities_from_manifest = None
    pytestmark = pytest.mark.skipif(True, reason="Phase 6A capability_tokens not implemented (SKIP_DEBT_CLEANUP_SUBPLAN § Product gaps)")


class TestCapabilityToken:
    """Tests for CapabilityToken class."""

    def test_create_capability_token(self) -> None:
        """Test creating a capability token."""
        token = CapabilityToken(Capability.FILE_READ)
        assert token.capability == Capability.FILE_READ
        assert token.is_valid

    def test_capability_token_expiry(self) -> None:
        """Test that expired tokens are invalid."""
        import time

        token = CapabilityToken(Capability.AUDIO_READ, ttl_seconds=0.001)
        time.sleep(0.01)
        assert not token.is_valid

    def test_capability_token_revocation(self) -> None:
        """Test revoking a capability token."""
        token = CapabilityToken(Capability.NETWORK_CONNECT)
        assert token.is_valid
        token.revoke()
        assert not token.is_valid

    def test_capability_from_string(self) -> None:
        """Test parsing capability from string."""
        token = CapabilityToken.from_string("file_read")
        assert token is not None
        assert token.capability == Capability.FILE_READ

    def test_invalid_capability_string(self) -> None:
        """Test that invalid strings return None."""
        token = CapabilityToken.from_string("invalid_capability")
        assert token is None


class TestCapabilitySet:
    """Tests for CapabilitySet class."""

    def test_create_empty_set(self) -> None:
        """Test creating an empty capability set."""
        cap_set = CapabilitySet()
        assert len(cap_set) == 0
        assert not cap_set.has(Capability.FILE_READ)

    def test_add_capability(self) -> None:
        """Test adding a capability."""
        cap_set = CapabilitySet()
        cap_set.add(Capability.FILE_READ)
        assert cap_set.has(Capability.FILE_READ)
        assert len(cap_set) == 1

    def test_remove_capability(self) -> None:
        """Test removing a capability."""
        cap_set = CapabilitySet()
        cap_set.add(Capability.AUDIO_READ)
        cap_set.remove(Capability.AUDIO_READ)
        assert not cap_set.has(Capability.AUDIO_READ)

    def test_capability_set_intersection(self) -> None:
        """Test capability set intersection."""
        set1 = CapabilitySet()
        set1.add(Capability.FILE_READ)
        set1.add(Capability.AUDIO_READ)

        set2 = CapabilitySet()
        set2.add(Capability.AUDIO_READ)
        set2.add(Capability.NETWORK_CONNECT)

        intersection = set1.intersection(set2)
        assert intersection.has(Capability.AUDIO_READ)
        assert not intersection.has(Capability.FILE_READ)
        assert not intersection.has(Capability.NETWORK_CONNECT)

    def test_capability_set_subset(self) -> None:
        """Test capability set subset check."""
        allowed = CapabilitySet()
        allowed.add(Capability.FILE_READ)
        allowed.add(Capability.AUDIO_READ)

        requested = CapabilitySet()
        requested.add(Capability.FILE_READ)

        assert requested.is_subset_of(allowed)

        requested.add(Capability.NETWORK_CONNECT)
        assert not requested.is_subset_of(allowed)


class TestParseCapabilities:
    """Tests for manifest capability parsing."""

    def test_parse_valid_permissions(self) -> None:
        """Test parsing valid permission strings."""
        permissions = ["file_read", "audio_read", "audio_write"]
        cap_set = parse_capabilities_from_manifest(permissions)

        assert cap_set.has(Capability.FILE_READ)
        assert cap_set.has(Capability.AUDIO_READ)
        assert cap_set.has(Capability.AUDIO_WRITE)

    def test_parse_ignores_invalid(self) -> None:
        """Test that invalid permissions are ignored."""
        permissions = ["file_read", "invalid_perm", "audio_read"]
        cap_set = parse_capabilities_from_manifest(permissions)

        assert cap_set.has(Capability.FILE_READ)
        assert cap_set.has(Capability.AUDIO_READ)
        assert len(cap_set) == 2

    def test_parse_empty_list(self) -> None:
        """Test parsing empty permission list."""
        cap_set = parse_capabilities_from_manifest([])
        assert len(cap_set) == 0


class TestCapabilitySecurityBoundaries:
    """Security-focused tests for capability system."""

    def test_capability_cannot_escalate(self) -> None:
        """Test that capabilities cannot escalate privileges."""
        cap_set = CapabilitySet()
        cap_set.add(Capability.FILE_READ)

        # Should not be able to write without explicit capability
        assert not cap_set.has(Capability.FILE_WRITE)

    def test_revoked_token_cannot_be_reused(self) -> None:
        """Test that revoked tokens stay revoked."""
        token = CapabilityToken(Capability.NETWORK_CONNECT)
        token.revoke()

        # Attempting to use revoked token should fail
        assert not token.is_valid
        # Token state should not change
        assert not token.is_valid

    def test_expired_token_stays_expired(self) -> None:
        """Test that expired tokens cannot be renewed."""
        import time

        token = CapabilityToken(Capability.FILE_WRITE, ttl_seconds=0.001)
        time.sleep(0.01)

        assert not token.is_valid
        # Token should not auto-renew
        assert not token.is_valid
