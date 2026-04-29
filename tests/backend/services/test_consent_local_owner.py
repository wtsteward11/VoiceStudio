"""
Regression test for: check_consent_required must return False for locally-owned
voice profiles (owner_user_id=None or "local").

Root cause fixed: voice_helpers.check_consent_required and
api.dependencies._profile_has_remote_owner were treating profiles with
owner_user_id=None or "local" as requiring third-party consent, blocking ALL
synthesis in a local single-user install.

Commit: see git log for fix to backend/services/voice_helpers.py and
backend/api/dependencies.py.
"""
from __future__ import annotations

from unittest.mock import MagicMock, patch


def _make_store(owner_user_id: str | None):
    """Return a mock profile store whose .get() returns a profile with the given owner."""
    store = MagicMock()
    store.get.return_value = {"id": "test-profile", "owner_user_id": owner_user_id}
    return store


class TestCheckConsentRequired:
    def test_no_owner_does_not_require_consent(self):
        """Profile with owner_user_id=None (locally created) must not require consent."""
        from backend.services.voice_helpers import check_consent_required

        with patch(
            "backend.project.management.profile_store.get_profile_store",
            return_value=_make_store(None),
        ):
            assert check_consent_required("test-profile") is False

    def test_local_sentinel_does_not_require_consent(self):
        """Profile with owner_user_id='local' (local mode default) must not require consent."""
        from backend.services.voice_helpers import check_consent_required

        with patch(
            "backend.project.management.profile_store.get_profile_store",
            return_value=_make_store("local"),
        ):
            assert check_consent_required("test-profile") is False

    def test_system_sentinel_does_not_require_consent(self):
        """Profile with owner_user_id='system' must not require consent."""
        from backend.services.voice_helpers import check_consent_required

        with patch(
            "backend.project.management.profile_store.get_profile_store",
            return_value=_make_store("system"),
        ):
            assert check_consent_required("test-profile") is False

    def test_current_user_owns_profile_does_not_require_consent(self):
        """Profile owned by the requesting user must not require consent."""
        from backend.services.voice_helpers import check_consent_required

        request = MagicMock()
        request.headers = {"X-User-ID": "user-abc"}

        with patch(
            "backend.project.management.profile_store.get_profile_store",
            return_value=_make_store("user-abc"),
        ):
            assert check_consent_required("test-profile", request) is False

    def test_remote_owner_requires_consent(self):
        """Profile owned by a different (non-local) user must require consent."""
        from backend.services.voice_helpers import check_consent_required

        with patch(
            "backend.project.management.profile_store.get_profile_store",
            return_value=_make_store("remote-user-xyz"),
        ):
            assert check_consent_required("test-profile") is True

    def test_missing_profile_requires_consent(self):
        """Missing/unknown profile must default to requiring consent (fail-safe)."""
        from backend.services.voice_helpers import check_consent_required

        store = MagicMock()
        store.get.return_value = None
        with patch(
            "backend.project.management.profile_store.get_profile_store",
            return_value=store,
        ):
            assert check_consent_required("unknown-profile") is True


class TestProfileHasRemoteOwner:
    def test_no_owner_is_not_remote(self):
        """Profile with no owner is treated as local (not remote)."""
        from backend.api.dependencies import _profile_has_remote_owner

        with patch(
            "backend.project.management.profile_store.get_profile_store",
            return_value=_make_store(None),
        ):
            assert _profile_has_remote_owner("test-profile") is False

    def test_local_sentinel_is_not_remote(self):
        """Profile with owner_user_id='local' is treated as local (not remote)."""
        from backend.api.dependencies import _profile_has_remote_owner

        with patch(
            "backend.project.management.profile_store.get_profile_store",
            return_value=_make_store("local"),
        ):
            assert _profile_has_remote_owner("test-profile") is False

    def test_named_user_is_remote(self):
        """Profile with an explicit non-local owner is treated as remote."""
        from backend.api.dependencies import _profile_has_remote_owner

        with patch(
            "backend.project.management.profile_store.get_profile_store",
            return_value=_make_store("real-remote-user"),
        ):
            assert _profile_has_remote_owner("test-profile") is True
