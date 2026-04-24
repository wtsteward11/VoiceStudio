"""
Context Enricher for VoiceStudio Audit System.

Automatically enriches audit entries with:
- Git commit hash
- File-to-subsystem mapping
- Role resolution from task ID
- Quality Ledger integration

This module provides the "magic" that ties file changes
to meaningful business context.
"""

from __future__ import annotations

import json
import re
import subprocess
from fnmatch import fnmatch
from pathlib import Path
from typing import Any

from .schema import AuditEntry


class ContextEnricher:
    """
    Enriches audit entries with Git, Ledger, and subsystem context.

    Uses a registry file to map file paths to logical subsystems,
    and parses the Quality Ledger to resolve task ownership.
    """

    def __init__(
        self,
        subsystem_registry_path: Path | None = None,
        ledger_path: Path | None = None,
        repo_root: Path | None = None,
    ):
        """
        Initialize ContextEnricher.

        Args:
            subsystem_registry_path: Path to subsystem_registry.json
            ledger_path: Path to Quality Ledger markdown file
            repo_root: Repository root directory
        """
        self._repo_root = repo_root or self._find_repo_root()

        # Load subsystem registry
        if subsystem_registry_path is None:
            subsystem_registry_path = Path(__file__).parent / "subsystem_registry.json"
        self._subsystem_map = self._load_subsystem_map(subsystem_registry_path)

        # Load ledger path
        if ledger_path is None:
            ledger_path = (
                self._repo_root / "docs" / "archive" / "Recovery_Plan" / "QUALITY_LEDGER.md"
            )
        self._ledger_path = ledger_path
        self._ledger_cache: dict[str, dict[str, Any]] = {}
        self._ledger_loaded = False

    def _find_repo_root(self) -> Path:
        """Find the repository root by looking for .git directory."""
        current = Path.cwd()
        while current != current.parent:
            if (current / ".git").exists():
                return current
            current = current.parent
        return Path.cwd()

    def _load_subsystem_map(self, registry_path: Path) -> list[dict[str, str]]:
        """Load subsystem mapping patterns from registry file."""
        if not registry_path.exists():
            # Return default patterns
            return self._get_default_patterns()

        try:
            with open(registry_path, encoding="utf-8") as f:
                data = json.load(f)
                patterns: list[dict[str, str]] = data.get("patterns", self._get_default_patterns())
                return patterns
        except (OSError, json.JSONDecodeError):
            return self._get_default_patterns()

    def _get_default_patterns(self) -> list[dict[str, str]]:
        """Return default subsystem mapping patterns."""
        return [
            {"glob": "src/VoiceStudio.App/Views/Panels/*View*", "subsystem": "UI.Panels"},
            {"glob": "src/VoiceStudio.App/ViewModels/*", "subsystem": "UI.ViewModels"},
            {"glob": "src/VoiceStudio.App/Resources/Styles/*", "subsystem": "UI.Styles.Global"},
            {"glob": "src/VoiceStudio.App/Services/*", "subsystem": "App.Services"},
            {"glob": "src/VoiceStudio.Core/*", "subsystem": "Core"},
            {"glob": "backend/api/routes/*", "subsystem": "Backend.API.Routes"},
            {"glob": "backend/api/middleware/*", "subsystem": "Backend.API.Middleware"},
            {"glob": "backend/api/*", "subsystem": "Backend.API"},
            {"glob": "backend/services/*", "subsystem": "Backend.Services"},
            {"glob": "app/core/engines/*", "subsystem": "Engines"},
            {"glob": "app/core/audio/*", "subsystem": "Audio"},
            {"glob": "app/core/monitoring/*", "subsystem": "Monitoring"},
            {"glob": "app/core/runtime/*", "subsystem": "Runtime"},
            {"glob": "app/core/training/*", "subsystem": "Training"},
            {"glob": "app/core/audit/*", "subsystem": "Audit"},
            {"glob": "tests/*", "subsystem": "Tests"},
            {"glob": "docs/*", "subsystem": "Documentation"},
            {"glob": ".cursor/rules/*", "subsystem": "Governance.Rules"},
            {"glob": "scripts/*", "subsystem": "Scripts"},
            {"glob": "tools/*", "subsystem": "Tools"},
            {"glob": "*.xaml", "subsystem": "UI.XAML"},
            {"glob": "*.cs", "subsystem": "CSharp"},
            {"glob": "*.py", "subsystem": "Python"},
        ]

    def _get_current_commit(self) -> str | None:
        """Get current Git commit hash."""
        try:
            result = subprocess.run(
                ["git", "rev-parse", "--short", "HEAD"],
                capture_output=True,
                text=True,
                cwd=self._repo_root,
                timeout=5,
            )
            if result.returncode == 0:
                return result.stdout.strip()
        # Best effort - failure is acceptable here
        except (subprocess.TimeoutExpired, FileNotFoundError, OSError):
            pass
        return None

    def _map_file_to_subsystem(self, file_path: str | None) -> str | None:
        """Map file path to logical subsystem using registry patterns."""
        if not file_path:
            return None

        # Normalize path separators
        normalized = file_path.replace("\\", "/")

        for pattern in self._subsystem_map:
            glob_pattern = pattern.get("glob", "")
            subsystem = pattern.get("subsystem", "unknown")

            # Try both with and without **/ prefix
            if fnmatch(normalized, glob_pattern) or fnmatch(normalized, f"**/{glob_pattern}"):
                return subsystem

            # Also check if pattern matches end of path
            if "/" in glob_pattern and normalized.endswith(glob_pattern.lstrip("*/")):
                return subsystem

        # Fallback based on file extension
        if normalized.endswith(".xaml"):
            return "UI.XAML"
        if normalized.endswith(".cs"):
            return "CSharp"
        if normalized.endswith(".py"):
            return "Python"
        if normalized.endswith(".md"):
            return "Documentation"

        return "unknown"

    def _load_ledger(self):
        """Load and parse the Quality Ledger."""
        if self._ledger_loaded:
            return

        if not self._ledger_path.exists():
            self._ledger_loaded = True
            return

        try:
            with open(self._ledger_path, encoding="utf-8") as f:
                content = f.read()

            # Parse ledger entries
            # Pattern: | VS-XXXX | ... | Role X | ...
            pattern = r"\|\s*(VS-\d{4})\s*\|[^|]*\|[^|]*\|[^|]*\|\s*([^|]+)\s*\|"
            matches = re.findall(pattern, content)

            for task_id, owner in matches:
                self._ledger_cache[task_id.strip()] = {
                    "owner": owner.strip(),
                    "role": self._extract_role(owner.strip()),
                }

            self._ledger_loaded = True
        except OSError:
            self._ledger_loaded = True

    def _extract_role(self, owner: str) -> str | None:
        """Extract role number/name from owner string."""
        # Match patterns like "Role 2", "Role 3 - UI Engineer", etc.
        match = re.match(r"Role\s*(\d+)", owner, re.IGNORECASE)
        if match:
            role_num = match.group(1)
            role_names = {
                "0": "Role 0 - Overseer",
                "1": "Role 1 - System Architect",
                "2": "Role 2 - Build & Tooling",
                "3": "Role 3 - UI Engineer",
                "4": "Role 4 - Core Platform",
                "5": "Role 5 - Engine Engineer",
                "6": "Role 6 - Release Engineer",
            }
            return role_names.get(role_num, f"Role {role_num}")
        return owner if owner else None

    def _get_task_info(self, task_id: str | None) -> dict[str, Any]:
        """Look up task from ledger: owner, gate, status."""
        if not task_id:
            return {}

        self._load_ledger()
        return self._ledger_cache.get(task_id, {})

    def _resolve_role(self, task_id: str | None) -> str | None:
        """Return role assigned to task."""
        task_info = self._get_task_info(task_id)
        return task_info.get("role")

    def enrich(self, entry: AuditEntry) -> AuditEntry:
        """
        Enrich audit entry with context information.

        Adds:
        - commit_hash: Current Git commit
        - subsystem: Mapped from file path
        - role: Resolved from task_id via ledger

        Args:
            entry: AuditEntry to enrich

        Returns:
            Enriched AuditEntry
        """
        # Add commit hash if not set
        if not entry.commit_hash:
            entry.commit_hash = self._get_current_commit()

        # Map file to subsystem if not set
        if not entry.subsystem and entry.file_path:
            entry.subsystem = self._map_file_to_subsystem(entry.file_path)

        # Resolve role from task_id if not set
        if not entry.role and entry.task_id:
            entry.role = self._resolve_role(entry.task_id)

        return entry

    def get_subsystem_for_file(self, file_path: str) -> str:
        """
        Public method to get subsystem for a file path.

        Args:
            file_path: Path to the file

        Returns:
            Subsystem name
        """
        return self._map_file_to_subsystem(file_path) or "unknown"

    def get_role_for_task(self, task_id: str) -> str | None:
        """
        Public method to get role for a task ID.

        Args:
            task_id: Task ID (e.g., "VS-0018")

        Returns:
            Role name or None
        """
        return self._resolve_role(task_id)

    def validate_task_id(self, task_id: str) -> bool:
        """
        Verify task_id exists in ledger.

        Args:
            task_id: Task ID to validate

        Returns:
            True if task exists in ledger
        """
        self._load_ledger()
        return task_id in self._ledger_cache


# Global enricher instance
_context_enricher: ContextEnricher | None = None


def get_context_enricher() -> ContextEnricher:
    """Get or create global context enricher."""
    global _context_enricher
    if _context_enricher is None:
        _context_enricher = ContextEnricher()
    return _context_enricher
