"""
Profile search service for global search and quality/voice routes.

Provides dict-like profile access for search without route-to-route imports.
"""

from __future__ import annotations

from typing import Any


class _DictToObject:
    """Wrapper to make dict accessible via attribute access (for legacy code)."""

    def __init__(self, data: dict) -> None:
        object.__setattr__(self, "_data", data or {})

    def __getattr__(self, name: str) -> Any:
        data = object.__getattribute__(self, "_data")
        return data.get(name)

    def get(self, key: str, default: Any = None) -> Any:
        data = object.__getattribute__(self, "_data")
        return data.get(key, default)

    def __getitem__(self, key: str) -> Any:
        data = object.__getattribute__(self, "_data")
        return data[key]

    def __contains__(self, key: object) -> bool:
        data = object.__getattribute__(self, "_data")
        return key in data

    def __repr__(self) -> str:
        data = object.__getattribute__(self, "_data")
        return f"_DictToObject({data})"


def get_profiles_for_search() -> dict[str, Any]:
    """
    Get profiles as a dict-like structure for search iteration.

    Returns {profile_id: {name, description, tags, language}} for search.
    """
    from backend.project.management.profile_store import get_profile_store

    store = get_profile_store()
    result: dict[str, Any] = {}
    for profile in store.list_profiles(limit=10000):
        pid = profile.get("id", "")
        if pid:
            full = store.get(pid)
            if full:
                result[pid] = {
                    "name": full.get("name", ""),
                    "description": full.get("description", ""),
                    "tags": full.get("tags", []),
                    "language": full.get("language", "en"),
                }
            else:
                result[pid] = {
                    "name": profile.get("name", ""),
                    "description": "",
                    "tags": profile.get("tags", []),
                    "language": profile.get("language", "en"),
                }
    return result


class _ProfilesProxy:
    """Dict-like proxy for profile lookup (quality/voice routes)."""

    def __init__(self) -> None:
        self._store = None

    @property
    def _profile_store(self):
        if self._store is None:
            from backend.project.management.profile_store import get_profile_store

            self._store = get_profile_store()
        return self._store

    def _wrap(self, data: dict[str, Any] | None) -> _DictToObject | None:
        """Wrap dict in object for attribute access."""
        if data is None:
            return None
        return _DictToObject(data)

    def get(self, profile_id: str, default: Any = None) -> _DictToObject | None:
        result = self._profile_store.get(profile_id)
        if result is None:
            return default
        return self._wrap(result)

    def __getitem__(self, profile_id: str) -> _DictToObject:
        result = self._profile_store.get(profile_id)
        if result is None:
            raise KeyError(profile_id)
        return self._wrap(result)

    def __setitem__(self, profile_id: str, profile: object) -> None:
        if isinstance(profile, dict):
            data = dict(profile)
        elif hasattr(profile, "model_dump"):
            data = profile.model_dump()
        elif hasattr(profile, "__dict__"):
            data = dict(profile.__dict__)
        else:
            data = {}
        data["id"] = profile_id
        self._profile_store.save(data)

    def __contains__(self, profile_id: object) -> bool:
        return self._profile_store.get(str(profile_id)) is not None

    def __iter__(self):
        return iter(self._profile_store.list_ids())

    def keys(self):
        return self._profile_store.list_ids()

    def values(self):
        return [self._wrap(p) for p in self._profile_store.list_profiles()]

    def items(self):
        for pid in self._profile_store.list_ids():
            yield pid, self._wrap(self._profile_store.get(pid))


# Singleton for quality/voice routes
_profiles_proxy: _ProfilesProxy | None = None


def get_profiles_proxy() -> _ProfilesProxy:
    """Get profiles proxy for quality/voice routes (replaces _profiles import)."""
    global _profiles_proxy
    if _profiles_proxy is None:
        _profiles_proxy = _ProfilesProxy()
    return _profiles_proxy


_profile_timestamps_store = None


def get_profile_timestamps_store():
    """Get profile timestamps PersistentStore (replaces _profile_timestamps import)."""
    global _profile_timestamps_store
    if _profile_timestamps_store is None:
        from backend.services.persistent_store import PersistentStore

        _profile_timestamps_store = PersistentStore("profile_timestamps")
    return _profile_timestamps_store
