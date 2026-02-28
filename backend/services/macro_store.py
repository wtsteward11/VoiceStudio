"""
Persistent Macro Store for VoiceStudio (GAP-BE-001)

Migrates macros, automation curves, and execution state from in-memory
storage to durable disk-backed JsonFileStore. Data persists across restarts.
"""

from __future__ import annotations

import logging
from typing import Any

from .json_file_store import JsonFileStore

logger = logging.getLogger(__name__)


class MacroStore:
    """
    Disk-backed macro storage.

    Uses JsonFileStore for persistent storage with in-memory caching.

    Storage layout:
        data/stores/macros/{macro_id}.json
    """

    def __init__(self, max_macros: int = 2000):
        self._store = JsonFileStore("macros", max_items=max_macros)

    def get(self, macro_id: str) -> dict[str, Any] | None:
        """Get a macro by ID."""
        return self._store.get(macro_id)

    def save(self, macro: dict[str, Any]) -> str:
        """
        Save a macro.

        Args:
            macro: Macro dictionary (must include 'id').

        Returns:
            Macro ID.
        """
        macro_id = macro.get("id", "")
        if not macro_id:
            import uuid

            macro_id = f"macro-{uuid.uuid4().hex[:8]}"
            macro["id"] = macro_id

        self._store.put(macro_id, macro)
        logger.debug(f"MacroStore: Saved macro {macro_id}")
        return macro_id

    def delete(self, macro_id: str) -> bool:
        """Delete a macro."""
        result = self._store.delete(macro_id)
        if result:
            logger.debug(f"MacroStore: Deleted macro {macro_id}")
        return result

    def list_all(self) -> list[dict[str, Any]]:
        """List all macros."""
        return self._store.list()

    def search(self, name: str | None = None, category: str | None = None) -> list[dict[str, Any]]:
        """Search macros by name or category."""

        def predicate(macro: dict[str, Any]) -> bool:
            if name:
                macro_name = macro.get("name", "").lower()
                if name.lower() not in macro_name:
                    return False
            if category:
                macro_category = macro.get("category", "").lower()
                if category.lower() != macro_category:
                    return False
            return True

        return self._store.search(predicate)

    def count(self) -> int:
        """Get total number of macros."""
        return self._store.count()

    def exists(self, macro_id: str) -> bool:
        """Check if a macro exists."""
        return self._store.exists(macro_id)


class AutomationCurveStore:
    """
    Disk-backed automation curve storage.

    Uses JsonFileStore for persistent storage with in-memory caching.

    Storage layout:
        data/stores/automation_curves/{curve_id}.json
    """

    def __init__(self, max_curves: int = 5000):
        self._store = JsonFileStore("automation_curves", max_items=max_curves)
        self._project_index: dict[str, list[str]] = {}
        self._rebuild_project_index()

    def _rebuild_project_index(self) -> None:
        """Rebuild the project-to-curve index from stored data."""
        self._project_index.clear()
        for curve in self._store.list():
            project_id = curve.get("project_id", "")
            if project_id:
                if project_id not in self._project_index:
                    self._project_index[project_id] = []
                curve_id = curve.get("id", "")
                if curve_id and curve_id not in self._project_index[project_id]:
                    self._project_index[project_id].append(curve_id)
        logger.info(
            f"AutomationCurveStore: Rebuilt index with {self._store.count()} curves "
            f"across {len(self._project_index)} projects"
        )

    def get(self, curve_id: str) -> dict[str, Any] | None:
        """Get an automation curve by ID."""
        return self._store.get(curve_id)

    def save(self, curve: dict[str, Any]) -> str:
        """
        Save an automation curve.

        Args:
            curve: Curve dictionary (must include 'id').

        Returns:
            Curve ID.
        """
        curve_id = curve.get("id", "")
        if not curve_id:
            import uuid

            curve_id = f"curve-{uuid.uuid4().hex[:8]}"
            curve["id"] = curve_id

        project_id = curve.get("project_id", "")

        # Save to store
        self._store.put(curve_id, curve)

        # Update project index
        if project_id:
            if project_id not in self._project_index:
                self._project_index[project_id] = []
            if curve_id not in self._project_index[project_id]:
                self._project_index[project_id].append(curve_id)

        logger.debug(f"AutomationCurveStore: Saved curve {curve_id}")
        return curve_id

    def delete(self, curve_id: str) -> bool:
        """Delete an automation curve."""
        curve = self._store.get(curve_id)
        if not curve:
            return False

        project_id = curve.get("project_id", "")

        # Remove from store
        result = self._store.delete(curve_id)

        # Update project index
        if project_id and project_id in self._project_index:
            if curve_id in self._project_index[project_id]:
                self._project_index[project_id].remove(curve_id)
            if not self._project_index[project_id]:
                del self._project_index[project_id]

        logger.debug(f"AutomationCurveStore: Deleted curve {curve_id}")
        return result

    def list_by_project(self, project_id: str) -> list[dict[str, Any]]:
        """List all curves for a project."""
        curve_ids = self._project_index.get(project_id, [])
        curves = []
        for curve_id in curve_ids:
            curve = self._store.get(curve_id)
            if curve:
                curves.append(curve)
        return curves

    def list_all(self) -> list[dict[str, Any]]:
        """List all automation curves."""
        return self._store.list()

    def count(self) -> int:
        """Get total number of curves."""
        return self._store.count()

    def exists(self, curve_id: str) -> bool:
        """Check if a curve exists."""
        return self._store.exists(curve_id)


class MacroExecutionStore:
    """
    Disk-backed macro execution state storage.

    Stores execution status and schedules for macros.

    Storage layout:
        data/stores/macro_execution/{macro_id}.json
    """

    def __init__(self, max_entries: int = 2000):
        self._store = JsonFileStore("macro_execution", max_items=max_entries)

    def get_status(self, macro_id: str) -> dict[str, Any] | None:
        """Get execution status for a macro."""
        entry = self._store.get(macro_id)
        if entry:
            return entry.get("status")
        return None

    def set_status(self, macro_id: str, status: dict[str, Any]) -> None:
        """Set execution status for a macro."""
        entry = self._store.get(macro_id) or {"id": macro_id}
        entry["status"] = status
        self._store.put(macro_id, entry)

    def get_schedule(self, macro_id: str) -> dict[str, Any] | None:
        """Get schedule for a macro."""
        entry = self._store.get(macro_id)
        if entry:
            return entry.get("schedule")
        return None

    def set_schedule(self, macro_id: str, schedule: dict[str, Any]) -> None:
        """Set schedule for a macro."""
        entry = self._store.get(macro_id) or {"id": macro_id}
        entry["schedule"] = schedule
        self._store.put(macro_id, entry)

    def clear_schedule(self, macro_id: str) -> None:
        """Clear schedule for a macro."""
        entry = self._store.get(macro_id)
        if entry:
            entry.pop("schedule", None)
            if entry.get("status") or len(entry) > 1:
                self._store.put(macro_id, entry)
            else:
                self._store.delete(macro_id)

    def delete(self, macro_id: str) -> bool:
        """Delete all execution data for a macro."""
        return self._store.delete(macro_id)

    def list_scheduled(self) -> list[dict[str, Any]]:
        """List all macros with schedules."""

        def has_schedule(entry: dict[str, Any]) -> bool:
            return "schedule" in entry

        return self._store.search(has_schedule)


# Singletons
_macro_store: MacroStore | None = None
_curve_store: AutomationCurveStore | None = None
_execution_store: MacroExecutionStore | None = None


def get_macro_store() -> MacroStore:
    """Get the global macro store singleton."""
    global _macro_store
    if _macro_store is None:
        _macro_store = MacroStore()
    return _macro_store


def get_automation_curve_store() -> AutomationCurveStore:
    """Get the global automation curve store singleton."""
    global _curve_store
    if _curve_store is None:
        _curve_store = AutomationCurveStore()
    return _curve_store


def get_macro_execution_store() -> MacroExecutionStore:
    """Get the global macro execution store singleton."""
    global _execution_store
    if _execution_store is None:
        _execution_store = MacroExecutionStore()
    return _execution_store
